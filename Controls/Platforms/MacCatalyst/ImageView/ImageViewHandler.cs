// Platforms/MacCatalyst/ImageViewHandler.cs
// MacCatalyst uses the same UIKit APIs as iOS — this file mirrors Platforms/iOS/ImageViewHandler.cs
using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

public sealed class ImageViewHandler : ViewHandler<ImageView, UIImageView>
{
    public static readonly PropertyMapper<ImageView, ImageViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(ImageView.Source)]           = (h, _) => h.LoadImage(),
            [nameof(ImageView.IsUrl)]            = (h, _) => h.LoadImage(),
            [nameof(ImageView.Placeholder)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.AspectMode)]       = (h, _) => h.ApplyContentMode(),
            [nameof(ImageView.MaxZoom)]          = (h, _) => { },
            [nameof(ImageView.EnableFullscreen)] = (h, _) => { },
        };

    public ImageViewHandler() : base(Mapper) { }

    // NSUrlSession.SharedSession usa a main queue como delegateQueue — callbacks na main thread.
    // Session própria com NSOperationQueue background garante callbacks fora da main thread.
    private static readonly NSUrlSession _session = NSUrlSession.FromConfiguration(
        NSUrlSessionConfiguration.DefaultSessionConfiguration,
        null!,
        new NSOperationQueue());

    private UITapGestureRecognizer? _tapGesture;
    private CancellationTokenSource? _loadCts;

    protected override UIImageView CreatePlatformView()
    {
        return new UIImageView
        {
            UserInteractionEnabled = true,
            ClipsToBounds          = true,
            ContentMode            = UIViewContentMode.ScaleAspectFill,
        };
    }

    protected override void ConnectHandler(UIImageView platformView)
    {
        base.ConnectHandler(platformView);
        _tapGesture = new UITapGestureRecognizer(OnImageTapped);
        platformView.AddGestureRecognizer(_tapGesture);
        ApplyContentMode();
        LoadImage();
    }

    protected override void DisconnectHandler(UIImageView platformView)
    {
        // Gesture removido ANTES de cancelar o CTS para evitar tap durante teardown
        if (_tapGesture is not null)
        {
            platformView.RemoveGestureRecognizer(_tapGesture);
            _tapGesture.Dispose();
            _tapGesture = null;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        platformView.Image = null;
        base.DisconnectHandler(platformView);
    }

    private void ApplyContentMode()
    {
        if (PlatformView is null) return;
        PlatformView.ContentMode = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? UIViewContentMode.ScaleAspectFill
            : UIViewContentMode.ScaleAspectFit;
    }

    private void LoadImage()
    {
        if (PlatformView is null) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(VirtualView.Source))
        {
            PlatformView.Image = null;
            return;
        }

        ApplyPlaceholder();

        if (VirtualView.IsUrl)
            _ = LoadFromUrlAsync(VirtualView.Source, _loadCts.Token);
        else
            LoadFromBundle(VirtualView.Source);
    }

    private void LoadFromBundle(string name)
    {
        var image = UIImage.FromBundle(name);
        if (image is not null)
        {
            PlatformView.Image = image;
            VirtualView?.RaiseImageLoaded();
        }
        else
        {
            ApplyPlaceholder();
            VirtualView?.RaiseImageFailed();
        }
    }

    private async Task LoadFromUrlAsync(string url, CancellationToken token)
    {
        // Lidos na main thread antes do ConfigureAwait — BindableObject e UIScreen não são thread-safe
        var targetW     = VirtualView.WidthRequest;
        var targetH     = VirtualView.HeightRequest;
        var screenScale = UIScreen.MainScreen.Scale;

        NSUrlSessionDataTask? dataTask = null;
        var tcs = new TaskCompletionSource<(NSData?, NSUrlResponse?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var request = new NSUrlRequest(new NSUrl(url));

            dataTask = _session.CreateDataTask(request, (data, response, error) =>
            {
                if (error is not null) tcs.TrySetException(new NSErrorException(error));
                else                   tcs.TrySetResult((data, response));
            });

            // Cancela a task nativa quando o token disparar, evitando esperar o timeout do sistema
            using var reg = token.Register(() =>
            {
                dataTask?.Cancel();
                tcs.TrySetCanceled(token);
            });

            dataTask.Resume();

            // ConfigureAwait(false): decode não roda na main thread
            var (data, response) = await tcs.Task.ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            if (response is null || ((NSHttpUrlResponse)response).StatusCode != 200 || data is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyPlaceholder();
                    VirtualView?.RaiseImageFailed();
                });
                return;
            }

            var image = DecodeDownsampled(data, targetW, targetH, screenScale);
            if (image is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyPlaceholder();
                    VirtualView?.RaiseImageFailed();
                });
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    PlatformView.Image = image;
                    VirtualView?.RaiseImageLoaded();
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (NSErrorException ex) when (ex.Error.Code == -999) { } // NSURLErrorCancelled — cancelado pelo token
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ZoomImageViewHandler MacCatalyst] Load error: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyPlaceholder();
                VirtualView?.RaiseImageFailed();
            });
        }
    }

    private static UIImage? DecodeDownsampled(NSData data, double w, double h, nfloat scale)
    {
        if (w > 0 && h > 0 && scale > 0)
        {
            try
            {
                var maxPx = (int)(Math.Max(w, h) * (double)scale);
                using var src = CGImageSource.FromData(data);
                if (src is not null)
                {
                    using var cg = src.CreateThumbnail(0, new CGImageThumbnailOptions
                    {
                        CreateThumbnailFromImageAlways = true,
                        CreateThumbnailWithTransform   = true,
                        MaxPixelSize                   = maxPx,
                    });
                    if (cg is not null)
                        return UIImage.FromImage(cg, scale, UIImageOrientation.Up);
                }
            }
            catch { /* fallback abaixo */ }
        }

        return UIImage.LoadFromData(data);
    }

    private void ApplyPlaceholder()
    {
        if (string.IsNullOrWhiteSpace(VirtualView.Placeholder)) return;
        var ph = UIImage.FromBundle(VirtualView.Placeholder);
        if (ph is not null) PlatformView.Image = ph;
    }

    private void OnImageTapped()
    {
        if (!VirtualView.EnableFullscreen) return;
        if (string.IsNullOrWhiteSpace(VirtualView.Source)) return;

        var vc = GetViewController();
        if (vc is null) return;

        var fsSource = VirtualView.FullscreenSource ?? VirtualView.Source;
        var fullscreen = new FullscreenZoomViewController(
            source:       fsSource,
            isUrl:        VirtualView.IsUrl,
            placeholder:  VirtualView.Placeholder,
            maxZoom:      VirtualView.MaxZoom,
            currentImage: PlatformView.Image);

        fullscreen.ModalPresentationStyle = UIModalPresentationStyle.FullScreen;
        fullscreen.ModalTransitionStyle   = UIModalTransitionStyle.CrossDissolve;

        vc.PresentViewController(fullscreen, animated: true, completionHandler: null);
    }

    private UIViewController? GetViewController()
    {
        var responder = PlatformView.NextResponder;
        while (responder is not null)
        {
            if (responder is UIViewController vc) return vc;
            responder = responder.NextResponder;
        }
        return null;
    }
}
