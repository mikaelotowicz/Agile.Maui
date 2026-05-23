// Platforms/Android/ZoomImageViewHandler.cs
using Android.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Load.Engine;
using Bumptech.Glide.Request;
using Microsoft.Maui.Handlers;
using Controls;

namespace Controls.Platforms.Android;

public sealed class ImageViewHandler : ViewHandler<ImageView, global::Android.Widget.ImageView>
{
    public static readonly PropertyMapper<ImageView, ImageViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(ImageView.Source)]           = (h, _) => h.LoadImage(),
            [nameof(ImageView.IsUrl)]            = (h, _) => h.LoadImage(),
            [nameof(ImageView.Placeholder)]      = (h, _) => h.LoadImage(),
            [nameof(ImageView.AspectMode)]       = (h, _) => h.ApplyScaleType(),
            [nameof(ImageView.MaxZoom)]          = (h, _) => { },
            [nameof(ImageView.EnableFullscreen)] = (h, _) => { },
        };

    public ImageViewHandler() : base(Mapper) { }

    protected override global::Android.Widget.ImageView CreatePlatformView()
    {
        var imageView = new global::Android.Widget.ImageView(Context)
        {
            Clickable = true,
            Focusable  = true,
        };

        // Ripple effect nativo ao tocar (Foreground requer API 23+)
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            using var ta = Context.ObtainStyledAttributes(
                new[] { global::Android.Resource.Attribute.SelectableItemBackground });
            imageView.Foreground = ta.GetDrawable(0);
            ta.Recycle();
        }

        return imageView;
    }

    protected override void ConnectHandler(global::Android.Widget.ImageView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Click += OnImageClick;
        ApplyScaleType();
        LoadImage();
    }

    protected override void DisconnectHandler(global::Android.Widget.ImageView platformView)
    {
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

    private void LoadImage()
    {
        if (PlatformView is null) return;

        if (string.IsNullOrWhiteSpace(VirtualView.Source))
        {
            // Cancela qualquer request pendente antes de limpar
            try { Glide.With(PlatformView).Clear(PlatformView); }
            catch { /* ignora */ }
            PlatformView.SetImageDrawable(null);
            return;
        }

        var options  = BuildRequestOptions();
        var listener = new ImgGlideRequestListener(
            onReady: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded()),
            onFail:  () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed()));

        if (VirtualView.IsUrl)
        {
            Glide.With(PlatformView)
                .Load(VirtualView.Source)
                .Apply(options)
                .Listener(listener)
                .Into(PlatformView);
        }
        else
        {
            var drawableId = ResolveDrawable(VirtualView.Source);
            if (drawableId == 0)
            {
                ApplyPlaceholderFallback();
                VirtualView?.RaiseImageFailed();
                return;
            }

            Glide.With(PlatformView)
                .Load(drawableId)
                .Apply(options)
                .Listener(listener)
                .Into(PlatformView);
        }
    }

    private RequestOptions BuildRequestOptions()
    {
        var options = VirtualView.AspectMode == ZoomImageAspect.CenterCrop
            ? new RequestOptions().CenterCrop()
            : new RequestOptions().FitCenter();


        var placeholderId = ResolveDrawable(VirtualView.Placeholder);
        if (placeholderId != 0)
            options = options.Clone().Placeholder(placeholderId).Error(placeholderId);

        return options;
    }

    private void ApplyPlaceholderFallback()
    {
        var placeholderId = ResolveDrawable(VirtualView.Placeholder);
        if (placeholderId != 0)
            PlatformView.SetImageResource(placeholderId);
        else
            PlatformView.SetImageDrawable(null);
    }

    private int ResolveDrawable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        return Context.Resources?.GetIdentifier(
            name, "drawable", Context.PackageName) ?? 0;
    }

    private void OnImageClick(object? sender, EventArgs e)
    {
        if (!VirtualView.EnableFullscreen) return;
        if (string.IsNullOrWhiteSpace(VirtualView.Source)) return;

        var activity = Context.GetActivity();
        if (activity is null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[ZoomImageViewHandler] FragmentActivity not found.");
            return;
        }

        var dialog = new FullscreenZoomDialogFragment(
            source:      VirtualView.Source,
            isUrl:       VirtualView.IsUrl,
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
