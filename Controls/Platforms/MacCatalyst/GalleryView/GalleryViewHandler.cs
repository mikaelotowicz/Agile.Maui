// Platforms/MacCatalyst/GalleryView/GalleryViewHandler.cs
// MacCatalyst uses the same UIKit APIs as iOS — mirrors Platforms/iOS/GalleryView/GalleryViewHandler.cs
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Controls.Platforms.iOS;

internal sealed class GalleryViewHandler : ViewHandler<GalleryView, ThumbGalleryView>
{
    public static readonly PropertyMapper<GalleryView, GalleryViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(GalleryView.Images)]        = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.SelectedIndex)] = (h, _) => h.SyncPage(),
            [nameof(GalleryView.IsUrl)]         = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.Placeholder)]   = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.AspectMode)]    = (h, _) => h.Reconfigure(),
            [nameof(GalleryView.MaxZoom)]       = (h, _) => { },
        };

    public GalleryViewHandler() : base(Mapper) { }

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
        platformView.OnPageChanged = null;
        platformView.OnPageTapped  = null;
        platformView.OnImageLoaded = null;
        platformView.OnImageFailed = null;
        base.DisconnectHandler(platformView);
    }

    private void Reconfigure()
    {
        if (PlatformView is null) return;
        var contentMode = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? UIViewContentMode.ScaleAspectFill
            : UIViewContentMode.ScaleAspectFit;
        PlatformView.Configure(
            images:      VirtualView.Images?.ToArray() ?? [],
            isUrl:       VirtualView.IsUrl,
            placeholder: VirtualView.Placeholder,
            contentMode: contentMode);
        SyncPage();
    }

    private void SyncPage()
    {
        if (PlatformView is null) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        PlatformView.SetPage(Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1), animated: false);
    }

    private void OnPageChanged(int index)
    {
        if (VirtualView is null) return;
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
            isUrl:          VirtualView.IsUrl,
            placeholder:    VirtualView.Placeholder,
            maxZoom:        VirtualView.MaxZoom,
            startIndex:     idx,
            onIndexChanged: index =>
            {
                if (VirtualView is null) return;
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
    private readonly UIScrollView    _scrollView;
    private string[]                 _images      = [];
    private bool                     _isUrl;
    private string?                  _placeholder;
    private UIViewContentMode        _contentMode = UIViewContentMode.ScaleAspectFill;
    private List<PageEntry>          _pages       = [];
    private bool                     _ignoreScroll;
    private int                      _pendingPage = -1;
    private int                      _currentPage;

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
    }

    public void Configure(string[] images, bool isUrl, string? placeholder, UIViewContentMode contentMode)
    {
        _images      = images;
        _isUrl       = isUrl;
        _placeholder = placeholder;
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

        foreach (var _ in images)
        {
            var iv = new UIImageView
            {
                ContentMode            = contentMode,
                ClipsToBounds          = true,
                UserInteractionEnabled = true,
            };
            _scrollView.AddSubview(iv);
            _pages.Add(new PageEntry(iv, null));
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

        if (_pendingPage >= 0)
        {
            _ignoreScroll = true;
            _scrollView.SetContentOffset(new CGPoint(_pendingPage * w, 0), false);
            _ignoreScroll = false;
            _pendingPage  = -1;
        }
    }

    public void SetPage(int index, bool animated)
    {
        if (index < 0 || index >= _pages.Count) return;
        _currentPage = index;
        var w = Bounds.Width;
        if (w <= 0) { _pendingPage = index; return; }
        _pendingPage  = -1;
        _ignoreScroll = true;
        _scrollView.SetContentOffset(new CGPoint(index * w, 0), animated);
        _ignoreScroll = false;
    }

    internal void NotifyScrollEnded()
    {
        if (_ignoreScroll) return;
        var w = _scrollView.Bounds.Width;
        if (w <= 0) return;
        var index = Math.Clamp((int)Math.Round(_scrollView.ContentOffset.X / w), 0, Math.Max(0, _pages.Count - 1));
        _currentPage = index;
        LoadWindow(index);
        OnPageChanged?.Invoke(index);
    }

    private void LoadWindow(int center)
    {
        var lo = Math.Max(0, center - 1);
        var hi = Math.Min(_pages.Count - 1, center + 1);
        for (int i = lo; i <= hi; i++)
        {
            if (_pages[i].ImageView.Image is null && _pages[i].Cts is null)
                LoadPage(i);
        }
    }

    private void LoadPage(int index)
    {
        if (index < 0 || index >= _images.Length) return;
        var entry  = _pages[index];
        var source = _images[index];

        if (_isUrl)
        {
            var cts = new CancellationTokenSource();
            _pages[index] = new PageEntry(entry.ImageView, cts);
            _ = LoadFromUrlAsync(entry.ImageView, source, cts.Token);
        }
        else
        {
            var image = UIImage.FromBundle(source);
            if (image is not null)
            {
                entry.ImageView.Image = image;
                OnImageLoaded?.Invoke();
            }
            else
            {
                ApplyPlaceholder(entry.ImageView);
                OnImageFailed?.Invoke();
            }
        }
    }

    private async Task LoadFromUrlAsync(UIImageView iv, string url, CancellationToken token)
    {
        try
        {
            ApplyPlaceholder(iv);
            var result = await NSUrlSession.SharedSession.CreateDataTaskAsync(new NSUrl(url));
            if (token.IsCancellationRequested) return;

            if (result.Data is null)
            {
                await FailAsync(iv, token);
                return;
            }

            var image = UIImage.LoadFromData(result.Data);
            if (image is null)
            {
                await FailAsync(iv, token);
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    iv.Image = image;
                    OnImageLoaded?.Invoke();
                }
            });
        }
        catch (OperationCanceledException) { }
        catch { await FailAsync(iv, token); }
    }

    private async Task FailAsync(UIImageView iv, CancellationToken token)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!token.IsCancellationRequested)
            {
                ApplyPlaceholder(iv);
                OnImageFailed?.Invoke();
            }
        });
    }

    private void ApplyPlaceholder(UIImageView iv)
    {
        if (string.IsNullOrWhiteSpace(_placeholder)) return;
        var ph = UIImage.FromBundle(_placeholder);
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

    private readonly record struct PageEntry(UIImageView ImageView, CancellationTokenSource? Cts);
}

internal sealed class ThumbScrollDelegate : UIScrollViewDelegate
{
    private readonly ThumbGalleryView _owner;
    public ThumbScrollDelegate(ThumbGalleryView owner) => _owner = owner;

    public override void DecelerationEnded(UIScrollView scrollView)
        => _owner.NotifyScrollEnded();
}
