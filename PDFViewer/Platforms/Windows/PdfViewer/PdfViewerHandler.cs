// Platforms/Windows/PdfViewer/PdfViewerHandler.cs
//
// Motor: PDFium via PDFiumCore (binding P/Invoke; mesmo motor do Edge/Chrome). O Windows.Data.Pdf
// nativo rasteriza conteúdo vetorial/texto de certos PDFs em BRANCO (só desenha imagens embutidas);
// PDFium rende todos corretamente. O documento fica ABERTO (sem reabrir por página); a página é
// rasterizada para um buffer BGRA (síncrono/CPU-bound → Task.Run) e codificada em PNG via WIC
// (nativo do Windows) para alimentar o pipeline de cache/decode existente. PDFiumCore também
// expõe a camada de texto (FPDFText_*) usada pela seleção/busca.
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
using Microsoft.UI.Xaml.Printing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using PDFiumCore;
using Windows.Graphics.Imaging;
using Windows.Graphics.Printing;
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
            [nameof(PdfViewer.Password)]            = (h, _) => h.LoadDocument(),
            [nameof(PdfViewer.CurrentPage)]         = (h, _) => h.SyncPage(),
            [nameof(PdfViewer.ZoomFactor)]          = (h, _) => h.SyncZoom(),
            [nameof(PdfViewer.MinZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxCacheMB)]          = (h, _) => h.ApplyCache(),
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.ScrollOrientation)]   = (h, _) => h.ApplyOrientation(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomEnabled(),
            [nameof(PdfViewer.RenderScale)]         = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => h.RenderVisible(),
            [nameof(PdfViewer.EnableThumbnailBar)]  = (h, _) => h.ApplyThumbnailBar(),
            [nameof(PdfViewer.ThumbnailBarPlacement)] = (h, _) => h.ApplyThumbnailBar(),
            [nameof(PdfViewer.ThumbnailBarTitleText)] = (h, _) => h.ApplyThumbnailTitle(),
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

    private PdfiumDoc?                    _pdfDoc;
    private PdfWinLruCache?          _cache;
    private CancellationTokenSource?      _loadCts;
    private CancellationTokenSource?      _prefetchCts;
    private CancellationTokenSource?      _zoomSettleCts;   // debounce: render só quando o zoom assenta
    // Serializa os renders: o PDFium (PDFtoImage) não é thread-safe; sem o gate, rasterizações
    // concorrentes corromperiam o estado nativo. Também limita o pico de CPU/memória.
    private readonly SemaphoreSlim        _renderGate = new(1, 1);
    // Serializa a DECODIFICAÇÃO (SetSourceAsync → BitmapImage). O decode do stream para bitmap
    // roda assíncrono em thread do WIC, então vários SetSourceAsync ficavam em voo ao mesmo tempo
    // (prefetch cria 6+ páginas por janela). Limitar a 2 decodes simultâneos contém o pico.
    private readonly SemaphoreSlim        _decodeGate = new(2, 2);
    private bool                          _syncingPage;
    private bool                          _syncingZoom;
    private float                         _lastZoom = 1f;   // último ZoomFactor visto (detecta zoom vs scroll)
    private long                          _lastWheelPageTick; // anti-rajada: 1 página por gesto de roda (ms, Environment.TickCount64)
    private float                         _renderedZoom = 1f; // zoom no qual as bitmaps atuais foram rasterizadas
    private double[]                      _pageOffsets    = Array.Empty<double>();
    private double[]                      _pageHeights    = Array.Empty<double>();   // altura EXIBIDA da folha (display)
    private double[]                      _pageRatios     = Array.Empty<double>();   // altura/largura por página, pré-medido em background
    private double                        _pageWidth;   // largura base da folha (metade da viewport:
                                                        // em 200% ela preenche a largura toda)
    // ── Eixo de scroll ────────────────────────────────────────────────────────
    // Vertical (padrão): folhas empilhadas em Y, largura fixa (_pageWidth), scroll contínuo.
    // Horizontal (paginado, tipo livro): cada página ocupa um "slot" da largura da viewport;
    // a folha é fit-page (cabe inteira, centralizada) com o deck cinza ao redor; snap por página.
    private bool                          _horizontal;
    private double[]                      _pageMain       = Array.Empty<double>();   // extensão no eixo de scroll (vert: altura; horiz: largura do slot)
    private double[]                      _pageDispW      = Array.Empty<double>();   // largura EXIBIDA da folha (display)
    private double                        _crossH;       // altura da viewport (modo horizontal: centraliza a folha em Y)
    private string?                       _tempPdfPath;
    private long                          _fileBytes;   // tamanho do PDF atual (heurística p/ RenderScale adaptativo)

    // Guarda quais páginas têm Image control ativo no canvas
    private readonly HashSet<int>         _activeImages   = new();
    private readonly object               _activeImgLock  = new();

    // Contagem de falhas de decode por página (re-render tardio). Manipulado só na UI thread.
    private readonly Dictionary<int, int> _decodeFailures = new();
    private const int                     MaxDecodeRetries = 3;

    // Teto de largura de rasterização (px físicos). ~50 MB/bitmap A4 — nítido e cabe na janela.
    private const double                  RenderCeiling = 3000;

    // ── Seleção de texto (mouse) ──────────────────────────────────────────────
    // v1: seleção dentro de UMA página (âncora..foco em índices de caractere do PDFium).
    private bool   _selecting;
    private int    _selPage   = -1;
    private int    _selAnchor = -1;
    private int    _selFocus  = -1;
    private string _selectedText = string.Empty;
    private const string SelRectTag  = "pdfsel";    // realce de seleção
    private const string FindRectTag = "pdffind";   // realce de busca

    // ── Busca ──────────────────────────────────────────────────────────────────
    private List<(int page, int index, int count)> _findHits = new();
    private int    _findCurrent = -1;
    private string _findTerm    = string.Empty;
    private CancellationTokenSource? _findCts;

    // Itens da barra de miniaturas (para destacar a página atual com borda azul)
    private List<PdfThumbItem>?           _thumbItems;

    public PdfViewerHandler() : base(Mapper, CommandMapper) { }

    protected override PdfWinContainer CreatePlatformView() => new();

    // ── Impressão ───────────────────────────────────────────────────────────────
    // Abre a UI de impressão do Windows (PrintManager) renderizando cada página do PDF.
    // O documento é reaberto do arquivo em disco para a impressão ter ciclo de vida próprio,
    // isolado do _pdfDoc usado na exibição (virtualização/cache).
    private async void Print()
    {
        string? path = _tempPdfPath ?? VirtualView?.Source;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            PdfViewerLog.Write("Pdf/Win", "Print: nenhum documento disponível.");
            return;
        }

        // HWND da janela atual — exigido pelo PrintManager do Windows App SDK (sem CoreWindow).
        var nativeWindow = VirtualView?.Window?.Handler?.PlatformView as global::Microsoft.UI.Xaml.Window;
        if (nativeWindow is null)
        {
            PdfViewerLog.Write("Pdf/Win", "Print: janela nativa indisponível.");
            return;
        }
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

        try
        {
            string job = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(job)) job = string.IsNullOrEmpty(VirtualView?.PrintJobName) ? "Document" : VirtualView!.PrintJobName;
            await new PdfWinPrintJob(hwnd, path, job).StartAsync();
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Win", $"Print ERRO: {ex.Message}");
        }
    }

    protected override void ConnectHandler(PdfWinContainer pv)
    {
        base.ConnectHandler(pv);
        pv.ScrollViewer.ViewChanged     += OnViewChanged;
        pv.SizeChanged                   += OnSizeChanged;
        // Roda do mouse: ligada ao PagesCanvas (filho do ScrollViewer) — o evento chega AQUI antes
        // de o ScrollViewer tratá-lo, então conseguimos interceptar e marcar Handled p/ paginar no
        // modo horizontal. Ligar no ScrollViewer não funciona (ele consome a roda internamente).
        pv.PagesCanvas.PointerWheelChanged += OnPointerWheel;
        pv.ThumbnailList.ContainerContentChanging += OnThumbChanging;
        pv.ThumbnailList.ItemClick                += OnThumbClick;

        // Seleção de texto: arraste do mouse sobre o canvas das páginas (toque continua rolando).
        pv.PagesCanvas.PointerPressed  += OnSelPointerPressed;
        pv.PagesCanvas.PointerMoved    += OnSelPointerMoved;
        pv.PagesCanvas.PointerReleased += OnSelPointerReleased;
        pv.PagesCanvas.RightTapped     += OnSelRightTapped;

        // Ctrl+C copia a seleção (sem depender de foco explícito).
        var copyAccel = new global::Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key       = global::Windows.System.VirtualKey.C,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        };
        copyAccel.Invoked += (_, e) => { CopySelection(); e.Handled = true; };
        pv.KeyboardAccelerators.Add(copyAccel);
        // Suprime o tooltip automático do atalho ("Ctrl+C") que o WinUI exibia sobre o visualizador.
        pv.KeyboardAcceleratorPlacementMode = global::Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        // Garante que o cache exista mesmo que o consumidor nunca sete MaxCacheMB
        // (caso contrário _cache fica null e nenhuma página renderiza).
        EnsureCache();
        ApplyThumbnailTitle();
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
        pv.PagesCanvas.PointerWheelChanged    -= OnPointerWheel;
        pv.ThumbnailList.ContainerContentChanging -= OnThumbChanging;
        pv.ThumbnailList.ItemClick                -= OnThumbClick;

        pv.PagesCanvas.PointerPressed  -= OnSelPointerPressed;
        pv.PagesCanvas.PointerMoved    -= OnSelPointerMoved;
        pv.PagesCanvas.PointerReleased -= OnSelPointerReleased;
        pv.PagesCanvas.RightTapped     -= OnSelRightTapped;

        _findCts?.Cancel(); _findCts?.Dispose(); _findCts = null;

        _cache?.Dispose(); _cache  = null;
        _renderGate.Dispose();
        _decodeGate.Dispose();
        _pdfDoc?.Dispose(); _pdfDoc = null;

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
        _pageRatios  = Array.Empty<double>();
        _pageMain    = Array.Empty<double>();
        _pageDispW   = Array.Empty<double>();
        _fileBytes   = 0;
        lock (_activeImgLock) _activeImages.Clear();
        _decodeFailures.Clear();
        _selecting = false; _selPage = _selAnchor = _selFocus = -1; _selectedText = string.Empty;
        _findCts?.Cancel(); _findHits = new(); _findCurrent = -1; _findTerm = string.Empty;
        _pdfDoc?.Dispose();   // fecha o handle PDFium do documento anterior
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

        var source   = VirtualView.Source;
        var stream   = VirtualView.PdfStream;
        var password = VirtualView.Password;
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
                // PDFium trabalha por caminho de arquivo: garante um path local. Para stream/URL
                // escreve um temp; para arquivo local usa o próprio Source (sem cópia/RAM extra).
                string path;

                if (stream is not null)
                {
                    var tp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    localTempPath = tp;
                    // O PdfStream pode vir posicionado no fim (já lido); rebobina se possível.
                    if (stream.CanSeek) stream.Position = 0;
                    await using (var fs = new FileStream(tp, FileMode.Create))
                        await stream.CopyToAsync(fs, cts.Token);
                    _fileBytes = SafeFileLength(tp);
                    path = tp;
                }
                else if (isUrl)
                {
                    using var http = PdfHttpClient.Create();
                    var bytes = await http.GetByteArrayAsync(source, cts.Token);
                    _fileBytes = bytes.LongLength;
                    var tp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    localTempPath = tp;
                    await System.IO.File.WriteAllBytesAsync(tp, bytes, cts.Token);
                    path = tp;
                }
                else
                {
                    _fileBytes = SafeFileLength(source!);
                    path = source!;
                }

                // Abre via PDFium (lê contagem e tamanhos das páginas — não rasteriza nada ainda).
                var doc = await Task.Run(() => new PdfiumDoc(path, password), cts.Token);

                if (cts.IsCancellationRequested)
                {
                    // Load cancelado: limpa o temp deste load sem tocar no campo compartilhado.
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
                int count = doc.PageCount;

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
        // Proporções de TODAS as páginas já vêm medidas do PdfiumDoc (GetPageSizes, no load) —
        // sem reabrir página a página (o que travava o load em PDFs de centenas de páginas).
        if (_pageRatios.Length != count)
        {
            var ratios = new double[count];
            for (int i = 0; i < count; i++) ratios[i] = _pdfDoc.Ratio(i);
            _pageRatios = ratios;
        }

        double spacing  = VirtualView.PageSpacing;
        _pageOffsets = new double[count];
        _pageHeights = new double[count];
        _pageMain    = new double[count];
        _pageDispW   = new double[count];

        if (_horizontal)
        {
            // ── Horizontal (paginado): cada página ocupa um SLOT da largura da viewport; a folha
            //    é fit-page (cabe inteira em slotW×viewportH, centralizada) com o deck ao redor.
            _crossH         = viewportH;
            double slotW    = viewportW;
            _pageWidth      = slotW;   // representativo (RenderTargetWidth usa PageDispW por página)
            double offsetH  = 0;
            for (int i = 0; i < count; i++)
            {
                double r = MeasuredRatio(i);             // altura/largura
                double w = slotW, h = slotW * r;         // fit por largura
                if (h > viewportH) { h = viewportH; w = viewportH / Math.Max(0.01, r); }  // fit por altura
                _pageDispW[i]   = Math.Max(50, w);
                _pageHeights[i] = Math.Max(50, h);
                _pageMain[i]    = slotW;
                _pageOffsets[i] = offsetH;
                offsetH        += slotW + spacing;
            }
            double totalW = Math.Max(0, count > 0 ? offsetH - spacing : 0);
            canvas.Width  = totalW;
            canvas.Height = viewportH;
        }
        else
        {
            // ── Vertical (contínuo): folhas empilhadas, largura fixa, ~80% da altura visível.
            const double visibleFraction = 0.80;
            double firstRatio = MeasuredRatio(0);
            double byWidth  = viewportW * 0.5;                              // limite de largura (deck nas laterais)
            double byHeight = viewportH / (visibleFraction * firstRatio);   // ~80% da altura visível
            _pageWidth      = Math.Max(50, Math.Min(byWidth, byHeight));

            double offset = 0;
            for (int i = 0; i < count; i++)
            {
                _pageOffsets[i] = offset;
                double h        = _pageWidth * MeasuredRatio(i);
                _pageHeights[i] = Math.Max(100, h);
                _pageDispW[i]   = _pageWidth;
                _pageMain[i]    = _pageHeights[i];
                offset         += _pageHeights[i] + spacing;
            }
            // Com count==0, offset==0 e (offset - spacing) seria negativo.
            double totalH = Math.Max(0, count > 0 ? offset - spacing : 0);
            // Canvas com a largura da PÁGINA; o ScrollViewer centraliza (margem que NÃO escala com o
            // zoom) e dá scroll quando o zoom faz a página exceder a viewport.
            canvas.Width  = _pageWidth;
            canvas.Height = totalH;
        }

        // Alinhamento horizontal do conteúdo no ScrollViewer:
        //  • Vertical: canvas ESTREITO (largura da página) → CENTRALIZA (deck cinza nas laterais).
        //  • Horizontal: canvas MAIS LARGO que a viewport (vários slots) → ESQUERDA, senão o
        //    ScrollViewer centraliza o conteúdo grande e o offset 0 cai no meio (página "jogada à
        //    direita"). Alinhado à esquerda, offset 0 = página 0; a centralização vem do SyncPage.
        var hAlign = _horizontal ? WinHAlign.Left : WinHAlign.Center;
        canvas.HorizontalAlignment = hAlign;
        PlatformView.ScrollViewer.HorizontalContentAlignment = hAlign;

        PlatformView.ScrollViewer.Content = canvas;
    }

    // ── Posicionamento por página (eixo-generalizado) ───────────────────────────
    // Vertical: folha em (0, offset), tamanho _pageWidth × _pageHeights[i].
    // Horizontal: folha fit-page CENTRALIZADA no slot — X = offset + (slotW - dispW)/2,
    //             Y = (viewportH - dispH)/2, tamanho _pageDispW[i] × _pageHeights[i].
    private double PageLeft(int i) =>
        _horizontal ? _pageOffsets[i] + (_pageMain[i] - _pageDispW[i]) / 2.0 : 0;
    private double PageTop(int i) =>
        _horizontal ? (_crossH - _pageHeights[i]) / 2.0 : _pageOffsets[i];
    private double PageDispW(int i) => (i >= 0 && i < _pageDispW.Length)  ? _pageDispW[i]  : _pageWidth;
    private double PageDispH(int i) => (i >= 0 && i < _pageHeights.Length) ? _pageHeights[i] : 0;
    private double PageMain(int i)  => (i >= 0 && i < _pageMain.Length)   ? _pageMain[i]   : 0;

    // Proporção (altura/largura) crua da página i, vinda da medição em background. Só abre a
    // página como fallback defensivo (ex.: cache ainda não populado). Usada no InitVirtualCanvas,
    // ANTES de _pageHeights existir — por isso não pode usar o PageRatio (que depende do layout).
    private double MeasuredRatio(int i)
    {
        if (i >= 0 && i < _pageRatios.Length) return _pageRatios[i];
        return _pdfDoc?.Ratio(i) ?? 1.414;   // A4 retrato como estimativa
    }

    // ── RenderVisible — renderiza / remove páginas conforme viewport ───────────

    // Janela de prefetch EFETIVA (páginas pré-renderizadas acima/abaixo da visível). A janela
    // RETIDA em memória = visível + above + below, e cada bitmap em alta resolução custa dezenas
    // de MB. Quando a largura de render está alta (escaneados / zoom), encolhe o prefetch para
    // conter o pico (evita OOM no decode → página branca). ComputeWindow e RenderVisible DEVEM
    // usar os mesmos valores, senão a janela ativa e a ordem de render divergem.
    private void EffectivePrefetch(out int above, out int below)
    {
        above = below = 0;
        if (VirtualView is null || !VirtualView.EnablePageCaching) return;
        above = Math.Max(0, VirtualView.PrefetchAbove);
        below = Math.Max(0, VirtualView.PrefetchBelow);

        double w = RenderTargetWidth(0);   // representativo (folha 0) só para o limiar do prefetch
        if (w > 2500)      { above = Math.Min(above, 0); below = Math.Min(below, 1); }
        else if (w > 1800) { above = Math.Min(above, 1); below = Math.Min(below, 2); }
    }

    // Calcula a janela visível em coordenadas do canvas (já convertidas do zoom).
    // Retorna false quando não há nada a mostrar.
    private bool ComputeWindow(out int firstVis, out int lastVis, out int activeStart, out int activeEnd)
    {
        firstVis = lastVis = activeStart = activeEnd = -1;
        if (PlatformView is null || VirtualView is null || _pdfDoc is null
            || _pageOffsets.Length == 0) return false;

        EffectivePrefetch(out int above, out int below);
        int total = _pdfDoc.PageCount;

        // O ScrollViewer do WinUI expressa VerticalOffset/ViewportHeight no espaço JÁ
        // escalado pelo ZoomFactor, enquanto _pageOffsets/_pageHeights estão no espaço
        // não-escalado do canvas. Sem converter pelo zoom, com ZoomFactor != 1 o cálculo
        // de páginas visíveis desalinha e, em offsets grandes (ex.: metade de um PDF de
        // centenas de páginas), firstVis fica -1 → nenhuma página renderiza → tela preta.
        var sv = PlatformView.ScrollViewer;
        float zoom = sv.ZoomFactor;
        if (zoom < 0.0001f) zoom = 1f;
        // Eixo de scroll: horizontal usa Offset/Viewport X; vertical, Y. _pageOffsets/_pageMain
        // estão no espaço NÃO-escalado do canvas, então converte dividindo pelo zoom.
        double top = (_horizontal ? sv.HorizontalOffset : sv.VerticalOffset) / zoom;
        double vph = (_horizontal ? sv.ViewportWidth     : sv.ViewportHeight) / zoom;
        if (vph < 1) vph = (_horizontal ? PlatformView.ActualWidth : PlatformView.ActualHeight) / zoom;
        double bot = top + vph;

        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            double pTop = _pageOffsets[i];
            double pBot = pTop + _pageMain[i];
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

        EffectivePrefetch(out int above, out int below);

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
    private double RenderTargetWidth(int idx)
    {
        double dispW  = PageDispW(idx);
        double raster = PlatformView?.XamlRoot?.RasterizationScale ?? 1.0;
        if (raster < 1.0) raster = 1.0;
        double rScale = EffectiveRenderScale();
        float  zoom   = PlatformView is not null ? PlatformView.ScrollViewer.ZoomFactor : 1f;
        if (zoom < 0.0001f) zoom = 1f;

        // O supersampling (rScale) só faz sentido quando a página é exibida pequena; em zoom alto
        // o próprio fator de zoom já entrega pixels de sobra. Multiplicar os dois fazia a largura
        // explodir (ex.: 500 DIP × zoom 4 × DPI 1.5 × rScale 1.5 = 4500px → bitmap A4 BGRA8 ≈
        // 4500×6360×4 ≈ 114 MB; com prefetch a janela retém ~6 → ~680 MB → OOM no decode →
        // PÁGINA BRANCA). Acima de 2× dispensa o supersampling.
        double effSuper = zoom >= 2f ? 1.0 : rScale;
        double target   = dispW * zoom * raster * effSuper;
        double minW     = Math.Max(1, dispW * raster);   // nunca abaixo da exibição base
        // Teto RenderCeiling (≈50 MB/bitmap A4) — nítido para leitura e cabe com a janela de
        // prefetch inteira. A largura mínima efetiva é elevada ao tamanho NATIVO da página em
        // RenderPageAsync (ver nota lá): o Windows.Data.Pdf renderiza conteúdo vetorial em BRANCO
        // quando DestinationWidth fica abaixo do nativo.
        return Math.Clamp(target, minW, RenderCeiling);
    }

    // RenderScale EFETIVO: limita a escala pedida pelo consumidor conforme o tamanho do PDF.
    // PDFs pesados rasterizam muito mais devagar (sobretudo páginas escaneadas); reduzir a escala
    // acelera load e troca de página, ao custo de leve perda de nitidez no zoom. Nunca AUMENTA
    // além do RenderScale pedido — só limita. Com tamanho desconhecido (0), não limita.
    private double EffectiveRenderScale()
    {
        double user = VirtualView?.RenderScale ?? 1.5;
        double mb   = _fileBytes / (1024.0 * 1024.0);

        // Também considera o PESO POR PÁGINA: 100MB/50pág ≈ 2MB/pág (escaneado, decode caro) é
        // bem mais pesado que 100MB/2000pág. Páginas pesadas dominam o custo de render.
        int    pages       = _pageOffsets.Length > 0 ? _pageOffsets.Length : Math.Max(1, _pdfDoc?.PageCount ?? 1);
        double mbPerPage   = mb / pages;

        double cap  = (mb, mbPerPage) switch
        {
            ( <= 0, _)          => double.MaxValue,   // tamanho desconhecido → sem limite
            (_, >= 1.0)         => 1.0,               // páginas pesadas (≥1MB/pág) → prioriza fluidez
            ( <= 15, _)         => double.MaxValue,   // arquivo leve → escala cheia
            ( <= 40, _)         => 1.5,
            _                   => 1.0,               // arquivo grande → prioriza fluidez
        };
        return Math.Min(user, cap);
    }

    private static long SafeFileLength(string path)
    {
        try { return new System.IO.FileInfo(path).Length; }
        catch { return 0; }
    }

    private async Task RenderPageAsync(int idx, CancellationToken ct)
    {
        if (_pdfDoc is null || _cache is null || PlatformView is null || VirtualView is null) return;
        if (idx < 0 || idx >= _pdfDoc.PageCount) return;

        // Cache hit: pega um lease (mantém o stream vivo durante o decode assíncrono).
        var hitLease = _cache.TryGetLease(idx);
        if (hitLease is not null)
        {
            // Stream cacheado: foi rasterizado na escala atual (o cache é esvaziado em mudança de
            // zoom/tamanho), então a largura de decode recalculada coincide com a do bitmap nativo.
            try { await ApplyStreamAsync(idx, hitLease.Stream, decodeWidth: null, ct); }
            finally { hitLease.Dispose(); }
            return;
        }

        InMemoryRandomAccessStream? stream = null;
        try
        {
            int destW = (int)Math.Max(1, RenderTargetWidth(idx));

            // UM render por vez: o PDFium não é thread-safe. A rasterização é SÍNCRONA/CPU-bound,
            // então roda em Task.Run (fora da UI thread) para não congelar a interface; o gate
            // serializa o acesso ao motor nativo.
            byte[] pixels = Array.Empty<byte>();
            int pw = 0, ph = 0;
            await _renderGate.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || _pdfDoc is null) return;
                var doc = _pdfDoc;
                (pixels, pw, ph) = await Task.Run(() => doc.RenderBgra(idx, destW), ct);
            }
            finally { _renderGate.Release(); }

            if (ct.IsCancellationRequested || pixels.Length == 0) return;

            // BGRA → PNG via WIC (nativo) para alimentar o cache/decode existente.
            stream = await PdfiumDoc.EncodeBgraToPngAsync(pixels, pw, ph, ct);
            if (stream is null || ct.IsCancellationRequested) { stream?.Dispose(); return; }

            // PutAndLease entrega o stream já com lease. Se não coube no cache (página maior que
            // o limite, ou cache encerrando), exibimos MESMO ASSIM, sem cachear, e descartamos o
            // stream depois — senão a página ficaria em branco. Aumentar MaxCacheMB reduz a chance
            // de não caber (e o re-render ao revisitar), mas exibir sempre é o correto.
            var lease = _cache.PutAndLease(idx, stream);
            if (lease is null)
            {
                try { await ApplyStreamAsync(idx, stream, decodeWidth: destW, ct); }
                finally { stream.Dispose(); }
                return;
            }

            // A partir daqui o cache é dono do stream (lease.Dispose só solta o ref-count).
            try { await ApplyStreamAsync(idx, stream, decodeWidth: destW, ct); }
            finally { lease.Dispose(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Win", $"RenderPage({idx}) ERRO: {ex.Message}");
        }
    }

    // decodeWidth: largura física com que o stream foi rasterizado (do RenderToStreamAsync).
    // Passada explicitamente para o decode coincidir com o bitmap nativo; null (cache hit)
    // recalcula a partir da escala atual (consistente, pois o cache é esvaziado ao mudar escala).
    private Task ApplyStreamAsync(int idx, InMemoryRandomAccessStream stream, int? decodeWidth, CancellationToken ct)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (PlatformView is null || idx >= _pageOffsets.Length) return;

                // NÃO cria nem remove Border aqui. A criação/remoção de placeholders é monopólio
                // de EnsurePlaceholders/TrimToWindow (decisão de JANELA VISÍVEL). Aqui apenas
                // PREENCHEMOS o Source de um Border que já existe. Se não existe, a página saiu da
                // janela enquanto rasterizávamos → descartamos o resultado, sem recriar Border
                // órfão fora da janela. (Antes, este método criava o Border e — no caminho de
                // cancelamento — o REMOVIA; um render obsoleto cancelado removia o Border de uma
                // renderização VÁLIDA já em curso para o mesmo idx → página branca permanente.)
                if (FindPageImage(idx) is null) return;

                int width = decodeWidth ?? (int)Math.Max(1, RenderTargetWidth(idx));
                var bmp = await DecodeAsync(idx, stream, width, ct);

                // Falha de decode: NÃO remove o placeholder (a folha branca permanece). Agenda um
                // re-render tardio para o caso do usuário ficar PARADO nesta página — sem isso não
                // haveria nova RenderVisible para re-tentar e a folha ficaria branca para sempre.
                if (bmp is null) { ScheduleDecodeRetry(idx); return; }

                // O decode é assíncrono: a página pode ter saído da janela nesse meio-tempo
                // (Border removido por TrimToWindow). Re-busca o Border ANTES de atribuir — se
                // sumiu, descarta. Decidir pela presença do Border (janela), não pelo token: um
                // token cancelado cuja página continua visível ainda deve receber a imagem.
                NativeImage? img = FindPageImage(idx);
                if (img is null) return;

                img.Source = bmp;
                _decodeFailures.Remove(idx);   // sucesso → zera o contador de re-tentativas
            }
            catch (Exception ex)
            {
                PdfViewerLog.Write("Pdf/Win", $"ApplyBitmap({idx}) ERRO: {ex.Message}");
            }
        });

    // Busca (sem criar) o Image do Border da página idx. Usado por ApplyStreamAsync para nunca
    // ressuscitar Borders fora da janela: a criação é exclusiva de EnsurePlaceholders.
    private NativeImage? FindPageImage(int idx)
    {
        var canvas = PlatformView?.PagesCanvas;
        if (canvas is null) return null;
        foreach (var child in canvas.Children)
            if (child is WinBorder b && b.Tag is int t && t == idx)
                return b.Child as NativeImage;
        return null;
    }

    // Re-render tardio após o decode falhar DEFINITIVAMENTE (já esgotado o retry interno do
    // DecodeAsync). Limitado a MaxDecodeRetries por página, com atraso curto (dá tempo ao GC sob
    // pressão de memória), e só re-tenta se a página continua na janela e sem imagem — evita
    // girar em falso em páginas genuinamente corrompidas.
    private void ScheduleDecodeRetry(int idx)
    {
        int n = _decodeFailures.TryGetValue(idx, out var c) ? c : 0;
        if (n >= MaxDecodeRetries) return;
        _decodeFailures[idx] = n + 1;

        _ = Task.Delay(400).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (PlatformView is null || FindPageImage(idx) is null) return;   // saiu da janela
                if (HasImage(idx)) { _decodeFailures.Remove(idx); return; }       // já preenchida
                var ct = _prefetchCts?.Token ?? CancellationToken.None;
                if (!ct.IsCancellationRequested) _ = RenderPageAsync(idx, ct);
            }), TaskScheduler.Default);
    }

    // Decodifica o stream para um BitmapImage no tamanho físico pedido, com a CONCORRÊNCIA
    // limitada por _decodeGate. Em caso de falha de decode (decoder saturado / pressão de
    // memória em páginas escaneadas de alta resolução, antes capturada e só logada → página
    // em branco), re-tenta UMA vez com metade da largura antes de desistir.
    private async Task<BitmapImage?> DecodeAsync(int idx, InMemoryRandomAccessStream stream, int width, CancellationToken ct)
    {
        await _decodeGate.WaitAsync(ct);
        try
        {
            try
            {
                return await DecodeOnceAsync(stream, width);
            }
            catch (Exception ex)
            {
                int half = Math.Max(1, width / 2);
                PdfViewerLog.Write("Pdf/Win", $"SetSource({idx}) falhou (w={width}): {ex.Message}. Retry w={half}.");
                if (ct.IsCancellationRequested) return null;
                try
                {
                    return await DecodeOnceAsync(stream, half);
                }
                catch (Exception ex2)
                {
                    PdfViewerLog.Write("Pdf/Win", $"SetSource({idx}) fallback falhou (w={half}): {ex2.Message}.");
                    return null;
                }
            }
        }
        finally { _decodeGate.Release(); }
    }

    // CRÍTICO p/ nitidez no zoom: sem DecodePixelWidth o WinUI faz "right-sizing" automático e
    // decodifica a bitmap no tamanho de EXIBIÇÃO (layout ~_pageWidth), jogando fora a resolução
    // extra; ao ampliar, o ScrollViewer faz upscale disso e borra. Forçando o decode na largura
    // de RENDER (física, alta), o zoom reamostra de uma bitmap de alta resolução → nítido.
    private static async Task<BitmapImage> DecodeOnceAsync(InMemoryRandomAccessStream stream, int width)
    {
        stream.Seek(0);
        var bmp = new BitmapImage
        {
            DecodePixelType  = DecodePixelType.Physical,
            DecodePixelWidth = Math.Max(1, width),
        };
        await bmp.SetSourceAsync(stream);
        return bmp;
    }

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
            Width           = PageDispW(idx),
            Height          = PageDispH(idx),
            Child           = ni,
            // Contorno sutil para destacar a folha branca sobre o deck claro (estilo Acrobat/Edge).
            BorderBrush     = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(
                                  global::Windows.UI.Color.FromArgb(0xFF, 0xCF, 0xCF, 0xD3)),
            BorderThickness = new WinThickness(1),
        };

        // Vertical: folha à esquerda (X=0), Y=offset. Horizontal: fit-page centralizada no slot.
        global::Microsoft.UI.Xaml.Controls.Canvas.SetLeft(border, PageLeft(idx));
        global::Microsoft.UI.Xaml.Controls.Canvas.SetTop(border, PageTop(idx));
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
            foreach (var b in toRemove)
            {
                // Solta a BitmapImage ANTES de remover: sem isto, a bitmap decodificada
                // (até dezenas de MB por página escaneada em alta resolução) só seria
                // coletada no próximo GC, mantendo o pico de memória alto ao rolar.
                if (b.Child is NativeImage ni) ni.Source = null;
                canvas.Children.Remove(b);
            }
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

        // Horizontal: alinha à página mais próxima (paginação). Se reposicionou, o settle
        // seguinte renderiza a página já alinhada — evita render numa posição intermediária.
        if (SnapHorizontalIfNeeded()) return;

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
        var svp = PlatformView.ScrollViewer;
        double viewportMain = (_horizontal ? svp.ViewportWidth : svp.ViewportHeight) / pgZoom;
        if (viewportMain < 1) viewportMain = (_horizontal ? PlatformView.ActualWidth : PlatformView.ActualHeight) / pgZoom;
        double centerBase = (_horizontal ? svp.HorizontalOffset : svp.VerticalOffset) / pgZoom + viewportMain / 2;
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
        InitVirtualCanvas(_pdfDoc.PageCount);   // limpa as imagens (canvas.Children.Clear)
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

        if (!ctrlDown)
        {
            // Modo horizontal em FIT (zoom ~1): a roda do mouse VIRA PÁGINA (desktop) — rolar para
            // baixo avança, para cima retrocede. Ampliado (zoom>1), deixa o pan vertical padrão do
            // ScrollViewer (a folha excede a viewport). No vertical, também não interfere.
            if (_horizontal && PlatformView is not null && VirtualView is not null)
            {
                float z = PlatformView.ScrollViewer.ZoomFactor;
                if (z < 0.0001f) z = 1f;
                if (z <= 1.05f)
                {
                    // UMA página por gesto: a direção vem do SINAL do delta (baixo → próxima,
                    // cima → anterior). Um único giro dispara uma rajada de eventos (mouse/touchpad);
                    // o cooldown coalesce essa rajada para avançar exatamente uma página.
                    int  dir = props.MouseWheelDelta < 0 ? +1 : (props.MouseWheelDelta > 0 ? -1 : 0);
                    long now = Environment.TickCount64;
                    if (dir != 0 && now - _lastWheelPageTick >= 220)
                    {
                        _lastWheelPageTick = now;
                        int total = _pdfDoc?.PageCount ?? 0;
                        if (total > 0)
                            VirtualView.CurrentPage = Math.Clamp(VirtualView.CurrentPage + dir, 0, total - 1);
                    }
                    e.Handled = true;
                }
            }
            return;
        }

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

        if (_horizontal)
        {
            // Paginado: ancora o centro em AMBOS os eixos (ao ampliar a folha excede a viewport
            // nos dois sentidos). Mantém o ponto central no centro após a nova escala.
            double cbx = (sv.HorizontalOffset + sv.ViewportWidth  / 2.0) / z0;
            double cby = (sv.VerticalOffset   + sv.ViewportHeight / 2.0) / z0;
            double nox = Math.Max(0, cbx * newZ - sv.ViewportWidth  / 2.0);
            double noy = Math.Max(0, cby * newZ - sv.ViewportHeight / 2.0);
            sv.ChangeView(nox, noy, newZ, disableAnimation: !animate);
            return;
        }

        // Vertical: ancora só no eixo de scroll (Y); o X é mantido pelo ChangeView (null).
        double centerBase = (sv.VerticalOffset + sv.ViewportHeight / 2.0) / z0;
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
        // Salto INSTANTÂNEO (sem animação): a animação de scroll por várias páginas atrasava o
        // render da página-alvo (que só ocorre ao assentar) e parecia travado. Pulando direto, o
        // ViewChanged final dispara o RenderVisible da página-alvo imediatamente.
        var sv = PlatformView.ScrollViewer;
        if (_horizontal)
        {
            // Paginado: CENTRALIZA o slot da página na viewport nos dois eixos. Em zoom 1 isso
            // equivale a alinhar o slot (slot = viewport); ampliado, mostra o CENTRO da página
            // (sem isto, alinhar o canto deixava a página "encostada" à esquerda/topo).
            double cx = (_pageOffsets[page] + _pageMain[page] / 2.0) * zoom - sv.ViewportWidth  / 2.0;
            double cy = (_crossH / 2.0) * zoom - sv.ViewportHeight / 2.0;
            sv.ChangeView(Math.Max(0, cx), Math.Max(0, cy), null, disableAnimation: true);
        }
        else
        {
            sv.ChangeView(null, _pageOffsets[page] * zoom, null, disableAnimation: true);
        }
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
        if (_pdfDoc is not null) InitVirtualCanvas(_pdfDoc.PageCount);
    }

    // Troca a direção do scroll: vertical (contínuo) ⇄ horizontal (paginado, tipo livro). Recalcula
    // o layout, esvazia o cache (a escala de render muda com a largura exibida), volta o zoom a 100%
    // e reposiciona na página atual.
    private void ApplyOrientation()
    {
        if (PlatformView is null || VirtualView is null) return;
        bool horizontal = VirtualView.ScrollOrientation == PdfScrollOrientation.Horizontal;
        if (horizontal == _horizontal && _pageOffsets.Length > 0) return;   // sem mudança
        _horizontal = horizontal;
        if (_pdfDoc is null) return;   // sem documento → o próximo InitVirtualCanvas já usa o modo

        var    sv   = PlatformView.ScrollViewer;
        int    page = Math.Clamp(VirtualView.CurrentPage, 0, Math.Max(0, _pdfDoc.PageCount - 1));
        // Zoom MÍNIMO ao trocar de modo (no Windows o mínimo é 100% por padrão — WindowsMinZoom).
        float  min  = sv.MinZoomFactor;
        if (min < 0.0001f) min = 1f;

        InitVirtualCanvas(_pdfDoc.PageCount);
        _cache?.EvictAll();
        _renderedZoom = min;
        _lastZoom     = min;

        // Mede o novo canvas ANTES do ChangeView — senão o ScrollViewer ainda tem o extent ANTIGO
        // e o ChangeView para a página-alvo é clampeado a 0 (volta para a 1ª página).
        sv.UpdateLayout();

        // Vai DIRETO para a página atual no zoom mínimo (sem passar por (0,0), que dispararia o
        // ReportCurrentPage com página 0 e perderia a página atual).
        if (page < _pageOffsets.Length)
        {
            if (_horizontal)
            {
                double cx = (_pageOffsets[page] + _pageMain[page] / 2.0) * min - sv.ViewportWidth  / 2.0;
                double cy = (_crossH / 2.0) * min - sv.ViewportHeight / 2.0;
                sv.ChangeView(Math.Max(0, cx), Math.Max(0, cy), min, disableAnimation: true);
            }
            else
            {
                sv.ChangeView(0, _pageOffsets[page] * min, min, disableAnimation: true);
            }
        }
        RenderVisible();
    }

    // Paginação "tipo livro" (horizontal): ao assentar o scroll em zoom ~1 (fit), alinha o slot da
    // página mais próxima à viewport. Retorna true se emitiu um ChangeView (o settle seguinte
    // renderiza a página já alinhada). Ampliado (zoom>1) NÃO pagina — o usuário faz pan livre.
    private bool SnapHorizontalIfNeeded()
    {
        if (!_horizontal || PlatformView is null || _pageOffsets.Length == 0 || _syncingPage) return false;
        var sv = PlatformView.ScrollViewer;
        float zoom = sv.ZoomFactor;
        if (zoom < 0.0001f) zoom = 1f;
        if (zoom > 1.05f) return false;

        double left = sv.HorizontalOffset / zoom;
        int nearest = 0; double best = double.MaxValue;
        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            double d = Math.Abs(_pageOffsets[i] - left);
            if (d < best) { best = d; nearest = i; }
        }
        double target = _pageOffsets[nearest] * zoom;
        if (Math.Abs(target - sv.HorizontalOffset) < 1.0) return false;   // já alinhado
        sv.ChangeView(target, null, null, disableAnimation: false);
        return true;
    }

    private void ApplyZoomEnabled()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ScrollViewer.ZoomMode = VirtualView.IsPinchZoomEnabled
            ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    // ── Barra de miniaturas (somente Windows) ──────────────────────────────────
    private void ApplyThumbnailTitle()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetThumbnailTitle(
            string.IsNullOrEmpty(VirtualView.ThumbnailBarTitleText) ? "Pages" : VirtualView.ThumbnailBarTitleText);
    }

    private void ApplyThumbnailBar()
    {
        if (PlatformView is null || VirtualView is null) return;
        var host = PlatformView.ThumbnailHost;
        var list = PlatformView.ThumbnailList;

        // None desabilita a sidebar mesmo com EnableThumbnailBar = true.
        if (!VirtualView.EnableThumbnailBar || _pdfDoc is null
            || VirtualView.ThumbnailBarPlacement == PdfThumbnailPlacement.None)
        {
            host.Visibility  = global::Microsoft.UI.Xaml.Visibility.Collapsed;
            list.ItemsSource = null;
            _thumbItems      = null;
            return;
        }

        // Lado da sidebar conforme ThumbnailBarPlacement (Left = esquerda, Right = direita).
        PlatformView.SetThumbnailSide(VirtualView.ThumbnailBarPlacement == PdfThumbnailPlacement.Right);

        int count = _pdfDoc.PageCount;
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
    private double PageRatio(int idx) => MeasuredRatio(idx);   // altura/largura real (independe do modo de layout)

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
        if (doc is null || item.PageIndex < 0 || item.PageIndex >= doc.PageCount) return;
        try
        {
            // Rasteriza a miniatura via PDFium (síncrono) fora da UI thread, serializado pelo gate.
            byte[] pixels; int tw, th;
            await _renderGate.WaitAsync();
            try { (pixels, tw, th) = await Task.Run(() => doc.RenderBgra(item.PageIndex, 130)); }
            finally { _renderGate.Release(); }
            if (pixels.Length == 0) return;

            var stream = await PdfiumDoc.EncodeBgraToPngAsync(pixels, tw, th, CancellationToken.None);
            if (stream is null) return;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);   // decode síncrono dentro do await; seguro dispor após
            stream.Dispose();
            item.Image = bmp;
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Win", $"Thumb({item.PageIndex}) ERRO: {ex.Message}");
        }
    }

    // ── Seleção de texto ────────────────────────────────────────────────────────

    // Converte um ponto no espaço do PagesCanvas (não escalado pelo zoom — o ScrollViewer já o
    // aplica) para a página sob ele e suas coordenadas em PONTOS PDF (origem inferior-esquerda).
    private bool HitPage(double cx, double cy, out int idx, out double xPt, out double yPt)
    {
        idx = -1; xPt = yPt = 0;
        if (_pdfDoc is null) return false;
        for (int i = 0; i < _pageOffsets.Length; i++)
        {
            // Retângulo EXIBIDO da folha (fit-page no horizontal → não preenche o slot).
            double left = PageLeft(i), top = PageTop(i);
            double dispW = PageDispW(i), dispH = PageDispH(i);
            if (cx < left || cx >= left + dispW || cy < top || cy >= top + dispH) continue;
            var (wPt, hPt) = _pdfDoc.PageSizePt(i);
            double dx = cx - left;
            double dy = cy - top;
            xPt = dx * wPt / dispW;
            yPt = hPt - dy * hPt / dispH;   // inverte Y (PDF cresce para cima)
            idx = i;
            return true;
        }
        return false;
    }

    private void OnSelPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (PlatformView is null || _pdfDoc is null) return;
        if (e.Pointer.PointerDeviceType != global::Microsoft.UI.Input.PointerDeviceType.Mouse) return; // toque = rolagem
        var pt = e.GetCurrentPoint(PlatformView.PagesCanvas);
        if (!pt.Properties.IsLeftButtonPressed) return;

        ClearRects(SelRectTag);
        _selectedText = string.Empty;
        if (!HitPage(pt.Position.X, pt.Position.Y, out int idx, out double xPt, out double yPt)) return;
        int ci = _pdfDoc.CharIndexAtPagePoint(idx, xPt, yPt, 8);
        if (ci < 0) { _selecting = false; return; }

        _selPage = idx; _selAnchor = ci; _selFocus = ci; _selecting = true;
        PlatformView.PagesCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSelPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting || PlatformView is null || _pdfDoc is null) return;
        var pt = e.GetCurrentPoint(PlatformView.PagesCanvas);
        if (!pt.Properties.IsLeftButtonPressed) { EndSelection(e); return; }
        if (!HitPage(pt.Position.X, pt.Position.Y, out int idx, out double xPt, out double yPt)) return;
        if (idx != _selPage) return;   // v1: seleção limitada à página inicial

        int ci = _pdfDoc.CharIndexAtPagePoint(idx, xPt, yPt, 8);
        if (ci < 0) return;
        _selFocus = ci;

        var (rects, text) = _pdfDoc.GetSelection(_selPage, _selAnchor, _selFocus);
        _selectedText = text;
        DrawRects(_selPage, rects, SelRectTag, global::Windows.UI.Color.FromArgb(0x55, 0x33, 0x80, 0xFF));
        e.Handled = true;
    }

    private void OnSelPointerReleased(object sender, PointerRoutedEventArgs e) => EndSelection(e);

    private void EndSelection(PointerRoutedEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;
        PlatformView?.PagesCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void OnSelRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (PlatformView is null || string.IsNullOrEmpty(_selectedText)) return;
        var flyout = new global::Microsoft.UI.Xaml.Controls.MenuFlyout();
        var copyText = string.IsNullOrEmpty(VirtualView?.CopyButtonText) ? "Copy" : VirtualView!.CopyButtonText;
        var copy   = new global::Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = copyText };
        copy.Click += (_, __) => CopySelection();
        flyout.Items.Add(copy);
        flyout.ShowAt(PlatformView.PagesCanvas, e.GetPosition(PlatformView.PagesCanvas));
        e.Handled = true;
    }

    // Desenha retângulos de realce (em PONTOS PDF) no canvas, convertendo para o espaço de
    // exibição. Ficam soltos no canvas (marcados por 'tag') e acompanham scroll/zoom naturalmente.
    private void DrawRects(int idx, List<(double l, double t, double r, double b)> rects,
                           string tag, global::Windows.UI.Color color)
    {
        if (PlatformView is null || _pdfDoc is null || idx < 0 || idx >= _pageHeights.Length) return;
        ClearRects(tag);
        var canvas = PlatformView.PagesCanvas;
        var (wPt, hPt) = _pdfDoc.PageSizePt(idx);
        double sx = PageDispW(idx) / wPt;
        double sy = PageDispH(idx) / hPt;
        double pageLeft = PageLeft(idx);
        double pageTop  = PageTop(idx);
        var fill = new global::Microsoft.UI.Xaml.Media.SolidColorBrush(color);

        foreach (var (l, t, r, b) in rects)
        {
            var rect = new global::Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width            = Math.Max(0, (r - l) * sx),
                Height           = Math.Max(0, (t - b) * sy),   // t (topo) > b (base) em PDF
                Fill             = fill,
                IsHitTestVisible = false,
                Tag              = tag,
            };
            global::Microsoft.UI.Xaml.Controls.Canvas.SetLeft(rect, pageLeft + l * sx);
            global::Microsoft.UI.Xaml.Controls.Canvas.SetTop(rect, pageTop + (hPt - t) * sy);
            canvas.Children.Add(rect);
        }
    }

    private void ClearRects(string tag)
    {
        var canvas = PlatformView?.PagesCanvas;
        if (canvas is null) return;
        var rm = canvas.Children.OfType<global::Microsoft.UI.Xaml.Shapes.Rectangle>()
                       .Where(r => (r.Tag as string) == tag).ToList();
        foreach (var r in rm) canvas.Children.Remove(r);
    }

    private void CopySelection()
    {
        if (string.IsNullOrEmpty(_selectedText)) return;
        try
        {
            var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(_selectedText);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            PdfViewerLog.Write("Pdf/Win", $"Copiado ({_selectedText.Length} caracteres).");
        }
        catch (Exception ex)
        {
            PdfViewerLog.Write("Pdf/Win", $"Copiar ERRO: {ex.Message}");
        }
    }

    // ── Busca (acionada por comandos do controle; UI fica no app consumidor) ─────

    // Varre o documento em background (FindAll segura o lock do PDFium) e mostra a 1ª ocorrência.
    // O total/índice são reportados ao controle via RaiseSearchResult (o app exibe o contador).
    private void DoSearch(string term)
    {
        _findCts?.Cancel(); _findCts?.Dispose();
        _findCts = new CancellationTokenSource();
        var ct = _findCts.Token;

        _findTerm = term;
        ClearRects(FindRectTag);
        _findHits = new(); _findCurrent = -1;

        if (_pdfDoc is null || string.IsNullOrWhiteSpace(term)) { VirtualView?.RaiseSearchResult(0, -1); return; }
        var doc = _pdfDoc;
        _ = Task.Run(() =>
        {
            var hits = doc.FindAll(term);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                _findHits = hits;
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

    // Realça a ocorrência i (amarelo) e rola até ela.
    private void ShowHit(int i)
    {
        if (_pdfDoc is null || i < 0 || i >= _findHits.Count) return;
        var (page, index, count) = _findHits[i];
        var (rects, _) = _pdfDoc.GetSelection(page, index, index + count - 1);
        DrawRects(page, rects, FindRectTag, global::Windows.UI.Color.FromArgb(0x99, 0xFF, 0xD1, 0x4A));
        ScrollToRects(page, rects);
    }

    private void ScrollToRects(int page, List<(double l, double t, double r, double b)> rects)
    {
        if (PlatformView is null || _pdfDoc is null || rects.Count == 0
            || page < 0 || page >= _pageHeights.Length) return;
        float zoom = PlatformView.ScrollViewer.ZoomFactor;
        if (zoom < 0.0001f) zoom = 1f;

        if (_horizontal)
        {
            // Horizontal (paginado): leva a página da ocorrência para a viewport (slot alinhado).
            double targetX = _pageOffsets[page] * zoom;
            PlatformView.ScrollViewer.ChangeView(Math.Max(0, targetX), null, null, disableAnimation: false);
            return;
        }

        var (_, hPt) = _pdfDoc.PageSizePt(page);
        double sy = PageDispH(page) / hPt;
        double topCanvas = PageTop(page) + (hPt - rects[0].t) * sy;
        // Posiciona a ocorrência a ~1/3 do topo da viewport.
        double target = topCanvas * zoom - PlatformView.ScrollViewer.ViewportHeight / 3.0;
        PlatformView.ScrollViewer.ChangeView(null, Math.Max(0, target), null, disableAnimation: false);
    }

    private void ClearSearchState()
    {
        _findCts?.Cancel();
        _findHits = new(); _findCurrent = -1; _findTerm = string.Empty;
        ClearRects(FindRectTag);
        VirtualView?.RaiseSearchResult(0, -1);
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

        // Coluna 0: sidebar à esquerda (Auto). Coluna 1: visualizador (*). Coluna 2: sidebar à
        // direita (Auto). A sidebar ocupa a 0 ou a 2 conforme ThumbnailBarPlacement (SetThumbnailSide).
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = global::Microsoft.UI.Xaml.GridLength.Auto });
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = new global::Microsoft.UI.Xaml.GridLength(1, global::Microsoft.UI.Xaml.GridUnitType.Star) });
        ColumnDefinitions.Add(new global::Microsoft.UI.Xaml.Controls.ColumnDefinition
            { Width = global::Microsoft.UI.Xaml.GridLength.Auto });

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
            Text              = "Pages",
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
            // Ambas as barras sempre presentes (a horizontal era Auto e sumia quando não havia
            // scroll no eixo). Em horizontal a barra inferior fica sempre visível ao paginar.
            HorizontalScrollBarVisibility = WinScrollBarVis.Visible,
            VerticalScrollBarVisibility   = WinScrollBarVis.Visible,
            HorizontalContentAlignment    = WinHAlign.Center,
            // Deck CLARO (estilo Acrobat/Edge) — a folha branca se destaca com leve sombra.
            Background                    = Brush(0xE6, 0xE6, 0xE8),
            Content                       = PagesCanvas,
        };
        // Engrossa as barras de rolagem (override do recurso de tema) para ficarem mais
        // presentes — o Windows ainda as afina quando ociosas (auto-hide do sistema), mas com
        // tamanho maior o indicador fica bem mais visível. Ver nota no resumo sobre desligar
        // totalmente o auto-hide (config do sistema ou re-template do ScrollBar).
        ScrollViewer.Resources["ScrollBarSize"] = 16.0;

        // Impede o visualizador nativo (ScrollViewer/Canvas) de ROUBAR o foco do teclado ao clicar
        // no PDF. Sem isto, no Windows o foco fica preso no visualizador e o PRIMEIRO clique em
        // botões MAUI (ex.: o menu ⋮) só transfere o foco, sem disparar o Clicked — só "destrava"
        // depois de focar/desfocar outro controle (ex.: a barra de busca). Scroll e seleção por
        // mouse/toque não dependem de foco, então desabilitar é seguro.
        ScrollViewer.IsTabStop = false;
        PagesCanvas.IsTabStop  = false;
        ScrollViewer.AllowFocusOnInteraction = false;
        PagesCanvas.AllowFocusOnInteraction  = false;
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ScrollViewer, 1);

        Children.Add(ThumbnailHost);
        Children.Add(ScrollViewer);

        // A barra de miniaturas segue o tema (claro/escuro). Aplica o estado inicial e
        // reage a mudanças de tema em runtime.
        ApplyThumbnailTheme();
        Loaded             += (_, __) => ApplyThumbnailTheme();
        ActualThemeChanged += (_, __) => ApplyThumbnailTheme();
    }

    /// <summary>Título da sidebar de miniaturas (localizável via PdfViewer.ThumbnailBarTitleText).</summary>
    internal void SetThumbnailTitle(string title)
    {
        if (!string.IsNullOrEmpty(title)) _thumbHeaderText.Text = title;
    }

    /// <summary>Posiciona a sidebar de miniaturas à esquerda (coluna 0) ou à direita (coluna 2).</summary>
    internal void SetThumbnailSide(bool right)
    {
        global::Microsoft.UI.Xaml.Controls.Grid.SetColumn(ThumbnailHost, right ? 2 : 0);
        // O divisor fica na borda voltada para o visualizador.
        _thumbVDivider.HorizontalAlignment = right ? WinHAlign.Left : WinHAlign.Right;
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
            // Em ambos os casos de recusa NÃO dispomos o stream: o chamador ainda vai EXIBIR
            // a página (sem cachear) e então descartá-lo. Dispor aqui deixava a página em branco.
            if (_disposed) return null;

            if (bytes > _maxBytes)
            {
                // Um único stream maior que o cache inteiro: não cacheia (mas o chamador exibe).
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

// ─────────────────────────────────────────────────────────────────────────────
// PdfWinPrintJob — pipeline de impressão do Windows (PrintManager + PrintDocument).
//
// O Windows App SDK não tem CoreWindow, então a UI de impressão é aberta por HWND via
// PrintManagerInterop. Cada página do PDF é rasterizada (PDFium/PDFtoImage) para um BitmapImage
// e colocada dentro de um Grid do tamanho da folha da impressora, com a imagem ajustada à área
// imprimível (ImageableRect). O objeto se mantém vivo enquanto a UI estiver aberta (StartAsync
// é aguardado) e se desinscreve dos eventos ao concluir.
// ─────────────────────────────────────────────────────────────────────────────

file sealed class PdfWinPrintJob
{
    private readonly nint   _hwnd;
    private readonly string _path;
    private readonly string _jobName;

    private PdfiumDoc?             _doc;
    private int                    _pageCount;
    private PrintDocument?         _printDoc;
    private IPrintDocumentSource?  _source;
    private PrintManager?          _printManager;

    private global::Windows.Foundation.Size _pageSize;       // folha física (px @96dpi)
    private global::Windows.Foundation.Rect _imageableRect;  // área imprimível (sem margens)

    public PdfWinPrintJob(nint hwnd, string path, string jobName)
    {
        _hwnd    = hwnd;
        _path    = path;
        _jobName = jobName;
    }

    public async global::System.Threading.Tasks.Task StartAsync()
    {
        _doc       = await global::System.Threading.Tasks.Task.Run(() => new PdfiumDoc(_path));
        _pageCount = _doc.PageCount;
        if (_pageCount == 0) return;

        _printDoc = new PrintDocument();
        _source   = _printDoc.DocumentSource;
        _printDoc.Paginate       += OnPaginate;
        _printDoc.GetPreviewPage += OnGetPreviewPage;
        _printDoc.AddPages       += OnAddPages;

        _printManager = global::Windows.Graphics.Printing.PrintManagerInterop.GetForWindow(_hwnd);
        _printManager.PrintTaskRequested += OnPrintTaskRequested;

        await global::Windows.Graphics.Printing.PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        var task = args.Request.CreatePrintTask(_jobName, req => req.SetSource(_source));
        task.Completed += (_, __) => Cleanup();
    }

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        // Captura o tamanho da folha/área imprimível escolhidos para dimensionar cada página.
        var desc = e.PrintTaskOptions.GetPageDescription(0);
        _pageSize      = desc.PageSize;
        _imageableRect = desc.ImageableRect;
        _printDoc!.SetPreviewPageCount(_pageCount, PreviewPageCountType.Final);
    }

    private async void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e)
        => _printDoc!.SetPreviewPage(e.PageNumber, await BuildPageAsync(e.PageNumber - 1));

    private async void OnAddPages(object sender, AddPagesEventArgs e)
    {
        for (int i = 0; i < _pageCount; i++)
            _printDoc!.AddPage(await BuildPageAsync(i));
        _printDoc!.AddPagesComplete();
    }

    // Rasteriza a página (PDFium, ~200 DPI relativo à área imprimível) e a envolve numa folha do
    // tamanho da impressora, com a imagem ajustada (Uniform) à área imprimível.
    private async global::System.Threading.Tasks.Task<FrameworkElement> BuildPageAsync(int index)
    {
        var doc = _doc!;
        // _imageableRect vem em DIPs (1/96"); a ~200 DPI a largura física ≈ ×200/96.
        int width = Math.Max(200, (int)(_imageableRect.Width * 200.0 / 96.0));
        var (pixels, w, h) = await global::System.Threading.Tasks.Task.Run(() => doc.RenderBgra(index, width));

        var stream = await PdfiumDoc.EncodeBgraToPngAsync(pixels, w, h, global::System.Threading.CancellationToken.None);

        var bmp = new BitmapImage();
        if (stream is not null) { await bmp.SetSourceAsync(stream); stream.Dispose(); }

        var image = new NativeImage
        {
            Source              = bmp,
            Stretch             = global::Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Width               = _imageableRect.Width,
            Height              = _imageableRect.Height,
            HorizontalAlignment = WinHAlign.Left,
            VerticalAlignment   = WinVAlign.Top,
            Margin              = new WinThickness(_imageableRect.X, _imageableRect.Y, 0, 0),
        };

        return new global::Microsoft.UI.Xaml.Controls.Grid
        {
            Width    = _pageSize.Width,
            Height   = _pageSize.Height,
            Children = { image },
        };
    }

    private void Cleanup()
    {
        if (_printManager is not null) _printManager.PrintTaskRequested -= OnPrintTaskRequested;
        if (_printDoc is not null)
        {
            _printDoc.Paginate       -= OnPaginate;
            _printDoc.GetPreviewPage -= OnGetPreviewPage;
            _printDoc.AddPages       -= OnAddPages;
        }
        _doc?.Dispose();
        _doc = null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfiumDoc — documento PDF aberto por CAMINHO, rasterizado via PDFium (PDFtoImage).
//
// As proporções de TODAS as páginas são lidas no construtor (GetPageSizes — só metadados, não
// rasteriza), o que evita abrir página a página e travar o load em PDFs de centenas de páginas.
// RenderPng é SÍNCRONO/CPU-bound: chame em Task.Run e serialize com o gate do handler, pois o
// PDFium não é thread-safe.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfiumDoc : IDisposable
{
    // PDFium NÃO é thread-safe — nem entre documentos distintos (tem estado global). TODA chamada
    // à lib é serializada por este lock de PROCESSO, que cobre viewer + impressão simultâneos.
    internal static readonly object Lib = new();
    private  static bool            _libInit;

    private const int FPDFBitmap_BGRA = 4;     // 8888 BGRA
    private const int FPDF_ANNOT      = 0x01;  // renderiza anotações

    private FpdfDocumentT?    _doc;
    private readonly double[] _wPt;             // largura da página em PONTOS PDF (1/72")
    private readonly double[] _hPt;             // altura  da página em PONTOS PDF

    // Cache de UMA página de texto (a que está em interação de seleção/busca). Abrir a text page
    // é caro para repetir a cada PointerMoved; mantemos a atual aberta e trocamos sob demanda.
    private int            _txtIdx = -1;
    private FpdfPageT?      _txtPage;
    private FpdfTextpageT?  _txtText;

    public int PageCount => _wPt.Length;

    public PdfiumDoc(string path, string? password = null)
    {
        lock (Lib)
        {
            if (!_libInit) { fpdfview.FPDF_InitLibrary(); _libInit = true; }

            _doc = fpdfview.FPDF_LoadDocument(path, password)
                   ?? throw new InvalidOperationException(
                       "PDFium não conseguiu abrir o documento (arquivo inválido ou senha incorreta).");

            int count = fpdfview.FPDF_GetPageCount(_doc);
            _wPt = new double[count];
            _hPt = new double[count];
            for (int i = 0; i < count; i++)
            {
                double w = 0, h = 0;
                fpdfview.FPDF_GetPageSizeByIndex(_doc, i, ref w, ref h);
                _wPt[i] = Math.Max(1.0, w);
                _hPt[i] = Math.Max(1.0, h);
            }
        }
    }

    public double Ratio(int i) => (i >= 0 && i < _hPt.Length) ? _hPt[i] / _wPt[i] : 1.414;

    // Tamanho da página em pontos PDF (origem bottom-left) — base para converter tela↔PDF.
    public (double w, double h) PageSizePt(int i)
        => (i >= 0 && i < _wPt.Length) ? (_wPt[i], _hPt[i]) : (612, 792);

    // ── Camada de texto (seleção/busca) ──────────────────────────────────────────

    // Abre/troca a página de texto cacheada. DEVE ser chamado sob lock(Lib).
    private void EnsureTextPage(int idx)
    {
        if (_txtIdx == idx && _txtText is not null) return;
        CloseTextPage();
        if (_doc is null || idx < 0 || idx >= _wPt.Length) return;
        _txtPage = fpdfview.FPDF_LoadPage(_doc, idx);
        if (_txtPage is null) return;
        _txtText = fpdf_text.FPDFTextLoadPage(_txtPage);
        _txtIdx  = idx;
    }

    private void CloseTextPage()
    {
        if (_txtText is not null) { fpdf_text.FPDFTextClosePage(_txtText); _txtText = null; }
        if (_txtPage is not null) { fpdfview.FPDF_ClosePage(_txtPage);     _txtPage = null; }
        _txtIdx = -1;
    }

    // Índice do caractere na posição (em PONTOS PDF, origem bottom-left) ou -1. tol em pontos.
    public int CharIndexAtPagePoint(int idx, double xPt, double yPt, double tol)
    {
        lock (Lib)
        {
            EnsureTextPage(idx);
            if (_txtText is null) return -1;
            return fpdf_text.FPDFTextGetCharIndexAtPos(_txtText, xPt, yPt, tol, tol);
        }
    }

    public int CharCount(int idx)
    {
        lock (Lib)
        {
            EnsureTextPage(idx);
            return _txtText is null ? 0 : fpdf_text.FPDFTextCountChars(_txtText);
        }
    }

    // Retângulos de realce (em PONTOS PDF) e o texto entre dois índices de caractere (inclusivo).
    // 'from'/'to' podem vir em qualquer ordem.
    public (List<(double l, double t, double r, double b)> rects, string text) GetSelection(int idx, int from, int to)
    {
        var rects = new List<(double, double, double, double)>();
        lock (Lib)
        {
            EnsureTextPage(idx);
            if (_txtText is null) return (rects, string.Empty);

            int a = Math.Min(from, to), z = Math.Max(from, to);
            if (a < 0) return (rects, string.Empty);
            int count = z - a + 1;
            if (count <= 0) return (rects, string.Empty);

            int n = fpdf_text.FPDFTextCountRects(_txtText, a, count);
            for (int i = 0; i < n; i++)
            {
                double l = 0, t = 0, r = 0, b = 0;
                fpdf_text.FPDFTextGetRect(_txtText, i, ref l, ref t, ref r, ref b);
                rects.Add((l, t, r, b));
            }

            // FPDFText_GetText escreve UTF-16 terminado em null; 'count+1' acomoda o terminador.
            var buf = new ushort[count + 1];
            int got = fpdf_text.FPDFTextGetText(_txtText, a, count, ref buf[0]);
            string text = string.Empty;
            if (got > 1)
            {
                var chars = new char[got - 1];   // descarta o terminador
                for (int i = 0; i < got - 1; i++) chars[i] = (char)buf[i];
                text = new string(chars);
            }
            return (rects, text);
        }
    }

    // Busca 'term' (case-insensitive) em TODO o documento. Retorna (página, índice do 1º char,
    // nº de chars) por ocorrência. Usa text pages temporárias (não a cacheada da seleção).
    public List<(int page, int index, int count)> FindAll(string term, int maxHits = 5000)
    {
        var hits = new List<(int, int, int)>();
        if (string.IsNullOrEmpty(term)) return hits;

        lock (Lib)
        {
            if (_doc is null) return hits;

            var wbuf = new ushort[term.Length + 1];     // UTF-16 terminado em null
            for (int i = 0; i < term.Length; i++) wbuf[i] = term[i];

            for (int p = 0; p < _wPt.Length && hits.Count < maxHits; p++)
            {
                var page = fpdfview.FPDF_LoadPage(_doc, p);
                if (page is null) continue;
                var tp = fpdf_text.FPDFTextLoadPage(page);
                if (tp is not null)
                {
                    var sh = fpdf_text.FPDFTextFindStart(tp, ref wbuf[0], 0, 0);   // flags 0 = case-insensitive
                    if (sh is not null)
                    {
                        while (hits.Count < maxHits && fpdf_text.FPDFTextFindNext(sh) != 0)
                        {
                            int idx = fpdf_text.FPDFTextGetSchResultIndex(sh);
                            int cnt = fpdf_text.FPDFTextGetSchCount(sh);
                            if (idx >= 0 && cnt > 0) hits.Add((p, idx, cnt));
                        }
                        fpdf_text.FPDFTextFindClose(sh);
                    }
                    fpdf_text.FPDFTextClosePage(tp);
                }
                fpdfview.FPDF_ClosePage(page);
            }
        }
        return hits;
    }

    // Rasteriza a página na largura pedida (proporção preservada) → buffer BGRA (8888) + dimensões.
    // SÍNCRONO/CPU-bound: chame em Task.Run. Serializado pelo lock de processo.
    public (byte[] pixels, int width, int height) RenderBgra(int index, int width)
    {
        lock (Lib)
        {
            if (_doc is null) return (Array.Empty<byte>(), 0, 0);
            double ratio = Ratio(index);
            int w = Math.Max(1, width);
            int h = Math.Max(1, (int)Math.Round(w * ratio));

            var page = fpdfview.FPDF_LoadPage(_doc, index);
            if (page is null) return (Array.Empty<byte>(), 0, 0);
            try
            {
                int stride   = w * 4;
                var pixels   = new byte[stride * h];
                var pin      = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    var bmp = fpdfview.FPDFBitmapCreateEx(w, h, FPDFBitmap_BGRA, pin.AddrOfPinnedObject(), stride);
                    if (bmp is null) return (Array.Empty<byte>(), 0, 0);
                    try
                    {
                        fpdfview.FPDFBitmapFillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);   // fundo branco (8888 ARGB)
                        fpdfview.FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, FPDF_ANNOT);
                    }
                    finally { fpdfview.FPDFBitmapDestroy(bmp); }
                }
                finally { pin.Free(); }
                return (pixels, w, h);
            }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    public void Dispose()
    {
        lock (Lib)
        {
            CloseTextPage();
            if (_doc is not null) { fpdfview.FPDF_CloseDocument(_doc); _doc = null; }
        }
    }

    // Codifica um buffer BGRA (8888) em PNG via WIC (Windows.Graphics.Imaging) — nativo, sem
    // dependência extra. Alimenta o cache LRU e o BitmapImage.SetSourceAsync, mantendo o pipeline
    // (e o cache comprimido) inalterados após a troca do motor para PDFium.
    internal static async Task<InMemoryRandomAccessStream?> EncodeBgraToPngAsync(byte[] bgra, int w, int h, CancellationToken ct)
    {
        if (bgra.Length == 0 || w <= 0 || h <= 0) return null;
        InMemoryRandomAccessStream? ras = null;
        try
        {
            ras = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras).AsTask(ct);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                                 (uint)w, (uint)h, 96, 96, bgra);
            await encoder.FlushAsync().AsTask(ct);
            ras.Seek(0);
            return ras;
        }
        catch
        {
            ras?.Dispose();
            return null;
        }
    }
}
