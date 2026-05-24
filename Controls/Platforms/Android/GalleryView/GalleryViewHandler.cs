// Platforms/Android/GalleryView/GalleryViewHandler.cs
using Android.Graphics.Drawables;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Request;
using Microsoft.Maui.Handlers;

namespace Controls.Platforms.Android;

public sealed class GalleryViewHandler : ViewHandler<GalleryView, GalleryContainerView>
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
            [nameof(GalleryView.ShowIndicator)] = (h, _) => h.UpdateDots(),
        };

    private GalleryPageCallback? _pageCallback;
    private bool _syncingPage;

    public GalleryViewHandler() : base(Mapper) { }

    protected override GalleryContainerView CreatePlatformView()
        => new(Context);

    protected override void ConnectHandler(GalleryContainerView platformView)
    {
        base.ConnectHandler(platformView);
        _pageCallback = new GalleryPageCallback(OnPageChanged);
        platformView.Pager.RegisterOnPageChangeCallback(_pageCallback);
        ReloadAdapter();
    }

    protected override void DisconnectHandler(GalleryContainerView platformView)
    {
        if (_pageCallback is not null)
        {
            platformView.Pager.UnregisterOnPageChangeCallback(_pageCallback);
            _pageCallback = null;
        }
        platformView.Pager.Adapter = null;
        base.DisconnectHandler(platformView);
    }

    private void ReloadAdapter()
    {
        if (PlatformView is null) return;
        var pager  = PlatformView.Pager;
        var images = VirtualView.Images;

        if (images is null || images.Count == 0)
        {
            pager.Adapter = null;
            UpdateDots();
            return;
        }

        var targetIdx = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);

        pager.Adapter = new ThumbPagerAdapter(
            images:        images.ToArray(),
            isUrl:         VirtualView.IsUrl,
            placeholder:   VirtualView.Placeholder,
            aspectMode:    VirtualView.AspectMode,
            onPageClick:   OpenFullscreen,
            onImageLoaded: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded()),
            onImageFailed: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed()));

        if (targetIdx > 0)
        {
            var observer = new OneShotAdapterObserver(pager, targetIdx);
            pager.Adapter.RegisterAdapterDataObserver(observer);
            pager.Post(observer.Apply);
        }

        UpdateDots();
    }

    private void SyncPage()
    {
        if (PlatformView is null || _syncingPage) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        var idx = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        PlatformView.Pager.SetCurrentItem(idx, false);
    }

    private void OnPageChanged(int index)
    {
        if (VirtualView is null) return;
        _syncingPage = true;
        VirtualView.SelectedIndex = index;
        VirtualView.RaiseSelectionChanged(index);
        _syncingPage = false;
        UpdateDots();
    }

    private void UpdateDots()
    {
        if (PlatformView is null) return;
        var count = VirtualView.Images?.Count ?? 0;
        var idx   = Math.Clamp(VirtualView.SelectedIndex, 0, Math.Max(0, count - 1));
        PlatformView.Dots.Update(VirtualView.ShowIndicator, count, idx);
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
                    () => PlatformView?.Pager.SetCurrentItem(index, false));
            });

        dialog.Show(activity.SupportFragmentManager, FullscreenGalleryFragment.Tag);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryContainerView — FrameLayout que envolve ViewPager2 + DotsView
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GalleryContainerView : global::Android.Widget.FrameLayout
{
    public   readonly ViewPager2 Pager;
    internal readonly DotsView   Dots;

    public GalleryContainerView(global::Android.Content.Context context) : base(context)
    {
        Pager = new ViewPager2(context) { Orientation = ViewPager2.OrientationHorizontal };
        Dots  = new DotsView(context);

        AddView(Pager, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));

        var lp = new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent,
                                  GravityFlags.Bottom | GravityFlags.CenterHorizontal);
        lp.BottomMargin = DpToPx(context, 10);
        AddView(Dots, lp);
    }

    private static int DpToPx(global::Android.Content.Context ctx, int dp) =>
        (int)(dp * ctx.Resources!.DisplayMetrics!.Density + 0.5f);
}

// ─────────────────────────────────────────────────────────────────────────────
// DotsView — indicador de páginas (bolinhas)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DotsView : global::Android.Widget.LinearLayout
{
    private const int DotDp    = 7;
    private const int MarginDp = 4;

    public DotsView(global::Android.Content.Context context) : base(context)
    {
        Orientation = global::Android.Widget.Orientation.Horizontal;
        SetGravity(GravityFlags.Center);
        Visibility = ViewStates.Gone;
    }

    public void Update(bool show, int count, int selected)
    {
        RemoveAllViews();
        if (!show || count <= 1) { Visibility = ViewStates.Gone; return; }

        var density  = Context!.Resources!.DisplayMetrics!.Density;
        int dotPx    = (int)(DotDp    * density + 0.5f);
        int marginPx = (int)(MarginDp * density + 0.5f);

        for (int i = 0; i < count; i++)
        {
            var dot = new global::Android.Views.View(Context);
            var lp  = new global::Android.Widget.LinearLayout.LayoutParams(dotPx, dotPx);
            lp.SetMargins(marginPx, 0, marginPx, 0);
            dot.LayoutParameters = lp;
            dot.Background = MakeDot(i == selected);
            AddView(dot);
        }
        Visibility = ViewStates.Visible;
    }

    private static Drawable MakeDot(bool active)
    {
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Oval);
        d.SetColor(active
            ? unchecked((int)0xFFFFFFFF)
            : unchecked((int)0x80FFFFFF));
        return d;
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

internal sealed class OneShotAdapterObserver : RecyclerView.AdapterDataObserver
{
    private readonly ViewPager2 _pager;
    private readonly int        _index;
    private bool                _done;

    public OneShotAdapterObserver(ViewPager2 pager, int index)
    {
        _pager = pager;
        _index = index;
    }

    public override void OnChanged() => Apply();

    public void Apply()
    {
        if (_done) return;
        if (_pager.Width == 0 || _pager.Height == 0)
        {
            _pager.Post(Apply);
            return;
        }
        _done = true;
        _pager.Adapter?.UnregisterAdapterDataObserver(this);
        _pager.SetCurrentItem(_index, false);
    }
}
