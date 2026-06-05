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
            [nameof(PdfViewer.CurrentPage)]         = (h, _) => h.SyncPage(),
            [nameof(PdfViewer.ZoomFactor)]          = (h, _) => h.SyncZoom(),
            [nameof(PdfViewer.MinZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.MaxZoom)]             = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomLimits(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.EnableThumbnailBar)]  = (h, _) => h.ApplyThumbnailBar(),
            // PdfView gerencia render/cache/qualidade internamente (vetorial, sob demanda).
            // Estas propriedades não têm efeito sob o motor nativo — mantidas no mapper apenas
            // para não disparar o ViewMapper genérico e documentar a intenção.
            [nameof(PdfViewer.RenderScale)]         = (h, _) => { },
            [nameof(PdfViewer.MaxCacheMB)]          = (h, _) => { },
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => { },
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => { },
        };

    private PdfDocument?             _document;
    private CancellationTokenSource? _loadCts;
    private string?                  _tempPath;
    private bool                     _reportingPage;   // origem da mudança = scroll nativo → não re-sincronizar
    private bool                     _syncingZoom;     // origem da mudança = zoom nativo  → não re-aplicar

    public PdfViewerHandler() : base(Mapper) { }

    protected override PdfNativeView CreatePlatformView() => new();

    protected override void ConnectHandler(PdfNativeView pv)
    {
        base.ConnectHandler(pv);

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

        ApplyZoomLimits();
        ApplySpacing();
        ApplyThumbnailBar();
    }

    protected override void DisconnectHandler(PdfNativeView pv)
    {
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;

        pv.OnPageChanged = null;
        pv.OnZoomChanged = null;
        pv.Teardown();

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
                    doc.Dispose();
                    throw new InvalidOperationException("PDF protegido por senha.");
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
                    ApplyThumbnailBar();
                    ResetZoomTo100();   // todo documento abre em 100% (página ajustada) — paridade com Android
                    ApplyZoomLimits();
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

    public Action<int>?    OnPageChanged { get; set; }
    public Action<double>? OnZoomChanged { get; set; }

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

        // Observa as notificações do PdfView filtrando pela NOSSA instância (vários
        // PdfViewer na mesma tela teriam, cada um, seu próprio PdfView).
        _pageObserver = PdfView.Notifications.ObservePageChanged((_, e) =>
        {
            if (!ReferenceEquals(e.Notification.Object, _pdfView)) return;
            int idx = CurrentPageIndex();
            if (idx >= 0) OnPageChanged?.Invoke(idx);
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

        // O tamanho da viewport mudou → o fit muda. Recalcula a ponte de zoom e reaplica
        // os limites, mantendo ZoomFactor coerente após rotação/resize.
        if (_pdfView.Document is not null)
            ApplyZoomLimitsToView();
    }
}
