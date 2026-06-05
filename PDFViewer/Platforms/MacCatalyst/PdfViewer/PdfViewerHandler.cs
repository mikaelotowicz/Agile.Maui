// Platforms/iOS/PdfViewer/PdfViewerHandler.cs
//
// Motor: PDFKit.PdfView — o controle nativo de alto nível da Apple para visualização de
//   PDF. Diferente da versão anterior (que renderizava cada página manualmente em bitmap e
//   reimplementava virtualização/cache/prefetch/zoom à mão), aqui delegamos TUDO ao PdfView:
//
//     • Virtualização de páginas (tiling) ........ nativa, sob demanda
//     • Scroll contínuo vertical ................. PdfDisplayMode.SinglePageContinuous
//     • Zoom por pinch + double-tap .............. nativo do PdfView
//     • Navegação por página ..................... GoToPage / CurrentPage
//     • Seleção de texto, links, anotações ....... nativos do PdfKit
//     • Cache de renderização .................... gerenciado internamente pelo PdfKit
//     • Barra de miniaturas ...................... PdfThumbnailView (EnableThumbnailBar)
//
//   Resultado: handler enxuto, robusto e fiel ao comportamento esperado pelo usuário do iOS,
//   sem as classes de cache LRU / engine / scheduler que existiam antes.
//
// Mapeamento de zoom: o PdfView trabalha com ScaleFactor ABSOLUTO (1.0 = 1 ponto PDF por
//   ponto de tela). A API pública (PdfViewer) expressa o zoom RELATIVO ao "ajuste à página"
//   (ZoomFactor 1.0 = página ajustada à largura, igual ao Android). A ponte entre os dois é
//   _fitScale = ScaleFactorForSizeToFit: zoom nativo = ZoomFactor × _fitScale.
//
// MacCatalyst: este arquivo é copiado byte-a-byte para Platforms/MacCatalyst/ (mesmo namespace
//   Agile.Maui.Platforms.iOS). Sempre replique após editar.

using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using PdfKit;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

// ─────────────────────────────────────────────────────────────────────────────
// Handler MAUI
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfViewerHandler
    : ViewHandler<PdfViewer, PdfNativeView>
{
    private const string Tag = "Pdf/iOS";

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
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.ScrollOrientation)]   = (h, _) => h.ApplyScrollOrientation(),
            // No iOS as miniaturas são o DRAWER sobreposto (IsThumbnailBarOpen), não uma barra fixa
            // — EnableThumbnailBar serve só como flag de "recurso habilitado" para o app.
            [nameof(PdfViewer.EnableThumbnailBar)]  = (h, _) => { },
            [nameof(PdfViewer.IsThumbnailBarOpen)]  = (h, _) => h.ApplyThumbnailDrawer(),
            [nameof(PdfViewer.ThumbnailBarPlacement)] = (h, _) => h.ApplyThumbPlacement(),
            // PdfView gerencia render/cache/qualidade internamente (vetorial, sob demanda).
            // Estas propriedades não têm efeito sob o motor nativo — mantidas no mapper apenas
            // para não disparar o ViewMapper genérico e documentar a intenção.
            [nameof(PdfViewer.RenderScale)]         = (h, _) => { },
            [nameof(PdfViewer.MaxCacheMB)]          = (h, _) => { },
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => { },
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.ApplyPageBackground(),
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

    private PdfDocument?             _document;
    private CancellationTokenSource? _loadCts;
    private string?                  _tempPath;
    private bool                     _reportingPage;   // origem da mudança = scroll nativo → não re-sincronizar
    private bool                     _syncingZoom;     // origem da mudança = zoom nativo  → não re-aplicar
    private int                      _findCount;       // total de ocorrências da busca atual
    private int                      _findIndex = -1;  // índice 0-based da ocorrência atual

    public PdfViewerHandler() : base(Mapper, CommandMapper) { }

    protected override PdfNativeView CreatePlatformView() => new();

    // ── Impressão ───────────────────────────────────────────────────────────────
    // Em iPad o controlador é apresentado como popover ancorado na view; em iPhone vira folha modal.
    private void Print()
    {
        if (_document is null) { PdfViewerLog.Write(Tag, "Print: nenhum documento."); return; }

        // Arquivo local já em disco → imprime pela URL do arquivo (instantâneo). Antes usávamos
        // GetDataRepresentation(), que RE-SERIALIZA o PDF inteiro em memória SÍNCRONO na main thread
        // — travava ~segundos antes de abrir o diálogo. Para PDFs locais isso é totalmente evitável.
        var src = VirtualView?.Source;
        if (!string.IsNullOrEmpty(src) && System.IO.File.Exists(src))
        {
            PresentPrint(NSUrl.FromFilename(src));
            return;
        }

        // Stream/URL em memória (sem arquivo): serializa em BACKGROUND e apresenta na main thread,
        // para não congelar a UI durante a serialização.
        var doc = _document;
        _ = Task.Run(() =>
        {
            NSData? data = null;
            try { data = doc.GetDataRepresentation(); } catch { }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (data is null || data.Length == 0) { PdfViewerLog.Write(Tag, "Print: sem dados."); return; }
                PresentPrint(data);
            });
        });
    }

    // Toca o subsistema de impressão uma vez para reduzir a latência da 1ª apresentação. Rodado
    // num idle (após a UI assentar) para não competir com a carga inicial.
    private static bool _printWarmed;
    private void WarmUpPrinting()
    {
        if (_printWarmed) return;
        _printWarmed = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { _ = UIPrintInteractionController.SharedPrintController; _ = UIPrintInfo.PrintInfo; }
            catch { }
        });
    }

    // Apresenta o diálogo de impressão nativo com o item informado (NSUrl de arquivo ou NSData).
    private void PresentPrint(NSObject item)
    {
        var printInfo = UIPrintInfo.PrintInfo;
        printInfo.OutputType = UIPrintInfoOutputType.General;
        printInfo.JobName    = VirtualView?.Source is { Length: > 0 } s
            ? System.IO.Path.GetFileNameWithoutExtension(s)
            : (string.IsNullOrEmpty(VirtualView?.PrintJobName) ? "Document" : VirtualView!.PrintJobName);

        var controller = UIPrintInteractionController.SharedPrintController;
        controller.PrintInfo    = printInfo;
        controller.PrintingItem = item;

        if (PlatformView is not null)
            controller.PresentFromRectInView(PlatformView.Bounds, PlatformView, true, null);
        else
            controller.Present(true, null);
    }

    protected override void ConnectHandler(PdfNativeView pv)
    {
        base.ConnectHandler(pv);

        // Pré-aquece o subsistema de impressão do iOS num idle em background — a 1ª impressão
        // inicializa AirPrint/printerd (latência de ~segundos); tocá-lo cedo a deixa mais rápida.
        WarmUpPrinting();

        // O PdfView reporta o scroll de página; espelhamos no controle MAUI. O guard
        // _reportingPage impede que o setter de CurrentPage dispare SyncPage de volta
        // (que rolaria de novo, competindo com o scroll do usuário → jank/loop).
        pv.OnPageChanged = page =>
        {
            _reportingPage = true;
            VirtualView?.RaisePageChanged(page);
            _reportingPage = false;
        };

        // O PdfView reporta a escala absoluta; convertemos para ZoomFactor relativo ao fit.
        pv.OnZoomChanged = zoomFactor =>
        {
            if (_syncingZoom) return;
            _syncingZoom = true;
            if (VirtualView is not null) VirtualView.ZoomFactor = zoomFactor;
            _syncingZoom = false;
        };

        // Drawer de miniaturas: tocar fora (scrim) ou numa miniatura fecha → devolve a propriedade
        // TwoWay para false, mantendo o botão do app em sincronia.
        pv.OnThumbnailDrawerDismissed = () =>
        {
            if (VirtualView is not null) VirtualView.IsThumbnailBarOpen = false;
        };
        pv.OnPageTapped = () => VirtualView?.RaisePageTapped();

        // Link (URL externa) tocado no PDF: dispara LinkTapped; se o app não tratar (Handled),
        // abrimos a URL no app padrão do sistema (comportamento padrão do PdfView).
        pv.OnLinkClicked = uri =>
        {
            if (VirtualView is null || string.IsNullOrEmpty(uri)) return;
            var args = VirtualView.RaiseLinkTapped(uri, -1);
            if (!args.Handled)
                UIApplication.SharedApplication.OpenUrl(new NSUrl(uri), new UIApplicationOpenUrlOptions(), null);
        };

        ApplyZoomLimits();
        ApplySpacing();
        ApplyScrollOrientation();
        ApplyPageBackground();
        ApplyThumbPlacement();
        ApplyThumbnailDrawer();
    }

    protected override void DisconnectHandler(PdfNativeView pv)
    {
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;

        pv.OnPageChanged = null;
        pv.OnZoomChanged = null;
        pv.OnThumbnailDrawerDismissed = null;
        pv.OnPageTapped = null;
        pv.OnLinkClicked = null;
        pv.Teardown();
        _findCount = 0; _findIndex = -1;

        _document?.Dispose(); _document = null;

        DeleteTemp(_tempPath); _tempPath = null;

        base.DisconnectHandler(pv);
    }

    // ── Carregamento do documento ───────────────────────────────────────────────

    private void LoadDocument()
    {
        if (PlatformView is null || VirtualView is null) return;

        _loadCts?.Cancel(); _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        PlatformView.SetDocument(null);
        _document?.Dispose(); _document = null;

        var source = VirtualView.Source;
        var stream = VirtualView.PdfStream;
        if (string.IsNullOrWhiteSpace(source) && stream is null) return;

        bool isUrl = !string.IsNullOrWhiteSpace(source)
            && (source!.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        PdfViewerLog.Write(Tag,
            $"LoadDocument source='{(source?.Length > 50 ? source[..50] + "…" : source ?? "stream")}' isUrl={isUrl}");

        var vv = VirtualView;
        var pv = PlatformView;

        _ = Task.Run(async () =>
        {
            string? newTemp = null;   // arquivo temporário desta carga (URL/stream) — só comitado em sucesso
            try
            {
                PdfDocument doc;

                if (stream is not null)
                {
                    var data = await ReadStreamAsync(stream, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    doc = new PdfDocument(data) ?? throw new InvalidOperationException("PDF inválido (stream).");
                }
                else if (isUrl)
                {
                    using var http  = PdfHttpClient.Create();
                    var       bytes = await http.GetByteArrayAsync(source, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    PdfViewerLog.Write(Tag, $"download {bytes.Length / 1024} KB");
                    using var data = NSData.FromArray(bytes);
                    doc = new PdfDocument(data) ?? throw new InvalidOperationException("PDF inválido (URL).");
                }
                else
                {
                    if (!System.IO.File.Exists(source))
                        throw new FileNotFoundException($"Arquivo não encontrado: {source}");
                    using var url = NSUrl.FromFilename(source!);
                    doc = new PdfDocument(url) ?? throw new InvalidOperationException("PDF inválido (arquivo).");
                }

                if (doc.IsLocked)
                {
                    // PDF protegido: tenta destravar com a senha informada (PDFium/PdfKit).
                    var pwd = vv.Password;
                    bool unlocked = !string.IsNullOrEmpty(pwd) && doc.Unlock(pwd!);
                    if (!unlocked)
                    {
                        doc.Dispose();
                        throw new InvalidOperationException("PDF protegido por senha (senha ausente/incorreta).");
                    }
                }

                int count = (int)doc.PageCount;
                if (count == 0) { doc.Dispose(); throw new InvalidOperationException("PDF com 0 páginas."); }

                if (cts.IsCancellationRequested) { doc.Dispose(); DeleteTemp(newTemp); return; }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (cts.IsCancellationRequested || PlatformView is null) { doc.Dispose(); DeleteTemp(newTemp); return; }

                    DeleteTemp(_tempPath);
                    _tempPath = newTemp;
                    _document = doc;

                    pv.SetDocument(doc);
                    ApplySpacing();
                    ApplyScrollOrientation();
                    ApplyPageBackground();
                    ResetZoomTo100();   // todo documento abre em 100% (página ajustada) — paridade com Android
                    ApplyZoomLimits();
                    ClearSearchState(); // novo documento → zera busca anterior
                    PdfViewerLog.Write(Tag, $"loaded pages={count}");
                    vv.RaiseDocumentLoaded(count);
                });
            }
            catch (OperationCanceledException) { DeleteTemp(newTemp); }
            catch (Exception ex)
            {
                DeleteTemp(newTemp);
                PdfViewerLog.Write(Tag, $"LoadDocument ERRO: [{ex.GetType().Name}] {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!cts.IsCancellationRequested) vv.RaiseDocumentLoadFailed(ex.Message);
                });
            }
        }, cts.Token);
    }

    private static async Task<NSData> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return NSData.FromArray(ms.ToArray());
    }

    private static void DeleteTemp(string? path)
    {
        if (path is null) return;
        try { System.IO.File.Delete(path); } catch { }
    }

    // ── Sincronização de propriedades ───────────────────────────────────────────

    private void SyncPage()
    {
        if (_reportingPage || PlatformView is null || VirtualView is null || _document is null) return;
        PlatformView.GoToPage(VirtualView.CurrentPage);
    }

    private void SyncZoom()
    {
        if (_syncingZoom || PlatformView is null || VirtualView is null) return;
        PlatformView.SetZoomFactor(VirtualView.ZoomFactor);
    }

    private void ResetZoomTo100()
    {
        if (PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        PlatformView.ResetZoom();
        VirtualView.ZoomFactor = 1.0;
        _syncingZoom = false;
    }

    private void ApplyZoomLimits()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetZoomRange(VirtualView.MinZoom, VirtualView.MaxZoom, VirtualView.IsPinchZoomEnabled);
    }

    private void ApplySpacing()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetPageSpacing((nfloat)VirtualView.PageSpacing);
    }

    private void ApplyThumbnailBar()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetThumbnailBarVisible(VirtualView.EnableThumbnailBar);
    }

    private void ApplyThumbnailDrawer()
    {
        if (PlatformView is null || VirtualView is null) return;
        // None desabilita; só abre quando habilitado (Left/Right) E solicitado.
        bool open = VirtualView.IsThumbnailBarOpen
                 && VirtualView.ThumbnailBarPlacement != PdfThumbnailPlacement.None;
        PlatformView.SetThumbnailDrawerOpen(open);
    }

    private void ApplyThumbPlacement()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetThumbnailPlacement(VirtualView.ThumbnailBarPlacement == PdfThumbnailPlacement.Right);
        ApplyThumbnailDrawer();   // reconcilia (None fecha; Left/Right reabre se solicitado)
    }

    // ── Orientação do scroll ──────────────────────────────────────────────────────
    private void ApplyScrollOrientation()
    {
        if (PlatformView is null || VirtualView is null) return;

        int page = VirtualView.CurrentPage;   // a troca de DisplayMode/UsePageViewController volta p/ a 1ª página
        PlatformView.SetHorizontal(VirtualView.ScrollOrientation == PdfScrollOrientation.Horizontal);

        // Zoom MÍNIMO ao trocar de modo (paridade com Android/Windows).
        _syncingZoom = true;
        VirtualView.ZoomFactor = VirtualView.MinZoom;
        PlatformView.SetZoomFactor(VirtualView.MinZoom);
        _syncingZoom = false;

        // Restaura a página atual depois que o novo modo/controller assenta.
        var pv = PlatformView;
        MainThread.BeginInvokeOnMainThread(() => pv?.GoToPage(page));
    }

    // ── Cor de fundo (deck atrás das páginas) ──────────────────────────────────────
    private void ApplyPageBackground()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.SetPageBackground(VirtualView.PageBackgroundColor.ToPlatform());
    }

    // ── Busca (PDFKit FindString) ──────────────────────────────────────────────────
    private void DoSearch(string term)
    {
        if (PlatformView is null) return;
        _findCount = PlatformView.FindAll(term);
        _findIndex = _findCount > 0 ? 0 : -1;
        if (_findIndex >= 0) PlatformView.GoToMatch(_findIndex);
        VirtualView?.RaiseSearchResult(_findCount, _findIndex);
    }

    private void StepHit(int delta)
    {
        if (_findCount == 0 || PlatformView is null) return;
        _findIndex = (_findIndex + delta + _findCount) % _findCount;
        PlatformView.GoToMatch(_findIndex);
        VirtualView?.RaiseSearchResult(_findCount, _findIndex);
    }

    private void ClearSearchState()
    {
        _findCount = 0; _findIndex = -1;
        PlatformView?.ClearSearch();
        VirtualView?.RaiseSearchResult(0, -1);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfNativeView — UIView raiz que encapsula o PdfView nativo e a barra de miniaturas.
// Toda a lógica de scroll/zoom/virtualização é do PdfKit; esta classe só faz a ponte
// com os callbacks do handler e o layout (PdfView ocupa a área principal; a barra de
// miniaturas, quando visível, ocupa uma faixa à esquerda).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfNativeView : UIView
{
    // Cinza de leitor do "deck" atrás das páginas (estilo Adobe/Edge), igual às demais
    // plataformas (#525659). As folhas brancas do PDF se destacam sobre este fundo.
    private static readonly UIColor ReaderBg = UIColor.FromRGB((byte)0x52, (byte)0x56, (byte)0x59);

    private const float ThumbBarWidth = 84f;

    private readonly PdfView           _pdfView;
    private          PdfThumbnailView? _thumbView;

    private NSObject? _pageObserver;
    private NSObject? _scaleObserver;

    // Ponte zoom absoluto (PdfView) ↔ relativo (PdfViewer). _fitScale = escala que ajusta a
    // página à viewport; ZoomFactor = ScaleFactor / _fitScale. Recalculado a cada layout.
    private nfloat _fitScale = 1f;
    private double _minZoom  = 0.5;
    private double _maxZoom  = 8.0;
    private bool   _pinchEnabled = true;
    private bool   _thumbnailsRequested;

    // ── Drawer de miniaturas (overlay deslizante à direita) ─────────────────────
    private const float       DrawerWidth  = 132f;
    private const float       ThumbRowH    = 150f;
    private UIView?           _drawerScrim;
    private UIView?           _drawerPanel;
    private UITableView?      _drawerTable;        // lista própria (uma miniatura por linha, top-aligned)
    private PdfThumbSource?   _thumbSource;
    private readonly Dictionary<int, UIImage> _thumbImages = new();   // cache de miniaturas renderizadas
    private bool              _drawerOpen;
    private bool              _thumbRight = true;   // lado do drawer (direita por padrão)

    public Action<int>?    OnPageChanged { get; set; }
    public Action<double>? OnZoomChanged { get; set; }
    /// <summary>Disparado ao tocar fora (scrim) ou numa miniatura — o handler fecha o drawer.</summary>
    public Action?         OnThumbnailDrawerDismissed { get; set; }
    public Action?         OnPageTapped { get; set; }
    /// <summary>Disparado ao tocar num link de URL externa no PDF (string da URL).</summary>
    public Action<string>? OnLinkClicked { get; set; }

    // Ocorrências da busca atual (PDFKit). Realçadas via HighlightedSelections.
    private PdfSelection[] _matches = Array.Empty<PdfSelection>();

    public PdfNativeView()
    {
        BackgroundColor = ReaderBg;
        ClipsToBounds   = true;

        _pdfView = new PdfView
        {
            BackgroundColor   = ReaderBg,
            DisplayMode       = PdfDisplayMode.SinglePageContinuous,
            DisplayDirection  = PdfDisplayDirection.Vertical,
            DisplaysPageBreaks = true,
            AutoScales        = true,
        };
        AddSubview(_pdfView);

        _pdfView.AddGestureRecognizer(new UITapGestureRecognizer(() => OnPageTapped?.Invoke())
        {
            CancelsTouchesInView = false,
            NumberOfTapsRequired = 1,
        });

        // Delegate p/ interceptar toque em links de URL (LinkTapped). Links internos (destino de
        // página) o PdfView navega sozinho; este callback dispara só para URLs externas.
        _pdfView.Delegate = new PdfLinkDelegate(this);

        // Observa as notificações do PdfView filtrando pela NOSSA instância (vários
        // PdfViewer na mesma tela teriam, cada um, seu próprio PdfView).
        _pageObserver = PdfView.Notifications.ObservePageChanged((_, e) =>
        {
            if (!ReferenceEquals(e.Notification.Object, _pdfView)) return;
            int idx = CurrentPageIndex();
            if (idx >= 0) OnPageChanged?.Invoke(idx);
            // Tocar numa miniatura navega a página → fecha o drawer (o scrim bloqueia o PDF, então
            // a única mudança de página com o drawer aberto vem da seleção de miniatura).
            if (_drawerOpen) OnThumbnailDrawerDismissed?.Invoke();
        });

        _scaleObserver = PdfView.Notifications.ObserveScaleChanged((_, e) =>
        {
            if (!ReferenceEquals(e.Notification.Object, _pdfView)) return;
            nfloat fit = _fitScale > 0.0001f ? _fitScale : 1f;
            OnZoomChanged?.Invoke((double)(_pdfView.ScaleFactor / fit));
        });
    }

    // ── Documento ───────────────────────────────────────────────────────────────

    internal void SetDocument(PdfDocument? doc)
    {
        _thumbImages.Clear();   // miniaturas do doc anterior (índices reusados)
        _pdfView.Document = doc;
        if (doc is not null)
        {
            _pdfView.AutoScales = true;          // ajusta à largura ao abrir
            _pdfView.GoToFirstPage(this);
            RecomputeFitScale();
            ApplyZoomLimitsToView();
            if (_thumbView is not null) _thumbView.PdfView = _pdfView;
        }
    }

    /// <summary>Libera observers e documento. Chamado no DisconnectHandler.</summary>
    internal void Teardown()
    {
        if (_pageObserver  is not null) { _pageObserver.Dispose();  _pageObserver  = null; }
        if (_scaleObserver is not null) { _scaleObserver.Dispose(); _scaleObserver = null; }

        if (_drawerTable is not null) _drawerTable.Source = null;
        _drawerTable?.RemoveFromSuperview(); _drawerTable?.Dispose(); _drawerTable = null;
        _thumbSource?.Dispose(); _thumbSource = null;
        _drawerPanel?.RemoveFromSuperview(); _drawerPanel?.Dispose(); _drawerPanel = null;
        _drawerScrim?.RemoveFromSuperview(); _drawerScrim?.Dispose(); _drawerScrim = null;
        foreach (var im in _thumbImages.Values) im.Dispose();
        _thumbImages.Clear();
        _drawerOpen = false;

        _pdfView.Document = null;
    }

    // ── Navegação ─────────────────────────────────────────────────────────────────

    internal void GoToPage(int index)
    {
        var doc = _pdfView.Document;
        if (doc is null || index < 0 || index >= (int)doc.PageCount) return;
        var page = doc.GetPage((nint)index);
        if (page is not null) _pdfView.GoToPage(page);
    }

    // ── Orientação do scroll ──────────────────────────────────────────────────────
    // Horizontal = paginado tipo livro (UsePageViewController). Vertical = scroll contínuo.
    internal void SetHorizontal(bool horizontal)
    {
        if (horizontal)
        {
            _pdfView.DisplayMode      = PdfDisplayMode.SinglePage;
            _pdfView.DisplayDirection = PdfDisplayDirection.Horizontal;
            _pdfView.UsePageViewController(true, null);
        }
        else
        {
            _pdfView.UsePageViewController(false, null);
            _pdfView.DisplayMode      = PdfDisplayMode.SinglePageContinuous;
            _pdfView.DisplayDirection = PdfDisplayDirection.Vertical;
        }
        _pdfView.AutoScales = true;
        RecomputeFitScale();
        ApplyZoomLimitsToView();
    }

    // ── Cor de fundo (deck atrás das páginas) ──────────────────────────────────────
    internal void SetPageBackground(UIColor? color)
    {
        if (color is null) return;
        _pdfView.BackgroundColor = color;
        BackgroundColor          = color;
    }

    // ── Busca (PDFKit) ─────────────────────────────────────────────────────────────
    internal int FindAll(string term)
    {
        ClearSearch();
        var doc = _pdfView.Document;
        if (doc is null || string.IsNullOrWhiteSpace(term)) return 0;
        var found = doc.Find(term, NSStringCompareOptions.CaseInsensitiveSearch);
        _matches = found ?? Array.Empty<PdfSelection>();
        if (_matches.Length > 0)
        {
            foreach (var m in _matches) m.Color = UIColor.Yellow;
            _pdfView.HighlightedSelections = _matches;
        }
        return _matches.Length;
    }

    internal void GoToMatch(int index)
    {
        if (index < 0 || index >= _matches.Length) return;
        var sel = _matches[index];
        foreach (var m in _matches) m.Color = UIColor.Yellow;
        sel.Color = UIColor.Orange;                 // realça a ocorrência ATUAL em laranja
        _pdfView.HighlightedSelections = _matches;  // reatribui p/ refletir as cores
        _pdfView.GoToSelection(sel);
    }

    internal void ClearSearch()
    {
        _matches = Array.Empty<PdfSelection>();
        _pdfView.HighlightedSelections = null;
    }

    // Chamado pelo delegate ao tocar num link de URL externa.
    internal void HandleLinkClicked(NSUrl? url)
    {
        var s = url?.AbsoluteString;
        if (!string.IsNullOrEmpty(s)) OnLinkClicked?.Invoke(s!);
    }

    // ── Miniaturas (drawer) — render por página com cache ──────────────────────────
    internal int ThumbPageCount => (int)(_pdfView.Document?.PageCount ?? 0);

    internal UIImage? GetThumb(int page)
    {
        if (_thumbImages.TryGetValue(page, out var img)) return img;
        var pg = _pdfView.Document?.GetPage((nint)page);
        if (pg is null) return null;
        var thumb = pg.GetThumbnail(new CGSize(100, 130), PdfDisplayBox.Crop);
        if (thumb is not null) _thumbImages[page] = thumb;
        return thumb;
    }

    // Toque numa miniatura: navega à página e fecha o drawer (via callback do handler).
    internal void OnThumbSelected(int page)
    {
        GoToPage(page);
        OnThumbnailDrawerDismissed?.Invoke();
    }

    private int CurrentPageIndex()
    {
        var doc  = _pdfView.Document;
        var page = _pdfView.CurrentPage;
        if (doc is null || page is null) return -1;
        return (int)doc.GetPageIndex(page);
    }

    // ── Zoom ────────────────────────────────────────────────────────────────────────

    internal void SetZoomRange(double minZoom, double maxZoom, bool pinchEnabled)
    {
        _minZoom      = minZoom > 0 ? minZoom : 0.1;
        _maxZoom      = maxZoom >= _minZoom ? maxZoom : _minZoom;
        _pinchEnabled = pinchEnabled;
        ApplyZoomLimitsToView();
    }

    internal void SetZoomFactor(double zoomFactor)
    {
        RecomputeFitScale();
        nfloat target = (nfloat)(Math.Clamp(zoomFactor, _minZoom, _maxZoom)) * _fitScale;
        // Em 100% reativa o auto-fit (reajusta em rotação/resize); ampliado, fixa a escala.
        _pdfView.AutoScales = Math.Abs(zoomFactor - 1.0) < 0.001;
        _pdfView.ScaleFactor = target;
    }

    internal void ResetZoom()
    {
        _pdfView.AutoScales = true;
        RecomputeFitScale();
        _pdfView.ScaleFactor = _fitScale;
    }

    private void RecomputeFitScale()
    {
        nfloat fit = _pdfView.ScaleFactorForSizeToFit;
        if (fit > 0.0001f) _fitScale = fit;
    }

    private void ApplyZoomLimitsToView()
    {
        RecomputeFitScale();
        if (_pinchEnabled)
        {
            _pdfView.MinScaleFactor = _fitScale * (nfloat)_minZoom;
            _pdfView.MaxScaleFactor = _fitScale * (nfloat)_maxZoom;
        }
        else
        {
            // Pinch desabilitado: trava no fit (100%) impedindo qualquer mudança de escala.
            _pdfView.MinScaleFactor = _fitScale;
            _pdfView.MaxScaleFactor = _fitScale;
        }
    }

    // ── Aparência ─────────────────────────────────────────────────────────────────

    internal void SetPageSpacing(nfloat spacing)
    {
        // PageBreakMargins desenha um respiro ao redor de cada página; usamos metade no topo
        // e metade na base para que o espaço VISÍVEL entre páginas adjacentes seja "spacing".
        nfloat half = (nfloat)Math.Max(0.0, (double)spacing / 2.0);
        _pdfView.PageBreakMargins = new UIEdgeInsets(half, 0, half, 0);
    }

    internal void SetThumbnailBarVisible(bool visible)
    {
        _thumbnailsRequested = visible;

        if (visible && _thumbView is null)
        {
            _thumbView = new PdfThumbnailView
            {
                PdfView         = _pdfView,
                BackgroundColor = ReaderBg,
                LayoutMode      = PdfThumbnailLayoutMode.Vertical,
                ThumbnailSize   = new CGSize(64, 88),
            };
            AddSubview(_thumbView);
        }
        else if (!visible && _thumbView is not null)
        {
            _thumbView.RemoveFromSuperview();
            _thumbView.Dispose();
            _thumbView = null;
        }

        SetNeedsLayout();
    }

    // ── Drawer de miniaturas (overlay à direita) ──────────────────────────────────
    internal void SetThumbnailDrawerOpen(bool open)
    {
        if (open) OpenDrawer();
        else      CloseDrawer();
    }

    internal void SetThumbnailPlacement(bool right)
    {
        if (_thumbRight == right) return;
        _thumbRight = right;
        // Reposiciona conforme o estado atual (aberto = visível no novo lado; fechado = fora dele).
        if (_drawerPanel is not null) LayoutDrawer(_drawerOpen);
    }

    private void BuildDrawer()
    {
        if (_drawerPanel is not null) return;

        _drawerScrim = new UIView { BackgroundColor = UIColor.FromRGBA(0f, 0f, 0f, 0.4f), Alpha = 0f, Hidden = true };
        _drawerScrim.AddGestureRecognizer(new UITapGestureRecognizer(() => OnThumbnailDrawerDismissed?.Invoke()));
        AddSubview(_drawerScrim);

        _drawerPanel = new UIView { BackgroundColor = UIColor.FromRGB((byte)0xF5, (byte)0xF5, (byte)0xF7), Hidden = true };
        _drawerPanel.Layer.ShadowColor   = UIColor.Black.CGColor;
        _drawerPanel.Layer.ShadowOpacity = 0.25f;
        _drawerPanel.Layer.ShadowRadius  = 8f;
        _drawerPanel.Layer.ShadowOffset  = new CGSize(-2, 0);

        _drawerTable = new UITableView
        {
            BackgroundColor = UIColor.Clear,
            SeparatorStyle  = UITableViewCellSeparatorStyle.None,
            RowHeight       = (nfloat)ThumbRowH,
            AllowsSelection = true,
        };
        _drawerTable.RegisterClassForCellReuse(typeof(PdfThumbCell), PdfThumbCell.ReuseId);
        _thumbSource = new PdfThumbSource(this);
        _drawerTable.Source = _thumbSource;
        _drawerPanel.AddSubview(_drawerTable);
        AddSubview(_drawerPanel);
    }

    private void OpenDrawer()
    {
        if (_drawerOpen || _pdfView.Document is null) return;
        BuildDrawer();
        _drawerOpen = true;

        if (_drawerTable is not null && _thumbSource is not null)
        {
            _thumbSource.Count   = (int)_pdfView.Document.PageCount;
            _thumbSource.Current = CurrentPageIndex();
            _drawerTable.ReloadData();   // começa no TOPO (linha 0); não rola para a página atual
        }

        _drawerScrim!.Hidden = false;
        _drawerPanel!.Hidden = false;
        BringSubviewToFront(_drawerScrim);
        BringSubviewToFront(_drawerPanel);

        LayoutDrawer(openState: false);   // começa fora da tela (à direita)
        UIView.Animate(0.22, () =>
        {
            _drawerScrim.Alpha = 1f;
            LayoutDrawer(openState: true);
        });
    }

    private void CloseDrawer()
    {
        if (!_drawerOpen || _drawerPanel is null || _drawerScrim is null) return;
        _drawerOpen = false;
        var scrim = _drawerScrim; var panel = _drawerPanel;
        UIView.Animate(0.20, () =>
        {
            scrim.Alpha = 0f;
            LayoutDrawer(openState: false);
        }, () =>
        {
            if (!_drawerOpen) { scrim.Hidden = true; panel.Hidden = true; }
        });
    }

    private void LayoutDrawer(bool openState)
    {
        if (_drawerPanel is null || _drawerScrim is null) return;
        _drawerScrim.Frame = Bounds;
        nfloat w = (nfloat)DrawerWidth;
        // Direita: aberto encosta na borda direita, fechado some à direita. Esquerda: espelhado.
        nfloat x = _thumbRight ? (openState ? Bounds.Width - w : Bounds.Width)
                               : (openState ? 0 : -w);
        _drawerPanel.Frame = new CGRect(x, 0, w, Bounds.Height);
        if (_drawerTable is not null)
            _drawerTable.Frame = new CGRect(0, 0, w, Bounds.Height);   // lista própria: top-aligned e rolável
    }

    // ── Layout ────────────────────────────────────────────────────────────────────

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        if (_thumbnailsRequested && _thumbView is not null)
        {
            _thumbView.Frame = new CGRect(0, 0, ThumbBarWidth, Bounds.Height);
            _pdfView.Frame   = new CGRect(ThumbBarWidth, 0, Bounds.Width - ThumbBarWidth, Bounds.Height);
        }
        else
        {
            _pdfView.Frame = Bounds;
        }

        // Mantém o drawer posicionado em rotação/resize (sem animar).
        if (_drawerPanel is not null && !_drawerPanel.Hidden)
            LayoutDrawer(_drawerOpen);

        // O tamanho da viewport mudou → o fit muda. Recalcula a ponte de zoom e reaplica
        // os limites, mantendo ZoomFactor coerente após rotação/resize.
        if (_pdfView.Document is not null)
            ApplyZoomLimitsToView();
    }

    // Delegate do PdfView: intercepta toque em links de URL externa (pdfViewWillClickOnLink).
    // Links internos (destino de página) são navegados pelo próprio PdfView.
    private sealed class PdfLinkDelegate : PdfViewDelegate
    {
        private readonly PdfNativeView _owner;
        public PdfLinkDelegate(PdfNativeView owner) => _owner = owner;
        public override void WillClickOnLink(PdfView sender, NSUrl url) => _owner.HandleLinkClicked(url);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Drawer de miniaturas — célula + fonte da tabela (uma miniatura por linha)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfThumbCell : UITableViewCell
{
    public const string ReuseId = "pdfthumb";
    private readonly UIImageView _img;
    private readonly UILabel     _label;
    private readonly UIView      _card;
    public UIImageView Image => _img;

    public PdfThumbCell(IntPtr handle) : base(handle)
    {
        BackgroundColor = UIColor.Clear;
        SelectionStyle  = UITableViewCellSelectionStyle.None;

        _card = new UIView { BackgroundColor = UIColor.White };
        _card.Layer.BorderWidth = 1f;
        _card.Layer.BorderColor = UIColor.FromRGB((byte)0xCF, (byte)0xCF, (byte)0xD3).CGColor;
        _img = new UIImageView { ContentMode = UIViewContentMode.ScaleAspectFit, ClipsToBounds = true };
        _card.AddSubview(_img);

        _label = new UILabel { Font = UIFont.SystemFontOfSize(11f), TextAlignment = UITextAlignment.Center };
        ContentView.AddSubview(_card);
        ContentView.AddSubview(_label);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        var b = ContentView.Bounds;
        nfloat cardW = 92f, cardH = 122f;
        nfloat cx = (nfloat)((b.Width - cardW) / 2.0);
        _card.Frame  = new CGRect(cx, 6, cardW, cardH);
        _img.Frame   = new CGRect(2, 2, cardW - 4, cardH - 4);
        _label.Frame = new CGRect(0, cardH + 8, b.Width, 16);
    }

    public void Configure(int page, bool current)
    {
        _label.Text = (page + 1).ToString();
        var accent = UIColor.FromRGB((byte)0x3F, (byte)0x51, (byte)0xB5);
        var border = UIColor.FromRGB((byte)0xCF, (byte)0xCF, (byte)0xD3);
        var label  = UIColor.FromRGB((byte)0x55, (byte)0x55, (byte)0x5A);
        _label.TextColor        = current ? accent : label;
        _card.Layer.BorderColor = (current ? accent : border).CGColor;
        _card.Layer.BorderWidth = current ? 2f : 1f;
    }
}

internal sealed class PdfThumbSource : UITableViewSource
{
    private readonly PdfNativeView _owner;
    public int Count;
    public int Current = -1;

    public PdfThumbSource(PdfNativeView owner) => _owner = owner;

    public override nint RowsInSection(UITableView tableView, nint section) => Count;

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        var cell = (PdfThumbCell)tableView.DequeueReusableCell(PdfThumbCell.ReuseId, indexPath);
        int page = (int)indexPath.Row;
        cell.Configure(page, page == Current);
        cell.Image.Image = _owner.GetThumb(page);
        return cell;
    }

    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, false);
        _owner.OnThumbSelected((int)indexPath.Row);
    }
}
