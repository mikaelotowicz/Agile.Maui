// Platforms/MacCatalyst/ImageViewHandler.cs
// MacCatalyst uses the same UIKit APIs as iOS — this file mirrors Platforms/iOS/ImageViewHandler.cs
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

public sealed class ImageViewHandler : ViewHandler<ImageView, UIImageView>
{
    public static readonly PropertyMapper<ImageView, ImageViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(ImageView.Source)]           = (h, _) => h.LoadImage(),
            ["IsUrl"]                            = (h, _) => h.LoadImage(),
            [nameof(ImageView.Placeholder)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.AspectMode)]       = (h, _) => h.ApplyContentMode(),
            [nameof(ImageView.DecodeMaxPx)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.MaxZoom)]          = (h, _) => { },
            [nameof(ImageView.EnableFullscreen)] = (h, _) => h.ApplyInteraction(),
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
            UserInteractionEnabled = false,
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
        ApplyInteraction();
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

        VirtualView?.SetIsLoading(false);
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

    private void ApplyInteraction()
    {
        if (PlatformView is null) return;
        PlatformView.UserInteractionEnabled = VirtualView.EnableFullscreen;
        if (_tapGesture is not null)
            _tapGesture.Enabled = VirtualView.EnableFullscreen;
    }

    private void LoadImage()
    {
        if (PlatformView is null) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        if (string.IsNullOrWhiteSpace(VirtualView.Source))
        {
            ApplyPlaceholder();
            VirtualView.SetIsLoading(false);
            return;
        }

        ApplyPlaceholder();
        VirtualView.SetIsLoading(true);

        if (ImageSourceResolver.IsRemote(VirtualView.Source, VirtualView.LegacyIsUrl))
        {
            _loadCts = new CancellationTokenSource();
            _ = LoadFromUrlAsync(VirtualView.Source, _loadCts.Token);
        }
        else
            LoadFromLocal(VirtualView.Source);
    }

    private void LoadFromLocal(string name)
    {
        var maxPixelSize = AppleImageCache.ResolveMaxPixelSize(
            PlatformView.Bounds.Width,
            PlatformView.Bounds.Height,
            UIScreen.MainScreen.Scale,
            VirtualView.DecodeMaxPx);
        var image = AppleImageCache.LoadLocal(name, maxPixelSize, UIScreen.MainScreen.Scale);
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
        var maxPixelSize = AppleImageCache.ResolveMaxPixelSize(
            targetW,
            targetH,
            screenScale,
            VirtualView.DecodeMaxPx);
        var cacheKey = AppleImageCache.Key(url, maxPixelSize);

        if (AppleImageCache.Get(cacheKey) is { } cached)
        {
            PlatformView.Image = cached;
            VirtualView?.RaiseImageLoaded();
            return;
        }

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

            if (response is not NSHttpUrlResponse http || http.StatusCode != 200 || data is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyPlaceholder();
                    VirtualView?.RaiseImageFailed();
                });
                return;
            }

            var image = AppleImageCache.Decode(data, maxPixelSize, screenScale);
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
                    AppleImageCache.Set(cacheKey, image);
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

    private void ApplyPlaceholder()
    {
        if (string.IsNullOrWhiteSpace(VirtualView.Placeholder)) return;
        var ph = AppleImageCache.LoadLocal(VirtualView.Placeholder, VirtualView.DecodeMaxPx, UIScreen.MainScreen.Scale);
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
            isUrl:        ImageSourceResolver.IsRemote(fsSource, VirtualView.LegacyIsUrl),
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
