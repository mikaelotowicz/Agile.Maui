// Platforms/Android/ZoomImageViewHandler.cs
using Android.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Load.Engine;
using Bumptech.Glide.Request;
using Microsoft.Maui.Handlers;
using Agile.Maui;

namespace Agile.Maui.Platforms.Android;

public sealed class ImageViewHandler : ViewHandler<ImageView, global::Android.Widget.ImageView>
{
    public static readonly PropertyMapper<ImageView, ImageViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(ImageView.Source)]           = (h, _) => h.LoadImage(),
            ["IsUrl"]                            = (h, _) => h.LoadImage(),
            [nameof(ImageView.Placeholder)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.AspectMode)]       = (h, _) => h.ApplyScaleType(),
            [nameof(ImageView.DecodeMaxPx)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.MaxZoom)]          = (h, _) => { },
            [nameof(ImageView.EnableFullscreen)] = (h, _) => h.ApplyInteraction(),
        };

    public ImageViewHandler() : base(Mapper) { }

    private bool _disposed;
    private ImgGlideRequestListener? _glideListener;

    protected override global::Android.Widget.ImageView CreatePlatformView()
    {
        return new global::Android.Widget.ImageView(Context)
        {
            Clickable = false,
            Focusable = false,
        };
    }

    protected override void ConnectHandler(global::Android.Widget.ImageView platformView)
    {
        base.ConnectHandler(platformView);
        _disposed = false;
        _glideListener = new ImgGlideRequestListener(
            onReady: () => { if (!_disposed) MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded()); },
            onFail:  () => { if (!_disposed) MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed()); });
        platformView.Click += OnImageClick;
        ApplyScaleType();
        ApplyInteraction();
        LoadImage();
    }

    protected override void DisconnectHandler(global::Android.Widget.ImageView platformView)
    {
        _disposed = true;
        VirtualView?.SetIsLoading(false);
        platformView.Click -= OnImageClick;

        try
        {
            Glide.With(platformView).Clear(platformView);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ZoomImageViewHandler] Glide.Clear error: {ex.Message}");
        }

        platformView.SetImageDrawable(null);
        _glideListener?.Dispose();
        _glideListener = null;
        base.DisconnectHandler(platformView);
    }

    private void ApplyScaleType()
    {
        if (PlatformView is null) return;
        PlatformView.SetScaleType(
            VirtualView.AspectMode == ZoomImageAspect.CenterCrop
                ? global::Android.Widget.ImageView.ScaleType.CenterCrop
                : global::Android.Widget.ImageView.ScaleType.FitCenter);
    }

    private void ApplyInteraction()
    {
        if (PlatformView is null) return;

        var enabled = VirtualView.EnableFullscreen;
        PlatformView.Clickable = enabled;
        PlatformView.Focusable = enabled;

        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            return;

        if (!enabled)
        {
            PlatformView.Foreground = null;
            return;
        }

        using var ta = Context.ObtainStyledAttributes(
            new[] { global::Android.Resource.Attribute.SelectableItemBackground });
        PlatformView.Foreground = ta.GetDrawable(0);
        ta.Recycle();
    }

    private void LoadImage()
    {
        if (_disposed || PlatformView is null) return;

        if (string.IsNullOrWhiteSpace(VirtualView.Source))
        {
            // Cancela qualquer request pendente antes de limpar
            try { Glide.With(PlatformView).Clear(PlatformView); }
            catch { /* ignora */ }
            ApplyPlaceholderFallback();
            VirtualView.SetIsLoading(false);
            return;
        }

        var options = BuildRequestOptions();
        VirtualView.SetIsLoading(true);

        AndroidImageLoader.LoadInto(
            PlatformView,
            VirtualView.Source,
            options,
            _glideListener,
            VirtualView.LegacyIsUrl);
    }

    private RequestOptions BuildRequestOptions()
    {
        var options = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? new RequestOptions().CenterCrop()
            : new RequestOptions().FitCenter();

        var decodeMaxPx = Math.Max(64, VirtualView.DecodeMaxPx);
        options = options
            .Override(decodeMaxPx, decodeMaxPx)
            .DontAnimate();
        options.SetDiskCacheStrategy(DiskCacheStrategy.Automatic!);

        var placeholderId = AndroidImageLoader.ResolveDrawable(Context, VirtualView.Placeholder);
        if (placeholderId != 0)
            options = options.Clone().Placeholder(placeholderId).Error(placeholderId);

        return options;
    }

    private void ApplyPlaceholderFallback()
    {
        var placeholderId = AndroidImageLoader.ResolveDrawable(Context, VirtualView.Placeholder);
        if (placeholderId != 0)
            PlatformView.SetImageResource(placeholderId);
        else
            PlatformView.SetImageDrawable(null);
    }

    private void OnImageClick(object? sender, EventArgs e)
    {
        if (_disposed || !VirtualView.EnableFullscreen) return;
        if (string.IsNullOrWhiteSpace(VirtualView.Source)) return;

        var activity = Context.GetActivity();
        if (activity is null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[ZoomImageViewHandler] FragmentActivity not found.");
            return;
        }

        var fsSource = VirtualView.FullscreenSource ?? VirtualView.Source;
        var dialog = new FullscreenZoomDialogFragment(
            source:      fsSource,
            isUrl:       ImageSourceResolver.IsRemote(fsSource, VirtualView.LegacyIsUrl),
            placeholder: VirtualView.Placeholder,
            maxZoom:     VirtualView.MaxZoom);

        dialog.Show(
            activity.SupportFragmentManager,
            FullscreenZoomDialogFragment.Tag);
    }
}

// ── Helper interno ────────────────────────────────────────────────────────

internal sealed class ImgGlideRequestListener
    : Java.Lang.Object, Bumptech.Glide.Request.IRequestListener
{
    private readonly Action _onReady;
    private readonly Action _onFail;

    public ImgGlideRequestListener(Action onReady, Action onFail)
    {
        _onReady = onReady;
        _onFail  = onFail;
    }

    public bool OnResourceReady(
        Java.Lang.Object? resource,
        Java.Lang.Object? model,
        Bumptech.Glide.Request.Target.ITarget? target,
        Bumptech.Glide.Load.DataSource dataSource,
        bool isFirstResource)
    {
        _onReady();
        return false;
    }

    public bool OnLoadFailed(
        GlideException? e,
        Java.Lang.Object? model,
        Bumptech.Glide.Request.Target.ITarget? target,
        bool isFirstResource)
    {
        _onFail();
        return false;
    }
}
