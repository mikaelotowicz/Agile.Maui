// Platforms/iOS/PdfViewer/PdfViewerHandler.cs
//
// Motor: CoreGraphics.CGPDFDocument (equivalente ao PDFium na plataforma Apple).
// Virtualização: UIScrollView com UIImageView por página — só cria views para páginas no viewport.
// Cache: LRU de UIImage com limite em MB; TrimToWindow libera páginas fora da janela ativa.
// Prefetch: renderiza em background N páginas acima/abaixo e adiciona UIImageView ao scroll.
// Zoom: UIScrollView.ZoomScale (pinch nativo) + double-tap.

using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using PdfKit;
using UIKit;
using Agile.Maui;

namespace Agile.Maui.Platforms.iOS;

// ─────────────────────────────────────────────────────────────────────────────
// Handler MAUI
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfViewerHandler
    : ViewHandler<PdfViewer, PdfScrollContainer>
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
            [nameof(PdfViewer.RenderScale)]         = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PageBackgroundColor)] = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PageSpacing)]         = (h, _) => h.ApplySpacing(),
            [nameof(PdfViewer.IsPinchZoomEnabled)]  = (h, _) => h.ApplyZoomEnabled(),
            [nameof(PdfViewer.EnablePageCaching)]   = (h, _) => h.ReRenderAll(),
            [nameof(PdfViewer.PrefetchAbove)]       = (h, _) => { },
            [nameof(PdfViewer.PrefetchBelow)]       = (h, _) => { },
        };

    private CgPdfEngine?          _engine;
    // Holder compartilhado: o container e o handler leem o cache SEMPRE por esta
    // referência única. Trocar MaxCacheMB substitui apenas holder.Cache — nunca
    // desconecta o container do cache real (fix inconsistência de instância).
    private readonly PdfCacheRef  _cacheRef = new();
    private PdfImageLruCache?     _cache => _cacheRef.Cache;
    private CancellationTokenSource?   _loadCts;
    private CancellationTokenSource?   _prefetchCts;
    private bool                       _syncingPage;
    private bool                       _syncingZoom;
    private int                        _lastPrefetchCenter = -1;  // dedup: evita re-prefetch do mesmo centro
    private string?                    _tempPath;

    private static readonly NSUrlSession _session = NSUrlSession.FromConfiguration(
        NSUrlSessionConfiguration.DefaultSessionConfiguration, null!, new NSOperationQueue());

    public PdfViewerHandler() : base(Mapper) { }

    protected override PdfScrollContainer CreatePlatformView() => new();

    protected override void ConnectHandler(PdfScrollContainer pv)
    {
        base.ConnectHandler(pv);

        pv.OnPageChanged = page =>
        {
            if (_syncingPage) return;
            VirtualView?.RaisePageChanged(page);
            TrimAndPrefetch(page);
        };

        pv.OnZoomChanged = zoom =>
        {
            if (_syncingZoom) return;
            _syncingZoom = true;
            if (VirtualView is not null) VirtualView.ZoomFactor = zoom;
            _syncingZoom = false;
            ReRenderAll();
        };

        ApplyCache();
        ApplyZoomLimits();
    }

    protected override void DisconnectHandler(PdfScrollContainer pv)
    {
        _loadCts?.Cancel();     _loadCts?.Dispose();     _loadCts    = null;
        _prefetchCts?.Cancel(); _prefetchCts?.Dispose(); _prefetchCts = null;

        pv.OnPageChanged = null;
        pv.OnZoomChanged = null;
        pv.ClearDocument();

        _engine?.Dispose(); _engine = null;
        _cacheRef.Cache?.EvictAll(); _cacheRef.Cache = null;

        if (_tempPath is not null) { try { System.IO.File.Delete(_tempPath); } catch { } _tempPath = null; }

        base.DisconnectHandler(pv);
    }

    // ── LoadDocument ──────────────────────────────────────────────────────────

    private void LoadDocument()
    {
        if (PlatformView is null || VirtualView is null) return;

        PdfViewerLog.Write("Pdf/iOS", $"LoadDocument start Source={(VirtualView.Source ?? "(null)")}");

        _loadCts?.Cancel(); _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _prefetchCts?.Cancel();
        PlatformView.ClearDocument();
        _engine?.Dispose(); _engine = null;
        _lastPrefetchCenter = -1;

        var source = VirtualView.Source;
        var stream = VirtualView.PdfStream;
        if (string.IsNullOrWhiteSpace(source) && stream is null) return;

        bool isUrl = !string.IsNullOrWhiteSpace(source)
            && (source!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        var vv = VirtualView;
        var pv = PlatformView;

        _ = Task.Run(async () =>
        {
            // Path temp desta carga: variável local para evitar race com cargas
            // concorrentes. Só é comitado em _tempPath (na main thread) após sucesso;
            // se cancelado/falho, é deletado aqui mesmo. Evita leak/sobrescrita.
            string? newTemp = null;
            try
            {
                string localPath;

                if (stream is not null)
                {
                    newTemp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    await using (var fs = new FileStream(newTemp, FileMode.Create))
                        await stream.CopyToAsync(fs, cts.Token);
                    localPath = newTemp;
                }
                else if (isUrl)
                {
                    var data = await DownloadDataAsync(new NSUrl(source!), cts.Token);
                    if (cts.IsCancellationRequested) return;
                    if (data is null || data.Length == 0) throw new Exception("Download vazio");
                    newTemp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
                    data.Save(newTemp, atomically: true);
                    try
                    {
                        var fi = new System.IO.FileInfo(newTemp);
                        PdfViewerLog.Write("Pdf/iOS", $"Downloaded file saved: {newTemp} size={fi.Length} bytes");
                        // Verifica header mínimo PDF
                        var headerBytes = System.IO.File.ReadAllBytes(newTemp);
                        string header = headerBytes.Length >= 4 ? System.Text.Encoding.ASCII.GetString(headerBytes, 0, 4) : "";
                        PdfViewerLog.Write("Pdf/iOS", $"Downloaded header: '{header}'");
                        if (!header.StartsWith("%PDF"))
                        {
                            throw new Exception($"Arquivo baixado não parece ser PDF (header='{header}')");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Propaga para o handler superior para reportar ao usuário
                        throw new Exception($"Falha no arquivo baixado: {ex.Message}");
                    }
                    localPath = newTemp;
                }
                else
                {
                    localPath = source!;
                }

                if (cts.IsCancellationRequested) { DeleteTemp(newTemp); return; }

                var engine = new CgPdfEngine(localPath);
                if (!engine.IsOpen) throw new Exception("Falha ao abrir PDF");
                _engine = engine;
                int count = engine.PageCount;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (cts.IsCancellationRequested) { DeleteTemp(newTemp); return; }
                    // Comita o novo temp e deleta o anterior de forma coerente.
                    if (newTemp is not null)
                    {
                        if (_tempPath is not null && _tempPath != newTemp) DeleteTemp(_tempPath);
                        _tempPath = newTemp;
                    }
                    pv.SetDocument(_engine!, _cacheRef, count,
                        (nfloat)vv.PageSpacing, vv.PageBackgroundColor.ToPlatform());
                    ApplyZoomLimits();
                    ApplyZoomEnabled();
                    vv.RaiseDocumentLoaded(count);
                    TrimAndPrefetch(0);
                });
            }
            catch (OperationCanceledException) { DeleteTemp(newTemp); }
            catch (Exception ex)
            {
                DeleteTemp(newTemp);
                PdfViewerLog.Write("Pdf/iOS", $"LoadDocument exception: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!cts.IsCancellationRequested) vv.RaiseDocumentLoadFailed(ex.Message);
                });
            }
        }, cts.Token);
    }

    // Download cancelável: NSUrlSessionDataTask + registro do token para Cancel().
    private static Task<NSData?> DownloadDataAsync(NSUrl url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<NSData?>();
        var task = _session.CreateDataTask(url, (data, _, error) =>
        {
            if (error is not null) tcs.TrySetException(new Exception(error.LocalizedDescription));
            else                   tcs.TrySetResult(data);
        });
        // Ao cancelar o token, cancela o data task → completa a Task cedo.
        var reg = ct.Register(() =>
        {
            try { task.Cancel(); } catch { }
            tcs.TrySetCanceled();
        });
        task.Resume();
        return tcs.Task.ContinueWith(t => { reg.Dispose(); return t.GetAwaiter().GetResult(); },
            TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void DeleteTemp(string? path)
    {
        if (path is null) return;
        try { System.IO.File.Delete(path); } catch { }
    }

    // ── Prefetch ──────────────────────────────────────────────────────────────

    private void TrimAndPrefetch(int centerPage)
    {
        PdfViewerLog.Write("Pdf/iOS", $"TrimAndPrefetch start center={centerPage} engineOpen={_engine?.IsOpen ?? false} cacheNull={_cache is null} virtualViewNull={VirtualView is null}");
        if (_engine is null || _cache is null || VirtualView is null) { PdfViewerLog.Write("Pdf/iOS", "TrimAndPrefetch aborted (nulls)"); return; }
        // Dedup: o scrollViewDidScroll dispara dezenas de vezes no mesmo centro; só refaz a
        // janela/prefetch quando o centro realmente muda. ReRenderAll/LoadDocument resetam p/ -1.
        if (centerPage == _lastPrefetchCenter) return;
        _lastPrefetchCenter = centerPage;

        int above = VirtualView.EnablePageCaching ? VirtualView.PrefetchAbove : 0;
        int below = VirtualView.EnablePageCaching ? VirtualView.PrefetchBelow : 0;
        int total = _engine.PageCount;
        int aS    = Math.Max(0, centerPage - above);
        int aE    = Math.Min(total - 1, centerPage + below);

        // ORDEM IMPORTA: remover as views primeiro desmarca as páginas como exibidas
        // (e solta a referência da UIImage), então TrimToWindow pode dispor com segurança
        // as imagens fora da janela. O inverso disporia imagem ainda em uso → crash.
        PlatformView?.RemovePageViewsOutside(aS, aE);
        _cache.TrimToWindow(aS, aE);

        _prefetchCts?.Cancel(); _prefetchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        var order = new List<int> { centerPage };
        for (int d = 1; d <= Math.Max(above, below); d++)
        {
            if (d <= below && centerPage + d <= aE) order.Add(centerPage + d);
            if (d <= above && centerPage - d >= aS) order.Add(centerPage - d);
        }

        double renderScale  = VirtualView?.RenderScale ?? 1.5;
        float  nativeScale  = (float)UIScreen.MainScreen.Scale;
        double scale        = renderScale * nativeScale;
        var    engine       = _engine;
        var    cache        = _cache;
        var    pv           = PlatformView;
        var    bgColor      = VirtualView?.PageBackgroundColor ?? Colors.White;
        // CRÍTICO: captura a largura AQUI (main thread). Acessar pv.ScrollView.Frame dentro do
        // Task.Run (thread de background) é acesso a UIKit fora da main thread → lança exceção,
        // que era engolida pelo catch e impedia QUALQUER página de renderizar (tela branca).
        nfloat viewW = pv is null ? 375 : (nfloat)pv.ScrollView.Frame.Width;
        if (viewW < 1) viewW = 375;

        PdfViewerLog.Write("Pdf/iOS", $"Prefetch order: [{string.Join(',', order)}] renderScale={VirtualView?.RenderScale} viewW={viewW}");
        _ = Task.Run(async () =>
        {
            foreach (int idx in order)
            {
                PdfViewerLog.Write("Pdf/iOS", $"Prefetch loop idx={idx}");
                if (cts.IsCancellationRequested || !engine.IsOpen) break;
                if (cache.Contains(idx)) { PdfViewerLog.Write("Pdf/iOS", $"Cache hit for {idx}"); MainThread.BeginInvokeOnMainThread(() => pv?.EnsurePageView(idx)); continue; }

                try
                {
                    var pageSize = engine.GetPageSize(idx);
                    double ratio = pageSize.Height / Math.Max(1, pageSize.Width);
                    int    w     = (int)(viewW * scale);
                    int    h     = (int)(w * ratio);

                    PdfViewerLog.Write("Pdf/iOS", $"Prefetch: rendering idx={idx} size={w}x{h}");
                    var img = await engine.RenderUIImageAsync(idx, w, h, bgColor, cts.Token);
                    if (img is null) continue;
                    // Se cancelou durante o render, a imagem recém-criada não vai ao cache
                    // nem a um UIImageView → dispõe aqui para não vazar o CGImage nativo.
                    if (cts.IsCancellationRequested) { try { img.Dispose(); } catch { } break; }

                    cache.Put(idx, img);
                    PdfViewerLog.Write("Pdf/iOS", $"Cached page {idx}");
                    int capturedIdx = idx;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!cts.IsCancellationRequested) pv?.EnsurePageView(capturedIdx);
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { PdfViewerLog.Write("Pdf/iOS", $"Prefetch render ERRO idx={idx}: {ex.Message}"); }
            }
        }, cts.Token);
    }

    private void ReRenderAll()
    {
        // Limpar as views primeiro desmarca todas as páginas como exibidas; só então
        // EvictAll pode dispor todas as UIImages com segurança (nenhuma em uso).
        PlatformView?.ClearPageViews();
        _cache?.EvictAll();
        _lastPrefetchCenter = -1;   // cache vazio → permite re-render do mesmo centro
        var lm = PlatformView?.ScrollView.ContentOffset;
        int page = 0;
        if (lm.HasValue && PlatformView is not null)
            page = PlatformView.PageAtOffset(lm.Value.Y);
        TrimAndPrefetch(page);
    }

    // ── Property sync ─────────────────────────────────────────────────────────

    private void SyncPage()
    {
        if (_syncingPage || PlatformView is null || VirtualView is null) return;
        _syncingPage = true;
        PlatformView.ScrollToPage(VirtualView.CurrentPage, animated: true);
        _syncingPage = false;
    }

    private void SyncZoom()
    {
        if (_syncingZoom || PlatformView is null || VirtualView is null) return;
        _syncingZoom = true;
        PlatformView.ScrollView.SetZoomScale((nfloat)VirtualView.ZoomFactor, animated: true);
        _syncingZoom = false;
    }

    private void ApplyZoomLimits()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.ScrollView.MinimumZoomScale = (nfloat)VirtualView.MinZoom;
        PlatformView.ScrollView.MaximumZoomScale = (nfloat)VirtualView.MaxZoom;
    }

    private void ApplyCache()
    {
        if (VirtualView is null) return;
        var old = _cacheRef.Cache;
        // Substitui apenas o conteúdo do holder compartilhado: container e handler
        // continuam apontando para o MESMO _cacheRef, então a troca de MaxCacheMB
        // nunca os desconecta do cache real (puts e gets na mesma instância).
        _cacheRef.Cache = new PdfImageLruCache((long)VirtualView.MaxCacheMB * 1024 * 1024);
        old?.EvictAll();
        PlatformView?.UpdateCache(_cacheRef);
    }

    private void ApplySpacing()
    {
        if (PlatformView is null || VirtualView is null) return;
        PlatformView.PageSpacing = (nfloat)VirtualView.PageSpacing;
    }

    private void ApplyZoomEnabled()
    {
        if (PlatformView is null || VirtualView is null) return;
        if (!VirtualView.IsPinchZoomEnabled)
        {
            var cur = PlatformView.ScrollView.ZoomScale;
            PlatformView.ScrollView.MinimumZoomScale = cur;
            PlatformView.ScrollView.MaximumZoomScale = cur;
        }
        else ApplyZoomLimits();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfScrollContainer
// UIView raiz com UIScrollView interno.
// Páginas são UIImageView adicionadas ao contentView dinamicamente.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PdfScrollContainer : UIView, IUIScrollViewDelegate
{
    public readonly UIScrollView ScrollView;

    private readonly UIView _contentView; // placeholder para zoom do UIScrollView

    private CgPdfEngine?      _engine;
    // Referência compartilhada com o handler: lê sempre _cacheRef.Cache (instância
    // viva), nunca uma cópia capturada → cache hits gravados e lidos na mesma instância.
    private PdfCacheRef?      _cacheRef;
    private PdfImageLruCache? _cache => _cacheRef?.Cache;
    private int                    _pageCount;
    private nfloat                 _spacing = 8;
    private UIColor                _bgColor = UIColor.White;
    private nfloat[]               _pageHeights  = Array.Empty<nfloat>();
    private nfloat[]               _pageOffsets  = Array.Empty<nfloat>();

    // UIImageViews ativas por pageIndex
    private readonly Dictionary<int, UIImageView> _pageViews = new();

    public Action<int>?    OnPageChanged { get; set; }
    public Action<double>? OnZoomChanged { get; set; }

    public nfloat PageSpacing
    {
        get => _spacing;
        set { _spacing = value; ComputeLayout(); ReattachAllPageViews(); }
    }

    public PdfScrollContainer()
    {
        BackgroundColor = UIColor.SystemBackground;
        ClipsToBounds   = true;

        _contentView = new UIView { BackgroundColor = UIColor.Clear };

        ScrollView = new UIScrollView
        {
            MinimumZoomScale    = 0.9f,
            MaximumZoomScale    = 8f,
            BouncesZoom         = true,
            ShowsVerticalScrollIndicator   = true,
            ShowsHorizontalScrollIndicator = false,
        };
        ScrollView.WeakDelegate = this;
        ScrollView.AddSubview(_contentView);
        AddSubview(ScrollView);

        var dtap = new UITapGestureRecognizer(() =>
        {
            nfloat newZ = ScrollView.ZoomScale > 1.05f
                ? ScrollView.MinimumZoomScale : (nfloat)2.5;
            ScrollView.SetZoomScale(newZ, animated: true);
        }) { NumberOfTapsRequired = 2 };
        _contentView.AddGestureRecognizer(dtap);
    }

    internal void SetDocument(CgPdfEngine engine, PdfCacheRef cacheRef,
        int pageCount, nfloat spacing, UIColor bgColor)
    {
        PdfViewerLog.Write("Pdf/iOS", $"SetDocument called pages={pageCount} spacing={spacing}");
        _engine    = engine;
        _cacheRef  = cacheRef;
        _pageCount = pageCount;
        _spacing   = spacing;
        _bgColor   = bgColor;
        ComputeLayout();
        ScrollView.SetContentOffset(CGPoint.Empty, animated: false);
        // Tenta garantir que a primeira página seja criada/visível imediatamente
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { EnsurePageView(0); PdfViewerLog.Write("Pdf/iOS", "EnsurePageView(0) invoked from SetDocument"); } catch (Exception ex) { PdfViewerLog.Write("Pdf/iOS", $"EnsurePageView(0) failed: {ex.Message}"); }
        });
    }

    internal void UpdateCache(PdfCacheRef cacheRef) => _cacheRef = cacheRef;

    public void ClearDocument()
    {
        _engine    = null;
        _pageCount = 0;
        _pageHeights = Array.Empty<nfloat>();
        _pageOffsets = Array.Empty<nfloat>();
        ClearPageViews();
        ScrollView.ContentSize = CGSize.Empty;
        _contentView.Frame     = CGRect.Empty;
    }

    public void ClearPageViews()
    {
        // Desmarca exibição ANTES de remover a view: assim a UIImage deixa de estar
        // "em uso" e o cache pode dispô-la com segurança em trims posteriores.
        foreach (var (idx, iv) in _pageViews)
        {
            _cache?.SetDisplayed(idx, false);
            iv.Image = null;
            iv.RemoveFromSuperview();
        }
        _pageViews.Clear();
    }

    public void RemovePageViewsOutside(int start, int end)
    {
        var toRemove = _pageViews.Keys.Where(k => k < start || k > end).ToList();
        foreach (var k in toRemove)
        {
            // Desmarca exibição e solta a referência da imagem antes de remover a view.
            _cache?.SetDisplayed(k, false);
            _pageViews[k].Image = null;
            _pageViews[k].RemoveFromSuperview();
            _pageViews.Remove(k);
        }
    }

    /// <summary>Garante que a página idx tem um UIImageView — cria se não existir, atualiza imagem se cache hit.</summary>
    public void EnsurePageView(int idx)
    {
        if (_engine is null || idx < 0 || idx >= _pageCount) return;

        if (!_pageViews.TryGetValue(idx, out var iv))
        {
            PdfViewerLog.Write("Pdf/iOS", $"EnsurePageView: creating view for page {idx}");
            nfloat width = _contentView.Frame.Width > 1 ? _contentView.Frame.Width : (ScrollView.Frame.Width > 1 ? ScrollView.Frame.Width : 375);
            iv = new UIImageView
            {
                ContentMode     = UIViewContentMode.ScaleAspectFit,
                BackgroundColor = _bgColor,
                Frame           = new CGRect(0, _pageOffsets[idx], width, _pageHeights[idx]),
            };
            _contentView.AddSubview(iv);
            _pageViews[idx] = iv;
        }

        if (_cache?.Get(idx) is UIImage img && iv.Image != img)
        {
            PdfViewerLog.Write("Pdf/iOS", $"EnsurePageView: setting image for page {idx}");
            iv.Image = img;
            // Página agora exibida: protege a UIImage de ser disposta pelo cache.
            _cache?.SetDisplayed(idx, true);
            try { iv.SetNeedsLayout(); iv.SetNeedsDisplay(); } catch { }
        }
    }

    public void ScrollToPage(int page, bool animated)
    {
        if (page < 0 || page >= _pageOffsets.Length) return;
        // ContentOffset está no espaço de conteúdo escalado pelo ZoomScale; _pageOffsets é base.
        nfloat zoom = ScrollView.ZoomScale > 0.0001f ? ScrollView.ZoomScale : 1f;
        ScrollView.SetContentOffset(new CGPoint(0, _pageOffsets[page] * zoom), animated);
    }

    public int PageAtOffset(nfloat offsetY)
    {
        // O ContentOffset do UIScrollView está no espaço de conteúdo JÁ escalado pelo ZoomScale
        // (ContentSize = base * ZoomScale), enquanto _pageOffsets está no espaço base. Sem
        // converter, com zoom != 1 a página calculada fica deslocada — a janela de prefetch erra
        // a região visível e as páginas que estão na tela ficam sem conteúdo. Converte para base.
        nfloat zoom  = ScrollView.ZoomScale > 0.0001f ? ScrollView.ZoomScale : 1f;
        nfloat baseY = offsetY / zoom;
        for (int i = _pageOffsets.Length - 1; i >= 0; i--)
            if (_pageOffsets[i] <= baseY + 1) return i;
        return 0;
    }

    private void ComputeLayout()
    {
        if (_engine is null || _pageCount == 0) return;

        nfloat viewW = Frame.Width > 1 ? Frame.Width : 375;
        _pageHeights = new nfloat[_pageCount];
        _pageOffsets = new nfloat[_pageCount];
        nfloat offset = 0;

        for (int i = 0; i < _pageCount; i++)
        {
            _pageOffsets[i] = offset;
            var sz = _engine.GetPageSize(i);
            nfloat ratio = (nfloat)(sz.Height / Math.Max(1, sz.Width));
            _pageHeights[i] = viewW * ratio;
            offset += _pageHeights[i] + _spacing;
        }

        nfloat totalH = (nfloat)Math.Max((double)(offset - _spacing), 0.0);
        _contentView.Frame = new CGRect(0, 0, viewW, totalH);
        ScrollView.ContentSize = new CGSize(viewW, totalH);
    }

    private void ReattachAllPageViews()
    {
        foreach (var (idx, iv) in _pageViews)
        {
            if (idx >= _pageOffsets.Length) continue;
            iv.Frame = new CGRect(0, _pageOffsets[idx], _contentView.Frame.Width, _pageHeights[idx]);
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        ScrollView.Frame = Bounds;
        ComputeLayout();
        ReattachAllPageViews();
    }

    // ── UIScrollViewDelegate ──────────────────────────────────────────────────

    [Export("viewForZoomingInScrollView:")]
    public UIView? ViewForZoomingInScrollView(UIScrollView sv) => _contentView;

    [Export("scrollViewDidZoom:")]
    public void DidZoom(UIScrollView sv)
    {
        nfloat ox = (nfloat)Math.Max((double)((sv.Bounds.Width  - _contentView.Frame.Width)  / 2), 0.0);
        nfloat oy = (nfloat)Math.Max((double)((sv.Bounds.Height - _contentView.Frame.Height) / 2), 0.0);
        _contentView.Center = new CGPoint(
            _contentView.Frame.Width  / 2 + ox,
            _contentView.Frame.Height / 2 + oy);
    }

    [Export("scrollViewDidEndZooming:withView:atScale:")]
    public void ZoomingEnded(UIScrollView sv, UIView? view, nfloat scale)
        => OnZoomChanged?.Invoke((double)scale);

    [Export("scrollViewDidScroll:")]
    public void Scrolled(UIScrollView sv)
        => OnPageChanged?.Invoke(PageAtOffset(sv.ContentOffset.Y));
}

// ─────────────────────────────────────────────────────────────────────────────
// CgPdfEngine — PDFKit + CoreGraphics (equivalente ao PDFium na Apple)
// PDFDocument gerencia o documento; PDFPage.Draw renderiza via CGContext.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class CgPdfEngine : IPdfEngine
{
    // CoreGraphics (CGPDFDocument), NÃO PdfKit: render via CGContext.DrawPDFPage num
    // CGBitmapContext é THREAD-SAFE em background. PdfKit (PdfPage.Draw/UIGraphicsImageRenderer)
    // é UIKit e só roda na main thread → causava "UIKit Consistency error" no prefetch (tela branca).
    private CGPDFDocument? _doc;
    private bool           _disposed;
    // Serializa acesso a _doc x Dispose. Dispose AGUARDA o render em andamento soltar o lock
    // antes de liberar _doc → elimina EXC_BAD_ACCESS em troca de Source/disconnect.
    private readonly object _docLock = new();

    public bool IsOpen    => _doc is not null && !_disposed;
    public int  PageCount => (int)(_doc?.Pages ?? 0);

    public CgPdfEngine(string path)
    {
        try { _doc = CGPDFDocument.FromFile(path); }
        catch { _doc = null; }
    }

    public Task<bool> OpenAsync(string path, string? pw = null, CancellationToken ct = default)
    {
        _doc?.Dispose();
        try { _doc = CGPDFDocument.FromFile(path); } catch { _doc = null; }
        return Task.FromResult(IsOpen);
    }

    public Task<bool> OpenAsync(Stream stream, string? pw = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public void Close() => Dispose();

    public SizeF GetPageSize(int idx)
    {
        // Mesmo lock do render: evita acesso a _doc disposto a partir do prefetch.
        lock (_docLock)
        {
            if (!IsOpen || idx < 0 || idx >= PageCount) return SizeF.Zero;
            var page = _doc!.GetPage(idx + 1);   // CGPDF: páginas são 1-based
            if (page is null) return SizeF.Zero;
            var box = page.GetBoxRect(CGPDFBox.Media);
            return new SizeF((float)box.Width, (float)box.Height);
        }
    }

    public Task<UIImage?> RenderUIImageAsync(
        int idx, int widthPx, int heightPx, Color bgColor, CancellationToken ct)
    {
        return Task.Run<UIImage?>(() =>
        {
            PdfViewerLog.Write("Pdf/iOS", $"RenderUIImageAsync start idx={idx} {widthPx}x{heightPx}");
            // Lock garante que _doc não será disposto enquanto DrawPDFPage executa.
            lock (_docLock)
            {
                try
                {
                    if (ct.IsCancellationRequested || !IsOpen) { PdfViewerLog.Write("Pdf/iOS", $"Render cancelled or engine closed idx={idx}"); return null; }
                    var page = _doc!.GetPage(idx + 1);   // 1-based
                    if (page is null) { PdfViewerLog.Write("Pdf/iOS", $"Render: page null idx={idx}"); return null; }

                    var box = page.GetBoxRect(CGPDFBox.Media);
                    nfloat scX = (nfloat)(widthPx  / Math.Max(1.0, box.Width));
                    nfloat scY = (nfloat)(heightPx / Math.Max(1.0, box.Height));

                    // CGBitmapContext + DrawPDFPage: render por CoreGraphics, thread-safe em background.
                    using var cs  = CGColorSpace.CreateDeviceRGB();
                    using var ctx = new CGBitmapContext(
                        IntPtr.Zero, widthPx, heightPx, 8, widthPx * 4, cs,
                        CGImageAlphaInfo.PremultipliedLast);

                    ctx.SetFillColor((nfloat)bgColor.Red, (nfloat)bgColor.Green,
                                     (nfloat)bgColor.Blue, (nfloat)bgColor.Alpha);
                    ctx.FillRect(new CGRect(0, 0, widthPx, heightPx));

                    // Página PDF tem origem inferior-esquerda; flip Y p/ a UIImage sair na orientação certa.
                    ctx.TranslateCTM(0, heightPx);
                    ctx.ScaleCTM(scX, -scY);
                    ctx.TranslateCTM(-(nfloat)box.X, -(nfloat)box.Y);
                    ctx.DrawPDFPage(page);

                    using var cgImage = ctx.ToImage();
                    var img = cgImage is null ? null : UIImage.FromImage(cgImage);
                    PdfViewerLog.Write("Pdf/iOS", img is null ? $"Render returned null idx={idx}" : $"Render succeeded idx={idx}");
                    return img;
                }
                catch (Exception ex)
                {
                    PdfViewerLog.Write("Pdf/iOS", $"Render exception idx={idx}: {ex.Message}");
                    return null;
                }
            }
        }, ct);
    }

    public async Task<byte[]?> RenderPageAsync(
        int idx, int widthPx, int heightPx, uint bg = 0xFFFFFFFF, CancellationToken ct = default)
    {
        Color c = Color.FromRgba(
            (int)((bg >> 16) & 0xFF), (int)((bg >> 8) & 0xFF),
            (int)(bg & 0xFF), (int)((bg >> 24) & 0xFF));
        var img = await RenderUIImageAsync(idx, widthPx, heightPx, c, ct);
        return img?.AsPNG()?.ToArray();
    }

    public Task<byte[]?> RenderThumbnailAsync(int idx, int tw, int th, CancellationToken ct = default)
        => RenderPageAsync(idx, tw, th, 0xFFFFFFFF, ct);

    public Task<string> ExtractTextAsync(int idx, CancellationToken ct = default)
        => Task.FromResult(string.Empty); // CGPDFDocument não expõe texto; reservado para o futuro.

    public void Dispose()
    {
        // Aguarda (lock) qualquer render/GetPage em andamento antes de liberar _doc.
        lock (_docLock)
        {
            if (_disposed) return;
            _disposed = true;
            _doc?.Dispose(); _doc = null;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfCacheRef — holder mutável compartilhado entre handler e container.
// Trocar MaxCacheMB substitui apenas .Cache; ambos continuam vendo a instância viva.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfCacheRef
{
    public PdfImageLruCache? Cache { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// PdfImageLruCache — LRU de UIImage com limite em bytes
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PdfImageLruCache
{
    private readonly long _maxBytes;
    private          long _usedBytes;
    private readonly Dictionary<int, (LinkedListNode<int> Node, UIImage Img, long Bytes)> _map = new();
    private readonly LinkedList<int> _order = new();
    // Páginas atualmente exibidas em um UIImageView: a UIImage destas NÃO pode ser
    // disposta (CGImage nativo em uso pelo UIKit → crash). Eviction sempre pula estas.
    private readonly HashSet<int> _displayed = new();

    public PdfImageLruCache(long maxBytes)
        => _maxBytes = Math.Max(10L * 1024 * 1024, maxBytes);

    public bool Contains(int idx) => _map.ContainsKey(idx);

    /// <summary>Marca/desmarca uma página como exibida. Enquanto exibida, sua UIImage nunca é disposta.</summary>
    public void SetDisplayed(int idx, bool displayed)
    {
        if (displayed) _displayed.Add(idx);
        else           _displayed.Remove(idx);
    }

    public UIImage? Get(int idx)
    {
        if (!_map.TryGetValue(idx, out var e)) return null;
        _order.Remove(e.Node); _order.AddFirst(e.Node);
        return e.Img;
    }

    public void Put(int idx, UIImage img)
    {
        long bytes = (long)(img.Size.Width * img.Size.Height * 4 *
                            img.CurrentScale * img.CurrentScale);
        if (_map.TryGetValue(idx, out var ex))
        {
            _order.Remove(ex.Node); _usedBytes -= ex.Bytes;
            // Substituindo imagem da mesma página: dispõe a antiga se não estiver exibida.
            if (!_displayed.Contains(idx) && ex.Img != img) TryDispose(ex.Img);
        }
        while (_usedBytes + bytes > _maxBytes && _order.Count > 0)
            if (!EvictLru()) break; // nada mais evictável (resto está exibido)
        var node = _order.AddFirst(idx);
        _map[idx] = (node, img, bytes);
        _usedBytes += bytes;
    }

    public void EvictAll()
    {
        // Dispõe somente UIImages que não estão exibidas; as exibidas continuam
        // vivas (pertencem ao UIImageView ativo) e serão liberadas ao remover a view.
        foreach (var kv in _map)
            if (!_displayed.Contains(kv.Key)) TryDispose(kv.Value.Img);
        _map.Clear(); _order.Clear(); _usedBytes = 0;
    }

    public void TrimToWindow(int start, int end)
    {
        foreach (var k in _map.Keys.Where(k => k < start || k > end).ToList())
        {
            if (!_map.TryGetValue(k, out var e)) continue;
            _order.Remove(e.Node); _usedBytes -= e.Bytes; _map.Remove(k);
            if (!_displayed.Contains(k)) TryDispose(e.Img);
        }
    }

    /// <summary>Evicta a entrada LRU não-exibida mais antiga. Retorna false se nada pôde ser evictado.</summary>
    private bool EvictLru()
    {
        // Procura a partir do fim (LRU) a primeira entrada que NÃO está exibida.
        var node = _order.Last;
        while (node is not null && _displayed.Contains(node.Value))
            node = node.Previous;
        if (node is null) return false; // tudo restante está exibido → não evicta

        int k = node.Value;
        _order.Remove(node);
        if (_map.TryGetValue(k, out var e)) { _usedBytes -= e.Bytes; _map.Remove(k); TryDispose(e.Img); }
        return true;
    }

    private static void TryDispose(UIImage img)
    {
        try { img.Dispose(); } catch { }
    }
}
