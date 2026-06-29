// Platforms/iOS/GalleryView/GalleryViewHandler.cs
using System.Collections.Specialized;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

internal sealed class GalleryViewHandler : ViewHandler<GalleryView, ThumbGalleryView>
{
    public static readonly PropertyMapper<GalleryView, GalleryViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(GalleryView.Images)]        = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.SelectedIndex)] = (h, _) => h.SyncPage(),
            ["IsUrl"]                           = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.Placeholder)]   = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.AspectMode)]    = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.ThumbMaxPx)]    = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.MaxZoom)]                = (h, _) => { },
            [nameof(GalleryView.ShowIndicator)]          = (h, _) => h.UpdateIndicator(),
            [nameof(GalleryView.IndicatorColor)]         = (h, _) => h.UpdateIndicator(),
            [nameof(GalleryView.IndicatorInactiveColor)] = (h, _) => h.UpdateIndicator(),
        };

    public GalleryViewHandler() : base(Mapper) { }

    private bool                      _disposed;
    private INotifyCollectionChanged? _imagesChangedSource;

    protected override ThumbGalleryView CreatePlatformView() => new();

    protected override void ConnectHandler(ThumbGalleryView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.OnPageChanged = OnPageChanged;
        platformView.OnPageTapped  = OpenFullscreen;
        platformView.OnImageLoaded = () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded());
        platformView.OnImageFailed = () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed());
        Reconfigure();
    }

    protected override void DisconnectHandler(ThumbGalleryView platformView)
    {
        _disposed = true;
        UnsubscribeImages();
        platformView.OnPageChanged = null;
        platformView.OnPageTapped  = null;
        platformView.OnImageLoaded = null;
        platformView.OnImageFailed = null;
        base.DisconnectHandler(platformView);
    }

    private void Reconfigure()
    {
        if (_disposed || PlatformView is null) return;
        UnsubscribeImages();
        var images = VirtualView.Images;
        var contentMode = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? UIViewContentMode.ScaleAspectFill
            : UIViewContentMode.ScaleAspectFit;
        PlatformView.Configure(
            images:      images?.ToArray() ?? [],
            isUrl:       VirtualView.LegacyIsUrl,
            placeholder: VirtualView.Placeholder,
            thumbMaxPx:  VirtualView.ThumbMaxPx,
            contentMode: contentMode);
        SubscribeImages(images);
        SyncPage();
        UpdateIndicator();
    }

    private void SubscribeImages(IList<string>? images)
    {
        if (images is INotifyCollectionChanged ncc)
        {
            _imagesChangedSource = ncc;
            ncc.CollectionChanged += OnImagesCollectionChanged;
        }
    }

    private void UnsubscribeImages()
    {
        if (_imagesChangedSource is not null)
        {
            _imagesChangedSource.CollectionChanged -= OnImagesCollectionChanged;
            _imagesChangedSource = null;
        }
    }

    private void OnImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed || PlatformView is null) return;
        MainThread.BeginInvokeOnMainThread(Reconfigure);
    }

    private void UpdateIndicator()
    {
        if (PlatformView is null) return;
        PlatformView.IndicatorVisible       = VirtualView.ShowIndicator;
        PlatformView.IndicatorActiveColor   = ToUIColor(VirtualView.IndicatorColor);
        PlatformView.IndicatorInactiveColor = ToUIColor(VirtualView.IndicatorInactiveColor);
    }

    private static UIColor ToUIColor(Microsoft.Maui.Graphics.Color c) =>
        UIColor.FromRGBA((nfloat)c.Red, (nfloat)c.Green, (nfloat)c.Blue, (nfloat)c.Alpha);

    private void SyncPage()
    {
        if (PlatformView is null) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        PlatformView.SetPage(Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1), animated: false);
    }

    private void OnPageChanged(int index)
    {
        if (_disposed || VirtualView is null) return;
        VirtualView.SelectedIndex = index;
        VirtualView.RaiseSelectionChanged(index);
    }

    private void OpenFullscreen(int startIndex)
    {
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        var vc = GetViewController();
        if (vc is null) return;

        var arr = images.ToArray();
        var idx = Math.Clamp(startIndex, 0, arr.Length - 1);

        var gallery = new FullscreenGalleryViewController(
            images:         arr,
            isUrl:          VirtualView.LegacyIsUrl,
            placeholder:    VirtualView.Placeholder,
            maxZoom:        VirtualView.MaxZoom,
            startIndex:     idx,
            onIndexChanged: index =>
            {
                if (_disposed || VirtualView is null) return;
                VirtualView.SelectedIndex = index;
                VirtualView.RaiseSelectionChanged(index);
                PlatformView?.SetPage(index, animated: false);
            });

        gallery.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        gallery.ModalTransitionStyle   = UIModalTransitionStyle.CrossDissolve;
        vc.PresentViewController(gallery, animated: true, completionHandler: null);
    }

    private UIViewController? GetViewController()
    {
        var responder = PlatformView?.NextResponder;
        while (responder is not null)
        {
            if (responder is UIViewController vc) return vc;
            responder = responder.NextResponder;
        }
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ThumbGalleryView — UIView com UIScrollView paging horizontal
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ThumbGalleryView : UIView
{
    // Session própria com queue de background — decode fora da main thread.
    // NSUrlSession.SharedSession usa a main queue e bloquearia a UI em imagens grandes.
    private static readonly NSUrlSession _urlSession = NSUrlSession.FromConfiguration(
        NSUrlSessionConfiguration.DefaultSessionConfiguration,
        null!,
        new NSOperationQueue());

    private readonly UIScrollView    _scrollView;
    private string[]                 _images      = [];
    private bool                     _isUrl;
    private string?                  _placeholder;
    private int                      _thumbMaxPx = 720;
    private UIViewContentMode        _contentMode = UIViewContentMode.ScaleAspectFill;
    private List<PageEntry>          _pages       = [];
    private bool                     _ignoreScroll;
    private int                      _pendingPage = -1;
    private int                      _currentPage;
    private readonly UIPageControl   _pageControl;

    public Action<int>? OnPageChanged { get; set; }
    public Action<int>? OnPageTapped  { get; set; }
    public Action?      OnImageLoaded { get; set; }
    public Action?      OnImageFailed { get; set; }

    public ThumbGalleryView()
    {
        ClipsToBounds = true;
        _scrollView = new UIScrollView
        {
            PagingEnabled                  = true,
            ShowsHorizontalScrollIndicator = false,
            ShowsVerticalScrollIndicator   = false,
            Bounces                        = true,
        };
        _scrollView.Delegate = new ThumbScrollDelegate(this);
        AddSubview(_scrollView);

        _pageControl = new UIPageControl
        {
            HidesForSinglePage            = true,
            Hidden                        = true,
            CurrentPageIndicatorTintColor = UIColor.White,
            PageIndicatorTintColor        = UIColor.FromWhiteAlpha(1f, 0.4f),
            UserInteractionEnabled        = false,
        };
        AddSubview(_pageControl);
    }

    public void Configure(string[] images, bool isUrl, string? placeholder, int thumbMaxPx, UIViewContentMode contentMode)
    {
        _images      = images;
        _isUrl       = isUrl;
        _placeholder = placeholder;
        _thumbMaxPx  = Math.Max(64, thumbMaxPx);
        _contentMode = contentMode;

        foreach (var p in _pages)
        {
            p.Cts?.Cancel();
            p.Cts?.Dispose();
            p.ImageView.RemoveFromSuperview();
            p.ImageView.Dispose();
        }
        _pages.Clear();
        _pendingPage = -1;
        _currentPage = 0;
        _pageControl.Pages       = images.Length;
        _pageControl.CurrentPage = 0;

        foreach (var _ in images)
        {
            var iv = new UIImageView
            {
                ContentMode            = contentMode,
                ClipsToBounds          = true,
                UserInteractionEnabled = true,
            };
            _scrollView.AddSubview(iv);
            _pages.Add(new PageEntry(iv, null, PageLoadState.Empty, null));
        }

        for (int i = 0; i < _pages.Count; i++)
        {
            var idx = i;
            _pages[i].ImageView.AddGestureRecognizer(
                new UITapGestureRecognizer(() => OnPageTapped?.Invoke(idx)));
        }

        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _scrollView.Frame       = Bounds;
        _scrollView.ContentSize = new CGSize(w * _pages.Count, h);

        for (int i = 0; i < _pages.Count; i++)
            _pages[i].ImageView.Frame = new CGRect(i * w, 0, w, h);

        LoadWindow(_currentPage);

        _pageControl.SizeToFit();
        _pageControl.Frame = new CGRect(
            (w - _pageControl.Frame.Width) / 2,
            h - _pageControl.Frame.Height - 8,
            _pageControl.Frame.Width,
            _pageControl.Frame.Height);

        if (_pendingPage >= 0)
        {
            _ignoreScroll = true;
            _scrollView.SetContentOffset(new CGPoint(_pendingPage * w, 0), false);
            _ignoreScroll = false;
            _pendingPage  = -1;
        }
    }

    public bool IndicatorVisible
    {
        set => _pageControl.Hidden = !value;
    }

    public UIColor IndicatorActiveColor
    {
        set => _pageControl.CurrentPageIndicatorTintColor = value;
    }

    public UIColor IndicatorInactiveColor
    {
        set => _pageControl.PageIndicatorTintColor = value;
    }

    public void SetPage(int index, bool animated)
    {
        if (index < 0 || index >= _pages.Count) return;
        _currentPage             = index;
        _pageControl.CurrentPage = index;
        var w = Bounds.Width;
        if (w <= 0) { _pendingPage = index; return; }
        _pendingPage  = -1;
        _ignoreScroll = true;
        _scrollView.SetContentOffset(new CGPoint(index * w, 0), animated);
        _ignoreScroll = false;
        LoadWindow(index);
    }

    internal void NotifyScrollEnded()
    {
        if (_ignoreScroll) return;
        var w = _scrollView.Bounds.Width;
        if (w <= 0) return;
        var index = Math.Clamp((int)Math.Round(_scrollView.ContentOffset.X / w), 0, Math.Max(0, _pages.Count - 1));
        _currentPage             = index;
        _pageControl.CurrentPage = index;
        LoadWindow(index);
        OnPageChanged?.Invoke(index);
    }

    private void LoadWindow(int center)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            if (i < center - 1 || i > center + 1)
            {
                var iv = _pages[i].ImageView;
                if (iv.Image is not null || _pages[i].State != PageLoadState.Empty)
                {
                    _pages[i].Cts?.Cancel();
                    _pages[i].Cts?.Dispose();
                    _pages[i] = new PageEntry(iv, null, PageLoadState.Empty, null);
                    iv.Image   = null;
                }
                continue;
            }
            if (_pages[i].State == PageLoadState.Empty)
                LoadPage(i);
        }
    }

    private void LoadPage(int index)
    {
        if (index < 0 || index >= _images.Length) return;
        var entry  = _pages[index];
        var source = _images[index];

        if (ImageSourceResolver.IsRemote(source, _isUrl))
        {
            var maxPixelSize = AppleImageCache.ResolveMaxPixelSize(
                entry.ImageView.Bounds.Width,
                entry.ImageView.Bounds.Height,
                UIScreen.MainScreen.Scale,
                _thumbMaxPx);
            var cacheKey = AppleImageCache.Key(source, maxPixelSize);
            if (AppleImageCache.Get(cacheKey) is { } cached)
            {
                entry.ImageView.Image = cached;
                _pages[index] = new PageEntry(entry.ImageView, null, PageLoadState.Loaded, source);
                OnImageLoaded?.Invoke();
                return;
            }

            ApplyPlaceholder(entry.ImageView);
            var cts = new CancellationTokenSource();
            _pages[index] = new PageEntry(entry.ImageView, cts, PageLoadState.Loading, source);
            _ = LoadFromUrlAsync(index, source, maxPixelSize, UIScreen.MainScreen.Scale, cts);
        }
        else
        {
            var maxPixelSize = AppleImageCache.ResolveMaxPixelSize(
                entry.ImageView.Bounds.Width,
                entry.ImageView.Bounds.Height,
                UIScreen.MainScreen.Scale,
                _thumbMaxPx);
            var image = AppleImageCache.LoadLocal(source, maxPixelSize, UIScreen.MainScreen.Scale);
            if (image is not null)
            {
                entry.ImageView.Image = image;
                _pages[index] = new PageEntry(entry.ImageView, null, PageLoadState.Loaded, source);
                OnImageLoaded?.Invoke();
            }
            else
            {
                ApplyPlaceholder(entry.ImageView);
                _pages[index] = new PageEntry(entry.ImageView, null, PageLoadState.Failed, source);
                OnImageFailed?.Invoke();
            }
        }
    }

    private async Task LoadFromUrlAsync(
        int index,
        string url,
        int maxPixelSize,
        nfloat screenScale,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        NSUrlSessionDataTask? dataTask = null;
        var tcs = new TaskCompletionSource<(NSData? Data, NSUrlResponse? Response)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var request = new NSUrlRequest(new NSUrl(url));
            dataTask = _urlSession.CreateDataTask(request, (data, response, error) =>
            {
                if (error is not null) tcs.TrySetException(new NSErrorException(error));
                else                   tcs.TrySetResult((data, response));
            });

            using var reg = token.Register(() =>
            {
                dataTask?.Cancel();
                tcs.TrySetCanceled(token);
            });

            dataTask.Resume();

            var (data, response) = await tcs.Task.ConfigureAwait(false);
            if (token.IsCancellationRequested) return;

            if (data is null ||
                response is not NSHttpUrlResponse http ||
                http.StatusCode < 200 ||
                http.StatusCode >= 300)
            {
                await FailAsync(index, url, cts);
                return;
            }

            var image = AppleImageCache.Decode(data, maxPixelSize, screenScale);
            if (image is null)
            {
                await FailAsync(index, url, cts);
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (IsCurrentLoad(index, url, cts))
                {
                    AppleImageCache.Set(AppleImageCache.Key(url, maxPixelSize), image);
                    var iv = _pages[index].ImageView;
                    iv.Image = image;
                    _pages[index] = new PageEntry(iv, null, PageLoadState.Loaded, url);
                    cts.Dispose();
                    OnImageLoaded?.Invoke();
                }
                else
                    cts.Dispose();
            });
        }
        catch (OperationCanceledException) { }
        catch (NSErrorException ex) when (ex.Error.Code == -999) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GalleryView iOS] Thumb load error: {ex.Message}");
            await FailAsync(index, url, cts);
        }
        finally
        {
            dataTask?.Dispose();
        }
    }

    private async Task FailAsync(int index, string source, CancellationTokenSource cts)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (IsCurrentLoad(index, source, cts))
            {
                var iv = _pages[index].ImageView;
                ApplyPlaceholder(iv);
                _pages[index] = new PageEntry(iv, null, PageLoadState.Failed, source);
                cts.Dispose();
                OnImageFailed?.Invoke();
            }
            else
                cts.Dispose();
        });
    }

    private bool IsCurrentLoad(int index, string source, CancellationTokenSource cts)
    {
        return index >= 0 &&
            index < _pages.Count &&
            ReferenceEquals(_pages[index].Cts, cts) &&
            string.Equals(_pages[index].Source, source, StringComparison.Ordinal);
    }

    private void ApplyPlaceholder(UIImageView iv)
    {
        if (string.IsNullOrWhiteSpace(_placeholder)) return;
        var ph = AppleImageCache.LoadLocal(_placeholder, _thumbMaxPx, UIScreen.MainScreen.Scale);
        if (ph is not null) iv.Image = ph;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var p in _pages)
            {
                p.Cts?.Cancel();
                p.Cts?.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private readonly record struct PageEntry(
        UIImageView ImageView,
        CancellationTokenSource? Cts,
        PageLoadState State,
        string? Source);

    private enum PageLoadState
    {
        Empty,
        Loading,
        Loaded,
        Failed
    }
}

internal sealed class ThumbScrollDelegate : UIScrollViewDelegate
{
    private readonly ThumbGalleryView _owner;
    public ThumbScrollDelegate(ThumbGalleryView owner) => _owner = owner;

    public override void DecelerationEnded(UIScrollView scrollView)
        => _owner.NotifyScrollEnded();

    // Cobre o caso em que o usuário arrasta devagar e solta sem gerar fase de deceleração.
    public override void DraggingEnded(UIScrollView scrollView, bool willDecelerate)
    {
        if (!willDecelerate) _owner.NotifyScrollEnded();
    }
}
