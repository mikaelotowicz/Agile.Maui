// Platforms/iOS/GalleryView/FullscreenGalleryViewController.cs
using CoreGraphics;
using Foundation;
using UIKit;

namespace Controls.Platforms.iOS;

public sealed class FullscreenGalleryViewController : UIViewController
{
    private readonly string[]    _images;
    private readonly bool        _isUrl;
    private readonly string?     _placeholder;
    private readonly float       _maxZoom;
    private readonly int         _startIndex;
    private readonly Action<int>? _onIndexChanged;

    // Outer paging scroll view
    private UIScrollView? _pageScrollView;

    // Per-page structures
    private UIScrollView[]?             _zoomScrollViews;
    private UIImageView[]?              _imageViews;
    private UIActivityIndicatorView[]?  _spinners;
    private GalleryZoomScrollDelegate[]? _zoomDelegates;
    private CancellationTokenSource[]?  _pageCts;

    private UIButton? _closeButton;
    private UILabel?  _indicator;

    private int _currentPage;
    private int _pageCount;

    public FullscreenGalleryViewController(
        string[]     images,
        bool         isUrl,
        string?      placeholder,
        float        maxZoom,
        int          startIndex,
        Action<int>? onIndexChanged)
    {
        _images         = images;
        _isUrl          = isUrl;
        _placeholder    = placeholder;
        _maxZoom        = Math.Max(1f, maxZoom);
        _startIndex     = Math.Clamp(startIndex, 0, Math.Max(0, images.Length - 1));
        _onIndexChanged = onIndexChanged;
        _pageCount      = images.Length;
        _currentPage    = _startIndex;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;

        SetupPageScrollView();
        SetupPages();
        SetupCloseButton();
        SetupIndicator();

        // Set initial page offset without animation
        if (_startIndex > 0)
        {
            View.LayoutIfNeeded();
            _pageScrollView!.ContentOffset = new CGPoint(
                _startIndex * View.Bounds.Width, 0);
        }

        LoadVisiblePages(_startIndex);
        UpdateIndicator(_startIndex);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();

        var bounds = View!.Bounds;
        _pageScrollView!.Frame = bounds;
        _pageScrollView.ContentSize = new CGSize(bounds.Width * _pageCount, bounds.Height);

        for (int i = 0; i < _pageCount; i++)
        {
            _zoomScrollViews![i].Frame = new CGRect(i * bounds.Width, 0, bounds.Width, bounds.Height);
            _spinners![i].Center       = new CGPoint(bounds.Width / 2, bounds.Height / 2);
            UpdateZoomScaleForPage(i);
        }

        PositionOverlays();
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        CancelAllLoads();
    }

    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
        => UIInterfaceOrientationMask.All;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAllLoads();
            _closeButton?.Dispose();
            _indicator?.Dispose();
            if (_zoomScrollViews is not null)
                foreach (var sv in _zoomScrollViews) sv.Dispose();
            if (_imageViews is not null)
                foreach (var iv in _imageViews) iv.Dispose();
            if (_spinners is not null)
                foreach (var sp in _spinners) sp.Dispose();
            _pageScrollView?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Setup ─────────────────────────────────────────────────────────────

    private void SetupPageScrollView()
    {
        _pageScrollView = new UIScrollView
        {
            Frame                          = View!.Bounds,
            BackgroundColor                = UIColor.Black,
            PagingEnabled                  = true,
            ShowsHorizontalScrollIndicator = false,
            ShowsVerticalScrollIndicator   = false,
            ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never,
        };
        var pageDelegate = new GalleryPageScrollDelegate(this);
        _pageScrollView.WeakDelegate = pageDelegate;
        View.AddSubview(_pageScrollView);
    }

    private void SetupPages()
    {
        _zoomScrollViews = new UIScrollView[_pageCount];
        _imageViews      = new UIImageView[_pageCount];
        _spinners        = new UIActivityIndicatorView[_pageCount];
        _zoomDelegates   = new GalleryZoomScrollDelegate[_pageCount];
        _pageCts         = new CancellationTokenSource[_pageCount];

        for (int i = 0; i < _pageCount; i++)
        {
            var imageView = new UIImageView
            {
                ContentMode            = UIViewContentMode.ScaleAspectFit,
                BackgroundColor        = UIColor.Black,
                UserInteractionEnabled = true,
            };

            var zoomSv = new UIScrollView
            {
                BackgroundColor                = UIColor.Black,
                ShowsHorizontalScrollIndicator = false,
                ShowsVerticalScrollIndicator   = false,
                DecelerationRate               = UIScrollView.DecelerationRateFast,
                ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never,
                MinimumZoomScale               = (nfloat)1.0,
                MaximumZoomScale               = (nfloat)_maxZoom,
            };

            var zoomDelegate = new GalleryZoomScrollDelegate(imageView, _pageScrollView!);
            zoomSv.WeakDelegate = zoomDelegate;
            _zoomDelegates[i]   = zoomDelegate;

            zoomSv.AddSubview(imageView);

            var spinner = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Large)
            {
                Color            = UIColor.White,
                HidesWhenStopped = true,
            };

            // Wrap in a container view added to _pageScrollView
            var pageView = new UIView { BackgroundColor = UIColor.Black };
            pageView.AddSubview(zoomSv);
            pageView.AddSubview(spinner);

            // Add zoom scroll view directly into page scroll view at page position
            _pageScrollView!.AddSubview(zoomSv);
            _pageScrollView.AddSubview(spinner);

            _zoomScrollViews[i] = zoomSv;
            _imageViews[i]      = imageView;
            _spinners[i]        = spinner;

            // Double-tap gesture on zoom scroll view
            var doubleTap = new UITapGestureRecognizer(r => OnDoubleTap(r, i))
                { NumberOfTapsRequired = 2 };
            zoomSv.AddGestureRecognizer(doubleTap);
        }
    }

    private void SetupCloseButton()
    {
        _closeButton = new UIButton(UIButtonType.System);
        var xmarkImage = UIImage.GetSystemImage("xmark.circle.fill");
        if (xmarkImage is not null)
            _closeButton.SetImage(xmarkImage, UIControlState.Normal);
        else
        {
            _closeButton.SetTitle("✕", UIControlState.Normal);
            _closeButton.SetTitleColor(UIColor.White, UIControlState.Normal);
        }
        _closeButton.TintColor          = UIColor.White;
        _closeButton.BackgroundColor    = UIColor.FromWhiteAlpha(0f, 0.4f);
        _closeButton.Layer.CornerRadius = 18f;
        _closeButton.ClipsToBounds      = true;
        _closeButton.TouchUpInside     += (_, _) => DismissViewController(animated: true, completionHandler: null);
        View!.AddSubview(_closeButton);
    }

    private void SetupIndicator()
    {
        if (_pageCount <= 1) return;

        _indicator = new UILabel
        {
            TextColor       = UIColor.White,
            BackgroundColor = UIColor.FromWhiteAlpha(0f, 0.0f),
            Font            = UIFont.SystemFontOfSize(14f),
            TextAlignment   = UITextAlignment.Left,
        };
        View!.AddSubview(_indicator);
    }

    private void PositionOverlays()
    {
        var safeTop = View!.SafeAreaInsets.Top;
        _closeButton!.Frame = new CGRect(
            x:      View.Bounds.Width - 52,
            y:      safeTop + 8,
            width:  36,
            height: 36);

        if (_indicator is not null)
        {
            _indicator.Frame = new CGRect(
                x:      16,
                y:      safeTop + 8,
                width:  80,
                height: 36);
        }
    }

    // ── Image loading ─────────────────────────────────────────────────────

    internal void LoadVisiblePages(int page)
    {
        // Load current page and neighbors
        for (int i = Math.Max(0, page - 1); i <= Math.Min(_pageCount - 1, page + 1); i++)
            LoadPage(i);
    }

    private void LoadPage(int index)
    {
        if (_imageViews![index].Image is not null) return; // already loaded

        _pageCts![index]?.Cancel();
        _pageCts[index]?.Dispose();
        _pageCts[index] = new CancellationTokenSource();

        _spinners![index].StartAnimating();

        var source = _images[index];

        if (_isUrl)
            _ = LoadFromUrlAsync(index, source, _pageCts[index].Token);
        else
            LoadFromBundle(index, source);
    }

    private void LoadFromBundle(int index, string name)
    {
        var image = UIImage.FromBundle(name);
        if (image is not null)
            SetPageImage(index, image);
        else
            ApplyPagePlaceholder(index);
    }

    private async Task LoadFromUrlAsync(int index, string url, CancellationToken token)
    {
        try
        {
            var result = await NSUrlSession.SharedSession.CreateDataTaskAsync(new NSUrl(url));

            if (token.IsCancellationRequested) return;

            if (result.Data is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => ApplyPagePlaceholder(index));
                return;
            }

            var image = UIImage.LoadFromData(result.Data);
            if (image is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => ApplyPagePlaceholder(index));
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested) SetPageImage(index, image);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FullscreenGalleryViewController] Page {index} load error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() => ApplyPagePlaceholder(index));
        }
    }

    private void SetPageImage(int index, UIImage image)
    {
        _imageViews![index].Image = image;
        _spinners![index].StopAnimating();
        UpdateZoomScaleForPage(index);
        CenterImageForPage(index);
    }

    private void ApplyPagePlaceholder(int index)
    {
        _spinners![index].StopAnimating();
        if (!string.IsNullOrWhiteSpace(_placeholder))
        {
            var ph = UIImage.FromBundle(_placeholder);
            if (ph is not null)
            {
                _imageViews![index].Image = ph;
                UpdateZoomScaleForPage(index);
                CenterImageForPage(index);
            }
        }
    }

    // ── Zoom ──────────────────────────────────────────────────────────────

    private void UpdateZoomScaleForPage(int index)
    {
        var iv = _imageViews![index];
        var sv = _zoomScrollViews![index];

        if (iv.Image is null) return;
        if (View?.Bounds.IsEmpty ?? true) return;

        var imgSize = iv.Image.Size;
        if (imgSize.Width <= 0 || imgSize.Height <= 0) return;

        var bounds   = View!.Bounds;
        var scaleW   = bounds.Width  / imgSize.Width;
        var scaleH   = bounds.Height / imgSize.Height;
        var minScale = (nfloat)Math.Min((double)scaleW, (double)scaleH);

        sv.MinimumZoomScale = minScale;
        sv.MaximumZoomScale = (nfloat)_maxZoom;
        sv.ZoomScale        = minScale;

        iv.Frame            = new CGRect(CGPoint.Empty, imgSize);
        sv.ContentSize      = imgSize;
    }

    private void CenterImageForPage(int index)
    {
        var iv = _imageViews![index];
        var sv = _zoomScrollViews![index];

        var offsetX = (nfloat)Math.Max(
            (sv.Bounds.Width  - sv.ContentSize.Width)  / 2, 0);
        var offsetY = (nfloat)Math.Max(
            (sv.Bounds.Height - sv.ContentSize.Height) / 2, 0);

        iv.Center = new CGPoint(
            sv.ContentSize.Width  / 2 + offsetX,
            sv.ContentSize.Height / 2 + offsetY);
    }

    // ── Gestures ──────────────────────────────────────────────────────────

    private void OnDoubleTap(UITapGestureRecognizer gesture, int pageIndex)
    {
        var sv = _zoomScrollViews![pageIndex];
        var iv = _imageViews![pageIndex];

        if (sv.ZoomScale > sv.MinimumZoomScale)
        {
            sv.SetZoomScale(sv.MinimumZoomScale, animated: true);
        }
        else
        {
            var midZoom = (nfloat)Math.Min(
                (double)sv.MaximumZoomScale,
                (double)sv.MinimumZoomScale * 3);
            var point = gesture.LocationInView(iv);
            var size  = new CGSize(sv.Frame.Width / midZoom, sv.Frame.Height / midZoom);
            var rect  = new CGRect(
                point.X - size.Width  / 2,
                point.Y - size.Height / 2,
                size.Width,
                size.Height);
            sv.ZoomToRect(rect, animated: true);
        }
    }

    // ── Page change ───────────────────────────────────────────────────────

    internal void OnPageChanged(int page)
    {
        _currentPage = page;
        UpdateIndicator(page);
        _onIndexChanged?.Invoke(page);
        LoadVisiblePages(page);
    }

    private void UpdateIndicator(int page)
    {
        if (_indicator is null) return;
        _indicator.Text = $"{page + 1} / {_pageCount}";
    }

    private void CancelAllLoads()
    {
        if (_pageCts is null) return;
        foreach (var cts in _pageCts)
        {
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryZoomScrollDelegate — per-page zoom delegate
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class GalleryZoomScrollDelegate : NSObject, IUIScrollViewDelegate
{
    private readonly UIImageView  _imageView;
    private readonly UIScrollView _pageScrollView;

    public GalleryZoomScrollDelegate(UIImageView imageView, UIScrollView pageScrollView)
    {
        _imageView      = imageView;
        _pageScrollView = pageScrollView;
    }

    [Export("viewForZoomingInScrollView:")]
    public UIView ViewForZooming(UIScrollView scrollView) => _imageView;

    [Export("scrollViewDidZoom:")]
    public void DidZoom(UIScrollView scrollView)
    {
        // Disable paging scroll when zoomed in
        _pageScrollView.ScrollEnabled = scrollView.ZoomScale <= scrollView.MinimumZoomScale * 1.01f;

        // Center image
        var offsetX = (nfloat)Math.Max(
            (scrollView.Bounds.Width  - scrollView.ContentSize.Width)  / 2, 0);
        var offsetY = (nfloat)Math.Max(
            (scrollView.Bounds.Height - scrollView.ContentSize.Height) / 2, 0);

        _imageView.Center = new CGPoint(
            scrollView.ContentSize.Width  / 2 + offsetX,
            scrollView.ContentSize.Height / 2 + offsetY);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryPageScrollDelegate — detects page changes in the outer scroll view
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class GalleryPageScrollDelegate : NSObject, IUIScrollViewDelegate
{
    private readonly FullscreenGalleryViewController _vc;

    public GalleryPageScrollDelegate(FullscreenGalleryViewController vc) => _vc = vc;

    [Export("scrollViewDidEndDecelerating:")]
    public void DecelerationEnded(UIScrollView scrollView)
    {
        var page = (int)(scrollView.ContentOffset.X / scrollView.Bounds.Width + 0.5);
        _vc.OnPageChanged(page);
    }
}
