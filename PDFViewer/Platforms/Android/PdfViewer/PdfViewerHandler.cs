// Platforms/Android/PdfViewer/PdfViewerHandler.cs
//
// Motor: PDFium via PDFiumCore (P/Invoke; mesmo motor do Edge/Chrome e dos handlers Windows/iOS).
// Substitui o android.graphics.pdf.PdfRenderer nativo, que rasteriza conteúdo vetorial/texto de
// certos PDFs em BRANCO; o PDFium renderiza tudo. Render e camada de texto (busca/seleção) vêm
// do MESMO PdfiumDoc (arquivo aberto uma vez).
//
// Modelo de renderização "fonte única coordenada":
//   - O RecyclerView (LinearLayoutManager) faz a virtualização das células.
//   - O adapter é "burro": só LÊ do cache e exibe. Em cache miss, pede render ao handler.
//   - O handler é a ÚNICA fonte de render (RequestRender) com deduplicação por página.
//     Bind e prefetch convergem para o mesmo caminho → zero double-render, zero corrida de Put.
//   - Largura de render FIXA (viewW × renderScale), independente do zoom (ScaleX amplia).
//     Evita OOM em zoom alto e mantém o cache estável (width nunca muda).
//   - O cache NUNCA chama Bitmap.Recycle() — em API 26+ os pixels vivem no heap nativo
//     gerenciado pelo GC; reciclar manualmente causa "Canvas: trying to use a recycled bitmap"
//     quando o GPU thread ainda referencia o bitmap.

using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Print;
using Android.Runtime;
using Android.Views;
using Android.Views.Animations;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using System.Net.Http;
using AView = Android.Views.View;
using OperationCanceledException = System.OperationCanceledException;

namespace Agile.Maui.Platforms.Android;

// ─────────────────────────────────────────────────────────────────────────────
// Handler MAUI
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfViewerHandler
    : ViewHandler<PdfViewer, PdfContainerView>
{
    private const string Tag = "Pdf/Droid";

    public static readonly PropertyMapper<PdfViewer, PdfViewerHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(PdfViewer.Source)]              = (h, _) => h.LoadDocument(),
            [nameof(PdfViewer.PdfStream)]           = (h, _) => h.LoadDocument(),
            [nameof(PdfViewer.Password)]            = (h, _) => h.LoadDocument(),
            [nameof(PdfViewer.CurrentPage)]         = (h, _) => h.SyncPage(),
            [nameof(PdfViewer.ZoomFactor)]          = (h, _) => h.SyncZoom(),
            [nameof(PdfViewer.MinZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxCacheMB)]          = (h, _) => h.ApplyCache(),
            [nameof(PdfViewer.RenderScale)]         = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.ApplyPageBackground(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.ScrollOrientation)]   = (h, _) => h.ApplyOrientation(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomEnabled(),
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.CopyButtonText)]      = (h, _) => h.ApplyCopyButtonText(),
            [nameof(PdfViewer.IsThumbnailBarOpen)]  = (h, _) => h.ApplyThumbnailBar(),
            [nameof(PdfViewer.ThumbnailBarPlacement)] = (h, _) => h.ApplyThumbPlacement(),
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
        };

    public static readonly CommandMapper<PdfViewer, PdfViewerHandler> CommandMapper =
        new(ViewCommandMapper)
        {
            [nameof(PdfViewer.PrintAsync)]   = (h, _, _) => h.Print(),
            [nameof(PdfViewer.Search)]       = (h, _, a) => h.DoSearch(a as string ?? string.Empty),
            [nameof(PdfViewer.FindNext)]     = (h, _, _) => h.StepHit(+1),
            [nameof(PdfViewer.FindPrevious)] = (h, _, _) => h.StepHit(-1),
            [nameof(PdfViewer.ClearSearch)]  = (h, _, _) => h.ClearSearchState(),
        };

    private PdfiumDoc?            _doc;
    private PdfAdapter?           _adapter;
    private PdfBitmapLruCache?    _cache;
    private PdfSpacingDecoration? _spacingDecoration;
    private CancellationTokenSource    _shutdownCts = new();   // cancela todos os renders
    private CancellationTokenSource?   _loadCts;
    // Render serializado num worker ÚNICO (o PDFium tem lock de processo, logo N threads de render
    // só competiriam por ele sem prioridade). A fila é priorizada por proximidade ao centro
    // visível: a página que o usuário olha renderiza ANTES dos prefetches e os itens que saíram
    // da janela são descartados em vez de ocupar o motor — elimina o "branco" ao rolar.
    private readonly object            _queueLock  = new();
    private readonly HashSet<int>      _queued     = new();
    private readonly SemaphoreSlim     _workerGate = new(1, 1);
    private volatile int               _currentCenter;
    private volatile int               _scrollDir = 1;  // +1 avançando páginas, -1 retrocedendo (prioriza a fila)
    private bool                       _syncingPage;
    private bool                       _reportingPage;  // mudança originada do scroll → não re-sincronizar
    private int                        _targetPage = -1;
    private int                        _lastPrefetchCenter = -1;  // evita prefetch redundante por frame
    private bool                       _syncingZoom;
    private string?                    _tempFilePath;
    private long                       _fileBytes;   // tamanho do PDF atual (heurística p/ RenderScale adaptativo)
    private SizeF[]?                   _pageSizes;   // tamanhos (pt) de todas as páginas — p/ recriar o adapter ao trocar a orientação

    // Busca de texto — mesma instância PdfiumDoc usada no render (_doc).
    private List<(int page, int index, int count)> _findHits = new();
    private int                        _findCurrent = -1;
    private string                     _findTerm    = string.Empty;
    private CancellationTokenSource?   _findCts;

    // Seleção de texto MULTI-PÁGINA: âncora (fixa no arraste) e foco (móvel), cada uma como
    // (página, índice de caractere do PDFium). Seleção ativa ⇔ _anchorPage >= 0.
    private int                        _anchorPage = -1, _anchorChar = -1;
    private int                        _focusPage  = -1, _focusChar  = -1;
    private global::Android.Widget.TextView? _copyPill;
    // Auto-scroll ao arrastar uma alça até a borda (rola p/ estender a seleção por outras páginas).
    private int                        _autoScrollDir;   // -1 cima, 0 nenhum, +1 baixo
    private float                      _lastDragCx, _lastDragCy;
    private CancellationTokenSource?   _autoScrollCts;

    // ── Barra de miniaturas (drawer sobreposto à direita) ──────────────────────────
    private global::Android.Widget.FrameLayout? _thumbOverlay;
    private global::Android.Widget.FrameLayout? _thumbScrim;   // FrameLayout p/ aceitar clique de forma confiável
    private AndroidX.RecyclerView.Widget.RecyclerView? _thumbRv;
    private PdfThumbAdapter?                    _thumbAdapter;
    private readonly Dictionary<int, Bitmap>    _thumbCache = new();
    private bool                                _thumbOpen;

    public PdfViewerHandler() : base(Mapper, CommandMapper) { }

    protected override PdfContainerView CreatePlatformView() => new(Context!);

    // ── Impressão ───────────────────────────────────────────────────────────────
    // Envia o PDF carregado ao framework de impressão do Android. O arquivo já está em
    // disco (temp para URL/stream; caminho original para arquivo local); um adapter
    // simplesmente copia esses bytes para o destino escolhido pelo usuário.
    private void Print()
    {
        string? path = _tempFilePath ?? VirtualView?.Source;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            PdfViewerLog.Write(Tag, "Print: nenhum documento disponível.");
            return;
        }

        if (Context?.GetSystemService(Context.PrintService) is not PrintManager pm)
        {
            PdfViewerLog.Write(Tag, "Print: PrintManager indisponível.");
            return;
        }

        string job = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(job)) job = string.IsNullOrEmpty(VirtualView?.PrintJobName) ? "Document" : VirtualView!.PrintJobName;
        pm.Print(job, new PdfFilePrintAdapter(path, job + ".pdf"), null);
    }

    protected override void ConnectHandler(PdfContainerView pv)
    {
        PdfViewerLog.Write(Tag, "ConnectHandler");
        base.ConnectHandler(pv);
        pv.LayoutParameters = new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

        pv.OnPageChanged = page =>
        {
            if (_syncingPage)
            {
                if (page == _targetPage)
                {
                    _syncingPage = false;
                    _targetPage  = -1;
                    ReportPage(page);
                    Prefetch(page);
                }
                return;
            }
            // A seleção PERMANECE ativa ao rolar (o usuário pode rolar e continuar selecionando).
            // A pílula, posicionada de forma absoluta, é escondida durante o scroll e reaparece
            // sobre a seleção no idle (OnScrollIdle → ShowCopyPill).
            if (HasSelection && !pv.Selecting) HideCopyPill();
            ReportPage(page);
            // Prefetch contínuo durante o scroll (sem trim, que fica só no idle) — antecipa
            // o render das páginas que estão entrando na viewport, evitando a célula em branco.
            Prefetch(page);
        };

        pv.OnSelectionStart      = BeginSelection;
        pv.OnSelectionDrag       = ExtendSelection;
        pv.OnSelectionEnd        = FinishSelection;
        pv.OnSelectionTapClear   = HandleTap;
        pv.OnSelectionHandleHit  = HandleHitTest;
        pv.OnSelectionHandleDown = BeginHandleDrag;

        pv.OnZoomChanged = zoom =>
        {
            UpdateSelectionHandleScale();   // alça mantém tamanho fixo na tela ao ampliar
            if (_syncingZoom) return;
            _syncingZoom = true;
            if (VirtualView is not null) VirtualView.ZoomFactor = zoom;
            _syncingZoom = false;
            // Zoom usa ScaleX do RecyclerView — NÃO re-renderiza (evita OOM e mantém cache).
        };

        pv.OnScrollIdle = page =>
        {
            if (_syncingPage) { _syncingPage = false; _targetPage = -1; ReportPage(page); }
            TrimAndPrefetch(page);
            if (HasSelection && !pv.Selecting) ShowCopyPill();   // pílula reaparece sobre a seleção
        };

        ApplyCache();
        ApplyZoomLimits();
    }

    protected override void DisconnectHandler(PdfContainerView pv)
    {
        PdfViewerLog.Write(Tag, "DisconnectHandler");

        _shutdownCts.Cancel();
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;

        pv.OnPageChanged = null;
        pv.OnZoomChanged = null;
        pv.OnScrollIdle  = null;
        pv.OnSelectionStart      = null;
        pv.OnSelectionDrag       = null;
        pv.OnSelectionEnd        = null;
        pv.OnSelectionTapClear   = null;
        pv.OnSelectionHandleHit  = null;
        pv.OnSelectionHandleDown = null;
        ClearSelection();

        pv.Rv.SetAdapter(null);
        _adapter?.Dispose();
        _adapter = null;

        // Barra de miniaturas: solta o adapter/overlay e o cache (sem recycle — GC coleta).
        _thumbRv?.SetAdapter(null);
        _thumbAdapter?.Dispose(); _thumbAdapter = null;
        if (_thumbOverlay is not null) { pv.RemoveView(_thumbOverlay); }
        pv.ThumbOverlay = null;
        _thumbOverlay = null; _thumbScrim = null; _thumbRv = null;
        _thumbCache.Clear();
        _thumbOpen = false;

        _findCts?.Cancel(); _findCts?.Dispose(); _findCts = null;

        // _doc serve render E texto — descartado uma vez (fecha o handle PDFium → libera o arquivo).
        _doc?.Dispose();
        _doc = null;

        // EvictAll SEM recycle — o RecyclerView pode ainda estar visível na animação de
        // saída com ImageViews referenciando bitmaps. Reciclar aqui causaria
        // "Canvas: trying to use a recycled bitmap". O GC coleta quando as views somem.
        _cache?.EvictAll();
        _cache = null;

        lock (_queueLock) _queued.Clear();

        if (_tempFilePath is not null)
        {
            try { System.IO.File.Delete(_tempFilePath); } catch { }
            _tempFilePath = null;
        }

        _shutdownCts.Dispose();
        base.DisconnectHandler(pv);
    }

    // ── LoadDocument ──────────────────────────────────────────────────────────

    private void LoadDocument()
    {
        if (PlatformView is null || VirtualView is null) return;

        // Reinicia o token de shutdown para esta sessão de documento.
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _shutdownCts = new CancellationTokenSource();
        lock (_queueLock) _queued.Clear();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        PlatformView.Rv.SetAdapter(null);
        _adapter?.Dispose();  _adapter = null;
        _doc?.Dispose();      _doc     = null;
        _pageSizes = null;
        _thumbCache.Clear();                 // miniaturas do doc anterior (índices reusados)
        _thumbAdapter?.Configure(0, 0);      // esvazia o drawer até o novo doc carregar
        _findCts?.Cancel(); _findHits = new(); _findCurrent = -1; _findTerm = string.Empty;
        _lastPrefetchCenter = -1;

        // Descarta as páginas do documento anterior: o cache é keyed por índice de página e o
        // novo PDF reusa os mesmos índices — sem esvaziar, as primeiras páginas do PDF antigo
        // apareceriam (cache hit) ao abrir o novo documento.
        _cache?.EvictAll();

        // O engine anterior já foi disposed acima (fecha renderer + pfd → libera o arquivo),
        // então é seguro deletar o temp anterior agora, na UI thread. O novo temp tem nome
        // único (Guid) e só é registrado em _tempFilePath na UI thread (ver MainThread abaixo),
        // eliminando qualquer corrida com o DisconnectHandler.
        if (_tempFilePath is not null)
        {
            try { System.IO.File.Delete(_tempFilePath); } catch { }
            _tempFilePath = null;
        }

        var source   = VirtualView.Source;
        var stream   = VirtualView.PdfStream;
        var password = VirtualView.Password;
        if (string.IsNullOrWhiteSpace(source) && stream is null) return;

        bool isUrl = !string.IsNullOrWhiteSpace(source)
            && (source!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        PdfViewerLog.Write(Tag,
            $"LoadDocument  source='{(source?.Length > 50 ? source[..50] + "…" : source ?? "stream")}'  isUrl={isUrl}");

        var vv      = VirtualView;
        var context = Context!;

        _ = Task.Run(async () =>
        {
            string?    newTemp = null;   // arquivo criado por esta sessão (se houver)
            PdfiumDoc? doc     = null;
            try
            {
                string localPath;

                if (stream is not null)
                {
                    newTemp = System.IO.Path.Combine(
                        context.CacheDir!.AbsolutePath, Guid.NewGuid().ToString("N") + ".pdf");
                    await using (var fs = new FileStream(newTemp, FileMode.Create, FileAccess.Write))
                        await stream.CopyToAsync(fs, cts.Token);
                    localPath = newTemp;
                }
                else if (isUrl)
                {
                    newTemp = System.IO.Path.Combine(
                        context.CacheDir!.AbsolutePath, Guid.NewGuid().ToString("N") + ".pdf");
                    using var http = PdfHttpClient.Create();
                    var bytes = await http.GetByteArrayAsync(source, cts.Token);
                    PdfViewerLog.Write(Tag, $"LoadDocument: download {bytes.Length / 1024} KB");
                    await System.IO.File.WriteAllBytesAsync(newTemp, bytes, cts.Token);
                    localPath = newTemp;
                }
                else
                {
                    localPath = source!;
                    if (!System.IO.File.Exists(localPath))
                        throw new FileNotFoundException($"Arquivo não encontrado: {localPath}");
                }

                if (cts.IsCancellationRequested) { CleanupTemp(newTemp); return; }

                _fileBytes = SafeFileLength(localPath);   // p/ RenderScale adaptativo (evita OOM em PDF pesado)

                // Abre via PDFium (lê contagem e tamanhos das páginas — não rasteriza nada ainda).
                // O MESMO documento serve render e a camada de texto (busca/seleção).
                doc = new PdfiumDoc(localPath, password);
                int count = doc.PageCount;
                PdfViewerLog.Write(Tag, $"LoadDocument: pageCount={count}");

                if (count == 0)
                {
                    doc.Dispose(); doc = null;
                    throw new InvalidOperationException("PDF com 0 páginas.");
                }

                if (cts.IsCancellationRequested) { doc.Dispose(); CleanupTemp(newTemp); return; }

                // Pré-calcula tamanhos de todas as páginas (só metadados, rápido).
                // Necessário para o adapter dar altura correta às células ANTES do render —
                // sem isso, células com bitmap null teriam height=0 e o LinearLayoutManager
                // criaria dezenas de ViewHolders de uma vez.
                var pageSizes = new SizeF[count];
                for (int i = 0; i < count && !cts.IsCancellationRequested; i++)
                    pageSizes[i] = doc.GetPageSize(i);

                var capturedDoc = doc;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Toda mutação de _doc / _tempFilePath acontece AQUI (UI thread),
                    // a mesma thread do DisconnectHandler — sem corrida.
                    if (cts.IsCancellationRequested || PlatformView is null)
                    {
                        capturedDoc.Dispose();
                        CleanupTemp(newTemp);
                        return;
                    }

                    _tempFilePath = newTemp;
                    _doc          = capturedDoc;
                    _pageSizes    = pageSizes;

                    // Aplica a orientação pedida ANTES de criar o adapter — o adapter dimensiona
                    // as células conforme o eixo (ver BuildAdapter). SetHorizontal é no-op se já
                    // estava na orientação correta.
                    PlatformView.SetHorizontal(vv.ScrollOrientation == PdfScrollOrientation.Horizontal);

                    _adapter = BuildAdapter(count, pageSizes);

                    ApplySpacing();
                    PlatformView.Rv.SetAdapter(_adapter);
                    ResetZoomTo100();   // todo documento abre em 100% (paridade entre plataformas)
                    vv.RaiseDocumentLoaded(count);
                    TrimAndPrefetch(0);
                });
            }
            catch (OperationCanceledException) { doc?.Dispose(); CleanupTemp(newTemp); }
            catch (Exception ex)
            {
                doc?.Dispose();
                CleanupTemp(newTemp);
                PdfViewerLog.Write(Tag, $"LoadDocument ERRO: [{ex.GetType().Name}] {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!cts.IsCancellationRequested) vv.RaiseDocumentLoadFailed(ex.Message);
                });
            }
        }, cts.Token);

        // Deleta um arquivo temporário órfão (cancelado/falhou antes de virar _tempFilePath).
        static void CleanupTemp(string? path)
        {
            if (path is not null) try { System.IO.File.Delete(path); } catch { }
        }
    }

    // ── Render coordenado (fonte única, com deduplicação) ──────────────────────

    /// <summary>Largura de render em px — FIXA, independente do zoom (ScaleX amplia).</summary>
    private int RenderWidthPx()
    {
        int w = PlatformView?.Rv.Width > 0
            ? PlatformView!.Rv.Width
            : Context?.Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        double scale = EffectiveRenderScale();
        return Math.Max(1, (int)(w * scale));
    }

    // RenderScale EFETIVO: limita a escala pedida pelo consumidor conforme o tamanho do PDF e o
    // peso por página. PDFs escaneados pesados em escala alta geram bitmaps enormes (largura da
    // viewport × scale × 4 bytes) que estouram o heap do Android (OOM) → página em branco.
    // Reduzir a escala evita o OOM. Nunca AUMENTA além do pedido; tamanho desconhecido → sem limite.
    private double EffectiveRenderScale()
    {
        double user  = VirtualView?.RenderScale ?? 1.5;
        double mb    = _fileBytes / (1024.0 * 1024.0);
        int    pages = _doc is not null && _doc.PageCount > 0 ? _doc.PageCount : 1;
        double perPg = mb / pages;

        double cap = (mb, perPg) switch
        {
            ( <= 0, _)  => double.MaxValue,   // desconhecido → sem limite
            (_, >= 1.0) => 1.0,               // páginas pesadas (≥1MB/pág) → evita OOM
            ( <= 15, _) => double.MaxValue,   // arquivo leve → escala cheia
            ( <= 40, _) => 1.5,
            _           => 1.0,               // arquivo grande → evita OOM
        };
        return Math.Min(user, cap);
    }

    private static long SafeFileLength(string path)
    {
        try { return new System.IO.FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// Solicita a renderização de uma página. Idempotente: se já está no cache ou já
    /// está sendo renderizada, não faz nada. Chamado tanto pelo bind (cache miss) quanto
    /// pelo prefetch — caminho único, sem double-render.
    /// </summary>
    private void RequestRender(int idx)
    {
        var engine = _doc;
        var cache  = _cache;
        if (engine is null || cache is null) return;
        if (idx < 0 || idx >= engine.PageCount) return;

        if (cache.ContainsPage(idx)) { ApplyToVisible(idx); return; }

        lock (_queueLock)
        {
            if (!_queued.Add(idx)) return;   // já enfileirado → dedup
        }
        TryStartRenderWorker();
    }

    /// <summary>
    /// Garante UM worker de render vivo. SemaphoreSlim(1,1) não-bloqueante assegura instância
    /// única; o worker drena a fila priorizando a proximidade ao centro visível.
    /// </summary>
    private void TryStartRenderWorker()
    {
        if (!_workerGate.Wait(0)) return;   // já há um worker drenando a fila
        CancellationToken token;
        // _shutdownCts é recriado/disposed no Load/Disconnect — se já foi descartado, não há sessão.
        try { token = _shutdownCts.Token; }
        catch (ObjectDisposedException) { _workerGate.Release(); return; }
        var pv = PlatformView;

        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    int idx;
                    lock (_queueLock)
                    {
                        if (token.IsCancellationRequested || _queued.Count == 0) return;
                        idx = PickNearestQueued();
                        _queued.Remove(idx);
                    }

                    var engine = _doc;
                    var cache  = _cache;
                    if (engine is null || cache is null || token.IsCancellationRequested) return;

                    // Chegou ao cache por outro caminho → só aplica.
                    if (cache.ContainsPage(idx)) { PostApply(pv, idx, token); continue; }

                    // Saiu da janela ativa enquanto esperava na fila → descarta (o re-bind
                    // re-solicita se voltar). Evita drenar o motor com renders obsoletos —
                    // é o que causava o "branco por segundos" ao rolar rápido.
                    if (IsOutsideWindow(idx)) continue;

                    int widthPx = RenderWidthPx();
                    var bg      = (VirtualView?.PageBackgroundColor ?? Colors.White).ToPlatform();
                    try
                    {
                        var bmp = await engine.RenderAndroidBitmapAsync(idx, widthPx, bg, token);
                        if (bmp is null || token.IsCancellationRequested) continue;
                        // Não escrever num cache órfão: se ApplyCache substituiu _cache, o bitmap
                        // nunca seria exibido → leak. ReferenceEquals cobre essa troca.
                        if (!ReferenceEquals(cache, _cache)) continue;
                        cache.Put(idx, bmp);
                        PostApply(pv, idx, token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Java.Lang.OutOfMemoryError)
                    {
                        PdfViewerLog.Write(Tag, $"Render pág {idx}: OOM");
                        cache.ReduceByHalf();
                        GC.Collect();
                    }
                    catch (Exception ex)
                    {
                        PdfViewerLog.Write(Tag, $"Render pág {idx}: ERRO [{ex.GetType().Name}] {ex.Message}");
                    }
                }
            }
            finally
            {
                _workerGate.Release();
                // Fecha a janela de corrida: se itens entraram na fila (ou sobraram de uma troca
                // de documento) enquanto saíamos, e a sessão segue viva, re-dispara o worker.
                bool more;
                lock (_queueLock) more = _queued.Count > 0;
                bool alive;
                try { alive = !_shutdownCts.IsCancellationRequested; }
                catch (ObjectDisposedException) { alive = false; }
                if (more && alive) TryStartRenderWorker();
            }
        }, token);
    }

    // Item enfileirado de maior prioridade. A página visível (distância 0) vem primeiro; entre as
    // demais, as que estão NA DIREÇÃO do scroll têm prioridade sobre as de trás à mesma distância
    // (senão a página anterior, ou uma mais distante, poderia "furar a fila" na frente da próxima
    // página que entra na tela). Chamado sob _queueLock.
    private int PickNearestQueued()
    {
        int best = -1; long bestScore = long.MaxValue;
        int center = _currentCenter, dir = _scrollDir;
        foreach (int q in _queued)
        {
            int  delta = q - center;
            long score = (long)Math.Abs(delta) * 2;          // distância (peso 2 p/ abrir o desempate)
            if (delta != 0 && Math.Sign(delta) != dir) score += 1;  // contra a direção → renderiza depois
            if (score < bestScore) { bestScore = score; best = q; }
        }
        return best;
    }

    // A página está fora da janela ativa (visível ± prefetch + margem)? A margem tolera o intervalo
    // entre enfileirar e processar; o re-bind re-solicita se a página voltar à viewport.
    private bool IsOutsideWindow(int idx)
    {
        var vv = VirtualView;
        if (vv is null) return false;
        int above  = vv.EnablePageCaching ? vv.PrefetchAbove : 0;
        int below  = vv.EnablePageCaching ? vv.PrefetchBelow : 0;
        int center = _currentCenter;
        return idx < center - above - 2 || idx > center + below + 2;
    }

    // Aplica o bitmap cacheado à ImageView visível na UI thread (se a página ainda está na tela).
    private void PostApply(PdfContainerView? pv, int idx, CancellationToken token)
        => pv?.Rv.Post(() => { if (!token.IsCancellationRequested) ApplyToVisible(idx); });

    /// <summary>
    /// Aplica o bitmap do cache diretamente na ImageView visível, se a página estiver
    /// na tela. Evita NotifyItemChanged (que pode lançar durante scroll/layout).
    /// </summary>
    private void ApplyToVisible(int idx)
    {
        var pv = PlatformView;
        if (pv is null) return;
        if (pv.Rv.FindViewHolderForAdapterPosition(idx) is PdfVH vh)
        {
            var bmp = _cache?.Get(idx);
            if (bmp is not null && !bmp.IsRecycled)
                vh.Iv.SetImageBitmap(bmp);
        }
    }

    /// <summary>
    /// Agenda o render das páginas da janela ativa, SEM recortar o cache.
    /// Chamado continuamente durante o scroll — o dedup e o cache hit evitam trabalho
    /// repetido; o LRU controla o tamanho. O trim agressivo fica para o idle (TrimAndPrefetch).
    /// </summary>
    private void Prefetch(int centerPage)
    {
        if (_doc is null || _cache is null || VirtualView is null) return;
        _currentCenter = centerPage;   // prioridade da fila de render segue o que está visível
        if (centerPage == _lastPrefetchCenter) return;  // já prefetchado para este centro
        if (_lastPrefetchCenter >= 0)   // direção do scroll → prioriza a fila na ordem de leitura
            _scrollDir = centerPage > _lastPrefetchCenter ? 1 : -1;
        _lastPrefetchCenter = centerPage;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = _doc.PageCount;
        int start = Math.Max(0, centerPage - above);
        int end   = Math.Min(total - 1, centerPage + below);

        // Pula o centro (d=0) — as páginas visíveis são renderizadas pelo próprio bind do
        // adapter (caminho confiável que aplica na ImageView). O prefetch só antecipa as
        // ADJACENTES no cache, para que o bind ache hit ao scrollar. Ordem: abaixo → acima.
        for (int d = 1; d <= Math.Max(above, below); d++)
        {
            if (d <= below && centerPage + d <= end) RequestRender(centerPage + d);
            if (d <= above && centerPage - d >= start) RequestRender(centerPage - d);
        }
    }

    /// <summary>Recorta o cache para a janela ativa e agenda o render. Usado no idle/load.</summary>
    private void TrimAndPrefetch(int centerPage)
    {
        if (_doc is null || _cache is null || VirtualView is null) return;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = _doc.PageCount;
        int start = Math.Max(0, centerPage - above);
        int end   = Math.Min(total - 1, centerPage + below);

        _cache.TrimToWindow(start, end);
        RequestVisible();      // garante que as páginas na tela renderizem (cobre o descarte do fling)
        Prefetch(centerPage);
    }

    /// <summary>
    /// Solicita o render de TODAS as páginas atualmente visíveis no RecyclerView. Necessário no
    /// idle: durante um fling rápido o bind de uma página pode tê-la enfileirado com _currentCenter
    /// ainda distante, fazendo o worker descartá-la (IsOutsideWindow) — sem um novo bind ela ficaria
    /// branca. Aqui _currentCenter já aponta para a página parada, então elas não são descartadas.
    /// </summary>
    private void RequestVisible()
    {
        if (PlatformView?.Rv.GetLayoutManager() is not LinearLayoutManager lm) return;
        int first = lm.FindFirstVisibleItemPosition();
        int last  = lm.FindLastVisibleItemPosition();
        if (first < 0) return;
        for (int i = first; i <= last; i++) RequestRender(i);
    }

    // ── Property sync ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reporta a página atual ao controle MAUI marcando a origem como "scroll do usuário".
    /// O guard _reportingPage impede que o setter de CurrentPage dispare SyncPage de volta
    /// (que chamaria SmoothScrollToPosition competindo com o scroll em curso → jank/loop).
    /// </summary>
    private void ReportPage(int page)
    {
        _currentCenter = page;   // mantém a prioridade da fila de render colada ao scroll
        _reportingPage = true;
        VirtualView?.RaisePageChanged(page);
        _reportingPage = false;
    }

    private void SyncPage()
    {
        if (_reportingPage || _syncingPage || PlatformView is null || VirtualView is null || _adapter is null) return;
        if (PlatformView.Rv.GetLayoutManager() is not LinearLayoutManager lm) return;

        _targetPage  = Math.Clamp(VirtualView.CurrentPage, 0, _adapter.ItemCount - 1);
        _syncingPage = true;

        // SmoothScrollToPosition apenas torna o item VISÍVEL; numa lista de páginas de tela
        // cheia isso deixa a página anterior ainda como "primeira visível", e o OnScrollIdle
        // reporta de volta a página antiga — anulando a navegação por botão. Usamos um
        // LinearSmoothScroller com SNAP_TO_START para ALINHAR a página de destino ao topo.
        var scroller = new SnapToStartSmoothScroller(Context) { TargetPosition = _targetPage };
        lm.StartSmoothScroll(scroller);
    }

    private void SyncZoom()
    {
        if (_syncingZoom || PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        PlatformView.SetZoom((float)VirtualView.ZoomFactor);
        _syncingZoom = false;
        UpdateSelectionHandleScale();   // alça mantém tamanho fixo na tela
    }

    /// <summary>Reseta o zoom para 100% (escala nativa e ZoomFactor) — chamado ao abrir cada documento.</summary>
    private void ResetZoomTo100()
    {
        if (PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        PlatformView.SetZoom(1f);
        VirtualView.ZoomFactor = 1.0;
        _syncingZoom = false;
    }

    /// <summary>Reseta o zoom para o MÍNIMO (MinZoom) — chamado ao trocar a orientação.</summary>
    private void ResetZoomToMin()
    {
        if (PlatformView is null || VirtualView is null) return;
        double min = VirtualView.MinZoom;
        _syncingZoom = true;
        PlatformView.SetZoom((float)min);
        VirtualView.ZoomFactor = min;
        _syncingZoom = false;
    }

    private void ApplyZoomLimits()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.MinZoom = (float)VirtualView.MinZoom;
        PlatformView.MaxZoom = (float)VirtualView.MaxZoom;
    }

    // ── Busca (PDFium) ───────────────────────────────────────────────────────────
    // Varre o documento em background; mostra a 1ª ocorrência (realce + scroll) e reporta o
    // total/índice ao controle via RaiseSearchResult (a UI da barra fica no app consumidor).
    private void DoSearch(string term)
    {
        _findCts?.Cancel(); _findCts?.Dispose();
        _findCts = new CancellationTokenSource();
        var ct = _findCts.Token;

        _findTerm = term;
        _adapter?.ClearSearchHighlight();
        _findHits = new(); _findCurrent = -1;

        if (_doc is null || string.IsNullOrWhiteSpace(term)) { VirtualView?.RaiseSearchResult(0, -1); return; }
        var text = _doc;
        _ = Task.Run(() =>
        {
            var hits = text.FindAll(term);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                _findHits    = hits;
                _findCurrent = hits.Count > 0 ? 0 : -1;
                if (_findCurrent >= 0) ShowHit(_findCurrent);
                VirtualView?.RaiseSearchResult(_findHits.Count, _findCurrent);
            });
        }, ct);
    }

    private void StepHit(int delta)
    {
        if (_findHits.Count == 0) return;
        _findCurrent = (_findCurrent + delta + _findHits.Count) % _findHits.Count;
        ShowHit(_findCurrent);
        VirtualView?.RaiseSearchResult(_findHits.Count, _findCurrent);
    }

    private void ShowHit(int i)
    {
        if (_doc is null || _adapter is null || i < 0 || i >= _findHits.Count) return;
        var (page, index, count) = _findHits[i];
        var (rects, _) = _doc.GetSelection(page, index, index + count - 1);
        _adapter.SetSearchHighlight(page, rects);
        ScrollToPage(page);
    }

    private void ClearSearchState()
    {
        _findCts?.Cancel();
        _findHits = new(); _findCurrent = -1; _findTerm = string.Empty;
        _adapter?.ClearSearchHighlight();
        VirtualView?.RaiseSearchResult(0, -1);
    }

    private void ScrollToPage(int page)
    {
        if (_adapter is null || PlatformView?.Rv.GetLayoutManager() is not LinearLayoutManager lm) return;
        var sc = new SnapToStartSmoothScroller(Context) { TargetPosition = Math.Clamp(page, 0, _adapter.ItemCount - 1) };
        lm.StartSmoothScroll(sc);
    }

    // ── Seleção de texto (MULTI-PÁGINA) ─────────────────────────────────────────────
    // Âncora..foco como (página, índice de caractere do PDFium). Long-press seleciona a palavra;
    // o arraste/alças estendem por QUALQUER página sob o dedo; auto-scroll nas bordas atravessa
    // páginas. O realce é por página (cada PdfPageImageView desenha sua parte); a cópia concatena
    // o texto das páginas no intervalo.

    private const double SelectTolPt = 10.0;   // tolerância (pontos PDF) para "acertar" um caractere

    private bool HasSelection => _anchorPage >= 0;

    // Ordena âncora/foco em (início ≤ fim) comparando 1º por página, depois por caractere.
    private (int sp, int sc, int ep, int ec) NormalizeSel()
    {
        if (_anchorPage < _focusPage || (_anchorPage == _focusPage && _anchorChar <= _focusChar))
            return (_anchorPage, _anchorChar, _focusPage, _focusChar);
        return (_focusPage, _focusChar, _anchorPage, _anchorChar);
    }

    // Long-press: começa a seleção na palavra sob o dedo. Retorna true se acertou texto.
    private bool BeginSelection(float cx, float cy)
    {
        StopAutoScroll();
        _lastDragCx = cx; _lastDragCy = cy;
        var doc = _doc;
        if (doc is null) return false;
        if (!MapToPageUnder(cx, cy, out int page, out double xPt, out double yPt)) return false;

        int ci = doc.CharIndexAtPagePoint(page, xPt, yPt, SelectTolPt);
        if (ci < 0) return false;

        // Expande para a palavra inteira (limites = espaços em branco).
        int count = doc.CharCount(page);
        string text = count > 0 ? doc.GetText(page, 0, count) : string.Empty;
        int a = ci, z = ci;
        if (ci < text.Length && !char.IsWhiteSpace(text[ci]))
        {
            while (a > 0 && !char.IsWhiteSpace(text[a - 1])) a--;
            while (z < text.Length - 1 && !char.IsWhiteSpace(text[z + 1])) z++;
        }

        _anchorPage = page; _anchorChar = a;
        _focusPage  = page; _focusChar  = z;
        HideCopyPill();            // some durante o arraste; reaparece ao soltar
        RefreshSelectionHighlight();
        return true;
    }

    // Arraste: move o foco para a página/caractere sob o dedo (pode ser OUTRA página → multi-página).
    // Perto das bordas verticais dispara o auto-scroll para atravessar páginas.
    private void ExtendSelection(float cx, float cy)
    {
        _lastDragCx = cx; _lastDragCy = cy;
        UpdateAutoScroll(cy);
        ExtendSelectionCore(cx, cy);
    }

    private void ExtendSelectionCore(float cx, float cy)
    {
        var doc = _doc;
        if (doc is null || !HasSelection) return;
        if (!MapToPageUnder(cx, cy, out int page, out double xPt, out double yPt)) return;

        int ci = doc.CharIndexAtPagePoint(page, xPt, yPt, SelectTolPt * 1.5);
        if (ci < 0) return;        // dedo na margem/entre linhas → mantém o foco atual
        if (page == _focusPage && ci == _focusChar) return;
        _focusPage = page; _focusChar = ci;
        RefreshSelectionHighlight();
    }

    // Liga/desliga o auto-scroll conforme o dedo entra/sai das zonas de borda (72dp) da viewport.
    private void UpdateAutoScroll(float cy)
    {
        var pv = PlatformView;
        if (pv is null || Context is null) { StopAutoScroll(); return; }
        float edge = Context.ToPixels(72);
        int dir = cy < edge ? -1 : (cy > pv.Height - edge ? +1 : 0);
        if (dir == _autoScrollDir) return;
        _autoScrollDir = dir;
        _autoScrollCts?.Cancel(); _autoScrollCts = null;
        if (dir == 0) return;

        var cts = new CancellationTokenSource();
        _autoScrollCts = cts;
        _ = AutoScrollLoop(cts.Token);
    }

    // Rola o RecyclerView em passos enquanto o dedo permanece na borda, re-estendendo a seleção com
    // a última posição do dedo (que passa a apontar para conteúdo novo, eventualmente outra página).
    // Passo normalizado pelo zoom p/ velocidade ~constante na tela. Para nas extremidades do doc.
    private async Task AutoScrollLoop(CancellationToken ct)
    {
        var pv = PlatformView;
        if (pv is null || Context is null) return;
        while (!ct.IsCancellationRequested && pv.Selecting && _autoScrollDir != 0)
        {
            if (!pv.Rv.CanScrollVertically(_autoScrollDir)) { StopAutoScroll(); return; } // topo/fim do doc
            int step = Math.Max(1, (int)(Context.ToPixels(10) / pv.CurrentZoom));
            pv.Rv.ScrollBy(0, _autoScrollDir * step);
            ExtendSelectionCore(_lastDragCx, _lastDragCy);
            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void StopAutoScroll()
    {
        _autoScrollDir = 0;
        _autoScrollCts?.Cancel(); _autoScrollCts = null;
    }

    private void FinishSelection()
    {
        StopAutoScroll();
        if (!HasSelection) { ClearSelection(); return; }
        ShowCopyPill();
    }

    // Rects (+ alças) da seleção que recaem na página dada, ou null se a página está fora do
    // intervalo / sem texto. Chamado pelo realce ao vivo E pelo adapter no bind (páginas que entram
    // por scroll). Cada borda recebe sua alça; páginas intermediárias são totalmente selecionadas.
    internal PdfPageSel? GetPageSelection(int page)
    {
        var doc = _doc;
        if (doc is null || !HasSelection) return null;
        var (sp, sc, ep, ec) = NormalizeSel();
        if (page < sp || page > ep) return null;

        int cnt = doc.CharCount(page);
        if (cnt <= 0) return null;
        int from = page == sp ? sc : 0;
        int to   = page == ep ? ec : cnt - 1;
        from = Math.Max(0, from);
        to   = Math.Min(cnt - 1, to);
        if (to < from) return null;

        var (rects, _) = doc.GetSelection(page, from, to);
        if (rects.Count == 0) return null;
        (double x, double y)? startPt = page == sp ? (rects[0].l, rects[0].b) : null;
        (double x, double y)? endPt   = page == ep ? (rects[rects.Count - 1].r, rects[rects.Count - 1].b) : null;
        return new PdfPageSel(rects, startPt, endPt);
    }

    // Aplica o realce ao vivo em TODAS as páginas visíveis (cada uma sua parte) e atualiza a escala
    // das alças. As páginas não visíveis são cobertas pelo provider do adapter no rebind.
    private void RefreshSelectionHighlight()
    {
        var pv = PlatformView; var ad = _adapter;
        if (pv is null || ad is null) return;
        ad.SelContentScale = pv.CurrentZoom;
        if (pv.Rv.GetLayoutManager() is not LinearLayoutManager lm) return;
        int first = lm.FindFirstVisibleItemPosition();
        int last  = lm.FindLastVisibleItemPosition();
        if (first < 0) return;
        for (int p = first; p <= last; p++)
        {
            if (pv.Rv.FindViewHolderForAdapterPosition(p) is not PdfVH vh) continue;
            var sel = GetPageSelection(p);
            vh.Iv.SelectionRects = sel?.Rects;
            vh.Iv.HandleStartPt  = sel?.StartPt;
            vh.Iv.HandleEndPt    = sel?.EndPt;
            vh.Iv.ContentScale   = pv.CurrentZoom;
            vh.Iv.Invalidate();
        }
    }

    // Mantém as alças com tamanho FIXO na tela quando o zoom muda (raio é compensado dividindo
    // pelo zoom — ver PdfPageImageView.HandleRadiusPx).
    private void UpdateSelectionHandleScale()
    {
        if (HasSelection) RefreshSelectionHighlight();
    }

    private void ClearSelection()
    {
        StopAutoScroll();
        _anchorPage = _anchorChar = _focusPage = _focusChar = -1;
        RefreshSelectionHighlight();   // com HasSelection=false, limpa o realce das páginas visíveis
        HideCopyPill();
    }

    // ── Alças de ajuste (handles) ──────────────────────────────────────────────────

    // Hit-test de toque (coords do container) sobre as alças: 0=nenhuma, 1=início, 2=fim.
    // A alça de início vive na página inicial; a de fim, na página final (podem ser visíveis ou não).
    private int HandleHitTest(float cx, float cy)
    {
        var pv = PlatformView;
        if (pv is null || !HasSelection) return 0;
        var (sp, _, ep, _) = NormalizeSel();
        float pad = 16f * (Context?.Resources?.DisplayMetrics?.Density ?? 2f) / pv.CurrentZoom;
        var (rx, ry) = pv.ToContent(cx, cy);

        int best = 0; float bestDist = float.MaxValue;
        TestHandle(sp, isStart: true);
        TestHandle(ep, isStart: false);
        return best;

        void TestHandle(int page, bool isStart)
        {
            if (pv.Rv.FindViewHolderForAdapterPosition(page) is not PdfVH vh) return;
            var sel = GetPageSelection(page);
            var pt  = isStart ? sel?.StartPt : sel?.EndPt;
            var p   = vh.Iv.HandlePixel(pt);
            if (p is null) return;
            var child = vh.ItemView!;
            float rad = vh.Iv.HandleRadiusPx;
            float tol = rad + pad;
            float hx  = child.Left + p.X + (isStart ? -rad : rad);
            float hy  = child.Top  + p.Y + rad;
            float d   = (float)Math.Sqrt((rx - hx) * (rx - hx) + (ry - hy) * (ry - hy));
            if (d <= tol && d < bestDist) { bestDist = d; best = isStart ? 1 : 2; }
        }
    }

    // Início do arraste de uma alça: fixa a extremidade OPOSTA como âncora e move o foco.
    private void BeginHandleDrag(int edge)
    {
        if (!HasSelection) return;
        var (sp, sc, ep, ec) = NormalizeSel();
        if (edge == 1) { _anchorPage = ep; _anchorChar = ec; _focusPage = sp; _focusChar = sc; } // arrasta INÍCIO
        else           { _anchorPage = sp; _anchorChar = sc; _focusPage = ep; _focusChar = ec; } // arrasta FIM
        HideCopyPill();
    }

    // Toque simples: se há seleção, limpa-a (exceto sobre uma alça); senão, tenta ativar um link.
    private void HandleTap(float cx, float cy)
    {
        if (HasSelection)
        {
            if (HandleHitTest(cx, cy) != 0) return;   // tocou alça → não dispensa
            ClearSelection();
            return;
        }
        TryActivateLinkAt(cx, cy);
    }

    // Detecta um link sob o toque e, salvo intercepção (LinkTapped.Handled), executa a ação padrão:
    // link interno → navega à página; URI externa → abre no app padrão do SO.
    private void TryActivateLinkAt(float cx, float cy)
    {
        var doc = _doc;
        if (doc is null || VirtualView is null) return;
        if (!MapToPageUnder(cx, cy, out int page, out double xPt, out double yPt)) return;

        var (uri, destPage) = doc.LinkAtPagePoint(page, xPt, yPt);
        if (uri is null && destPage < 0) return;   // sem link

        var args = VirtualView.RaiseLinkTapped(uri, destPage);
        if (args.Handled) return;

        if (destPage >= 0)
            VirtualView.CurrentPage = destPage;     // link interno → navega
        else if (!string.IsNullOrEmpty(uri))
            _ = Microsoft.Maui.ApplicationModel.Launcher.Default.TryOpenAsync(uri);   // URI externa
    }

    // Concatena o texto da seleção em todas as páginas do intervalo (uma quebra de linha por página).
    private string ComputeSelectedText()
    {
        var doc = _doc;
        if (doc is null || !HasSelection) return string.Empty;
        var (sp, sc, ep, ec) = NormalizeSel();
        var sb = new System.Text.StringBuilder();
        for (int p = sp; p <= ep; p++)
        {
            int cnt = doc.CharCount(p);
            if (cnt <= 0) continue;
            int from = Math.Max(0, p == sp ? sc : 0);
            int to   = Math.Min(cnt - 1, p == ep ? ec : cnt - 1);
            if (to < from) continue;
            var (_, text) = doc.GetSelection(p, from, to);
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }
        return sb.ToString();
    }

    // ── Mapeamento toque → página/ponto PDF ────────────────────────────────────────

    // Ponto do container → página SOB o ponto + coords em pontos PDF.
    private bool MapToPageUnder(float cx, float cy, out int page, out double xPt, out double yPt)
    {
        page = -1; xPt = yPt = 0;
        var pv = PlatformView;
        if (pv is null) return false;
        var (rx, ry) = pv.ToContent(cx, cy);
        var child = pv.Rv.FindChildViewUnder(rx, ry);
        if (child is null) return false;
        page = pv.Rv.GetChildAdapterPosition(child);
        if (page < 0) return false;
        return LocalToPdf(child, page, rx, ry, clamp: false, out xPt, out yPt);
    }

    // Retângulo (offset + dimensões) onde a folha é desenhada pelo FitCenter dentro de uma célula
    // w×h, dada a proporção da página (wPt:hPt): a folha mantém a proporção e fica centralizada;
    // o entorno é faixa de fundo. Fonte única usada pelo desenho dos realces, pelo mapeamento do
    // toque e pela posição da pílula. No vertical (célula com a proporção da página) → offset 0,
    // dimensões = célula inteira (compatível com o comportamento anterior).
    internal static (float ox, float oy, float dw, float dh) FitBox(float w, float h, double wPt, double hPt)
    {
        if (w <= 0 || h <= 0 || wPt <= 0 || hPt <= 0) return (0f, 0f, w, h);
        float pageAspect = (float)(wPt / hPt);
        float viewAspect = w / h;
        float dw, dh;
        if (pageAspect > viewAspect) { dw = w; dh = w / pageAspect; }   // limitado pela LARGURA
        else                         { dh = h; dw = h * pageAspect; }   // limitado pela ALTURA
        return ((w - dw) / 2f, (h - dh) / 2f, dw, dh);
    }

    // Coords do conteúdo do RV → pontos PDF (origem inferior-esquerda) dentro da célula.
    private bool LocalToPdf(AView child, int page, float rx, float ry, bool clamp, out double xPt, out double yPt)
    {
        xPt = yPt = 0;
        var doc = _doc;
        if (doc is null) return false;
        float w = child.Width, h = child.Height;
        if (w <= 0 || h <= 0) return false;
        var (wPt, hPt) = doc.PageSizePt(page);
        if (wPt <= 0 || hPt <= 0) return false;

        // A folha é desenhada por FitCenter dentro da célula (w×h): no horizontal ela não preenche
        // a célula, ficando centralizada com faixa de fundo. Mapeia o toque para o retângulo real.
        var (ox, oy, dw, dh) = FitBox(w, h, wPt, hPt);
        float lx = rx - child.Left - ox, ly = ry - child.Top - oy;
        if (clamp) { lx = Math.Clamp(lx, 0, dw); ly = Math.Clamp(ly, 0, dh); }
        xPt = lx / dw * wPt;
        yPt = (1 - ly / dh) * hPt;   // tela cresce p/ baixo; PDF p/ cima
        return true;
    }

    // ── Pílula "Copiar" ────────────────────────────────────────────────────────────

    private void EnsureCopyPill()
    {
        if (_copyPill is not null || PlatformView is null || Context is null) return;
        var tv = new global::Android.Widget.TextView(Context)
        {
            Text = string.IsNullOrEmpty(VirtualView?.CopyButtonText) ? "Copy" : VirtualView!.CopyButtonText,
        };
        tv.SetTextColor(global::Android.Graphics.Color.White);
        tv.TextSize = 14;
        int padH = (int)Context.ToPixels(20), padV = (int)Context.ToPixels(10);
        tv.SetPadding(padH, padV, padH, padV);
        var bg = new global::Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(global::Android.Graphics.Color.Rgb(0x32, 0x32, 0x32));
        bg.SetCornerRadius(Context.ToPixels(22));
        tv.Background  = bg;
        tv.Elevation   = Context.ToPixels(6);
        tv.Clickable   = true;
        tv.Click      += (_, _) => CopySelection();
        tv.LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        tv.Visibility = ViewStates.Gone;
        PlatformView.AddView(tv);
        _copyPill = tv;
    }

    // Posiciona a pílula:
    //  • Seleção de UMA linha (mesma página, 1 rect) → ACIMA da seleção (não cobre o conteúdo
    //    abaixo, como no padrão do Android).
    //  • Caso contrário → no FIM (abaixo do último rect da página final), acompanhando onde termina.
    // Se a extremidade preferida saiu da tela, cai na outra; esconde se nenhuma está visível.
    private void ShowCopyPill()
    {
        if (!HasSelection) { HideCopyPill(); return; }
        var (sp, _, ep, _) = NormalizeSel();

        if (sp == ep)
        {
            var sel = GetPageSelection(sp);
            if (sel is not null && sel.Rects.Count <= 1)   // uma única linha
            {
                if (TryPositionCopyPill(sp, atTop: true))  return;   // acima
                if (TryPositionCopyPill(sp, atTop: false)) return;   // fallback: abaixo
                HideCopyPill();
                return;
            }
        }

        if (TryPositionCopyPill(ep, atTop: false)) return;   // fim da seleção (acompanha)
        if (TryPositionCopyPill(sp, atTop: true))  return;   // fallback: início
        HideCopyPill();
    }

    // Posiciona a pílula ancorada no topo (atTop) ou na base da seleção daquela página. Retorna
    // false se a página não está visível ou o ponto âncora caiu fora da viewport.
    private bool TryPositionCopyPill(int page, bool atTop)
    {
        var pv = PlatformView; var doc = _doc;
        if (pv is null || doc is null) return false;
        if (pv.Rv.FindViewHolderForAdapterPosition(page) is not PdfVH vh) return false;
        var sel = GetPageSelection(page);
        if (sel is null || sel.Rects.Count == 0) return false;

        var child = vh.ItemView!;
        var (wPt, hPt) = doc.PageSizePt(page);
        var r = atTop ? sel.Rects[0] : sel.Rects[sel.Rects.Count - 1];
        // Posição da folha na célula (FitCenter) — alinha a pílula ao texto também no horizontal.
        var (ox, oy, dw, dh) = FitBox(child.Width, child.Height, wPt, hPt);
        float cxPx = ox + (float)((r.l + r.r) / 2.0 / wPt * dw);
        double yPt = atTop ? r.t : r.b;                       // topo do 1º rect / base do último
        float yPx  = oy + (float)((1 - yPt / hPt) * dh);
        var (containerX, containerY) = pv.ToContainer(child.Left + cxPx, child.Top + yPx);
        if (containerY < 0 || containerY > pv.Height) return false;

        EnsureCopyPill();
        if (_copyPill is null) return false;
        _copyPill.Text = string.IsNullOrEmpty(VirtualView?.CopyButtonText) ? "Copy" : VirtualView!.CopyButtonText;
        _copyPill.Measure(
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified),
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        int pw = _copyPill.MeasuredWidth, ph = _copyPill.MeasuredHeight;
        int gap = (int)Context!.ToPixels(8);

        int left = (int)(containerX - pw / 2f);
        int top  = atTop ? (int)(containerY - ph - gap) : (int)(containerY + gap);
        if (top < 0)               top = (int)(containerY + gap);   // não coube acima → abaixo
        if (top + ph > pv.Height)  top = (int)(containerY - ph - gap);
        left = Math.Clamp(left, 0, Math.Max(0, pv.Width - pw));
        top  = Math.Clamp(top,  0, Math.Max(0, pv.Height - ph));

        if (_copyPill.LayoutParameters is global::Android.Widget.FrameLayout.LayoutParams lp)
        {
            lp.LeftMargin = left; lp.TopMargin = top;
            _copyPill.LayoutParameters = lp;
        }
        _copyPill.Visibility = ViewStates.Visible;
        _copyPill.BringToFront();
        return true;
    }

    private void HideCopyPill()
    {
        if (_copyPill is not null) _copyPill.Visibility = ViewStates.Gone;
    }

    // CopyButtonText mudou: se a pílula está visível, reposiciona com o novo texto (re-mede a largura).
    private void ApplyCopyButtonText()
    {
        if (_copyPill is null || _copyPill.Visibility != ViewStates.Visible) return;
        ShowCopyPill();
    }

    private void CopySelection()
    {
        var text = ComputeSelectedText();
        if (!string.IsNullOrEmpty(text))
            _ = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
        ClearSelection();
        var copied = string.IsNullOrEmpty(VirtualView?.CopiedMessageText) ? "Copied" : VirtualView!.CopiedMessageText;
        try { global::Android.Widget.Toast.MakeText(Context, copied, global::Android.Widget.ToastLength.Short)?.Show(); }
        catch { }
    }

    private void ApplyCache()
    {
        if (VirtualView is null) return;
        long maxBytes = (long)VirtualView.MaxCacheMB * 1024 * 1024;

        if (_cache is null)
        {
            // Primeira criação (ConnectHandler) — cache vazio.
            _cache = new PdfBitmapLruCache(maxBytes);
            _lastPrefetchCenter = -1;
            return;
        }

        // MaxCacheMB mudou: apenas ajusta o limite do cache EXISTENTE, evictando o excedente
        // por LRU. Mantém os bitmaps válidos já renderizados (não descarta um cache bom).
        // Não recriamos o cache → tasks em voo continuam escrevendo no cache correto
        // (a checagem ReferenceEquals em RequestRender só abortaria se trocássemos a instância).
        _cache.SetMaxBytes(maxBytes);
        _adapter?.UpdateCache(_cache);
    }

    private void ReRenderAll()
    {
        if (PlatformView is null) return;
        _cache?.EvictAll();
        _lastPrefetchCenter = -1;  // cache vazio → permite re-render do mesmo centro
        var lm  = PlatformView.Rv.GetLayoutManager() as LinearLayoutManager;
        int pos = lm?.FindFirstVisibleItemPosition() ?? 0;
        if (pos < 0) pos = 0;
        TrimAndPrefetch(pos);
    }

    private void ApplyPageBackground()
    {
        if (PlatformView is null || VirtualView is null) return;
        // O deck permanece cinza de leitor; a cor da página é aplicada às células (folhas).
        _adapter?.SetPageColor(VirtualView.PageBackgroundColor.ToPlatform());
        ReRenderAll();
    }

    private void ApplySpacing()
    {
        if (PlatformView is null || VirtualView is null) return;
        if (_spacingDecoration is not null)
        {
            PlatformView.Rv.RemoveItemDecoration(_spacingDecoration);
            _spacingDecoration = null;
        }
        int spacePx = (int)Context.ToPixels(VirtualView.PageSpacing);
        if (spacePx <= 0) return;
        _spacingDecoration = new PdfSpacingDecoration(spacePx, PlatformView.Horizontal);
        PlatformView.Rv.AddItemDecoration(_spacingDecoration);
    }

    // Cria o adapter com as dimensões atuais da viewport e a orientação do container. O adapter
    // dimensiona as células conforme o eixo (ver PageMainSizePx): vertical = preenche a largura
    // (scroll contínuo), horizontal = página inteira centralizada por tela (fit-page, fundo cinza).
    private PdfAdapter BuildAdapter(int count, SizeF[] pageSizes)
    {
        int vw = PlatformView!.Rv.Width > 0
            ? PlatformView.Rv.Width
            : Context?.Resources?.DisplayMetrics?.WidthPixels ?? 1080;
        int vh = PlatformView.Rv.Height > 0
            ? PlatformView.Rv.Height
            : Context?.Resources?.DisplayMetrics?.HeightPixels ?? 1920;
        var bg = (VirtualView?.PageBackgroundColor ?? Colors.White).ToPlatform();
        return new PdfAdapter(count, _cache!, pageSizes, vw, vh, PlatformView.Horizontal, bg, RequestRender)
        {
            SelectionProvider = GetPageSelection,   // realce de seleção por página no bind
        };
    }

    // Troca a direção do scroll (vertical ⇄ horizontal). Recria o LayoutManager E o adapter
    // (que dimensiona as células conforme o eixo) e restaura a página atual após o re-layout.
    private void ApplyOrientation()
    {
        if (PlatformView is null || VirtualView is null) return;

        // Volta ao zoom MÍNIMO ANTES de trocar o eixo (ainda na orientação atual, dimensões
        // válidas) — sem isto a folha herda a escala/pan da orientação anterior (zoom à direita).
        ResetZoomToMin();

        bool horizontal = VirtualView.ScrollOrientation == PdfScrollOrientation.Horizontal;
        if (!PlatformView.SetHorizontal(horizontal)) return;   // já estava nessa orientação
        if (_doc is null || _pageSizes is null) return;        // sem documento → basta o novo LayoutManager

        int page = VirtualView.CurrentPage;
        PlatformView.Rv.SetAdapter(null);
        _adapter?.Dispose();
        _adapter = BuildAdapter(_doc.PageCount, _pageSizes);
        ApplySpacing();                 // o espaçamento depende do eixo → reaplica
        PlatformView.Rv.SetAdapter(_adapter);
        _lastPrefetchCenter = -1;       // cache da janela muda de eixo → permite re-prefetch

        // A troca de LayoutManager reseta a posição de scroll; restaura a página no próximo frame.
        PlatformView.Rv.Post(() =>
        {
            if (PlatformView is null || _doc is null) return;
            ResetZoomToMin();           // reforça no mínimo após o re-layout (pivô com dimensões finais)
            if (PlatformView.Rv.GetLayoutManager() is LinearLayoutManager lm)
                lm.ScrollToPositionWithOffset(Math.Clamp(page, 0, Math.Max(0, _doc.PageCount - 1)), 0);
            TrimAndPrefetch(page);
        });
    }

    private void ApplyZoomEnabled()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ZoomEnabled = VirtualView.IsPinchZoomEnabled;
    }

    // ── Barra de miniaturas (drawer sobreposto, lado direito) ──────────────────────
    // Overlay = scrim (escurece o PDF, fecha ao tocar) + painel RecyclerView de miniaturas que
    // desliza da direita. Fechar via toque no scrim/clique numa página devolve IsThumbnailBarOpen
    // para false (binding TwoWay sincroniza o botão do app).

    // Lado do drawer (true = direita, padrão).
    private bool ThumbRight => (VirtualView?.ThumbnailBarPlacement ?? PdfThumbnailPlacement.Right) == PdfThumbnailPlacement.Right;

    private void ApplyThumbnailBar()
    {
        if (PlatformView is null || VirtualView is null) return;
        // None desabilita o drawer; só abre quando habilitado (Left/Right) E solicitado.
        bool enabled = VirtualView.ThumbnailBarPlacement != PdfThumbnailPlacement.None;
        if (enabled && VirtualView.IsThumbnailBarOpen) OpenThumbBar();
        else CloseThumbBar();
    }

    // Lado/None mudou: descarta o overlay (o lado é definido na construção) e reconcilia o estado.
    private void ApplyThumbPlacement()
    {
        if (_thumbOverlay is not null)
        {
            _thumbRv?.SetAdapter(null);
            _thumbAdapter?.Dispose(); _thumbAdapter = null;
            if (PlatformView is not null) { PlatformView.RemoveView(_thumbOverlay); PlatformView.ThumbOverlay = null; }
            _thumbOverlay = null; _thumbScrim = null; _thumbRv = null;
            _thumbOpen = false;
        }
        ApplyThumbnailBar();   // reabre no novo lado se habilitado (Left/Right) e solicitado; None fecha
    }

    private void BuildThumbOverlay()
    {
        if (_thumbOverlay is not null || PlatformView is null || Context is null) return;
        var ctx = Context;

        var overlay = new global::Android.Widget.FrameLayout(ctx) { Visibility = ViewStates.Gone };

        var scrim = new global::Android.Widget.FrameLayout(ctx) { Clickable = true, Alpha = 0f };
        scrim.SetBackgroundColor(global::Android.Graphics.Color.Argb(0x66, 0, 0, 0));
        scrim.LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        scrim.Click += (_, _) => { if (VirtualView is not null) VirtualView.IsThumbnailBarOpen = false; };

        int panelW = (int)ctx.ToPixels(140);
        var rv = new AndroidX.RecyclerView.Widget.RecyclerView(ctx);
        rv.SetLayoutManager(new LinearLayoutManager(ctx, LinearLayoutManager.Vertical, false));
        rv.SetBackgroundColor(global::Android.Graphics.Color.Rgb(0xF5, 0xF5, 0xF7));
        rv.SetClipToPadding(false);
        int vpad = (int)ctx.ToPixels(8);
        rv.SetPadding(0, vpad, 0, vpad);
        bool right = ThumbRight;
        rv.LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
            panelW, ViewGroup.LayoutParams.MatchParent)
            { Gravity = right ? global::Android.Views.GravityFlags.Right : global::Android.Views.GravityFlags.Left };
        rv.TranslationX = right ? panelW : -panelW;   // começa fora da tela (lado escolhido)
        // Sombra/elevação para destacar o painel sobre o PDF.
        rv.Elevation = ctx.ToPixels(8);

        var adapter = new PdfThumbAdapter(BindThumb, OnThumbClicked);
        rv.SetAdapter(adapter);

        overlay.AddView(scrim);
        overlay.AddView(rv);
        PlatformView.AddView(overlay);
        PlatformView.ThumbOverlay = overlay;

        _thumbOverlay = overlay; _thumbScrim = scrim; _thumbRv = rv; _thumbAdapter = adapter;
    }

    private void OpenThumbBar()
    {
        if (_doc is null) return;
        BuildThumbOverlay();
        if (_thumbOverlay is null || _thumbRv is null || _thumbAdapter is null || _thumbScrim is null) return;
        if (_thumbOpen) return;
        _thumbOpen = true;

        int current = Math.Clamp(VirtualView?.CurrentPage ?? 0, 0, Math.Max(0, _doc.PageCount - 1));
        _thumbAdapter.Configure(_doc.PageCount, current);

        _thumbOverlay.Visibility = ViewStates.Visible;
        _thumbOverlay.BringToFront();

        if (_thumbRv.GetLayoutManager() is LinearLayoutManager lm)
            lm.ScrollToPositionWithOffset(current, (int)(Context!.ToPixels(120)));

        _thumbScrim.Animate()?.Alpha(1f)?.SetDuration(180)?.Start();
        _thumbRv.Animate()?.TranslationX(0f)?.SetDuration(220)?.Start();
    }

    private void CloseThumbBar()
    {
        if (!_thumbOpen || _thumbOverlay is null || _thumbRv is null) return;
        _thumbOpen = false;
        float panelW = _thumbRv.Width > 0 ? _thumbRv.Width : Context!.ToPixels(140);
        float off    = ThumbRight ? panelW : -panelW;
        _thumbScrim?.Animate()?.Alpha(0f)?.SetDuration(160)?.Start();
        var overlay = _thumbOverlay;
        _thumbRv.Animate()?.TranslationX(off)?.SetDuration(200)?
            .WithEndAction(new Java.Lang.Runnable(() =>
            {
                if (!_thumbOpen && overlay is not null) overlay.Visibility = ViewStates.Gone;
            }))?.Start();
    }

    private void OnThumbClicked(int idx)
    {
        if (VirtualView is null) return;
        VirtualView.CurrentPage = idx;
        VirtualView.IsThumbnailBarOpen = false;
    }

    // Renderiza/atribui a miniatura da página idx ao ViewHolder (com cache de bitmaps pequenos).
    private void BindThumb(int idx, PdfThumbVH vh)
    {
        vh.Bound = idx;
        if (_thumbCache.TryGetValue(idx, out var cached) && cached is not null && !cached.IsRecycled)
        {
            vh.Iv.SetImageBitmap(cached);
            return;
        }
        vh.Iv.SetImageBitmap(null);

        var doc = _doc;
        if (doc is null || Context is null) return;
        int w = (int)Context.ToPixels(116);
        var bg = global::Android.Graphics.Color.White;
        CancellationToken tok;
        try { tok = _shutdownCts.Token; } catch (ObjectDisposedException) { return; }

        _ = Task.Run(async () =>
        {
            try
            {
                var bmp = await doc.RenderAndroidBitmapAsync(idx, w, bg, tok);
                if (bmp is null || tok.IsCancellationRequested) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (tok.IsCancellationRequested) return;
                    _thumbCache[idx] = bmp;
                    if (vh.Bound == idx) vh.Iv.SetImageBitmap(bmp);   // ainda visível → aplica; senão, fica no cache p/ o próximo bind
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { PdfViewerLog.Write(Tag, $"Thumb {idx}: {ex.Message}"); }
        }, tok);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfContainerView — FrameLayout + RecyclerView + pinch/double-tap zoom
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfContainerView : global::Android.Widget.FrameLayout,
    ScaleGestureDetector.IOnScaleGestureListener
{
    internal readonly ClippedRecyclerView Rv;
    /// <summary>Overlay da barra de miniaturas (drawer). Quando presente e visível, é layoutado em tela cheia.</summary>
    internal AView? ThumbOverlay;

    // Cinza de leitor do "deck" (atrás das páginas). O espaçamento entre folhas mostra esta cor.
    internal static readonly global::Android.Graphics.Color ReaderBg =
        global::Android.Graphics.Color.Rgb(0xC2, 0xC6, 0xC9);

    private readonly ScaleGestureDetector     _sgd;
    private readonly GestureDetector          _gd;
    internal         float                    _currentZoom = 1f;
    internal         float                    _gestureZoom = 1f;
    private          CancellationTokenSource? _commitCts;
    // Snap "tipo livro" (horizontal): cada fling avança UMA página e trava nela. Null no vertical.
    private          PagerSnapHelper?         _pagerSnap;

    public float MinZoom     { get; set; } = 0.9f;
    public float MaxZoom     { get; set; } = 8f;
    public bool  ZoomEnabled { get; set; } = true;

    /// <summary>Zoom atual aplicado ao RecyclerView (ScaleX). Usado p/ manter as alças com tamanho fixo.</summary>
    public float CurrentZoom => _currentZoom <= 0 ? 1f : _currentZoom;

    /// <summary>Orientação do scroll: false = vertical (padrão), true = horizontal (lado a lado).</summary>
    public bool Horizontal { get; private set; }

    /// <summary>Troca a orientação do RecyclerView. Retorna true se a orientação mudou de fato.</summary>
    public bool SetHorizontal(bool horizontal)
    {
        if (Horizontal == horizontal && Rv.GetLayoutManager() is not null) return false;
        Horizontal = horizontal;
        Rv.SetLayoutManager(new LinearLayoutManager(Context,
            horizontal ? LinearLayoutManager.Horizontal : LinearLayoutManager.Vertical, false));
        Rv.TranslationX = 0; Rv.TranslationY = 0;

        // Horizontal: paginação "tipo livro" — o PagerSnapHelper limita cada fling a uma página e
        // alinha a página na viewport. Vertical: sem snap (scroll contínuo).
        _pagerSnap?.AttachToRecyclerView(null);
        _pagerSnap = null;
        if (horizontal)
        {
            _pagerSnap = new PagerSnapHelper();
            _pagerSnap.AttachToRecyclerView(Rv);
        }
        return true;
    }

    public Action<int>?   OnPageChanged { get; set; }
    public Action<float>? OnZoomChanged { get; set; }
    public Action<int>?   OnScrollIdle  { get; set; }

    // ── Seleção de texto ──────────────────────────────────────────────────────────
    // Long-press inicia (OnSelectionStart retorna true se acertou texto), arraste estende, soltar
    // finaliza; toque simples limpa. Enquanto Selecting, o container intercepta o toque (o
    // RecyclerView não rola e o pan é suspenso). Coordenadas em px do CONTAINER.
    internal bool                  Selecting;
    public Func<float, float, bool>? OnSelectionStart      { get; set; }
    public Action<float, float>?     OnSelectionDrag       { get; set; }
    public Action?                   OnSelectionEnd        { get; set; }
    public Action<float, float>?     OnSelectionTapClear   { get; set; }
    public Func<float, float, int>?  OnSelectionHandleHit  { get; set; }   // 0=nenhuma,1=início,2=fim
    public Action<int>?              OnSelectionHandleDown { get; set; }

    internal void HandleLongPress(float x, float y)
    {
        if (Selecting) return;   // já ajustando uma alça → não inicia nova seleção
        if (OnSelectionStart?.Invoke(x, y) == true)
        {
            Selecting = true;
            Parent?.RequestDisallowInterceptTouchEvent(true);
        }
    }

    internal void HandleSingleTap(float x, float y) => OnSelectionTapClear?.Invoke(x, y);

    // Converte um ponto do CONTAINER → coordenadas do CONTEÚDO do RecyclerView (desfaz o
    // zoom: ScaleX/Y + pivô + TranslationX). Inverso de ToContainer.
    public (float x, float y) ToContent(float cx, float cy)
    {
        float s = _currentZoom <= 0 ? 1f : _currentZoom;
        float rx = Rv.PivotX + (cx - Rv.TranslationX - Rv.PivotX) / s;
        float ry = Rv.PivotY + (cy - Rv.TranslationY - Rv.PivotY) / s;
        return (rx, ry);
    }

    // Conteúdo do RecyclerView → ponto do CONTAINER (aplica o zoom). Usado para posicionar a pílula.
    public (float x, float y) ToContainer(float rx, float ry)
    {
        float s = _currentZoom <= 0 ? 1f : _currentZoom;
        float cx = Rv.PivotX + (rx - Rv.PivotX) * s + Rv.TranslationX;
        float cy = Rv.PivotY + (ry - Rv.PivotY) * s + Rv.TranslationY;
        return (cx, cy);
    }

    protected PdfContainerView(IntPtr h, JniHandleOwnership t) : base(h, t) { Rv = null!; _sgd = null!; _gd = null!; }

    public PdfContainerView(Context ctx) : base(ctx)
    {
        OutlineProvider = ViewOutlineProvider.Bounds;
        ClipToOutline   = true;

        _sgd = new ScaleGestureDetector(ctx, this);
        _gd  = new GestureDetector(ctx, new PdfDoubleTapListener(this));

        Rv = new ClippedRecyclerView(ctx);
        Rv.SetLayoutManager(new LinearLayoutManager(ctx, LinearLayoutManager.Vertical, false));
        Rv.SetItemAnimator(null);
        Rv.NestedScrollingEnabled = false;
        Rv.SetClipChildren(true);
        Rv.SetClipToPadding(true);
        Rv.AddOnScrollListener(new PdfScrollListener(this));

        // Fundo cinza de leitor (estilo Edge/Adobe). As células (páginas) são pintadas com a
        // cor da página (branco) pelo adapter, então o espaçamento entre páginas e o overscroll
        // aparecem em cinza — separando visualmente as folhas — em vez de preto ou tudo branco
        // (que esconde o espaçamento). Sem isso, o gap entre páginas fica invisível.
        SetBackgroundColor(ReaderBg);
        Rv.SetBackgroundColor(ReaderBg);

        AddView(Rv, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));
    }

    /// <summary>
    /// Mantém o "deck" (container + RecyclerView) no cinza de leitor. A cor da página é aplicada
    /// às células pelo adapter, não ao deck — assim o espaçamento entre páginas permanece visível.
    /// </summary>
    public void ResetReaderBackground()
    {
        SetBackgroundColor(ReaderBg);
        Rv.SetBackgroundColor(ReaderBg);
    }

    protected override void DispatchDraw(global::Android.Graphics.Canvas? canvas)
    {
        if (canvas is null) { base.DispatchDraw(canvas); return; }
        int save = canvas.Save();
        canvas.ClipRect(0, 0, Width, Height);
        base.DispatchDraw(canvas);
        canvas.RestoreToCount(save);
    }

    public void SetZoom(float zoom)
    {
        _currentZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _gestureZoom = _currentZoom;
        Rv.ScaleX    = _currentZoom;
        Rv.ScaleY    = _currentZoom;
        Rv.PivotX    = Width  / 2f;
        Rv.PivotY    = Height / 2f;
        ClampPan();
    }

    // ── Pan horizontal quando ampliado ─────────────────────────────────────────
    // O RecyclerView só rola na vertical; ao ampliar via ScaleX, as laterais saem da
    // viewport e ficam inacessíveis. TranslationX desloca o conteúdo na horizontal para
    // revelá-las. O scroll vertical continua a cargo do RecyclerView (ScaleY).

    /// <summary>
    /// Limites do pan no eixo CRUZADO ao scroll para o zoom/pivô atuais. Vertical → pan horizontal
    /// (TranslationX); horizontal → pan vertical (TranslationY). O eixo de scroll fica com o RV.
    /// </summary>
    private void ClampPan()
    {
        if (_currentZoom <= 1.05f) { Rv.TranslationX = 0; Rv.TranslationY = 0; return; }
        if (!Horizontal)
        {
            float px = Rv.PivotX;
            float minTx = -(_currentZoom - 1f) * (Width - px);   // revela a borda direita
            float maxTx =  (_currentZoom - 1f) * px;             // revela a borda esquerda
            Rv.TranslationX = Math.Clamp(Rv.TranslationX, minTx, maxTx);
            Rv.TranslationY = 0;
        }
        else
        {
            float py = Rv.PivotY;
            float minTy = -(_currentZoom - 1f) * (Height - py);  // revela a borda inferior
            float maxTy =  (_currentZoom - 1f) * py;             // revela a borda superior
            Rv.TranslationY = Math.Clamp(Rv.TranslationY, minTy, maxTy);
            Rv.TranslationX = 0;
        }
    }

    /// <summary>Arrasta a página no eixo cruzado (gesto de 1 dedo quando ampliado). Retorna true se ampliada.</summary>
    public bool PanCross(float distanceX, float distanceY)
    {
        if (_currentZoom <= 1.05f)
        {
            if (Rv.TranslationX != 0) Rv.TranslationX = 0;
            if (Rv.TranslationY != 0) Rv.TranslationY = 0;
            return false;
        }
        if (!Horizontal) Rv.TranslationX -= distanceX;   // distanceX>0 = dedo p/ esquerda → conteúdo acompanha
        else             Rv.TranslationY -= distanceY;
        ClampPan();
        return true;
    }

    protected override void OnLayout(bool changed, int l, int t, int r, int b)
    {
        int w = r - l, h = b - t;
        // Mede o RecyclerView com a dimensão EXATA do layout antes de posicioná-lo. Como este
        // OnLayout não chama base, sem isto o RV ficaria com a altura MEDIDA pelo FrameLayout, que
        // pode divergir da altura de LAYOUT em transições (ex.: carga lenta com overlay). Nessa
        // divergência, a célula MatchParent ficaria mais curta que a viewport → a 1ª página
        // apareceria colada no topo (cinza embaixo) em vez de centralizada.
        Rv.Measure(
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(w, MeasureSpecMode.Exactly),
            global::Android.Views.View.MeasureSpec.MakeMeasureSpec(h, MeasureSpecMode.Exactly));
        Rv.Layout(0, 0, w, h);

        // Overlay da barra de miniaturas: ocupa a tela cheia (scrim + painel). É um FrameLayout
        // padrão, então seus filhos (scrim MatchParent, painel com Gravity=Right) são posicionados
        // pelo próprio layout dele — basta medi-lo/posicioná-lo em tela cheia aqui.
        if (ThumbOverlay is not null && ThumbOverlay.Visibility != ViewStates.Gone)
        {
            ThumbOverlay.Measure(
                global::Android.Views.View.MeasureSpec.MakeMeasureSpec(w, MeasureSpecMode.Exactly),
                global::Android.Views.View.MeasureSpec.MakeMeasureSpec(h, MeasureSpecMode.Exactly));
            ThumbOverlay.Layout(0, 0, w, h);
        }

        // Filhos extras (ex.: a pílula "Copiar") são posicionados pelas suas margens
        // (FrameLayout.LayoutParams). Sem isto eles ficariam em 0×0 (invisíveis), pois este
        // OnLayout não chama base — só layouta o RecyclerView.
        for (int i = 0; i < ChildCount; i++)
        {
            var c = GetChildAt(i);
            if (c is null || ReferenceEquals(c, Rv) || ReferenceEquals(c, ThumbOverlay)
                || c.Visibility == ViewStates.Gone) continue;
            int cw = c.MeasuredWidth, ch = c.MeasuredHeight;
            int cl = 0, ct = 0;
            if (c.LayoutParameters is LayoutParams flp) { cl = flp.LeftMargin; ct = flp.TopMargin; }
            c.Layout(cl, ct, cl + cw, ct + ch);
        }
    }

    public override bool OnInterceptTouchEvent(MotionEvent? ev)
    {
        if (Selecting) return true;   // durante a seleção, o container fica com o toque (sem scroll)
        // Pegar numa alça (no toque inicial) inicia o ajuste daquela extremidade.
        if (ev?.ActionMasked == MotionEventActions.Down && OnSelectionHandleHit is not null)
        {
            int edge = OnSelectionHandleHit(ev.GetX(), ev.GetY());
            if (edge != 0)
            {
                Selecting = true;
                OnSelectionHandleDown?.Invoke(edge);
                Parent?.RequestDisallowInterceptTouchEvent(true);
                return true;
            }
        }
        if (ZoomEnabled && ev?.PointerCount >= 2) return true;
        return base.OnInterceptTouchEvent(ev);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev is not null)
        {
            _gd.OnTouchEvent(ev);                  // long-press/double-tap/single-tap (sempre)
            if (ZoomEnabled) _sgd.OnTouchEvent(ev); // pinch só com zoom habilitado
        }
        return base.DispatchTouchEvent(ev);
    }

    public override bool OnTouchEvent(MotionEvent? ev)
    {
        if (Selecting && ev is not null)
        {
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Move:
                    OnSelectionDrag?.Invoke(ev.GetX(), ev.GetY());
                    return true;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                case MotionEventActions.PointerUp:
                    Selecting = false;
                    OnSelectionEnd?.Invoke();
                    return true;
            }
            return true;
        }
        if (ZoomEnabled && ev?.PointerCount >= 2) return true;
        return base.OnTouchEvent(ev);
    }

    public override void RequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
        if (!disallowIntercept) base.RequestDisallowInterceptTouchEvent(false);
    }

    public bool OnScaleBegin(ScaleGestureDetector? d)
    {
        _commitCts?.Cancel(); _commitCts = null;
        _currentZoom = Math.Clamp(Rv.ScaleX, MinZoom, MaxZoom);
        _gestureZoom = _currentZoom;
        return true;
    }

    public bool OnScale(ScaleGestureDetector? d)
    {
        if (d is null) return false;
        _gestureZoom = Math.Clamp(_gestureZoom * d.ScaleFactor, MinZoom, MaxZoom);
        Rv.ScaleX = _gestureZoom; Rv.ScaleY = _gestureZoom;
        Rv.PivotX = d.FocusX;    Rv.PivotY = d.FocusY;
        return true;
    }

    public void OnScaleEnd(ScaleGestureDetector? d)
    {
        var finalZoom = _gestureZoom;
        _commitCts?.Cancel();
        var cts = new CancellationTokenSource();
        _commitCts = cts;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(120, cts.Token); }
            catch (OperationCanceledException) { return; }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (cts.IsCancellationRequested) return;
                SetZoom(finalZoom);
                OnZoomChanged?.Invoke(_currentZoom);
            });
        }, cts.Token);
    }

    public void StartZoomAnimation(float from, float to, float fx, float fy)
    {
        _commitCts?.Cancel(); _commitCts = null;
        var anim = ValueAnimator.OfFloat(from, to)!;
        anim.SetDuration(220);
        anim.SetInterpolator(new DecelerateInterpolator());
        anim.Update += (s, _) =>
        {
            var z = (float)((ValueAnimator)s!).AnimatedValue!;
            Rv.ScaleX = z; Rv.ScaleY = z; Rv.PivotX = fx; Rv.PivotY = fy;
        };
        anim.AnimationEnd += (_, _) =>
        {
            _currentZoom = to; _gestureZoom = to;
            Rv.PivotX = to > 1.05f ? fx : Width  / 2f;
            Rv.PivotY = to > 1.05f ? fy : Height / 2f;
            ClampPan();   // ao voltar ao zoom 1, zera o pan; ampliado, mantém dentro dos limites
            OnZoomChanged?.Invoke(to);
        };
        anim.Start();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Gestures + Scroll
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfDoubleTapListener : GestureDetector.SimpleOnGestureListener
{
    private readonly PdfContainerView _o;
    protected PdfDoubleTapListener(IntPtr h, JniHandleOwnership t) : base(h, t) { _o = null!; }
    public PdfDoubleTapListener(PdfContainerView o) => _o = o;

    public override bool OnDoubleTap(MotionEvent e)
    {
        if (!_o.ZoomEnabled) return false;
        float target = _o._currentZoom > 1.05f ? _o.MinZoom : 2.5f;
        _o.StartZoomAnimation(_o._currentZoom, target, e.GetX(), e.GetY());
        return true;
    }

    // Necessário retornar true em OnDown para o detector entregar os eventos de OnScroll.
    public override bool OnDown(MotionEvent e) => true;

    // Pressionar e segurar (sem mover) → inicia a seleção de texto sob o dedo.
    public override void OnLongPress(MotionEvent e) => _o.HandleLongPress(e.GetX(), e.GetY());

    // Toque simples (confirmado, não é double-tap) → limpa a seleção ativa, se houver.
    public override bool OnSingleTapConfirmed(MotionEvent e) { _o.HandleSingleTap(e.GetX(), e.GetY()); return false; }

    // Arrasto de 1 dedo: quando ampliado, faz pan horizontal (o scroll vertical fica com o
    // RecyclerView). Com 2 dedos é pinch, tratado pelo ScaleGestureDetector — ignora aqui.
    public override bool OnScroll(MotionEvent? e1, MotionEvent? e2, float distanceX, float distanceY)
    {
        if (_o.Selecting) return true;   // o arraste estende a seleção (tratado em OnTouchEvent)
        if (!_o.ZoomEnabled) return false;
        if (e2 is not null && e2.PointerCount > 1) return false;
        return _o.PanCross(distanceX, distanceY);
    }
}

// Scroller que alinha a página de destino ao TOPO da viewport (não apenas "torná-la visível").
// Essencial para a navegação por botões prev/próxima numa lista de páginas de tela cheia.
internal sealed class SnapToStartSmoothScroller : LinearSmoothScroller
{
    protected SnapToStartSmoothScroller(IntPtr h, JniHandleOwnership t) : base(h, t) { }
    public SnapToStartSmoothScroller(Context context) : base(context) { }

    // Alinha ao início em ambos os eixos; o LinearLayoutManager usa o do seu eixo de orientação.
    protected override int VerticalSnapPreference   => SnapToStart;
    protected override int HorizontalSnapPreference => SnapToStart;
}

internal sealed class PdfScrollListener : RecyclerView.OnScrollListener
{
    private readonly PdfContainerView _o;
    protected PdfScrollListener(IntPtr h, JniHandleOwnership t) : base(h, t) { _o = null!; }
    public PdfScrollListener(PdfContainerView o) => _o = o;

    public override void OnScrolled(RecyclerView rv, int dx, int dy)
    {
        var lm  = rv.GetLayoutManager() as LinearLayoutManager;
        int pos = lm?.FindFirstVisibleItemPosition() ?? RecyclerView.NoPosition;
        if (pos != RecyclerView.NoPosition) _o.OnPageChanged?.Invoke(pos);
    }

    public override void OnScrollStateChanged(RecyclerView rv, int newState)
    {
        if (newState != 0) return; // 0 = SCROLL_STATE_IDLE
        var lm  = rv.GetLayoutManager() as LinearLayoutManager;
        int pos = lm?.FindFirstVisibleItemPosition() ?? RecyclerView.NoPosition;
        if (pos != RecyclerView.NoPosition) _o.OnScrollIdle?.Invoke(pos);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfAdapter — "burro": só lê do cache; em miss pede render ao handler
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfAdapter : RecyclerView.Adapter
{
    private readonly int                            _pageCount;
    private          PdfBitmapLruCache          _cache;
    private readonly SizeF[]                        _pageSizes;
    // Dimensões da viewport (px).
    //  • Vertical:   a célula tem a proporção EXATA da página → preenche a LARGURA, altura
    //    proporcional (scroll contínuo). A página ocupa a célula inteira.
    //  • Horizontal: a célula ocupa uma TELA (largura da viewport); o FitCenter encaixa a folha
    //    inteira, centralizada, e o entorno (sobras) fica no cinza do leitor. O mapeamento dos
    //    realces usa o retângulo real do FitCenter (ver PdfPageImageView.FitBox).
    private readonly int                            _viewportW;
    private readonly int                            _viewportH;
    private readonly bool                           _horizontal;
    private          global::Android.Graphics.Color _bg;
    private readonly Action<int>                    _requestRender;
    private          bool                           _disposed;

    protected PdfAdapter(IntPtr h, JniHandleOwnership t) : base(h, t)
    {
        _cache = null!; _pageSizes = Array.Empty<SizeF>();
        _viewportW = 1080; _viewportH = 1920; _requestRender = _ => { };
    }

    public PdfAdapter(int pageCount, PdfBitmapLruCache cache,
        SizeF[] pageSizes, int viewportW, int viewportH, bool horizontal,
        global::Android.Graphics.Color bg, Action<int> requestRender)
    {
        _pageCount      = pageCount;
        _cache          = cache;
        _pageSizes      = pageSizes;
        _viewportW      = Math.Max(1, viewportW);
        _viewportH      = Math.Max(1, viewportH);
        _horizontal     = horizontal;
        _bg             = bg;
        _requestRender  = requestRender;
    }

    public override int ItemCount => _pageCount;

    public void UpdateCache(PdfBitmapLruCache cache) => _cache = cache;

    // ── Realce de busca e seleção ──────────────────────────────────────────────────
    // Busca: página alvo + rects em PONTOS PDF (a PdfPageImageView converte para px no desenho).
    private int _searchPage = -1;
    private List<(double l, double t, double r, double b)> _searchRectsPt = new();

    // Seleção MULTI-PÁGINA: o adapter não armazena rects — pede ao handler (provider) a parte da
    // seleção que recai em cada página, no momento do bind (cobre páginas que entram por scroll).
    public Func<int, PdfPageSel?>? SelectionProvider { get; set; }
    public float                   SelContentScale   { get; set; } = 1f;

    public void SetSearchHighlight(int page, List<(double l, double t, double r, double b)> rectsPt)
    {
        int old = _searchPage;
        _searchPage    = page;
        _searchRectsPt = rectsPt;
        if (old >= 0 && old != page) NotifyItemChanged(old);
        NotifyItemChanged(page);
    }

    public void ClearSearchHighlight()
    {
        int old = _searchPage;
        _searchPage    = -1;
        _searchRectsPt = new();
        if (old >= 0) NotifyItemChanged(old);
    }

    /// <summary>Atualiza a cor de fundo das páginas (folhas) e força o rebind para aplicá-la.</summary>
    public void SetPageColor(global::Android.Graphics.Color color)
    {
        _bg = color;
        NotifyDataSetChanged();
    }

    // Dimensão do eixo PRINCIPAL de scroll de uma página.
    //  • Horizontal: LARGURA fixa = uma tela (viewport). O FitCenter encaixa a folha inteira
    //    centralizada dentro da célula viewportW×viewportH (fit-page).
    //  • Vertical:   ALTURA proporcional à largura da viewport (a folha preenche a largura).
    private int PageMainSizePx(int position)
    {
        if (_horizontal) return _viewportW;

        // Padrão A4 retrato (1.414) p/ proporção desconhecida.
        if ((uint)position >= (uint)_pageSizes.Length)
            return (int)(_viewportW * 1.414f);
        var sz = _pageSizes[position];
        if (sz.Width <= 0 || sz.Height <= 0)
            return (int)(_viewportW * 1.414f);
        return (int)((long)_viewportW * sz.Height / sz.Width);   // altura = largura × (h/w)
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        // O eixo principal NUNCA pode ser 0 (o LinearLayoutManager criaria ViewHolders em excesso).
        // Horizontal: largura = uma tela. Vertical: estimativa A4 (ajustada no bind). No eixo
        // cruzado a célula preenche o RecyclerView (MatchParent).
        int initMain = _horizontal ? _viewportW : (int)(_viewportW * 1.414f);
        var lp = _horizontal
            ? new RecyclerView.LayoutParams(initMain, ViewGroup.LayoutParams.MatchParent)
            : new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, initMain);
        var iv = new PdfPageImageView(parent.Context!) { LayoutParameters = lp };
        iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitCenter);
        // Horizontal: a folha (fit-page) não preenche a célula → o entorno usa o cinza do leitor
        // (como o espaçamento do modo vertical). Vertical: cor da página (a folha preenche a célula).
        // Sem um fundo, a célula com bitmap null mostraria o fundo do container até o render chegar.
        iv.SetBackgroundColor(_horizontal ? PdfContainerView.ReaderBg : _bg);
        iv.PageColor = _bg;   // placeholder "folha em branco" enquanto o render não chega
        return new PdfVH(iv);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not PdfVH vh || (uint)position >= (uint)_pageCount) return;

        // Reaplica a cor de fundo da célula (reusada após SetPageColor). Horizontal: cinza de
        // leitor no entorno da folha; vertical: cor da página (que preenche a célula).
        vh.Iv.SetBackgroundColor(_horizontal ? PdfContainerView.ReaderBg : _bg);
        vh.Iv.PageColor = _bg;   // placeholder "folha em branco" enquanto o render não chega

        int mainSize = PageMainSizePx(position);
        if (vh.Iv.LayoutParameters is { } lp)
        {
            if (_horizontal && lp.Width != mainSize)  { lp.Width  = mainSize; vh.Iv.LayoutParameters = lp; }
            else if (!_horizontal && lp.Height != mainSize) { lp.Height = mainSize; vh.Iv.LayoutParameters = lp; }
        }

        // Tamanho da página (pontos PDF) + realces desta posição → a view desenha por cima do bitmap.
        if ((uint)position < (uint)_pageSizes.Length)
        {
            vh.Iv.WPt = _pageSizes[position].Width;
            vh.Iv.HPt = _pageSizes[position].Height;
        }
        vh.Iv.SearchRects    = position == _searchPage ? _searchRectsPt : null;
        var sel              = SelectionProvider?.Invoke(position);
        vh.Iv.SelectionRects = sel?.Rects;
        vh.Iv.HandleStartPt  = sel?.StartPt;
        vh.Iv.HandleEndPt    = sel?.EndPt;
        vh.Iv.ContentScale   = SelContentScale <= 0 ? 1f : SelContentScale;

        var cached = _cache.Get(position);
        if (cached is not null && !cached.IsRecycled)
        {
            vh.Iv.SetImageBitmap(cached);
            return;
        }

        // Cache miss → NÃO renderiza aqui. Delega ao handler (RequestRender), a ÚNICA fonte de
        // render: enfileira na fila priorizada por proximidade ao centro visível, drenada por um
        // worker único (dedup por _queued, checagem do token antes do Put). Isso elimina
        // double-render e Put órfão, e faz a página visível renderizar antes dos prefetches.
        // Quando o render terminar, o handler aplica o bitmap na ImageView visível via ApplyToVisible.
        vh.Iv.SetImageBitmap(null);
        _requestRender(position);
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is PdfVH vh)
            vh.Iv.SetImageBitmap(null); // solta a ref do bitmap → GC pode coletar
        base.OnViewRecycled(holder);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        // Os renders são geridos pelo handler (cancelados via _shutdownCts no Disconnect/Load),
        // não pelo adapter — nada a cancelar aqui.
        base.Dispose(disposing);
    }
}

internal sealed class PdfVH : RecyclerView.ViewHolder
{
    public PdfPageImageView Iv { get; }
    protected PdfVH(IntPtr h, JniHandleOwnership t) : base(h, t) => Iv = null!;
    public PdfVH(PdfPageImageView iv) : base(iv) => Iv = iv;
}

// ─────────────────────────────────────────────────────────────────────────────
// Barra de miniaturas (drawer) — ViewHolder + Adapter
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfThumbVH : RecyclerView.ViewHolder
{
    public global::Android.Widget.ImageView   Iv   { get; }
    public global::Android.Widget.TextView    Tv   { get; }
    public global::Android.Widget.FrameLayout Card { get; }
    public int Bound = -1;

    protected PdfThumbVH(IntPtr h, JniHandleOwnership t) : base(h, t)
    { Iv = null!; Tv = null!; Card = null!; }

    public PdfThumbVH(global::Android.Views.View root,
        global::Android.Widget.ImageView iv, global::Android.Widget.TextView tv,
        global::Android.Widget.FrameLayout card) : base(root)
    { Iv = iv; Tv = tv; Card = card; }
}

internal sealed class PdfThumbAdapter : RecyclerView.Adapter
{
    private int _count;
    public  int CurrentPage { get; private set; }
    private readonly Action<int, PdfThumbVH> _bind;
    private readonly Action<int>             _click;

    private static readonly global::Android.Graphics.Color Accent = global::Android.Graphics.Color.Rgb(0x3F, 0x51, 0xB5);
    private static readonly global::Android.Graphics.Color Border = global::Android.Graphics.Color.Rgb(0xCF, 0xCF, 0xD3);
    private static readonly global::Android.Graphics.Color Label  = global::Android.Graphics.Color.Rgb(0x55, 0x55, 0x5A);

    protected PdfThumbAdapter(IntPtr h, JniHandleOwnership t) : base(h, t)
    { _bind = (_, _) => { }; _click = _ => { }; }

    public PdfThumbAdapter(Action<int, PdfThumbVH> bind, Action<int> click)
    { _bind = bind; _click = click; }

    public void Configure(int count, int current)
    {
        _count = count; CurrentPage = current;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var ctx = parent.Context!;
        int cw = (int)ctx.ToPixels(104), ch = (int)ctx.ToPixels(136);
        int pad = (int)ctx.ToPixels(6);

        var col = new global::Android.Widget.LinearLayout(ctx) { Orientation = global::Android.Widget.Orientation.Vertical };
        col.SetGravity(global::Android.Views.GravityFlags.CenterHorizontal);
        col.SetPadding(pad, pad, pad, pad);
        col.LayoutParameters = new RecyclerView.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

        var card = new global::Android.Widget.FrameLayout(ctx);
        card.LayoutParameters = new global::Android.Widget.LinearLayout.LayoutParams(cw, ch);

        var iv = new global::Android.Widget.ImageView(ctx);
        iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitCenter);
        iv.LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        card.AddView(iv);

        var tv = new global::Android.Widget.TextView(ctx) { TextSize = 11 };
        tv.SetTextColor(Label);
        tv.Gravity = global::Android.Views.GravityFlags.Center;
        var tvlp = new global::Android.Widget.LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        tvlp.TopMargin = (int)ctx.ToPixels(4);
        tv.LayoutParameters = tvlp;

        col.AddView(card);
        col.AddView(tv);

        var vh = new PdfThumbVH(col, iv, tv, card);
        col.Click += (_, _) => { if (vh.Bound >= 0) _click(vh.Bound); };
        return vh;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not PdfThumbVH vh) return;
        vh.Tv.Text = (position + 1).ToString();
        bool current = position == CurrentPage;
        vh.Tv.SetTextColor(current ? Accent : Label);

        // Folha branca com borda; página atual → borda azul mais grossa (estilo Acrobat/Edge).
        float dm = vh.Card.Context?.Resources?.DisplayMetrics?.Density ?? 2f;
        var bg = new global::Android.Graphics.Drawables.GradientDrawable();
        bg.SetColor(global::Android.Graphics.Color.White);
        bg.SetStroke((int)((current ? 2f : 1f) * dm), current ? Accent : Border);
        vh.Card.Background = bg;

        _bind(position, vh);
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is PdfThumbVH vh) { vh.Iv.SetImageBitmap(null); vh.Bound = -1; }
        base.OnViewRecycled(holder);
    }
}

// Parte da seleção multi-página que recai numa página: rects (pontos PDF) + as alças de início/fim
// (só preenchidas na página inicial/final). Devolvida pelo handler ao adapter via SelectionProvider.
internal sealed class PdfPageSel
{
    public List<(double l, double t, double r, double b)> Rects { get; }
    public (double x, double y)? StartPt { get; }
    public (double x, double y)? EndPt   { get; }
    public PdfPageSel(List<(double l, double t, double r, double b)> rects,
        (double x, double y)? startPt, (double x, double y)? endPt)
    {
        Rects = rects; StartPt = startPt; EndPt = endPt;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfPageImageView — ImageView que desenha o realce de busca (amarelo) e de seleção
// (azul) por cima da página. Os rects vêm em PONTOS PDF (origem inferior-esquerda) +
// o tamanho da página em pontos; a conversão para px usa o retângulo do FitCenter (FitBox):
// no vertical ele cobre a view inteira; no horizontal reflete a folha centralizada + faixa de fundo.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class PdfPageImageView : global::Android.Widget.ImageView
{
    private static readonly global::Android.Graphics.Paint SearchPaint = MakePaint(unchecked((int)0x99FFD14A)); // amarelo translúcido
    private static readonly global::Android.Graphics.Paint SelPaint    = MakePaint(unchecked((int)0x553F51B5)); // azul translúcido
    private static readonly global::Android.Graphics.Paint HandlePaint = MakePaint(unchecked((int)0xFF3F51B5)); // azul opaco (alças)

    private static global::Android.Graphics.Paint MakePaint(int argb)
    {
        var p = new global::Android.Graphics.Paint { AntiAlias = true, Color = new global::Android.Graphics.Color(argb) };
        p.SetStyle(global::Android.Graphics.Paint.Style.Fill);
        return p;
    }

    public double WPt { get; set; }
    public double HPt { get; set; }
    public List<(double l, double t, double r, double b)>? SearchRects    { get; set; }
    public List<(double l, double t, double r, double b)>? SelectionRects { get; set; }
    // Pontos-base (em pontos PDF) das alças de ajuste: início (canto inf-esq da 1ª linha) e
    // fim (canto inf-dir da última). null = sem seleção → não desenha alça.
    public (double x, double y)? HandleStartPt { get; set; }
    public (double x, double y)? HandleEndPt   { get; set; }

    /// <summary>
    /// Escala do conteúdo (zoom do RecyclerView). A célula é escalada por ScaleX no zoom, então o
    /// raio da alça é DIVIDIDO por esta escala para manter tamanho FIXO na tela ao ampliar.
    /// </summary>
    public float ContentScale { get; set; } = 1f;

    /// <summary>Raio da alça em px da célula, compensado pelo zoom (≈7dp fixos na tela).</summary>
    public float HandleRadiusPx
        => 7f * (Resources?.DisplayMetrics?.Density ?? 2f) / Math.Max(0.05f, ContentScale);

    /// <summary>
    /// Cor da folha (página). Usada como placeholder enquanto o render não chegou, para a página
    /// não piscar com o fundo do leitor ao rolar (no horizontal a célula tem fundo cinza).
    /// </summary>
    public global::Android.Graphics.Color PageColor { get; set; } = global::Android.Graphics.Color.White;
    private readonly global::Android.Graphics.Paint _placeholderPaint = new() { AntiAlias = false };

    protected PdfPageImageView(IntPtr h, JniHandleOwnership t) : base(h, t) { }
    public PdfPageImageView(Context ctx) : base(ctx) { }

    // Retângulo (px da view) onde a folha é desenhada pelo FitCenter — delega ao helper único do
    // handler (ver PdfViewerHandler.FitBox). No vertical cobre a view inteira; no horizontal
    // reflete a folha centralizada + faixa de fundo.
    private (float ox, float oy, float dw, float dh) FitBox()
        => PdfViewerHandler.FitBox(Width, Height, WPt, HPt);

    protected override void OnDraw(global::Android.Graphics.Canvas canvas)
    {
        base.OnDraw(canvas);   // fundo da célula + bitmap (FitCenter), se houver
        if (WPt <= 0 || HPt <= 0) return;

        // Sem bitmap ainda (render em andamento): pinta a "folha em branco" no retângulo da página,
        // para a página não piscar escura (fundo do leitor) ao rolar. No vertical o retângulo cobre
        // a célula inteira → equivale ao fundo de página anterior.
        if (Drawable is null)
        {
            var (ox, oy, dw, dh) = FitBox();
            _placeholderPaint.Color = PageColor;
            canvas.DrawRect(ox, oy, ox + dw, oy + dh, _placeholderPaint);
        }

        DrawRects(canvas, SearchRects, SearchPaint);
        DrawRects(canvas, SelectionRects, SelPaint);
        DrawHandle(canvas, HandleStartPt, isStart: true);
        DrawHandle(canvas, HandleEndPt,   isStart: false);
    }

    // Posição em px (no espaço da célula) do ponto-base (tip) de uma alça. null se não há.
    public global::Android.Graphics.PointF? HandlePixel((double x, double y)? pt)
    {
        if (pt is null || WPt <= 0 || HPt <= 0) return null;
        var (ox, oy, dw, dh) = FitBox();
        float px = ox + (float)(pt.Value.x / WPt * dw);
        float py = oy + (float)((1 - pt.Value.y / HPt) * dh);
        return new global::Android.Graphics.PointF(px, py);
    }

    // Lágrima/pinça assimétrica (estilo Android): a PONTA fica na extremidade do texto (tip) e o
    // bojo pende para baixo, para o lado oposto. O lado junto ao texto é RETO (vertical) e o lado
    // de fora AFUNILA do bojo até a ponta (bezier) → forma de gota, menos redonda que um círculo.
    // Início → ponta no canto superior-direito; fim → ponta no canto superior-esquerdo.
    private void DrawHandle(global::Android.Graphics.Canvas canvas, (double x, double y)? pt, bool isStart)
    {
        var p = HandlePixel(pt);
        if (p is null) return;
        float r  = HandleRadiusPx;
        float tx = p.X, ty = p.Y;                 // tip (na linha de base do texto)
        float cx = isStart ? tx - r : tx + r;     // centro do bojo (deslocado p/ o lado oposto)
        float cy = ty + r;                        // … e abaixo da base
        var oval = new global::Android.Graphics.RectF(cx - r, cy - r, cx + r, cy + r);

        using var path = new global::Android.Graphics.Path();
        path.MoveTo(tx, ty);                      // tip
        if (isStart)
        {
            path.LineTo(tx, cy);                  // lado RETO junto ao texto (vertical, à direita)
            path.ArcTo(oval, 0f, 180f);           // semicírculo inferior: direita → base → esquerda
            path.QuadTo(cx - r, ty, tx, ty);      // afunila da esquerda do bojo até a ponta
        }
        else
        {
            path.LineTo(tx, cy);                  // lado RETO junto ao texto (vertical, à esquerda)
            path.ArcTo(oval, 180f, -180f);        // semicírculo inferior: esquerda → base → direita
            path.QuadTo(cx + r, ty, tx, ty);      // afunila da direita do bojo até a ponta
        }
        path.Close();
        canvas.DrawPath(path, HandlePaint);
    }

    private void DrawRects(global::Android.Graphics.Canvas canvas,
        List<(double l, double t, double r, double b)>? rects, global::Android.Graphics.Paint paint)
    {
        if (rects is null || rects.Count == 0) return;
        var (ox, oy, dw, dh) = FitBox();
        foreach (var (l, t, r, b) in rects)
        {
            // t/b em coords PDF (origem inferior, y cresce p/ cima); a tela cresce p/ baixo.
            float left  = ox + (float)(l / WPt * dw);
            float right = ox + (float)(r / WPt * dw);
            float top   = oy + (float)((1 - t / HPt) * dh);
            float bot   = oy + (float)((1 - b / HPt) * dh);
            canvas.DrawRect(left, top, right, bot, paint);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfBitmapLruCache — LRU por bytes, thread-safe, NUNCA recicla
// ─────────────────────────────────────────────────────────────────────────────
//
// Em API 26+ os pixels de Bitmap vivem no heap nativo gerenciado pelo GC.
// Recycle() manual em qualquer rota que o GPU thread ainda referencie causa
// "Canvas: trying to use a recycled bitmap". Deixamos o GC coletar.

internal sealed class PdfBitmapLruCache
{
    private          long _maxBytes;
    private          long _usedBytes;
    private readonly Dictionary<int, (LinkedListNode<int> Node, Bitmap Bmp)> _map = new();
    private readonly LinkedList<int> _order = new();
    private readonly object _lock = new();

    public long MaxBytes { get { lock (_lock) return _maxBytes; } }
    public long UsedBytes { get { lock (_lock) return _usedBytes; } }

    public PdfBitmapLruCache(long maxBytes)
        => _maxBytes = Math.Max(10L * 1024 * 1024, maxBytes);

    /// <summary>
    /// Ajusta o limite do cache em tempo de execução, evictando o excedente (LRU) se o novo
    /// limite for menor. Preserva os bitmaps válidos que ainda cabem — evita descartar um cache
    /// inteiro só porque MaxCacheMB mudou.
    /// </summary>
    public void SetMaxBytes(long maxBytes)
    {
        lock (_lock)
        {
            _maxBytes = Math.Max(10L * 1024 * 1024, maxBytes);
            while (_usedBytes > _maxBytes && _order.Count > 0)
                EvictLru_Locked();
        }
    }

    public bool ContainsPage(int idx) { lock (_lock) return _map.ContainsKey(idx); }

    public Bitmap? Get(int idx)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(idx, out var e)) return null;
            _order.Remove(e.Node);
            _order.AddFirst(e.Node);
            return e.Bmp;
        }
    }

    /// <summary>
    /// Insere o bitmap no cache. Retorna false se o bitmap NÃO foi inserido (porque um único
    /// bitmap excede o limite total do cache) — nesse caso o chamador pode exibi-lo mesmo assim,
    /// mas o cache o ignora (não fica "preso" acima do limite, evitando thrashing para 1 item).
    /// </summary>
    public bool Put(int idx, Bitmap bmp)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(idx, out var existing))
            {
                _order.Remove(existing.Node);
                _usedBytes -= existing.Bmp.ByteCount;
                _map.Remove(idx);
            }
            // Um único bitmap maior que o limite total nunca cabe: inseri-lo forçaria
            // _usedBytes a ficar permanentemente acima de _maxBytes (o while evictaria tudo
            // e ainda assim inseriria), reduzindo o cache a 1 item perpetuamente. Não inserir.
            if (bmp.ByteCount > _maxBytes) return false;
            while (_usedBytes + bmp.ByteCount > _maxBytes && _order.Count > 0)
                EvictLru_Locked();
            var node = _order.AddFirst(idx);
            _map[idx] = (node, bmp);
            _usedBytes += bmp.ByteCount;
            return true;
        }
    }

    public void EvictAll()
    {
        lock (_lock) { _map.Clear(); _order.Clear(); _usedBytes = 0; }
    }

    public void TrimToWindow(int start, int end)
    {
        lock (_lock)
        {
            foreach (var k in _map.Keys.Where(k => k < start || k > end).ToList())
            {
                if (!_map.TryGetValue(k, out var e)) continue;
                _order.Remove(e.Node);
                _usedBytes -= e.Bmp.ByteCount;
                _map.Remove(k);
            }
        }
    }

    public void ReduceByHalf()
    {
        lock (_lock)
        {
            long target = _usedBytes / 2;
            while (_usedBytes > target && _order.Last is not null)
                EvictLru_Locked();
        }
    }

    private void EvictLru_Locked()
    {
        var last = _order.Last;
        if (last is null) return;
        int k = last.Value;
        _order.RemoveLast();
        if (_map.TryGetValue(k, out var e))
        {
            _usedBytes -= e.Bmp.ByteCount;
            _map.Remove(k);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfSpacingDecoration
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfSpacingDecoration : RecyclerView.ItemDecoration
{
    private readonly int  _spacePx;
    private readonly bool _horizontal;
    protected PdfSpacingDecoration(IntPtr h, JniHandleOwnership t) : base(h, t) { }
    public PdfSpacingDecoration(int spacePx, bool horizontal) { _spacePx = spacePx; _horizontal = horizontal; }

    public override void GetItemOffsets(
        global::Android.Graphics.Rect outRect, AView view,
        RecyclerView parent, RecyclerView.State state)
    {
        int pos   = parent.GetChildAdapterPosition(view);
        int total = parent.GetAdapter()?.ItemCount ?? 0;
        if (pos >= 0 && pos < total - 1)
        {
            if (_horizontal) outRect.Right  = _spacePx;   // gap entre páginas lado a lado
            else             outRect.Bottom = _spacePx;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfFilePrintAdapter — entrega um PDF já existente em disco ao framework de
// impressão do Android, copiando seus bytes para o destino (impressora/PDF/etc.).
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// ClippedRecyclerView — RecyclerView com clip de canvas para evitar overflow
// visual durante animações de scroll em containers com ClipToOutline.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ClippedRecyclerView : RecyclerView
{
    public ClippedRecyclerView(Context context) : base(context) { }

    protected override void DispatchDraw(global::Android.Graphics.Canvas? canvas)
    {
        if (canvas is null) return;
        var save = canvas.Save();
        canvas.ClipRect(0, 0, Width, Height);
        base.DispatchDraw(canvas);
        canvas.RestoreToCount(save);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfFilePrintAdapter — entrega um PDF já existente em disco ao framework de
// impressão do Android, copiando seus bytes para o destino (impressora/PDF/etc.).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfFilePrintAdapter : PrintDocumentAdapter
{
    private readonly string _path;
    private readonly string _jobName;

    protected PdfFilePrintAdapter(IntPtr h, JniHandleOwnership t) : base(h, t) { _path = ""; _jobName = ""; }
    public PdfFilePrintAdapter(string path, string jobName) { _path = path; _jobName = jobName; }

    public override void OnLayout(
        PrintAttributes? oldAttributes, PrintAttributes? newAttributes,
        global::Android.OS.CancellationSignal? cancellationSignal,
        PrintDocumentAdapter.LayoutResultCallback? callback, Bundle? extras)
    {
        if (cancellationSignal?.IsCanceled == true) { callback?.OnLayoutCancelled(); return; }

        var info = new PrintDocumentInfo.Builder(_jobName)
            .SetContentType(PrintContentType.Document)
            .SetPageCount(PrintDocumentInfo.PageCountUnknown)
            .Build();

        // Segundo parâmetro: layout mudou? Sempre true (não dependemos das PrintAttributes).
        callback?.OnLayoutFinished(info, true);
    }

    public override void OnWrite(
        PageRange[]? pages, ParcelFileDescriptor? destination,
        global::Android.OS.CancellationSignal? cancellationSignal,
        PrintDocumentAdapter.WriteResultCallback? callback)
    {
        try
        {
            using var input  = new Java.IO.FileInputStream(_path);
            using var output = new Java.IO.FileOutputStream(destination!.FileDescriptor);

            var buffer = new byte[16 * 1024];
            int read;
            while ((read = input.Read(buffer)) != -1)
            {
                if (cancellationSignal?.IsCanceled == true) { callback?.OnWriteCancelled(); return; }
                output.Write(buffer, 0, read);
            }
            output.Flush();

            callback?.OnWriteFinished(new[] { PageRange.AllPages });
        }
        catch (Exception ex)
        {
            callback?.OnWriteFailed(ex.Message);
        }
    }
}
