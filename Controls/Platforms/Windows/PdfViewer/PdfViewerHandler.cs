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
    private bool                          _syncingPage;
    private bool                          _syncingZoom;
    private float                         _lastZoom = 1f;   // último ZoomFactor visto (detecta zoom vs scroll)
    private double[]                      _pageOffsets    = Array.Empty<double>();
    private double[]                      _pageHeights    = Array.Empty<double>();
    private double                        _pageWidth;   // largura base da folha (metade da viewport:
                                                        // em 200% ela preenche a largura toda)
    private string?                       _tempPdfPath;

    // Guarda quais páginas têm Image control ativo no canvas
    private readonly HashSet<int>         _activeImages   = new();
    private readonly object               _activeImgLock  = new();

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
        PlatformView.PagesCanvas.Children.Clear();
        _pageOffsets = Array.Empty<double>();
        _pageHeights = Array.Empty<double>();
        lock (_activeImgLock) _activeImages.Clear();
        _pdfDoc = null;

        // Garante o cache mesmo sem o consumidor setar MaxCacheMB.
        EnsureCache();

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

        // Usa a largura da ÁREA DE VISUALIZAÇÃO (ScrollViewer), não do Grid inteiro — senão a
        // barra de miniaturas é incluída e a página fica larga demais. A página recebe uma
        // margem lateral e é centralizada, deixando o deck cinza aparecer dos dois lados.
        double viewportW = PlatformView.ScrollViewer.ActualWidth > 1 ? PlatformView.ScrollViewer.ActualWidth : 800;
        // Em zoom 1 (100%) a página ocupa 50% da largura disponível (centralizada, com o deck
        // cinza nas laterais). Renderizada em alta resolução → nítida; o zoom in amplia a partir daí.
        _pageWidth       = Math.Max(50, viewportW * 0.5);
        double spacing   = VirtualView.PageSpacing;

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

    private void RenderVisible()
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
        foreach (int idx in order.Distinct())
            if (!HasImage(idx)) _ = RenderPageAsync(idx, cts.Token);
    }

    // Largura-alvo de renderização da página em PIXELS FÍSICOS: largura da viewport × DPI do
    // monitor (RasterizationScale) × RenderScale. Considerar o DPI é o que dá a nitidez do
    // Chrome em telas 125–150%. Teto para não estourar a memória do cache em DPI muito alto.
    private double RenderTargetWidth()
    {
        double vpW    = PlatformView is not null && PlatformView.ScrollViewer.ActualWidth > 1
                            ? PlatformView.ScrollViewer.ActualWidth : 800;
        double raster = PlatformView?.XamlRoot?.RasterizationScale ?? 1.0;
        if (raster < 1.0) raster = 1.0;
        double rScale = VirtualView?.RenderScale ?? 1.5;
        return Math.Min(vpW * raster * rScale, 4800);
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
            Tag        = idx,
            Background = bgBrush,
            Width      = _pageWidth,
            Height     = _pageHeights[idx],
            Child      = ni,
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
            return;
        }
        RenderVisible();

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

        // Ao dar zoom, o VerticalOffset muda e o cálculo de página "pularia" de página
        // (avançar/voltar). Só atualizamos a página em SCROLL puro, não durante o zoom.
        if (zoomChanged) return;

        // Page changed?
        if (_syncingPage || _pageOffsets.Length == 0 || PlatformView is null) return;
        // VerticalOffset está no espaço escalado; converte para o espaço do canvas (ver RenderVisible).
        float  pgZoom    = PlatformView.ScrollViewer.ZoomFactor;
        if (pgZoom < 0.0001f) pgZoom = 1f;
        double scrollTop = PlatformView.ScrollViewer.VerticalOffset / pgZoom;
        int page = 0;
        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            if (_pageOffsets[i] <= scrollTop + 1) page = i;
            else break;
        }
        _syncingPage = true;
        VirtualView?.RaisePageChanged(page);
        _syncingPage = false;

        // Mantém a miniatura da página atual destacada e visível na barra (quando ativa).
        var tl = PlatformView.ThumbnailList;
        if (tl.Visibility == global::Microsoft.UI.Xaml.Visibility.Visible && tl.SelectedIndex != page)
        {
            tl.SelectedIndex = page;
            if (tl.SelectedItem is not null) tl.ScrollIntoView(tl.SelectedItem);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pdfDoc is null) return;
        // Recalcula layout quando o container é redimensionado
        InitVirtualCanvas((int)_pdfDoc.PageCount);
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

        double delta = props.MouseWheelDelta > 0 ? 1.15 : 1.0 / 1.15;
        if (PlatformView is not null)
        {
            float newZ = Math.Clamp(
                PlatformView.ScrollViewer.ZoomFactor * (float)delta,
                PlatformView.ScrollViewer.MinZoomFactor,
                PlatformView.ScrollViewer.MaxZoomFactor);
            PlatformView.ScrollViewer.ChangeView(null, null, newZ, disableAnimation: false);
        }
        e.Handled = true;
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
        PlatformView.ScrollViewer.ChangeView(null, null, (float)VirtualView.ZoomFactor, disableAnimation: false);
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
        var list = PlatformView.ThumbnailList;

        if (!VirtualView.EnableThumbnailBar || _pdfDoc is null)
        {
            list.Visibility  = global::Microsoft.UI.Xaml.Visibility.Collapsed;
            list.ItemsSource = null;
            return;
        }

        int count = (int)_pdfDoc.PageCount;
        var items = new List<PdfThumbItem>(count);
        for (int i = 0; i < count; i++) items.Add(new PdfThumbItem(i));
        list.ItemsSource  = items;
        list.SelectedIndex = Math.Clamp(VirtualView.CurrentPage, 0, Math.Max(0, count - 1));
        list.Visibility   = global::Microsoft.UI.Xaml.Visibility.Visible;
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
            VirtualView.CurrentPage = item.PageIndex;
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

    public PdfWinContainer()
    {
        // Coluna 0: barra de miniaturas (Auto/colapsável). Coluna 1: visualizador.
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = global::Microsoft.UI.Xaml.GridLength.Auto });
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = new global::Microsoft.UI.Xaml.GridLength(1, global::Microsoft.UI.Xaml.GridUnitType.Star) });

        ThumbnailList = new global::Microsoft.UI.Xaml.Controls.ListView
        {
            Width              = 272,
            Visibility         = global::Microsoft.UI.Xaml.Visibility.Collapsed,
            SelectionMode      = global::Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            // Barra estreita e escura (estilo do visualizador do Chrome).
            Background         = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(
                                     global::Windows.UI.Color.FromArgb(0xFF, 0x3C, 0x3C, 0x3C)),
            ItemTemplate       = (global::Microsoft.UI.Xaml.DataTemplate)
                                     global::Microsoft.UI.Xaml.Markup.XamlReader.Load(ThumbItemTemplateXaml),
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ThumbnailList, 0);

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
            VerticalScrollBarVisibility   = WinScrollBarVis.Auto,
            HorizontalContentAlignment    = WinHAlign.Center,
            // Deck cinza escuro (estilo do visualizador do Chrome).
            Background                    = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(
                                                global::Windows.UI.Color.FromArgb(0xFF, 0x52, 0x56, 0x59)),
            Content                       = PagesCanvas,
        };
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ScrollViewer, 1);

        Children.Add(ThumbnailList);
        Children.Add(ScrollViewer);
    }

    // Template do item da barra: folha branca com a miniatura + número da página.
    private const string ThumbItemTemplateXaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
          "<StackPanel Margin=\"2,5,2,0\">" +
            "<Border Background=\"White\" BorderBrush=\"#1A1A1A\" BorderThickness=\"1\" HorizontalAlignment=\"Center\">" +
              "<Image Source=\"{Binding Image}\" Stretch=\"Uniform\" Height=\"150\"/>" +
            "</Border>" +
            "<TextBlock Text=\"{Binding Label}\" HorizontalAlignment=\"Center\" " +
                      "FontSize=\"11\" Margin=\"0,3,0,4\" Foreground=\"#CCCCCC\"/>" +
          "</StackPanel>" +
        "</DataTemplate>";
}

// Item da barra de miniaturas. A miniatura (Image) é renderizada sob demanda quando o
// ListView realiza o container (virtualização), e notifica o binding ao ficar pronta.
internal sealed class PdfThumbItem : global::System.ComponentModel.INotifyPropertyChanged
{
    public int    PageIndex { get; }
    public string Label     { get; }

    public PdfThumbItem(int pageIndex)
    {
        PageIndex = pageIndex;
        Label     = (pageIndex + 1).ToString();
    }

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
