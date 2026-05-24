// Platforms/Android/GalleryView/GalleryViewHandler.cs
using Android.Views;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Request;
using Microsoft.Maui.Handlers;

namespace Controls.Platforms.Android;

public sealed class GalleryViewHandler : ViewHandler<GalleryView, ViewPager2>
{
    public static readonly PropertyMapper<GalleryView, GalleryViewHandler> Mapper =
        new(ViewMapper)
        {
            [nameof(GalleryView.Images)]        = (h, _) => h.ReloadAdapter(),
            [nameof(GalleryView.SelectedIndex)] = (h, _) => h.SyncPage(),
            [nameof(GalleryView.IsUrl)]         = (h, _) => h.ReloadAdapter(),
            [nameof(GalleryView.Placeholder)]   = (h, _) => h.ReloadAdapter(),
            [nameof(GalleryView.AspectMode)]    = (h, _) => h.ReloadAdapter(),
            [nameof(GalleryView.MaxZoom)]       = (h, _) => { },
        };

    private GalleryPageCallback? _pageCallback;
    private bool _syncingPage;

    public GalleryViewHandler() : base(Mapper) { }

    protected override ViewPager2 CreatePlatformView()
        => new(Context) { Orientation = ViewPager2.OrientationHorizontal };

    protected override void ConnectHandler(ViewPager2 platformView)
    {
        base.ConnectHandler(platformView);
        _pageCallback = new GalleryPageCallback(OnPageChanged);
        platformView.RegisterOnPageChangeCallback(_pageCallback);
        ReloadAdapter();
    }

    protected override void DisconnectHandler(ViewPager2 platformView)
    {
        if (_pageCallback is not null)
        {
            platformView.UnregisterOnPageChangeCallback(_pageCallback);
            _pageCallback = null;
        }
        platformView.Adapter = null;
        base.DisconnectHandler(platformView);
    }

    private void ReloadAdapter()
    {
        if (PlatformView is null) return;

        var images = VirtualView.Images;
        if (images is null || images.Count == 0)
        {
            PlatformView.Adapter = null;
            return;
        }

        PlatformView.Adapter = new ThumbPagerAdapter(
            images:        images.ToArray(),
            isUrl:         VirtualView.IsUrl,
            placeholder:   VirtualView.Placeholder,
            aspectMode:    VirtualView.AspectMode,
            onPageClick:   OpenFullscreen,
            onImageLoaded: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded()),
            onImageFailed: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed()));

        SyncPage();
    }

    private void SyncPage()
    {
        if (PlatformView is null || _syncingPage) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        var idx = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        PlatformView.SetCurrentItem(idx, false);
    }

    private void OnPageChanged(int index)
    {
        if (VirtualView is null) return;
        _syncingPage = true;
        VirtualView.SelectedIndex = index;
        VirtualView.RaiseSelectionChanged(index);
        _syncingPage = false;
    }

    private void OpenFullscreen(int startIndex)
    {
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        var activity = Context.GetActivity();
        if (activity is null) return;

        var arr = images.ToArray();
        var idx = Math.Clamp(startIndex, 0, arr.Length - 1);

        var dialog = new FullscreenGalleryFragment(
            images:        arr,
            isUrl:         VirtualView.IsUrl,
            placeholder:   VirtualView.Placeholder,
            maxZoom:       VirtualView.MaxZoom,
            startIndex:    idx,
            onIndexChanged: index =>
            {
                if (VirtualView is null) return;
                VirtualView.SelectedIndex = index;
                VirtualView.RaiseSelectionChanged(index);
                MainThread.BeginInvokeOnMainThread(
                    () => PlatformView?.SetCurrentItem(index, false));
            });

        dialog.Show(activity.SupportFragmentManager, FullscreenGalleryFragment.Tag);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ThumbPagerAdapter — páginas simples sem zoom, click abre fullscreen
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ThumbPagerAdapter : RecyclerView.Adapter
{
    private readonly string[]        _images;
    private readonly bool            _isUrl;
    private readonly string?         _placeholder;
    private readonly ZoomImageAspect _aspectMode;
    private readonly Action<int>     _onPageClick;
    private readonly Action          _onImageLoaded;
    private readonly Action          _onImageFailed;

    public ThumbPagerAdapter(
        string[]        images,
        bool            isUrl,
        string?         placeholder,
        ZoomImageAspect aspectMode,
        Action<int>     onPageClick,
        Action          onImageLoaded,
        Action          onImageFailed)
    {
        _images        = images;
        _isUrl         = isUrl;
        _placeholder   = placeholder;
        _aspectMode    = aspectMode;
        _onPageClick   = onPageClick;
        _onImageLoaded = onImageLoaded;
        _onImageFailed = onImageFailed;
    }

    public override int ItemCount => _images.Length;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var context   = parent.Context!;
        var imageView = new global::Android.Widget.ImageView(context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
            Clickable = true,
            Focusable = true,
        };
        imageView.SetScaleType(_aspectMode == ZoomImageAspect.CenterCrop
            ? global::Android.Widget.ImageView.ScaleType.CenterCrop
            : global::Android.Widget.ImageView.ScaleType.FitCenter);

        var holder = new ThumbPageViewHolder(imageView);
        imageView.Click += (_, _) =>
        {
            var pos = holder.BindingAdapterPosition;
            if (pos >= 0) _onPageClick(pos);
        };
        return holder;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not ThumbPageViewHolder vh) return;

        try { Glide.With(vh.ImageView).Clear(vh.ImageView); } catch { }
        vh.ImageView.SetImageDrawable(null);

        var source   = _images[position];
        var opts     = BuildOptions(vh.ImageView.Context!);
        var listener = new ImgGlideRequestListener(
            onReady: _onImageLoaded,
            onFail:  _onImageFailed);

        if (_isUrl)
        {
            Glide.With(vh.ImageView).Load(source).Apply(opts).Listener(listener).Into(vh.ImageView);
        }
        else
        {
            var id = ResolveDrawable(vh.ImageView.Context!, source);
            if (id == 0) { _onImageFailed(); return; }
            // SetImageResource é mais confiável que Glide para drawables locais
            vh.ImageView.SetImageResource(id);
            _onImageLoaded();
        }
    }

    private RequestOptions BuildOptions(global::Android.Content.Context context)
    {
        var o = _aspectMode == ZoomImageAspect.CenterCrop
            ? new RequestOptions().CenterCrop()
            : new RequestOptions().FitCenter();
        var ph = ResolveDrawable(context, _placeholder);
        if (ph != 0) o = o.Clone().Placeholder(ph).Error(ph);
        return o;
    }

    private static int ResolveDrawable(global::Android.Content.Context context, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        return context.Resources?.GetIdentifier(name, "drawable", context.PackageName) ?? 0;
    }
}

internal sealed class ThumbPageViewHolder : RecyclerView.ViewHolder
{
    public global::Android.Widget.ImageView ImageView { get; }

    public ThumbPageViewHolder(global::Android.Widget.ImageView imageView) : base(imageView)
        => ImageView = imageView;
}

