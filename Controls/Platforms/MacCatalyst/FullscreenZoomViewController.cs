// Platforms/MacCatalyst/FullscreenZoomViewController.cs
// MacCatalyst uses the same UIKit APIs as iOS — this file mirrors Platforms/iOS/FullscreenZoomViewController.cs
using CoreGraphics;
using Foundation;
using UIKit;

namespace Controls.Platforms.iOS;

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
            else LoadFromBundle(_source);
        }
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        _scrollView!.Frame = View!.Bounds;
        _imageView!.Frame  = View.Bounds;
        _spinner!.Center   = View.Center;
        PositionCloseButton();
        UpdateZoomScale();
        CenterImage();
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _loadCts?.Cancel();
    }

    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
        => UIInterfaceOrientationMask.All;

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
    private void LoadFromBundle(string name)
    {
        var image = UIImage.FromBundle(name);
        if (image is not null) SetImage(image);
        else ApplyPlaceholder();
    }

    private async Task LoadFromUrlAsync(string url)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var result = await NSUrlSession.SharedSession.CreateDataTaskAsync(new NSUrl(url));

            if (token.IsCancellationRequested) return;

            if (result.Data is null)
            {
                await MainThread.InvokeOnMainThreadAsync(ApplyPlaceholder);
                return;
            }

            var image = UIImage.LoadFromData(result.Data);
            if (image is null)
            {
                await MainThread.InvokeOnMainThreadAsync(ApplyPlaceholder);
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested) SetImage(image);
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

    private void SetImage(UIImage image)
    {
        _imageView!.Image = image;
        _spinner!.StopAnimating();
        UpdateZoomScale();
        CenterImage();
    }

    private void ApplyPlaceholder()
    {
        _spinner!.StopAnimating();
        if (!string.IsNullOrWhiteSpace(_placeholder))
        {
            var ph = UIImage.FromBundle(_placeholder);
            if (ph is not null) _imageView!.Image = ph;
        }
        UpdateZoomScale();
        CenterImage();
    }

    // ── UIScrollViewDelegate ──────────────────────────────────────────────
    [Export("viewForZoomingInScrollView:")]
    public UIView ViewForZoomingInScrollView(UIScrollView scrollView)
        => _imageView!;

    [Export("scrollViewDidZoom:")]
    public void DidZoom(UIScrollView scrollView)
        => CenterImage();

    // ── Zoom e centralizacao ──────────────────────────────────────────────
    private void UpdateZoomScale()
    {
        if (_imageView?.Image is null) return;
        if (View?.Bounds.IsEmpty ?? true) return;

        var imgSize = _imageView.Image.Size;
        if (imgSize.Width <= 0 || imgSize.Height <= 0) return;

        var scaleW    = View!.Bounds.Width  / imgSize.Width;
        var scaleH    = View.Bounds.Height / imgSize.Height;
        var minScale  = (nfloat)Math.Min((double)scaleW, (double)scaleH);

        _scrollView!.MinimumZoomScale = minScale;
        _scrollView.MaximumZoomScale  = (nfloat)_maxZoom;
        _scrollView.ZoomScale         = minScale;

        _imageView.Frame              = new CGRect(CGPoint.Empty, imgSize);
        _scrollView.ContentSize       = imgSize;
    }

    private void CenterImage()
    {
        var offsetX = (nfloat)Math.Max(
            (_scrollView!.Bounds.Width  - _scrollView.ContentSize.Width)  / 2, 0);
        var offsetY = (nfloat)Math.Max(
            (_scrollView.Bounds.Height - _scrollView.ContentSize.Height) / 2, 0);

        _imageView!.Center = new CGPoint(
            _scrollView.ContentSize.Width  / 2 + offsetX,
            _scrollView.ContentSize.Height / 2 + offsetY);
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
