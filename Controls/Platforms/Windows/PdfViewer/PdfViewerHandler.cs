// Platforms/Windows/PdfViewer/PdfViewerHandler.cs
//
// Motor: Windows.Data.Pdf (WinRT, API nativa do Windows 10+).
// Virtualização: ScrollViewer + Canvas virtual; só mantém Image controls para páginas no viewport ± buffer.
// Cache: LRU de InMemoryRandomAccessStream com limite em MB.
// Zoom: ScrollViewer.ZoomMode + Ctrl+Scroll (mouse wheel).
// Prefetch: pré-renderiza N páginas acima/abaixo em background.
// Memória: TrimToWindow descarta entradas fora da janela ativa no ViewChanged.

using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

using NativeImage     = Microsoft.UI.Xaml.Controls.Image;
using WinBorder       = Microsoft.UI.Xaml.Controls.Border;
using WinThickness    = Microsoft.UI.Xaml.Thickness;
using WinHAlign       = Microsoft.UI.Xaml.HorizontalAlignment;
using WinVAlign       = Microsoft.UI.Xaml.VerticalAlignment;
using WinScrollMode   = Microsoft.UI.Xaml.Controls.ScrollMode;
using WinScrollBarVis = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;

namespace Agile.Maui.Platforms.Windows;

// ─────────────────────────────────────────────────────────────────────────────
// Handler MAUI
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfViewerHandler
    : ViewHandler<PdfViewer, PdfWinContainer>
{
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
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomEnabled(),
            [nameof(PdfViewer.RenderScale)]         = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.EnableThumbnailBar)]  = (h, _) => h.ApplyThumbnailBar(),
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
        };

    private PdfDocument?                  _pdfDoc;
    private PdfWinLruCache?          _cache;
    private CancellationTokenSource?      _loadCts;
    private CancellationTokenSource?      _prefetchCts;
    private CancellationTokenSource?      _zoomSettleCts;   // debounce: render só quando o zoom assenta
    private bool                          _syncingPage;
    private bool                          _syncingZoom;
    private float                         _lastZoom = 1f;   // último ZoomFactor visto (detecta zoom vs scroll)
    private float                         _renderedZoom = 1f; // zoom no qual as bitmaps atuais foram rasterizadas
    private double[]                      _pageOffsets    = Array.Empty<double>();
    private double[]                      _pageHeights    = Array.Empty<double>();
    private double                        _pageWidth;   // largura base da folha (metade da viewport:
                                                        // em 200% ela preenche a largura toda)
    private string?                       _tempPdfPath;

    // Guarda quais páginas têm Image control ativo no canvas
    private readonly HashSet<int>         _activeImages   = new();
    private readonly object               _activeImgLock  = new();

    // Itens da barra de miniaturas (para destacar a página atual com borda azul)
    private List<PdfThumbItem>?           _thumbItems;

    public PdfViewerHandler() : base(Mapper) { }

    protected override PdfWinContainer CreatePlatformView() => new();

    protected override void ConnectHandler(PdfWinContainer pv)
    {
        base.ConnectHandler(pv);
        pv.ScrollViewer.ViewChanged     += OnViewChanged;
        pv.SizeChanged                   += OnSizeChanged;
        pv.ScrollViewer.PointerWheelChanged += OnPointerWheel;
        pv.ThumbnailList.ContainerContentChanging += OnThumbChanging;
        pv.ThumbnailList.ItemClick                += OnThumbClick;

        // Garante que o cache exista mesmo que o consumidor nunca sete MaxCacheMB
        // (caso contrário _cache fica null e nenhuma página renderiza).
        EnsureCache();
    }

    // Inicializa _cache a partir do MaxCacheMB atual (default 200) se ainda não existir.
    private void EnsureCache()
    {
        if (_cache is not null || VirtualView is null) return;
        long mb = VirtualView.MaxCacheMB > 0 ? VirtualView.MaxCacheMB : 200;
        _cache = new PdfWinLruCache(mb * 1024 * 1024);
    }

    protected override void DisconnectHandler(PdfWinContainer pv)
    {
        _loadCts?.Cancel();    _loadCts?.Dispose();    _loadCts    = null;
        _prefetchCts?.Cancel();_prefetchCts?.Dispose();_prefetchCts = null;
        _zoomSettleCts?.Cancel();_zoomSettleCts?.Dispose();_zoomSettleCts = null;

        pv.ScrollViewer.ViewChanged          -= OnViewChanged;
        pv.SizeChanged                        -= OnSizeChanged;
        pv.ScrollViewer.PointerWheelChanged   -= OnPointerWheel;
        pv.ThumbnailList.ContainerContentChanging -= OnThumbChanging;
        pv.ThumbnailList.ItemClick                -= OnThumbClick;

        _cache?.Dispose(); _cache  = null;
        _pdfDoc = null;

        if (_tempPdfPath is not null)
        {
            try { System.IO.File.Delete(_tempPdfPath); } catch { }
            _tempPdfPath = null;
        }

        base.DisconnectHandler(pv);
    }

    // ── LoadDocument ──────────────────────────────────────────────────────────

    private void LoadDocument()
    {
        if (PlatformView is null || VirtualView is null) return;

        _loadCts?.Cancel(); _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _prefetchCts?.Cancel();
        _zoomSettleCts?.Cancel();
        PlatformView.PagesCanvas.Children.Clear();
        _pageOffsets = Array.Empty<double>();
        _pageHeights = Array.Empty<double>();
        lock (_activeImgLock) _activeImages.Clear();
        _pdfDoc = null;

        // Limpa a barra de miniaturas do documento anterior.
        _thumbItems = null;
        PlatformView.ThumbnailHost.Visibility = global::Microsoft.UI.Xaml.Visibility.Collapsed;
        PlatformView.ThumbnailList.ItemsSource = null;

        // Garante o cache mesmo sem o consumidor setar MaxCacheMB e DESCARTA as páginas do
        // documento anterior: o cache é keyed por índice de página e o novo PDF reusa os
        // mesmos índices — sem esvaziar, as primeiras páginas do PDF antigo apareceriam (cache hit).
        EnsureCache();
        _cache?.EvictAll();
        _renderedZoom = 1f;
        _lastZoom     = 1f;

        // Volta o scroll/zoom ao início para o novo documento abrir em 100%, no topo.
        PlatformView.ScrollViewer.ChangeView(0, 0, 1f, disableAnimation: true);

        var source = VirtualView.Source;
        var stream = VirtualView.PdfStream;
        if (string.IsNullOrWhiteSpace(source) && stream is null) return;

        bool isUrl = !string.IsNullOrWhiteSpace(source)
            && (source!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        var vv = VirtualView;

        _ = Task.Run(async () =>
        {
            // Path temp criado por ESTE load. Só é comitado em _tempPdfPath ao final,
            // para não corrermos com loads concorrentes que leem/escrevem o campo.
            string? localTempPath = null;
            try
            {
                PdfDocument? doc = null;

                if (stream is not null)
                {
                    var tp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    localTempPath = tp;
                    // O PdfStream pode vir posicionado no fim (já lido); rebobina se possível.
                    if (stream.CanSeek) stream.Position = 0;
                    await using (var fs = new FileStream(tp, FileMode.Create))
                        await stream.CopyToAsync(fs, cts.Token);
                    var sf = await StorageFile.GetFileFromPathAsync(tp).AsTask(cts.Token);
                    doc = await PdfDocument.LoadFromFileAsync(sf).AsTask(cts.Token);
                }
                else if (isUrl)
                {
                    using var http = PdfHttpClient.Create();
                    var bytes = await http.GetByteArrayAsync(source, cts.Token);
                    var tp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    localTempPath = tp;
                    await System.IO.File.WriteAllBytesAsync(tp, bytes, cts.Token);
                    var sf = await StorageFile.GetFileFromPathAsync(tp).AsTask(cts.Token);
                    doc = await PdfDocument.LoadFromFileAsync(sf).AsTask(cts.Token);
                }
                else
                {
                    var sf = await StorageFile.GetFileFromPathAsync(source!).AsTask(cts.Token);
                    doc = await PdfDocument.LoadFromFileAsync(sf).AsTask(cts.Token);
                }

                if (cts.IsCancellationRequested || doc is null)
                {
                    // Load cancelado/falho: limpa o temp deste load sem tocar no campo compartilhado.
                    if (localTempPath is not null) try { System.IO.File.Delete(localTempPath); } catch { }
                    return;
                }

                // Comita o estado deste load só agora que sabemos que venceu. Remove o temp
                // anterior (se houver) e adota o novo.
                var prevTemp = _tempPdfPath;
                _tempPdfPath = localTempPath;
                if (prevTemp is not null && prevTemp != localTempPath)
                    try { System.IO.File.Delete(prevTemp); } catch { }

                _pdfDoc = doc;
                int count = (int)doc.PageCount;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (cts.IsCancellationRequested || PlatformView is null) return;
                    InitVirtualCanvas(count);
                    ApplySpacing();
                    ApplyZoomLimits();
                    vv.RaiseDocumentLoaded(count);
                    RenderVisible();
                    ApplyThumbnailBar();
                });
            }
            catch (OperationCanceledException)
            {
                if (localTempPath is not null && localTempPath != _tempPdfPath)
                    try { System.IO.File.Delete(localTempPath); } catch { }
            }
            catch (Exception ex)
            {
                if (localTempPath is not null && localTempPath != _tempPdfPath)
                    try { System.IO.File.Delete(localTempPath); } catch { }
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!cts.IsCancellationRequested) vv.RaiseDocumentLoadFailed(ex.Message);
                });
            }
        }, cts.Token);
    }

    // ── InitVirtualCanvas — define tamanho total do canvas virtual ─────────────

    private void InitVirtualCanvas(int count)
    {
        if (PlatformView is null || VirtualView is null || _pdfDoc is null) return;

        var    canvas  = PlatformView.PagesCanvas;
        canvas.Children.Clear();
        lock (_activeImgLock) _activeImages.Clear();
        // Layout recomeçando do zero (load/resize/spacing) → o "100%" é a base de rasterização.
        _renderedZoom = 1f;

        // Usa a largura da ÁREA DE VISUALIZAÇÃO (ScrollViewer), não do Grid inteiro — senão a
        // barra de miniaturas é incluída e a página fica larga demais. A página recebe uma
        // margem lateral e é centralizada, deixando o deck cinza aparecer dos dois lados.
        double viewportW = PlatformView.ScrollViewer.ActualWidth  > 1 ? PlatformView.ScrollViewer.ActualWidth  : 800;
        double viewportH = PlatformView.ScrollViewer.ActualHeight > 1 ? PlatformView.ScrollViewer.ActualHeight : viewportW * 1.3;

        // "100%" = mostrar ~80% da ALTURA da página na viewport (não exceder 50% da largura).
        // Antes era só 50% da largura, ignorando a altura — em telas largas a página A4 ficava
        // alta demais e só ~⅔ dela aparecia. Dimensiona a folha pela proporção da 1ª página:
        // para 80% visível, a altura da página = viewportH / 0,80 → largura = altura / proporção.
        const double visibleFraction = 0.80;
        double firstRatio;
        using (var p0 = _pdfDoc.GetPage(0))
            firstRatio = p0.Size.Height / Math.Max(1, p0.Size.Width);
        double byWidth  = viewportW * 0.5;                                  // limite de largura (deck nas laterais)
        double byHeight = viewportH / (visibleFraction * firstRatio);       // ~80% da altura visível
        _pageWidth      = Math.Max(50, Math.Min(byWidth, byHeight));
        double spacing  = VirtualView.PageSpacing;

        _pageOffsets = new double[count];
        _pageHeights = new double[count];
        double offset = 0;

        for (int i = 0; i < count; i++)
        {
            _pageOffsets[i] = offset;
            using var page  = _pdfDoc.GetPage((uint)i);
            double ratio    = page.Size.Height / Math.Max(1, page.Size.Width);
            double h        = _pageWidth * ratio;
            _pageHeights[i] = Math.Max(100, h);
            offset         += _pageHeights[i] + spacing;
        }

        // Com count==0, offset==0 e (offset - spacing) seria negativo.
        double totalH = Math.Max(0, count > 0 ? offset - spacing : 0);
        // Canvas com a largura da PÁGINA; o ScrollViewer centraliza (margem que NÃO escala com o
        // zoom) e dá scroll quando o zoom faz a página exceder a viewport.
        canvas.Width  = _pageWidth;
        canvas.Height = totalH;
        PlatformView.ScrollViewer.Content = canvas;
    }

    // ── RenderVisible — renderiza / remove páginas conforme viewport ───────────

    // Calcula a janela visível em coordenadas do canvas (já convertidas do zoom).
    // Retorna false quando não há nada a mostrar.
    private bool ComputeWindow(out int firstVis, out int lastVis, out int activeStart, out int activeEnd)
    {
        firstVis = lastVis = activeStart = activeEnd = -1;
        if (PlatformView is null || VirtualView is null || _pdfDoc is null
            || _pageOffsets.Length == 0) return false;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = (int)_pdfDoc.PageCount;

        // O ScrollViewer do WinUI expressa VerticalOffset/ViewportHeight no espaço JÁ
        // escalado pelo ZoomFactor, enquanto _pageOffsets/_pageHeights estão no espaço
        // não-escalado do canvas. Sem converter pelo zoom, com ZoomFactor != 1 o cálculo
        // de páginas visíveis desalinha e, em offsets grandes (ex.: metade de um PDF de
        // centenas de páginas), firstVis fica -1 → nenhuma página renderiza → tela preta.
        float zoom = PlatformView.ScrollViewer.ZoomFactor;
        if (zoom < 0.0001f) zoom = 1f;
        double top = PlatformView.ScrollViewer.VerticalOffset / zoom;
        double vph = PlatformView.ScrollViewer.ViewportHeight / zoom;
        if (vph < 1) vph = PlatformView.ActualHeight / zoom;
        double bot = top + vph;

        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            double pTop = _pageOffsets[i];
            double pBot = pTop + _pageHeights[i];
            if (pBot < top || pTop > bot) continue;
            if (firstVis < 0) firstVis = i;
            lastVis = i;
        }
        if (firstVis < 0) return false;

        activeStart = Math.Max(0,         firstVis - above);
        activeEnd   = Math.Min(total - 1, lastVis  + below);
        return true;
    }

    // Cria os placeholders (folhas com PageBackgroundColor) de toda a janela ativa e remove
    // os de fora. Síncrono e leve — chamado TAMBÉM durante o scroll para que o usuário veja a
    // "folha" da página carregando em vez do fundo escuro. NÃO dispara render assíncrono.
    private void EnsurePlaceholders(int activeStart, int activeEnd)
    {
        var canvas = PlatformView?.PagesCanvas;
        if (canvas is null) return;

        List<int> toRemove;
        lock (_activeImgLock)
            toRemove = _activeImages.Where(i => i < activeStart || i > activeEnd).ToList();
        foreach (int r in toRemove) RemovePageImage(r);

        for (int i = activeStart; i <= activeEnd; i++)
            GetOrCreatePageImage(i, canvas);   // cria o Border (folha) se ainda não existir
    }

    // A página idx já tem imagem renderizada (Source != null)?
    private bool HasImage(int idx)
    {
        var canvas = PlatformView?.PagesCanvas;
        if (canvas is null) return false;
        foreach (var child in canvas.Children)
            if (child is WinBorder b && b.Tag is int t && t == idx)
                return (b.Child as NativeImage)?.Source is not null;
        return false;
    }

    private void RenderVisible(bool force = false)
    {
        if (!ComputeWindow(out int firstVis, out int lastVis, out int activeStart, out int activeEnd))
            return;

        int above = VirtualView!.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView!.EnablePageCaching ? VirtualView.PrefetchBelow : 0;

        // Folhas brancas já visíveis (placeholders); depois renderiza por cima.
        EnsurePlaceholders(activeStart, activeEnd);
        _cache?.TrimToWindow(activeStart, activeEnd);

        _prefetchCts?.Cancel(); _prefetchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        var order = new List<int>();
        for (int i = firstVis; i <= lastVis; i++) order.Add(i);
        for (int d = 1; d <= Math.Max(above, below); d++)
        {
            if (d <= below && lastVis  + d <= activeEnd)   order.Add(lastVis  + d);
            if (d <= above && firstVis - d >= activeStart) order.Add(firstVis - d);
        }

        // Só renderiza páginas que ainda não têm imagem (a folha já está na tela).
        // RenderVisible roda ao PARAR o scroll, então não há rajada que cause render duplicado.
        // force=true (re-render por mudança de zoom): rerasteriza mesmo com imagem presente —
        // a imagem antiga (outra escala) permanece visível até a nova chegar (substitui in-place,
        // sem flash). O cache já foi esvaziado pelo chamador, então não há lease da escala antiga.
        foreach (int idx in order.Distinct())
            if (force || !HasImage(idx)) _ = RenderPageAsync(idx, cts.Token);
    }

    // Largura-alvo de renderização da página em PIXELS FÍSICOS, na resolução REAL de exibição
    // da página no ZOOM ATUAL: _pageWidth (DIPs de exibição) × zoom × DPI do monitor
    // (RasterizationScale) × RenderScale (supersampling).
    //
    // Basear no _pageWidth (e não na largura da viewport) mantém a razão bitmap/exibição
    // ~constante (≈ RenderScale). Antes, renderizando pela largura da viewport e exibindo num
    // Border bem menor (_pageWidth), a GPU fazia um downscale grande (3–4×) com interpolação
    // bilinear — o que BORRAVA. Combinado com o re-render ao mudar o zoom (ScheduleRenderAfterZoom),
    // a página é sempre rasterizada (vetorialmente) na escala em que é exibida → nitidez tipo Chrome.
    private double RenderTargetWidth()
    {
        double raster = PlatformView?.XamlRoot?.RasterizationScale ?? 1.0;
        if (raster < 1.0) raster = 1.0;
        double rScale = VirtualView?.RenderScale ?? 1.5;
        float  zoom   = PlatformView is not null ? PlatformView.ScrollViewer.ZoomFactor : 1f;
        if (zoom < 0.0001f) zoom = 1f;

        double target = _pageWidth * zoom * raster * rScale;
        double minW   = Math.Max(1, _pageWidth * raster);   // nunca abaixo da exibição base
        return Math.Clamp(target, minW, 5000);              // teto p/ não estourar memória
    }

    private async Task RenderPageAsync(int idx, CancellationToken ct)
    {
        if (_pdfDoc is null || _cache is null || PlatformView is null || VirtualView is null) return;
        if (idx < 0 || idx >= (int)_pdfDoc.PageCount) return;

        // Cache hit: pega um lease (mantém o stream vivo durante o decode assíncrono).
        var hitLease = _cache.TryGetLease(idx);
        if (hitLease is not null)
        {
            try { await ApplyStreamAsync(idx, hitLease.Stream, ct); }
            finally { hitLease.Dispose(); }
            return;
        }

        try
        {
            // Renderiza na largura CHEIA da viewport em PIXELS FÍSICOS (× RasterizationScale do
            // monitor) × RenderScale. Ignorar o DPI da tela deixava o texto "mole" em telas 125–150%;
            // considerá-lo aproxima a nitidez do visualizador do Chrome.
            uint destW = (uint)Math.Max(1, RenderTargetWidth());

            using var page = _pdfDoc.GetPage((uint)idx);
            var opts = new PdfPageRenderOptions { DestinationWidth = destW };
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, opts).AsTask(ct);

            if (ct.IsCancellationRequested) { stream.Dispose(); return; }

            // PutAndLease entrega o stream já com lease; se não coube no cache (stream
            // maior que o limite) ele já foi disposed e não há nada a aplicar.
            var lease = _cache.PutAndLease(idx, stream);
            if (lease is null) return;

            try { await ApplyStreamAsync(idx, stream, ct); }
            finally { lease.Dispose(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfViewer] Win RenderPage({idx}): {ex.Message}");
        }
    }

    private Task ApplyStreamAsync(int idx, InMemoryRandomAccessStream stream, CancellationToken ct)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                // Re-checa cancelamento e pertinência à janela ativa ANTES de criar o Image,
                // para não deixar Borders órfãos / páginas duplicadas após um RemovePageImage.
                if (ct.IsCancellationRequested) return;
                if (PlatformView is null || idx >= _pageOffsets.Length) return;

                var canvas = PlatformView.PagesCanvas;
                NativeImage? img = GetOrCreatePageImage(idx, canvas);
                if (img is null) return;

                stream.Seek(0);
                // CRÍTICO p/ nitidez no zoom: sem DecodePixelWidth o WinUI faz "right-sizing"
                // automático e decodifica a bitmap no tamanho de EXIBIÇÃO (layout ~_pageWidth),
                // jogando fora a resolução extra; ao ampliar, o ScrollViewer faz upscale disso e
                // borra. Forçando o decode na largura de RENDER (física, alta), o zoom reamostra
                // de uma bitmap de alta resolução → nítido.
                var bmp = new BitmapImage
                {
                    DecodePixelType  = DecodePixelType.Physical,
                    DecodePixelWidth = (int)Math.Max(1, RenderTargetWidth()),
                };
                await bmp.SetSourceAsync(stream);

                // O decode é assíncrono: o token pode ter sido cancelado e a página
                // removida nesse meio-tempo. Re-checa antes de atribuir o Source.
                if (ct.IsCancellationRequested) { RemovePageImage(idx); return; }
                bool stillActive;
                lock (_activeImgLock) stillActive = _activeImages.Contains(idx);
                if (!stillActive) return;

                img.Source = bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfViewer] ApplyBitmap({idx}): {ex.Message}");
            }
        });

    private NativeImage? GetOrCreatePageImage(int idx, global::Microsoft.UI.Xaml.Controls.Canvas canvas)
    {
        // Procura Image existente com Tag == idx (dedup: nunca dois Borders para o mesmo idx)
        foreach (var child in canvas.Children)
        {
            if (child is WinBorder b && b.Tag is int t && t == idx)
                return b.Child as NativeImage;
        }

        // Marca como ativo de forma atômica. Esta rota roda sempre na UI thread
        // (ApplyStreamAsync via InvokeOnMainThreadAsync), então o scan acima + o Add
        // não podem ser intercalados por outro GetOrCreatePageImage do mesmo idx.
        lock (_activeImgLock) _activeImages.Add(idx);

        var vv      = VirtualView;
        var bgBrush = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(
            ToWinColor(vv?.PageBackgroundColor ?? Colors.White));

        var ni = new NativeImage { HorizontalAlignment = WinHAlign.Stretch };
        var border = new WinBorder
        {
            Tag             = idx,
            Background      = bgBrush,
            Width           = _pageWidth,
            Height          = _pageHeights[idx],
            Child           = ni,
            // Contorno sutil para destacar a folha branca sobre o deck claro (estilo Acrobat/Edge).
            BorderBrush     = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(
                                  global::Windows.UI.Color.FromArgb(0xFF, 0xCF, 0xCF, 0xD3)),
            BorderThickness = new WinThickness(1),
        };

        global::Microsoft.UI.Xaml.Controls.Canvas.SetLeft(border, 0);
        global::Microsoft.UI.Xaml.Controls.Canvas.SetTop(border, _pageOffsets[idx]);
        canvas.Children.Add(border);
        return ni;
    }

    private void RemovePageImage(int idx)
    {
        var canvas = PlatformView?.PagesCanvas;
        if (canvas is null) return;

        void DoRemove()
        {
            var toRemove = canvas.Children
                .OfType<WinBorder>()
                .Where(b => b.Tag is int t && t == idx)
                .ToList();
            foreach (var b in toRemove) canvas.Children.Remove(b);
        }

        // Remove o Border de forma SÍNCRONA quando já na UI thread, para que _activeImages e o
        // conteúdo real do canvas nunca fiquem dessincronizados — uma remoção adiada podia
        // apagar um Border recém-reaproveitado por GetOrCreatePageImage (página sumindo / preta).
        if (MainThread.IsMainThread) DoRemove();
        else MainThread.BeginInvokeOnMainThread(DoRemove);

        lock (_activeImgLock) _activeImages.Remove(idx);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            // Durante o scroll só criamos/removemos as folhas (placeholders) da janela atual,
            // para o usuário ver a página carregando em vez do fundo escuro. O render assíncrono
            // (mais pesado) fica para quando o scroll parar (evento não-intermediário).
            if (ComputeWindow(out _, out _, out int aStart, out int aEnd))
                EnsurePlaceholders(aStart, aEnd);
            // Atualiza o número da página atual continuamente (barra inferior fluida). É leve —
            // só uma comparação de offsets; não rola a barra de miniaturas a cada frame.
            ReportCurrentPage(syncThumbnailScroll: false);
            return;
        }
        // Detecta se ESTE evento foi uma mudança de zoom (vs. scroll puro). Comparar com o
        // último zoom visto cobre todas as origens: slider, pinch e Ctrl+roda.
        float curZoom = PlatformView is not null ? PlatformView.ScrollViewer.ZoomFactor : _lastZoom;
        bool  zoomChanged = Math.Abs(curZoom - _lastZoom) > 0.001f;
        _lastZoom = curZoom;

        // Zoom changed? Propaga para o ZoomFactor do controle.
        if (!_syncingZoom && PlatformView is not null && VirtualView is not null
            && Math.Abs(curZoom - VirtualView.ZoomFactor) > 0.01)
        {
            _syncingZoom = true;
            VirtualView.ZoomFactor = curZoom;
            _syncingZoom = false;
        }

        if (zoomChanged)
        {
            // Durante o zoom NÃO renderiza: chamar RenderVisible a cada passo cancela e
            // reinicia o render das páginas visíveis (flicker) e acumula trabalho (trava).
            // Mostra só as folhas (placeholders, leve) e agenda UM render ao ASSENTAR.
            // Também não atualizamos a página aqui — o cálculo "pularia" durante o zoom.
            if (ComputeWindow(out _, out _, out int zStart, out int zEnd))
                EnsurePlaceholders(zStart, zEnd);
            ScheduleRenderAfterZoom();
            return;
        }

        // Scroll puro (zoom estável): render completo das páginas visíveis.
        RenderVisible();

        // Página atual + sincroniza a rolagem da barra de miniaturas (só ao assentar o scroll).
        ReportCurrentPage(syncThumbnailScroll: true);
    }

    // Calcula a página no CENTRO da viewport e notifica o controle. Leve (só comparação de
    // offsets) → pode rodar a cada frame de scroll para manter a barra inferior fluida.
    // 'syncThumbnailScroll' rola a barra de miniaturas até a página atual (evitado durante o
    // scroll contínuo para não brigar com o gesto do usuário; só no settle).
    private void ReportCurrentPage(bool syncThumbnailScroll)
    {
        if (_syncingPage || _pageOffsets.Length == 0 || PlatformView is null) return;

        // VerticalOffset está no espaço escalado; converte para o espaço do canvas (ver RenderVisible).
        float pgZoom = PlatformView.ScrollViewer.ZoomFactor;
        if (pgZoom < 0.0001f) pgZoom = 1f;
        // A "página atual" é a que está no CENTRO da viewport, não no topo. O zoom é centrado:
        // ao ampliar, o ponto no topo desliza para outra página (mudando o número de página
        // sem o usuário rolar), mas o ponto central permanece na mesma página → estável.
        double viewportH = PlatformView.ScrollViewer.ViewportHeight / pgZoom;
        if (viewportH < 1) viewportH = PlatformView.ActualHeight / pgZoom;
        double centerBase = PlatformView.ScrollViewer.VerticalOffset / pgZoom + viewportH / 2;
        int page = 0;
        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            if (_pageOffsets[i] <= centerBase) page = i;
            else break;
        }
        _syncingPage = true;
        VirtualView?.RaisePageChanged(page);
        _syncingPage = false;

        // Mantém a miniatura da página atual destacada (borda azul) e visível na barra.
        if (PlatformView.ThumbnailHost.Visibility == global::Microsoft.UI.Xaml.Visibility.Visible)
        {
            var tl = PlatformView.ThumbnailList;
            if (tl.SelectedIndex != page)
            {
                tl.SelectedIndex = page;
                if (syncThumbnailScroll && tl.SelectedItem is not null) tl.ScrollIntoView(tl.SelectedItem);
            }
            HighlightThumb(page);
        }
    }

    // Debounce do render durante o zoom: cada passo do zoom reagenda; quando o zoom para
    // (nenhum passo por ~150 ms), renderiza UMA vez na nova escala (nítido, sem flicker/trava).
    private void ScheduleRenderAfterZoom()
    {
        _zoomSettleCts?.Cancel(); _zoomSettleCts?.Dispose();
        var cts = new CancellationTokenSource();
        _zoomSettleCts = cts;
        _ = Task.Delay(150, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || cts.IsCancellationRequested) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (cts.IsCancellationRequested || PlatformView is null) return;

                float z = PlatformView.ScrollViewer.ZoomFactor;
                if (z < 0.0001f) z = 1f;

                // O zoom mudou o bastante desde a última rasterização? As bitmaps atuais foram
                // geradas em outra escala; exibidas agora, ficam super/sub-amostradas (borradas).
                // Re-rasteriza (vetorial) na escala atual para nitidez. Limiar de 15% evita
                // re-render a cada micro-passo.
                double ratio = Math.Max(z / _renderedZoom, _renderedZoom / Math.Max(0.0001f, z));
                if (ratio > 1.15)
                {
                    _renderedZoom = z;
                    _cache?.EvictAll();             // streams da escala antiga → obsoletos
                    RenderVisible(force: true);     // re-render in-place (sem remover imagens → sem flash)
                }
                else
                {
                    RenderVisible();
                }
            });
        }, TaskScheduler.Default);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pdfDoc is null) return;
        // Recalcula layout quando o container é redimensionado. _pageWidth muda → a resolução
        // das bitmaps cacheadas fica obsoleta; esvazia o cache e re-renderiza na nova escala.
        InitVirtualCanvas((int)_pdfDoc.PageCount);   // limpa as imagens (canvas.Children.Clear)
        _cache?.EvictAll();
        float z = PlatformView is not null ? PlatformView.ScrollViewer.ZoomFactor : 1f;
        _renderedZoom = z < 0.0001f ? 1f : z;
        RenderVisible();
    }

    // Ctrl+Scroll: zoom programático via mouse wheel
    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(PlatformView).Properties;
        if (props.IsHorizontalMouseWheel) return;

        // WinUI 3: verifica Ctrl via InputKeyboardSource
        bool ctrlDown = false;
        try
        {
            var ks = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                global::Windows.System.VirtualKey.Control);
            ctrlDown = (ks & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        }
        catch { /* InputKeyboardSource pode não estar disponível em todos os contextos */ }

        if (!ctrlDown) return;

        // Passo PROPORCIONAL ao delta: um notch de mouse vale 120 (→ fator 1.15). Sem isso,
        // um touchpad de precisão dispara dezenas de micro-eventos por gesto e, aplicando 1.15
        // fixo a cada um, o zoom dispara muito além do pretendido (1.15^n).
        double steps  = props.MouseWheelDelta / 120.0;
        double factor = Math.Pow(1.15, steps);
        if (PlatformView is not null)
        {
            float newZ = Math.Clamp(
                PlatformView.ScrollViewer.ZoomFactor * (float)factor,
                PlatformView.ScrollViewer.MinZoomFactor,
                PlatformView.ScrollViewer.MaxZoomFactor);
            ZoomToCenter(newZ, animate: false);
        }
        e.Handled = true;
    }

    // Aplica uma nova escala ANCORANDO o ponto central da viewport — sem isso o ChangeView
    // mantém o VerticalOffset numérico e, como o conteúdo escala, o ponto visível desliza
    // (o documento "rola" para outra página ao ampliar). Recalcula o offset para que o que
    // está no centro continue no centro.
    private void ZoomToCenter(float newZ, bool animate)
    {
        if (PlatformView is null) return;
        var sv = PlatformView.ScrollViewer;
        float z0 = sv.ZoomFactor;
        if (z0 < 0.0001f) z0 = 1f;

        // Ponto central atual em coordenadas BASE (não escaladas).
        double centerBase = (sv.VerticalOffset + sv.ViewportHeight / 2.0) / z0;
        // Offset que recoloca esse mesmo ponto no centro após a nova escala.
        double newOffset  = centerBase * newZ - sv.ViewportHeight / 2.0;
        if (newOffset < 0) newOffset = 0;

        sv.ChangeView(null, newOffset, newZ, disableAnimation: !animate);
    }

    // ── Property sync ─────────────────────────────────────────────────────────

    private void SyncPage()
    {
        if (_syncingPage || PlatformView is null || VirtualView is null
            || _pageOffsets.Length == 0) return;
        int page = Math.Clamp(VirtualView.CurrentPage, 0, _pageOffsets.Length - 1);
        // ChangeView usa o espaço escalado pelo zoom; _pageOffsets é não-escalado.
        float zoom = PlatformView.ScrollViewer.ZoomFactor;
        if (zoom < 0.0001f) zoom = 1f;
        _syncingPage = true;
        PlatformView.ScrollViewer.ChangeView(null, _pageOffsets[page] * zoom, null, disableAnimation: false);
        _syncingPage = false;
    }

    private void SyncZoom()
    {
        if (_syncingZoom || PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        // Ancora no centro (igual ao Ctrl+roda) e ANIMA a transição — o slider/API dá saltos
        // grandes (ex.: 10%), e animar evita o "pulo"/pisca da troca instantânea de escala.
        ZoomToCenter((float)VirtualView.ZoomFactor, animate: true);
        _syncingZoom = false;
    }

    // No Windows o zoom mínimo é 50% (não o MinZoom de 90% das demais plataformas).
    // Zoom mínimo = 100% no Windows: o "ajuste" base já é 50% da largura; dar zoom out abaixo
    // disso deixaria a página pequena demais. Respeita um MinZoom maior, se o consumidor pedir.
    private const float WindowsMinZoom = 1.0f;

    private void ApplyZoomLimits()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ScrollViewer.MinZoomFactor = Math.Max(WindowsMinZoom, (float)VirtualView.MinZoom);
        PlatformView.ScrollViewer.MaxZoomFactor = (float)VirtualView.MaxZoom;
    }

    private void ApplyCache()
    {
        if (VirtualView is null) return;
        long mb = VirtualView.MaxCacheMB > 0 ? VirtualView.MaxCacheMB : 200;
        var old = _cache;
        _cache = new PdfWinLruCache(mb * 1024 * 1024);
        // Dispose adia a liberação de streams com lease ativo (não corre com decode em uso).
        old?.Dispose();
    }

    private void ApplySpacing()
    {
        if (PlatformView is null || VirtualView is null || _pageOffsets.Length == 0) return;
        // Recalcula offsets com novo spacing
        if (_pdfDoc is not null) InitVirtualCanvas((int)_pdfDoc.PageCount);
    }

    private void ApplyZoomEnabled()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ScrollViewer.ZoomMode = VirtualView.IsPinchZoomEnabled
            ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    // ── Barra de miniaturas (somente Windows) ──────────────────────────────────
    private void ApplyThumbnailBar()
    {
        if (PlatformView is null || VirtualView is null) return;
        var host = PlatformView.ThumbnailHost;
        var list = PlatformView.ThumbnailList;

        if (!VirtualView.EnableThumbnailBar || _pdfDoc is null)
        {
            host.Visibility  = global::Microsoft.UI.Xaml.Visibility.Collapsed;
            list.ItemsSource = null;
            _thumbItems      = null;
            return;
        }

        int count = (int)_pdfDoc.PageCount;
        // Largura fixa da miniatura (igual ao DestinationWidth do render → imagem nítida e
        // sem reescala). A altura segue a proporção real de cada página (retrato/paisagem).
        const double thumbW = 130;
        var items = new List<PdfThumbItem>(count);
        for (int i = 0; i < count; i++)
            items.Add(new PdfThumbItem(i, thumbW, thumbW * PageRatio(i)));
        _thumbItems       = items;
        list.ItemsSource  = items;
        int cur = Math.Clamp(VirtualView.CurrentPage, 0, Math.Max(0, count - 1));
        list.SelectedIndex = cur;
        HighlightThumb(cur);
        host.Visibility   = global::Microsoft.UI.Xaml.Visibility.Visible;
    }

    // Marca a página atual na sidebar (borda/rótulo azul) — independente do realce de seleção.
    private void HighlightThumb(int page)
    {
        var items = _thumbItems;
        if (items is null) return;
        for (int i = 0; i < items.Count; i++)
            items[i].IsCurrent = (i == page);
    }

    // Proporção altura/largura da página idx. Reaproveita o layout já calculado
    // (_pageHeights/_pageWidth) quando disponível; senão consulta o tamanho real da página.
    private double PageRatio(int idx)
    {
        if (_pageWidth > 0 && idx < _pageHeights.Length && _pageHeights[idx] > 0)
            return _pageHeights[idx] / _pageWidth;
        if (_pdfDoc is not null && (uint)idx < _pdfDoc.PageCount)
        {
            using var p = _pdfDoc.GetPage((uint)idx);
            return p.Size.Height / Math.Max(1, p.Size.Width);
        }
        return 1.414; // A4 retrato como fallback
    }

    // Renderiza a miniatura sob demanda (virtualização do ListView).
    private void OnThumbChanging(
        global::Microsoft.UI.Xaml.Controls.ListViewBase sender,
        global::Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is PdfThumbItem item && item.Image is null)
            _ = RenderThumbAsync(item);
    }

    // Clique numa miniatura navega para a página (CurrentPage → SyncPage faz o scroll).
    private void OnThumbClick(object sender, global::Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
    {
        if (e.ClickedItem is PdfThumbItem item && VirtualView is not null)
        {
            // Destaca a miniatura IMEDIATAMENTE (borda azul) — sem isso, a seleção só aparecia
            // ao FIM do scroll (no OnViewChanged), dando a impressão de que a página carrega
            // antes de selecionar. Aqui o feedback é instantâneo; o scroll vem em seguida.
            if (PlatformView is not null) PlatformView.ThumbnailList.SelectedIndex = item.PageIndex;
            HighlightThumb(item.PageIndex);
            VirtualView.CurrentPage = item.PageIndex;
        }
    }

    private async Task RenderThumbAsync(PdfThumbItem item)
    {
        var doc = _pdfDoc;
        if (doc is null || (uint)item.PageIndex >= doc.PageCount) return;
        try
        {
            using var page = doc.GetPage((uint)item.PageIndex);
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 130 });
            stream.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);   // decode síncrono dentro do await; seguro dispor após
            stream.Dispose();
            item.Image = bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfViewer] Thumb({item.PageIndex}): {ex.Message}");
        }
    }

    private static global::Windows.UI.Color ToWinColor(Color c) =>
        global::Windows.UI.Color.FromArgb(
            (byte)(c.Alpha * 255), (byte)(c.Red * 255),
            (byte)(c.Green * 255), (byte)(c.Blue * 255));
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfWinContainer — Grid com ScrollViewer + Canvas virtual
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfWinContainer : global::Microsoft.UI.Xaml.Controls.Grid
{
    public   readonly ScrollViewer                                ScrollViewer;
    internal readonly global::Microsoft.UI.Xaml.Controls.Canvas   PagesCanvas;
    public   readonly global::Microsoft.UI.Xaml.Controls.ListView ThumbnailList;
    public   readonly global::Microsoft.UI.Xaml.Controls.Grid     ThumbnailHost;   // sidebar "Páginas"

    // Elementos de "chrome" da barra de miniaturas que seguem o tema (claro/escuro).
    private readonly global::Microsoft.UI.Xaml.Controls.TextBlock _thumbHeaderText;
    private readonly WinBorder                                    _thumbHeaderDivider;
    private readonly WinBorder                                    _thumbVDivider;

    private static global::Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(0xFF, r, g, b));

    public PdfWinContainer()
    {
        // O PdfViewer não tem modo escuro: força toda a subárvore nativa (sidebar de
        // miniaturas, divisores, rótulos, chrome das scrollbars) para o tema CLARO,
        // ignorando o tema do sistema. Sem isto, ActualTheme acompanha o Windows e a
        // barra de páginas/scrollbars ficavam escuras destoando do deck claro.
        RequestedTheme = global::Microsoft.UI.Xaml.ElementTheme.Light;

        // Coluna 0: sidebar de páginas (Auto/colapsável). Coluna 1: visualizador.
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = global::Microsoft.UI.Xaml.GridLength.Auto });
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = new global::Microsoft.UI.Xaml.GridLength(1, global::Microsoft.UI.Xaml.GridUnitType.Star) });

        // ── Sidebar de páginas (fundo claro, estilo Acrobat/Edge) ──
        ThumbnailList = new global::Microsoft.UI.Xaml.Controls.ListView
        {
            SelectionMode      = global::Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            Background         = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(global::Microsoft.UI.Colors.Transparent),
            Padding            = new WinThickness(0, 2, 0, 8),
            ItemTemplate       = (global::Microsoft.UI.Xaml.DataTemplate)
                                     global::Microsoft.UI.Xaml.Markup.XamlReader.Load(ThumbItemTemplateXaml),
            ItemContainerStyle = (global::Microsoft.UI.Xaml.Style)
                                     global::Microsoft.UI.Xaml.Markup.XamlReader.Load(ThumbItemContainerStyleXaml),
        };
        // Remove o realce de seleção padrão (a página atual é marcada por BORDA AZUL no template).
        var clear = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver",
                                    "ListViewItemBackgroundSelectedPressed", "ListViewItemBackgroundSelectedDisabled" })
            ThumbnailList.Resources[key] = clear;
        // Barra de rolagem da sidebar sempre visível/ativa (não some no hover-out).
        ScrollViewer.SetVerticalScrollBarVisibility(ThumbnailList, WinScrollBarVis.Visible);
        global::Microsoft.UI.Xaml.Controls.Grid.SetRow(ThumbnailList, 1);

        // Cabeçalho "Páginas"
        _thumbHeaderText = new global::Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text              = "Páginas",
            VerticalAlignment = WinVAlign.Center,
            FontSize          = 13,
            FontWeight        = global::Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var header = new WinBorder
        {
            Height  = 42,
            Padding = new WinThickness(14, 0, 14, 0),
            Child   = _thumbHeaderText,
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetRow(header, 0);
        _thumbHeaderDivider = new WinBorder
        {
            Height            = 1,
            VerticalAlignment = WinVAlign.Bottom,
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetRow(_thumbHeaderDivider, 0);

        ThumbnailHost = new global::Microsoft.UI.Xaml.Controls.Grid
        {
            Width      = 210,
            Visibility = global::Microsoft.UI.Xaml.Visibility.Collapsed,
        };
        ThumbnailHost.RowDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.RowDefinition
            { Height = global::Microsoft.UI.Xaml.GridLength.Auto });
        ThumbnailHost.RowDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.RowDefinition
            { Height = new global::Microsoft.UI.Xaml.GridLength(1, global::Microsoft.UI.Xaml.GridUnitType.Star) });
        ThumbnailHost.Children.Add(ThumbnailList);
        ThumbnailHost.Children.Add(header);
        ThumbnailHost.Children.Add(_thumbHeaderDivider);

        // Divisor vertical entre a sidebar e o visualizador.
        _thumbVDivider = new WinBorder
        {
            Width               = 1,
            HorizontalAlignment = WinHAlign.Right,
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetRow(_thumbVDivider, 0);
        global::Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(_thumbVDivider, 2);
        ThumbnailHost.Children.Add(_thumbVDivider);
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ThumbnailHost, 0);

        // HorizontalAlignment=Center: o ScrollViewer centraliza o canvas (largura da página)
        // quando há folga e dá scroll ao ampliar além da viewport — margem que não escala.
        PagesCanvas = new global::Microsoft.UI.Xaml.Controls.Canvas
        {
            HorizontalAlignment = WinHAlign.Center,
            VerticalAlignment   = WinVAlign.Top,
        };

        ScrollViewer = new ScrollViewer
        {
            ZoomMode                      = ZoomMode.Enabled,
            HorizontalScrollMode          = WinScrollMode.Enabled,
            HorizontalScrollBarVisibility = WinScrollBarVis.Auto,
            // Barra de rolagem vertical sempre visível/ativa (não some quando o ponteiro sai).
            VerticalScrollBarVisibility   = WinScrollBarVis.Visible,
            HorizontalContentAlignment    = WinHAlign.Center,
            // Deck CLARO (estilo Acrobat/Edge) — a folha branca se destaca com leve sombra.
            Background                    = Brush(0xE6, 0xE6, 0xE8),
            Content                       = PagesCanvas,
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ScrollViewer, 1);

        Children.Add(ThumbnailHost);
        Children.Add(ScrollViewer);

        // A barra de miniaturas segue o tema (claro/escuro). Aplica o estado inicial e
        // reage a mudanças de tema em runtime.
        ApplyThumbnailTheme();
        Loaded             += (_, __) => ApplyThumbnailTheme();
        ActualThemeChanged += (_, __) => ApplyThumbnailTheme();
    }

    // Pinta o "chrome" da barra de miniaturas conforme o tema atual e notifica os itens.
    internal void ApplyThumbnailTheme()
    {
        bool dark = ActualTheme == global::Microsoft.UI.Xaml.ElementTheme.Dark;

        ThumbnailHost.Background       = dark ? Brush(0x1C, 0x1C, 0x1E) : Brush(0xFA, 0xFA, 0xFA);
        _thumbHeaderText.Foreground    = dark ? Brush(0xEB, 0xEB, 0xEF) : Brush(0x3C, 0x3C, 0x42);
        _thumbHeaderDivider.Background = dark ? Brush(0x3A, 0x3A, 0x3C) : Brush(0xE2, 0xE2, 0xE2);
        _thumbVDivider.Background      = dark ? Brush(0x3A, 0x3A, 0x3C) : Brush(0xD8, 0xD8, 0xD8);

        PdfThumbItem.DarkTheme = dark;
        if (ThumbnailList.ItemsSource is global::System.Collections.IEnumerable items)
            foreach (var it in items)
                (it as PdfThumbItem)?.RefreshTheme();
    }

    // Template do item da barra: folha branca com a miniatura + número da página.
    // O Border tem a dimensão da PÁGINA (ThumbWidth×ThumbHeight, com a proporção real de cada
    // página) e serve de placeholder branco ENQUANTO a miniatura ainda não renderizou. Sem
    // dimensões, o Image com Stretch=Uniform e Source=null mede largura 0, o Border colapsa e
    // aparece o fundo escuro do ListView ao rolar. Com a folha dimensionada, o usuário vê a
    // folha branca na proporção certa de imediato e a imagem preenche depois.
    private const string ThumbItemTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
          "<StackPanel Margin=\"0,6,0,2\">" +
            "<Border Background=\"White\" CornerRadius=\"2\" " +
                   "BorderBrush=\"{Binding BorderBrush}\" BorderThickness=\"{Binding BorderThickness}\" " +
                   "Width=\"{Binding ThumbWidth}\" Height=\"{Binding ThumbHeight}\" HorizontalAlignment=\"Center\">" +
              "<Image Source=\"{Binding Image}\" Stretch=\"Uniform\"/>" +
            "</Border>" +
            "<TextBlock Text=\"{Binding Label}\" HorizontalAlignment=\"Center\" " +
                      "FontSize=\"11\" Margin=\"0,4,0,4\" Foreground=\"{Binding LabelBrush}\"/>" +
          "</StackPanel>" +
        "</DataTemplate>";

    // Container do item: remove padding/realce padrão para a borda azul do template ser o
    // único indicador da página atual (e ocupar a largura toda, centralizando a miniatura).
    private const string ThumbItemContainerStyleXaml =
        "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
               "TargetType=\"ListViewItem\">" +
          "<Setter Property=\"Padding\" Value=\"0\"/>" +
          "<Setter Property=\"MinHeight\" Value=\"0\"/>" +
          "<Setter Property=\"Margin\" Value=\"0\"/>" +
          "<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\"/>" +
          "<Setter Property=\"Background\" Value=\"Transparent\"/>" +
        "</Style>";
}

// Item da barra de miniaturas. A miniatura (Image) é renderizada sob demanda quando o
// ListView realiza o container (virtualização), e notifica o binding ao ficar pronta.
internal sealed class PdfThumbItem : global::System.ComponentModel.INotifyPropertyChanged
{
    // Página atual: azul accent — legível tanto no tema claro quanto no escuro.
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush CurrentStroke =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x73, 0xE8));
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush CurrentText =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x73, 0xE8));
    // Estado normal: borda/rótulo seguem o tema (claro vs escuro).
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush NormalStrokeLight =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xD0));
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush NormalStrokeDark =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x48, 0x48, 0x4A));
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush NormalTextLight =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x60, 0x60, 0x66));
    private static readonly global::Microsoft.UI.Xaml.Media.SolidColorBrush NormalTextDark =
        new(global::Windows.UI.Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA6));

    // Tema atual da barra, definido pelo container conforme ActualTheme.
    internal static bool DarkTheme;

    public int    PageIndex   { get; }
    public string Label       { get; }
    // Dimensões da folha (placeholder e moldura da miniatura) com a PROPORÇÃO REAL da página:
    // largura fixa, altura proporcional. Mantém retrato/paisagem corretos por página.
    public double ThumbWidth  { get; }
    public double ThumbHeight { get; }

    public PdfThumbItem(int pageIndex, double width, double height)
    {
        PageIndex   = pageIndex;
        Label       = (pageIndex + 1).ToString();
        ThumbWidth  = width;
        ThumbHeight = height;
    }

    // Página atual → borda/rótulo azul (estilo Acrobat). Demais → cinza.
    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            Raise(nameof(BorderBrush));
            Raise(nameof(BorderThickness));
            Raise(nameof(LabelBrush));
        }
    }

    public global::Microsoft.UI.Xaml.Media.Brush BorderBrush     => _isCurrent ? CurrentStroke : (DarkTheme ? NormalStrokeDark : NormalStrokeLight);
    public global::Microsoft.UI.Xaml.Thickness   BorderThickness => _isCurrent ? new(2) : new(1);
    public global::Microsoft.UI.Xaml.Media.Brush LabelBrush      => _isCurrent ? CurrentText : (DarkTheme ? NormalTextDark : NormalTextLight);

    // Reavalia BorderBrush/LabelBrush quando o tema muda (chamado pelo container).
    internal void RefreshTheme()
    {
        Raise(nameof(BorderBrush));
        Raise(nameof(LabelBrush));
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(name));

    private global::Microsoft.UI.Xaml.Media.ImageSource? _image;
    public global::Microsoft.UI.Xaml.Media.ImageSource? Image
    {
        get => _image;
        set
        {
            _image = value;
            PropertyChanged?.Invoke(this,
                new global::System.ComponentModel.PropertyChangedEventArgs(nameof(Image)));
        }
    }

    public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfWinLruCache — LRU de streams com limite em bytes
// ─────────────────────────────────────────────────────────────────────────────

// Cada entrada tem ref-count: enquanto um stream estiver "em uso" (sendo aplicado
// a um BitmapImage via SetSourceAsync assíncrono) ele NÃO pode ser disposed pelo
// LRU/Trim. A disposição é adiada até o último Release(). Isso evita a
// ObjectDisposedException / página em branco quando EvictLru roda durante o decode.
internal sealed class PdfWinLruCache : IDisposable
{
    internal sealed class Entry
    {
        public LinkedListNode<int>        Node    = null!;
        public InMemoryRandomAccessStream Stream  = null!;
        public long                       Bytes;
        public int                        RefCount;
        public bool                       Evicted;   // removido do mapa, aguardando último Release
    }

    // Handle de lease: enquanto não-disposed mantém o stream vivo. O Dispose decrementa
    // o ref-count diretamente na Entry (não re-procura por idx, evitando vazamento caso a
    // entrada já tenha sido evicted/substituída no meio-tempo).
    internal sealed class Lease : IDisposable
    {
        private readonly PdfWinLruCache _cache;
        private readonly Entry               _entry;
        private          bool                _released;
        internal Lease(PdfWinLruCache cache, Entry entry) { _cache = cache; _entry = entry; }
        public InMemoryRandomAccessStream Stream => _entry.Stream;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _cache.ReleaseEntry(_entry);
        }
    }

    private readonly long _maxBytes;
    private          long _usedBytes;
    private readonly Dictionary<int, Entry> _map   = new();
    private readonly LinkedList<int>        _order = new();
    private readonly object                 _lock  = new();
    private          bool                   _disposed;

    public PdfWinLruCache(long maxBytes)
        => _maxBytes = Math.Max(10L * 1024 * 1024, maxBytes);

    // Retorna um lease (ref-count incrementado) ou null se ausente. O chamador DEVE
    // dispor o lease ao terminar de usar o stream (após ApplyStreamAsync).
    public Lease? TryGetLease(int idx)
    {
        lock (_lock)
        {
            if (!_disposed && _map.TryGetValue(idx, out var e))
            {
                _order.Remove(e.Node);
                _order.AddFirst(e.Node);
                e.RefCount++;
                return new Lease(this, e);
            }
        }
        return null;
    }

    // Insere o stream e já o entrega com lease (RefCount=1). Se o stream sozinho exceder
    // o limite, NÃO é inserido (evita thrashing / usedBytes ultrapassar o limite): é
    // disposed e retorna null.
    public Lease? PutAndLease(int idx, InMemoryRandomAccessStream stream)
    {
        long bytes = (long)stream.Size;
        lock (_lock)
        {
            if (_disposed) { stream.Dispose(); return null; }

            if (bytes > _maxBytes)
            {
                // Um único stream maior que o cache inteiro: não cacheia.
                stream.Dispose();
                return null;
            }

            if (_map.TryGetValue(idx, out var existing))
            {
                _order.Remove(existing.Node);
                _usedBytes -= existing.Bytes;
                _map.Remove(idx);
                existing.Evicted = true;
                if (existing.RefCount <= 0) existing.Stream.Dispose();
            }

            while (_usedBytes + bytes > _maxBytes && _order.Count > 0) EvictLruLocked();

            var node  = _order.AddFirst(idx);
            var entry = new Entry { Node = node, Stream = stream, Bytes = bytes, RefCount = 1 };
            _map[idx] = entry;
            _usedBytes += bytes;
            return new Lease(this, entry);
        }
    }

    private void ReleaseEntry(Entry e)
    {
        lock (_lock)
        {
            if (e.RefCount > 0) e.RefCount--;
            // Só dispõe quando a entrada já saiu do cache E não há mais leases ativos.
            if (e.Evicted && e.RefCount <= 0) e.Stream.Dispose();
        }
    }

    // Esvazia todo o cache (ex.: mudança de escala de render → streams obsoletos). Respeita os
    // leases: stream com lease ativo só é disposto no último Release (Evicted=true marca isso).
    public void EvictAll()
    {
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var (_, e) in _map)
            {
                e.Evicted = true;
                if (e.RefCount <= 0) e.Stream.Dispose();
            }
            _map.Clear(); _order.Clear(); _usedBytes = 0;
        }
    }

    public void TrimToWindow(int start, int end)
    {
        lock (_lock)
        {
            foreach (var k in _map.Keys.Where(k => k < start || k > end).ToList())
            {
                if (!_map.TryGetValue(k, out var e)) continue;
                _order.Remove(e.Node);
                _usedBytes -= e.Bytes;
                _map.Remove(k);
                e.Evicted = true;
                // Só dispõe se não houver leases ativos; caso contrário aguarda o último Release.
                if (e.RefCount <= 0) e.Stream.Dispose();
            }
        }
    }

    private void EvictLruLocked()
    {
        var last = _order.Last;
        if (last is null) return;
        int k = last.Value;
        _order.RemoveLast();
        if (_map.TryGetValue(k, out var e))
        {
            _usedBytes -= e.Bytes;
            _map.Remove(k);
            e.Evicted = true;
            if (e.RefCount <= 0) e.Stream.Dispose();
        }
    }

    public long UsedBytes { get { lock (_lock) return _usedBytes; } }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            // Dispõe streams sem lease ativo; os com lease serão disposed no último Release.
            foreach (var (_, e) in _map)
            {
                e.Evicted = true;
                if (e.RefCount <= 0) e.Stream.Dispose();
            }
            _map.Clear(); _order.Clear();
        }
    }
}
