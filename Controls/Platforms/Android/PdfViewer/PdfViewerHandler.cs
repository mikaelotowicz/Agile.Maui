// Platforms/Android/PdfViewer/PdfViewerHandler.cs
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
using Android.Graphics.Pdf;
using Android.OS;
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
            [nameof(PdfViewer.CurrentPage)]         = (h, _) => h.SyncPage(),
            [nameof(PdfViewer.ZoomFactor)]          = (h, _) => h.SyncZoom(),
            [nameof(PdfViewer.MinZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxCacheMB)]          = (h, _) => h.ApplyCache(),
            [nameof(PdfViewer.RenderScale)]         = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.ApplyPageBackground(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomEnabled(),
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
        };

    private PdfEngine?            _engine;
    private PdfAdapter?           _adapter;
    private PdfBitmapLruCache?    _cache;
    private PdfSpacingDecoration? _spacingDecoration;
    private CancellationTokenSource    _shutdownCts = new();   // cancela todos os renders
    private CancellationTokenSource?   _loadCts;
    private readonly HashSet<int>      _renderInFlight = new();
    private readonly object            _renderLock = new();
    private bool                       _syncingPage;
    private bool                       _reportingPage;  // mudança originada do scroll → não re-sincronizar
    private int                        _targetPage = -1;
    private int                        _lastPrefetchCenter = -1;  // evita prefetch redundante por frame
    private bool                       _syncingZoom;
    private string?                    _tempFilePath;

    public PdfViewerHandler() : base(Mapper) { }

    protected override PdfContainerView CreatePlatformView() => new(Context!);

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
            ReportPage(page);
            // Prefetch contínuo durante o scroll (sem trim, que fica só no idle) — antecipa
            // o render das páginas que estão entrando na viewport, evitando a célula em branco.
            Prefetch(page);
        };

        pv.OnZoomChanged = zoom =>
        {
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

        pv.Rv.SetAdapter(null);
        _adapter?.Dispose();
        _adapter = null;

        _engine?.Dispose();
        _engine = null;

        // EvictAll SEM recycle — o RecyclerView pode ainda estar visível na animação de
        // saída com ImageViews referenciando bitmaps. Reciclar aqui causaria
        // "Canvas: trying to use a recycled bitmap". O GC coleta quando as views somem.
        _cache?.EvictAll();
        _cache = null;

        lock (_renderLock) _renderInFlight.Clear();

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
        lock (_renderLock) _renderInFlight.Clear();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        PlatformView.Rv.SetAdapter(null);
        _adapter?.Dispose();  _adapter = null;
        _engine?.Dispose();   _engine  = null;
        _lastPrefetchCenter = -1;

        // O engine anterior já foi disposed acima (fecha renderer + pfd → libera o arquivo),
        // então é seguro deletar o temp anterior agora, na UI thread. O novo temp tem nome
        // único (Guid) e só é registrado em _tempFilePath na UI thread (ver MainThread abaixo),
        // eliminando qualquer corrida com o DisconnectHandler.
        if (_tempFilePath is not null)
        {
            try { System.IO.File.Delete(_tempFilePath); } catch { }
            _tempFilePath = null;
        }

        var source = VirtualView.Source;
        var stream = VirtualView.PdfStream;
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
            string?            newTemp = null;   // arquivo criado por esta sessão (se houver)
            PdfEngine?    engine  = null;
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

                var pfd = ParcelFileDescriptor.Open(new Java.IO.File(localPath), ParcelFileMode.ReadOnly)
                          ?? throw new InvalidOperationException("ParcelFileDescriptor null");
                var renderer = new PdfRenderer(pfd);
                int count    = renderer.PageCount;
                PdfViewerLog.Write(Tag, $"LoadDocument: pageCount={count}");

                if (count == 0)
                {
                    renderer.Close(); pfd.Close();
                    throw new InvalidOperationException("PDF com 0 páginas.");
                }

                engine = new PdfEngine(renderer, pfd);
                if (cts.IsCancellationRequested) { engine.Dispose(); CleanupTemp(newTemp); return; }

                // Pré-calcula tamanhos de todas as páginas (só metadados, rápido).
                // Necessário para o adapter dar altura correta às células ANTES do render —
                // sem isso, células com bitmap null teriam height=0 e o LinearLayoutManager
                // criaria dezenas de ViewHolders de uma vez.
                var pageSizes = new SizeF[count];
                for (int i = 0; i < count && !cts.IsCancellationRequested; i++)
                    pageSizes[i] = engine.GetPageSize(i);

                var capturedEngine = engine;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Toda mutação de _engine / _tempFilePath acontece AQUI (UI thread),
                    // a mesma thread do DisconnectHandler — sem corrida.
                    if (cts.IsCancellationRequested || PlatformView is null)
                    {
                        capturedEngine.Dispose();
                        CleanupTemp(newTemp);
                        return;
                    }

                    _tempFilePath = newTemp;
                    _engine       = capturedEngine;

                    int viewW = PlatformView.Rv.Width > 0
                        ? PlatformView.Rv.Width
                        : context.Resources?.DisplayMetrics?.WidthPixels ?? 1080;

                    var bg = vv.PageBackgroundColor.ToPlatform();

                    _adapter = new PdfAdapter(
                        count, _cache!, pageSizes, viewW, bg,
                        requestRender: RequestRender);

                    ApplySpacing();
                    PlatformView.Rv.SetAdapter(_adapter);
                    vv.RaiseDocumentLoaded(count);
                    TrimAndPrefetch(0);
                });
            }
            catch (OperationCanceledException) { engine?.Dispose(); CleanupTemp(newTemp); }
            catch (Exception ex)
            {
                engine?.Dispose();
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
        double scale = VirtualView?.RenderScale ?? 1.5;
        return Math.Max(1, (int)(w * scale));
    }

    /// <summary>
    /// Solicita a renderização de uma página. Idempotente: se já está no cache ou já
    /// está sendo renderizada, não faz nada. Chamado tanto pelo bind (cache miss) quanto
    /// pelo prefetch — caminho único, sem double-render.
    /// </summary>
    private void RequestRender(int idx)
    {
        var engine = _engine;
        var cache  = _cache;
        if (engine is null || cache is null) return;
        if (idx < 0 || idx >= engine.PageCount) return;

        if (cache.ContainsPage(idx)) { ApplyToVisible(idx); return; }

        lock (_renderLock)
        {
            if (_renderInFlight.Contains(idx)) return;
            _renderInFlight.Add(idx);
        }

        int  widthPx = RenderWidthPx();
        var  bg      = (VirtualView?.PageBackgroundColor ?? Colors.White).ToPlatform();
        var  token   = _shutdownCts.Token;
        var  pv      = PlatformView;

        _ = Task.Run(async () =>
        {
            try
            {
                var bmp = await engine.RenderAndroidBitmapAsync(idx, widthPx, bg, token);
                if (bmp is null || token.IsCancellationRequested) return;
                // Não escrever num cache órfão: se ApplyCache substituiu _cache enquanto este
                // render estava em voo, o bitmap nunca seria exibido (cache descartado) → leak.
                // O token cobre LoadDocument/Disconnect (que cancelam _shutdownCts); a checagem
                // de referência cobre ApplyCache (troca _cache sem cancelar o token).
                if (!ReferenceEquals(cache, _cache)) return;
                cache.Put(idx, bmp);

                pv?.Rv.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    ApplyToVisible(idx);
                });
            }
            catch (OperationCanceledException) { }
            catch (Java.Lang.OutOfMemoryError)
            {
                PdfViewerLog.Write(Tag, $"RequestRender pág {idx}: OOM");
                cache.ReduceByHalf();
                GC.Collect();
            }
            catch (Exception ex)
            {
                PdfViewerLog.Write(Tag, $"RequestRender pág {idx}: ERRO [{ex.GetType().Name}] {ex.Message}");
            }
            finally
            {
                lock (_renderLock) _renderInFlight.Remove(idx);
            }
        }, token);
    }

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
        if (_engine is null || _cache is null || VirtualView is null) return;
        if (centerPage == _lastPrefetchCenter) return;  // já prefetchado para este centro
        _lastPrefetchCenter = centerPage;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = _engine.PageCount;
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
        if (_engine is null || _cache is null || VirtualView is null) return;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = _engine.PageCount;
        int start = Math.Max(0, centerPage - above);
        int end   = Math.Min(total - 1, centerPage + below);

        _cache.TrimToWindow(start, end);
        Prefetch(centerPage);
    }

    // ── Property sync ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reporta a página atual ao controle MAUI marcando a origem como "scroll do usuário".
    /// O guard _reportingPage impede que o setter de CurrentPage dispare SyncPage de volta
    /// (que chamaria SmoothScrollToPosition competindo com o scroll em curso → jank/loop).
    /// </summary>
    private void ReportPage(int page)
    {
        _reportingPage = true;
        VirtualView?.RaisePageChanged(page);
        _reportingPage = false;
    }

    private void SyncPage()
    {
        if (_reportingPage || _syncingPage || PlatformView is null || VirtualView is null || _adapter is null) return;
        _targetPage  = Math.Clamp(VirtualView.CurrentPage, 0, _adapter.ItemCount - 1);
        _syncingPage = true;
        PlatformView.Rv.SmoothScrollToPosition(_targetPage);
    }

    private void SyncZoom()
    {
        if (_syncingZoom || PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        PlatformView.SetZoom((float)VirtualView.ZoomFactor);
        _syncingZoom = false;
    }

    private void ApplyZoomLimits()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.MinZoom = (float)VirtualView.MinZoom;
        PlatformView.MaxZoom = (float)VirtualView.MaxZoom;
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
        _spacingDecoration = new PdfSpacingDecoration(spacePx);
        PlatformView.Rv.AddItemDecoration(_spacingDecoration);
    }

    private void ApplyZoomEnabled()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ZoomEnabled = VirtualView.IsPinchZoomEnabled;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfContainerView — FrameLayout + RecyclerView + pinch/double-tap zoom
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfContainerView : global::Android.Widget.FrameLayout,
    ScaleGestureDetector.IOnScaleGestureListener
{
    internal readonly ClippedRecyclerView Rv;

    // Cinza de leitor do "deck" (atrás das páginas). O espaçamento entre folhas mostra esta cor.
    internal static readonly global::Android.Graphics.Color ReaderBg =
        global::Android.Graphics.Color.Rgb(0x52, 0x56, 0x59);

    private readonly ScaleGestureDetector     _sgd;
    private readonly GestureDetector          _gd;
    internal         float                    _currentZoom = 1f;
    internal         float                    _gestureZoom = 1f;
    private          CancellationTokenSource? _commitCts;

    public float MinZoom     { get; set; } = 0.9f;
    public float MaxZoom     { get; set; } = 8f;
    public bool  ZoomEnabled { get; set; } = true;

    public Action<int>?   OnPageChanged { get; set; }
    public Action<float>? OnZoomChanged { get; set; }
    public Action<int>?   OnScrollIdle  { get; set; }

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

    /// <summary>Limites do pan horizontal para o zoom/pivô atuais (não deixa arrastar além da borda).</summary>
    private void ClampPan()
    {
        if (_currentZoom <= 1.05f) { Rv.TranslationX = 0; return; }
        float px = Rv.PivotX;
        float minTx = -(_currentZoom - 1f) * (Width - px);   // revela a borda direita
        float maxTx =  (_currentZoom - 1f) * px;             // revela a borda esquerda
        Rv.TranslationX = Math.Clamp(Rv.TranslationX, minTx, maxTx);
    }

    /// <summary>Arrasta a página na horizontal (chamado pelo gesto de 1 dedo). Retorna true se ampliada.</summary>
    public bool PanHorizontal(float distanceX)
    {
        if (_currentZoom <= 1.05f) { if (Rv.TranslationX != 0) Rv.TranslationX = 0; return false; }
        Rv.TranslationX -= distanceX;   // distanceX>0 = dedo p/ esquerda → conteúdo acompanha
        ClampPan();
        return true;
    }

    protected override void OnLayout(bool changed, int l, int t, int r, int b)
        => Rv.Layout(0, 0, r - l, b - t);

    public override bool OnInterceptTouchEvent(MotionEvent? ev)
    {
        if (ZoomEnabled && ev?.PointerCount >= 2) return true;
        return base.OnInterceptTouchEvent(ev);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ZoomEnabled && ev is not null) { _gd.OnTouchEvent(ev); _sgd.OnTouchEvent(ev); }
        return base.DispatchTouchEvent(ev);
    }

    public override bool OnTouchEvent(MotionEvent? ev)
    {
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

    // Arrasto de 1 dedo: quando ampliado, faz pan horizontal (o scroll vertical fica com o
    // RecyclerView). Com 2 dedos é pinch, tratado pelo ScaleGestureDetector — ignora aqui.
    public override bool OnScroll(MotionEvent? e1, MotionEvent? e2, float distanceX, float distanceY)
    {
        if (!_o.ZoomEnabled) return false;
        if (e2 is not null && e2.PointerCount > 1) return false;
        return _o.PanHorizontal(distanceX);
    }
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
    private readonly int                            _containerW;
    private          global::Android.Graphics.Color _bg;
    private readonly Action<int>                    _requestRender;
    private          bool                           _disposed;

    protected PdfAdapter(IntPtr h, JniHandleOwnership t) : base(h, t)
    {
        _cache = null!; _pageSizes = Array.Empty<SizeF>();
        _containerW = 1080; _requestRender = _ => { };
    }

    public PdfAdapter(int pageCount, PdfBitmapLruCache cache,
        SizeF[] pageSizes, int containerW, global::Android.Graphics.Color bg,
        Action<int> requestRender)
    {
        _pageCount      = pageCount;
        _cache          = cache;
        _pageSizes      = pageSizes;
        _containerW     = Math.Max(1, containerW);
        _bg             = bg;
        _requestRender  = requestRender;
    }

    public override int ItemCount => _pageCount;

    public void UpdateCache(PdfBitmapLruCache cache) => _cache = cache;

    /// <summary>Atualiza a cor de fundo das páginas (folhas) e força o rebind para aplicá-la.</summary>
    public void SetPageColor(global::Android.Graphics.Color color)
    {
        _bg = color;
        NotifyDataSetChanged();
    }

    private int PageHeightPx(int position)
    {
        if ((uint)position >= (uint)_pageSizes.Length) return (int)(_containerW * 1.414f);
        var sz = _pageSizes[position];
        return sz.Width > 0 ? (int)((long)_containerW * sz.Height / sz.Width)
                            : (int)(_containerW * 1.414f);
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        // Altura inicial A4 — itens NUNCA podem ter height=0 (LinearLayoutManager criaria
        // ViewHolders em excesso). A altura exata é aplicada no bind.
        var iv = new global::Android.Widget.ImageView(parent.Context!)
        {
            LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, (int)(_containerW * 1.414f)),
        };
        iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitCenter);
        // Fundo = cor da página (branco). Sem isso, a célula com bitmap null mostra o fundo
        // escuro do container durante o scroll → "tela preta" até o render chegar.
        iv.SetBackgroundColor(_bg);
        return new PdfVH(iv);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not PdfVH vh || (uint)position >= (uint)_pageCount) return;

        // Reaplica a cor da página (folha) — necessário para views reusadas após SetPageColor.
        vh.Iv.SetBackgroundColor(_bg);

        int correctH = PageHeightPx(position);
        if (vh.Iv.LayoutParameters is { } lp && lp.Height != correctH)
        {
            lp.Height = correctH;
            vh.Iv.LayoutParameters = lp;
        }

        var cached = _cache.Get(position);
        if (cached is not null && !cached.IsRecycled)
        {
            vh.Iv.SetImageBitmap(cached);
            return;
        }

        // Cache miss → NÃO renderiza aqui. Delega ao handler (RequestRender), a ÚNICA fonte
        // de render, com deduplicação por página (_renderInFlight) e checagem do token de
        // shutdown antes do Put. Isso elimina double-render e Put órfão. Quando o render
        // terminar, o handler aplica o bitmap na ImageView visível via ApplyToVisible.
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
    public global::Android.Widget.ImageView Iv { get; }
    protected PdfVH(IntPtr h, JniHandleOwnership t) : base(h, t) => Iv = null!;
    public PdfVH(global::Android.Widget.ImageView iv) : base(iv) => Iv = iv;
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfEngine — serializa PdfRenderer (thread-safe via SemaphoreSlim)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfEngine : IPdfEngine
{
    private const string Tag = "Pdf/Engine";

    private readonly PdfRenderer          _renderer;
    private readonly ParcelFileDescriptor _pfd;
    private readonly SemaphoreSlim        _sem = new(1, 1);
    private          bool                 _disposed;

    public bool IsOpen    => !_disposed;
    public int  PageCount => _disposed ? 0 : _renderer.PageCount;

    public PdfEngine(PdfRenderer renderer, ParcelFileDescriptor pfd)
    {
        _renderer = renderer;
        _pfd      = pfd;
    }

    public Task<bool> OpenAsync(string path, string? pw = null, CancellationToken ct = default)
        => Task.FromResult(true);
    public Task<bool> OpenAsync(Stream stream, string? pw = null, CancellationToken ct = default)
        => Task.FromResult(true);
    public void Close() => Dispose();

    public SizeF GetPageSize(int idx)
    {
        if (_disposed || (uint)idx >= (uint)PageCount) return SizeF.Zero;
        _sem.Wait();
        try
        {
            if (_disposed) return SizeF.Zero;
            var page = _renderer.OpenPage(idx);
            if (page is null) return SizeF.Zero;
            try { return new SizeF(page.Width, page.Height); }
            finally { page.Close(); } // Close() Java explícito (Dispose sozinho não garante)
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write(Tag, $"GetPageSize {idx}: {ex.Message}");
            return SizeF.Zero;
        }
        finally { _sem.Release(); }
    }

    public async Task<Bitmap?> RenderAndroidBitmapAsync(
        int idx, int widthPx, global::Android.Graphics.Color bg, CancellationToken ct)
    {
        if (_disposed || (uint)idx >= (uint)PageCount) return null;

        try { await _sem.WaitAsync(ct); }
        catch (ObjectDisposedException) { return null; }
        catch (OperationCanceledException) { return null; }

        try
        {
            if (_disposed || ct.IsCancellationRequested) return null;

            var page = _renderer.OpenPage(idx);
            if (page is null) return null;
            try
            {
                int h   = Math.Max(1, (int)((long)widthPx * page.Height / Math.Max(1, page.Width)));
                var bmp = Bitmap.CreateBitmap(widthPx, h, Bitmap.Config.Argb8888!);
                if (bmp is null) return null;
                try
                {
                    using var canvas = new Canvas(bmp);
                    canvas.DrawColor(bg);
                    page.Render(bmp, null, null, PdfRenderMode.ForDisplay);
                }
                catch
                {
                    // Falha no render → o bitmap recém-alocado nunca foi exibido nem cacheado,
                    // então é seguro reciclar agora (evita leak nativo). Repropaga o erro.
                    bmp.Recycle();
                    throw;
                }
                return bmp;
            }
            finally { page.Close(); }
        }
        catch (Java.Lang.OutOfMemoryError) { throw; } // tratado no chamador
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            PdfViewerLog.Write(Tag, $"Render {idx}: [{ex.GetType().Name}] {ex.Message}");
            return null;
        }
        finally
        {
            try { _sem.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public async Task<byte[]?> RenderPageAsync(
        int idx, int widthPx, int heightPx, uint bg = 0xFFFFFFFF, CancellationToken ct = default)
    {
        var color = new global::Android.Graphics.Color(
            (byte)((bg >> 16) & 0xFF), (byte)((bg >> 8) & 0xFF),
            (byte)(bg & 0xFF), (byte)((bg >> 24) & 0xFF));
        Bitmap? bmp;
        try { bmp = await RenderAndroidBitmapAsync(idx, widthPx, color, ct); }
        catch (Java.Lang.OutOfMemoryError) { return null; }
        if (bmp is null) return null;
        return await Task.Run(() =>
        {
            using var ms = new System.IO.MemoryStream();
            bmp.Compress(Bitmap.CompressFormat.Png!, 100, ms);
            bmp.Recycle(); // bmp local, nunca foi pro cache nem pra view — seguro reciclar
            return ms.ToArray();
        }, ct);
    }

    public Task<byte[]?> RenderThumbnailAsync(int idx, int tw, int th, CancellationToken ct = default)
        => RenderPageAsync(idx, tw, th, 0xFFFFFFFF, ct);

    public Task<string> ExtractTextAsync(int idx, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // CRÍTICO: drenar renders em voo ANTES de fechar o renderer nativo.
        // _disposed=true já impede novos OpenPage/Render (todos checam _disposed após
        // adquirir o semáforo). Adquirir o semáforo aqui garante que nenhum render esteja
        // DENTRO de OpenPage/Render/page.Close quando _renderer.Close()/_pfd.Close() rodar
        // → evita IllegalStateException/SIGSEGV nativo.
        // Wait com timeout: se um render demorar patologicamente, não travamos a UI thread
        // indefinidamente. Mesmo que o timeout estoure, _disposed=true minimiza a janela.
        bool acquired = false;
        try { acquired = _sem.Wait(TimeSpan.FromSeconds(5)); } catch { }
        try
        {
            try { _renderer.Close(); } catch { }
            try { _pfd.Close();      } catch { }
        }
        finally
        {
            if (acquired) { try { _sem.Release(); } catch { } }
            _sem.Dispose();
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
    private readonly int _spacePx;
    protected PdfSpacingDecoration(IntPtr h, JniHandleOwnership t) : base(h, t) { }
    public PdfSpacingDecoration(int spacePx) => _spacePx = spacePx;

    public override void GetItemOffsets(
        global::Android.Graphics.Rect outRect, AView view,
        RecyclerView parent, RecyclerView.State state)
    {
        int pos   = parent.GetChildAdapterPosition(view);
        int total = parent.GetAdapter()?.ItemCount ?? 0;
        if (pos >= 0 && pos < total - 1) outRect.Bottom = _spacePx;
    }
}
