// Platforms/MacCatalyst/FullscreenZoomViewController.cs
// MacCatalyst uses the same UIKit APIs as iOS — this file mirrors Platforms/iOS/FullscreenZoomViewController.cs
using CoreGraphics;
using Foundation;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

public sealed class FullscreenZoomViewController
    : UIViewController, IUIScrollViewDelegate
{
    private readonly string   _source;
    private readonly bool     _isUrl;
    private readonly string?  _placeholder;
    private readonly float    _maxZoom;
    private readonly UIImage? _cachedImage;

    private UIScrollView?              _scrollView;
    private UIImageView?               _imageView;
    private UIActivityIndicatorView?   _spinner;
    private UIButton?                  _closeButton;
    private UITapGestureRecognizer?    _singleTap;
    private UITapGestureRecognizer?    _doubleTap;
    private CancellationTokenSource?   _loadCts;

    private const float DismissScaleThreshold = 1.05f;

    public FullscreenZoomViewController(
        string   source,
        bool     isUrl,
        string?  placeholder,
        float    maxZoom,
        UIImage? currentImage = null)
    {
        _source      = source;
        _isUrl       = isUrl;
        _placeholder = placeholder;
        _maxZoom     = Math.Max(1f, maxZoom);
        _cachedImage = currentImage;
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;

        SetupScrollView();
        SetupImageView();
        SetupSpinner();
        SetupCloseButton();
        SetupGestures();

        if (_cachedImage is not null)
        {
            SetImage(_cachedImage);
            if (_isUrl) _ = LoadFromUrlAsync(_source);
        }
        else
        {
            if (_isUrl) _ = LoadFromUrlAsync(_source);
            else LoadFromLocal(_source);
        }
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        _scrollView!.Frame = View!.Bounds;
        _spinner!.Center   = View.Center;
        PositionCloseButton();
        if (UpdateZoomScale())
            _imageView!.Hidden = false;
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _loadCts?.Cancel();
    }

    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
        => UIInterfaceOrientationMask.All;

    public override bool PrefersStatusBarHidden() => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _singleTap?.Dispose();
            _doubleTap?.Dispose();
            _scrollView?.Dispose();
            _imageView?.Dispose();
            _spinner?.Dispose();
            _closeButton?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Setup ─────────────────────────────────────────────────────────────
    private void SetupScrollView()
    {
        _scrollView = new UIScrollView
        {
            Frame                          = View!.Bounds,
            BackgroundColor                = UIColor.Black,
            ShowsHorizontalScrollIndicator = false,
            ShowsVerticalScrollIndicator   = false,
            DecelerationRate               = UIScrollView.DecelerationRateFast,
            ContentInsetAdjustmentBehavior =
                UIScrollViewContentInsetAdjustmentBehavior.Never,
        };
        _scrollView.WeakDelegate = this;
        View.AddSubview(_scrollView);
    }

    private void SetupImageView()
    {
        _imageView = new UIImageView
        {
            ContentMode            = UIViewContentMode.ScaleAspectFit,
            BackgroundColor        = UIColor.Black,
            UserInteractionEnabled = true,
            Hidden                 = true,
        };
        _scrollView!.AddSubview(_imageView);
    }

    private void SetupSpinner()
    {
        _spinner = new UIActivityIndicatorView(UIActivityIndicatorViewStyle.Large)
        {
            Color            = UIColor.White,
            HidesWhenStopped = true,
        };
        _spinner.StartAnimating();
        View!.AddSubview(_spinner);
    }

    private void SetupCloseButton()
    {
        _closeButton = new UIButton(UIButtonType.System);

        // UIButtonType.System renderiza imagens como template usando TintColor automaticamente
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
        _closeButton.TouchUpInside     += (_, _) => Dismiss();
        View!.AddSubview(_closeButton);
    }

    private void PositionCloseButton()
    {
        var safeTop = View!.SafeAreaInsets.Top;
        _closeButton!.Frame = new CGRect(
            x:      View.Bounds.Width - 52,
            y:      safeTop + 8,
            width:  36,
            height: 36);
    }

    private void SetupGestures()
    {
        _doubleTap = new UITapGestureRecognizer(OnDoubleTap)
            { NumberOfTapsRequired = 2 };
        _scrollView!.AddGestureRecognizer(_doubleTap);

        _singleTap = new UITapGestureRecognizer(OnSingleTap)
            { NumberOfTapsRequired = 1 };
        _singleTap.RequireGestureRecognizerToFail(_doubleTap);
        _scrollView.AddGestureRecognizer(_singleTap);
    }

    // ── Carregamento ──────────────────────────────────────────────────────
    private void LoadFromLocal(string name)
    {
        var image = AppleImageCache.LoadLocal(name, GetFullscreenMaxPixelSize(), UIScreen.MainScreen.Scale);
        if (image is not null) SetImage(image);
        else ApplyPlaceholder();
    }

    private async Task LoadFromUrlAsync(string url)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var maxPixelSize = GetFullscreenMaxPixelSize();
        var cacheKey = AppleImageCache.Key(url, maxPixelSize);

        try
        {
            if (AppleImageCache.Get(cacheKey) is { } cached)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!token.IsCancellationRequested) SetImage(cached);
                });
                return;
            }

            var result = await NSUrlSession.SharedSession.CreateDataTaskAsync(new NSUrl(url));

            if (token.IsCancellationRequested) return;

            if (result.Data is null)
            {
                await MainThread.InvokeOnMainThreadAsync(ApplyPlaceholder);
                return;
            }

            var image = AppleImageCache.Decode(result.Data, maxPixelSize, UIScreen.MainScreen.Scale);
            if (image is null)
            {
                await MainThread.InvokeOnMainThreadAsync(ApplyPlaceholder);
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    AppleImageCache.Set(cacheKey, image);
                    SetImage(image);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[FullscreenZoomViewController] Load error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(ApplyPlaceholder);
        }
    }

    private int GetFullscreenMaxPixelSize()
    {
        var bounds = View?.Bounds ?? UIScreen.MainScreen.Bounds;
        var maxPoints = Math.Max(bounds.Width, bounds.Height);
        var scaled = (int)Math.Ceiling(maxPoints * UIScreen.MainScreen.Scale * Math.Max(1f, Math.Min(_maxZoom, 3f)));
        return Math.Clamp(scaled, 720, 4096);
    }

    private void SetImage(UIImage image)
    {
        _imageView!.Image = image;
        _spinner!.StopAnimating();
        _imageView.Hidden = !UpdateZoomScale();
    }

    private void ApplyPlaceholder()
    {
        _spinner!.StopAnimating();
        if (!string.IsNullOrWhiteSpace(_placeholder))
        {
            var ph = AppleImageCache.LoadLocal(_placeholder, GetFullscreenMaxPixelSize(), UIScreen.MainScreen.Scale);
            if (ph is not null) _imageView!.Image = ph;
        }
        if (_imageView?.Image is not null)
            _imageView.Hidden = !UpdateZoomScale();
    }

    // ── UIScrollViewDelegate ──────────────────────────────────────────────
    [Export("viewForZoomingInScrollView:")]
    public UIView ViewForZoomingInScrollView(UIScrollView scrollView)
        => _imageView!;

    [Export("scrollViewDidZoom:")]
    public void DidZoom(UIScrollView scrollView)
        => CenterImage();

    // ── Zoom e centralizacao ──────────────────────────────────────────────
    private bool UpdateZoomScale()
    {
        if (_imageView?.Image is null || _scrollView is null) return false;
        if (_scrollView.Bounds.IsEmpty) return false;

        var imgSize = _imageView.Image.Size;
        if (imgSize.Width <= 0 || imgSize.Height <= 0) return false;

        _scrollView.MinimumZoomScale = 1f;
        _scrollView.MaximumZoomScale = (nfloat)Math.Max(1f, _maxZoom);
        _scrollView.ZoomScale = 1f;
        _imageView.Frame        = new CGRect(CGPoint.Empty, imgSize);
        _scrollView.ContentSize = imgSize;

        var viewport = _scrollView.Bounds.Size;
        var scaleW   = viewport.Width / imgSize.Width;
        var scaleH   = viewport.Height / imgSize.Height;
        var minScale = (nfloat)Math.Min((double)scaleW, (double)scaleH);
        if (minScale <= 0) return false;

        _scrollView.MinimumZoomScale = minScale;
        _scrollView.MaximumZoomScale = (nfloat)Math.Max((double)_maxZoom, (double)minScale);
        _scrollView.SetZoomScale(minScale, false);
        CenterImage();
        return true;
    }

    private void CenterImage()
    {
        if (_imageView is null || _scrollView is null) return;

        var imageFrame = _imageView.Frame;
        var offsetX = (nfloat)Math.Max((_scrollView.Bounds.Width  - imageFrame.Width)  / 2, 0);
        var offsetY = (nfloat)Math.Max((_scrollView.Bounds.Height - imageFrame.Height) / 2, 0);

        _imageView.Center = new CGPoint(
            imageFrame.Width  / 2 + offsetX,
            imageFrame.Height / 2 + offsetY);
    }

    // ── Gestos ────────────────────────────────────────────────────────────
    private void OnDoubleTap(UITapGestureRecognizer gesture)
    {
        if (_scrollView!.ZoomScale > _scrollView.MinimumZoomScale)
        {
            _scrollView.SetZoomScale(_scrollView.MinimumZoomScale, animated: true);
        }
        else
        {
            var midZoom = (nfloat)Math.Min(
                (double)_scrollView.MaximumZoomScale,
                (double)_scrollView.MinimumZoomScale * 3);
            var point = gesture.LocationInView(_imageView);
            _scrollView.ZoomToRect(ZoomRectForScale(midZoom, point), animated: true);
        }
    }

    private void OnSingleTap(UITapGestureRecognizer gesture)
    {
        if (_scrollView!.ZoomScale <= _scrollView.MinimumZoomScale * DismissScaleThreshold)
            Dismiss();
    }

    private CGRect ZoomRectForScale(nfloat scale, CGPoint center)
    {
        var size = new CGSize(
            _scrollView!.Frame.Width  / scale,
            _scrollView.Frame.Height / scale);
        return new CGRect(
            x:      center.X - size.Width  / 2,
            y:      center.Y - size.Height / 2,
            width:  size.Width,
            height: size.Height);
    }

    private void Dismiss()
        => DismissViewController(animated: true, completionHandler: null);
}
