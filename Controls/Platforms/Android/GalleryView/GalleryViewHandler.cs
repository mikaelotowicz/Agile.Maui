// Platforms/Android/GalleryView/GalleryViewHandler.cs
using System.Collections.Specialized;
using Android.Graphics.Drawables;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Request;
using Microsoft.Maui.Handlers;

namespace Agile.Maui.Platforms.Android;

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
            [nameof(GalleryView.MaxZoom)]                = (h, _) => { },
            [nameof(GalleryView.ShowIndicator)]          = (h, _) => h.UpdateDots(),
            [nameof(GalleryView.IndicatorColor)]         = (h, _) => h.UpdateDots(),
            [nameof(GalleryView.IndicatorInactiveColor)] = (h, _) => h.UpdateDots(),
            [nameof(GalleryView.ThumbMaxPx)]             = (h, _) => h.ReloadAdapter(),
        };

    private GalleryPageCallback?         _pageCallback;
    private INotifyCollectionChanged?    _imagesChangedSource;
    private bool                         _syncingPage;
    private bool                         _disposed;
    private bool                         _pendingReload;

    public GalleryViewHandler() : base(Mapper) { }

    protected override GalleryContainerView CreatePlatformView()
        => new(Context);

    protected override void ConnectHandler(GalleryContainerView platformView)
    {
        base.ConnectHandler(platformView);
        _pageCallback = new GalleryPageCallback(OnPageChanged);
        platformView.Pager.RegisterOnPageChangeCallback(_pageCallback);
        platformView.OnLayoutChanged = UpdateDots;
        ReloadAdapter();
    }

    protected override void DisconnectHandler(GalleryContainerView platformView)
    {
        _disposed = true;
        UnsubscribeImages();
        platformView.OnLayoutChanged = null;
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
        if (_disposed || PlatformView is null) return;
        if (_pendingReload) return;
        _pendingReload = true;
        PlatformView.Post(() =>
        {
            _pendingReload = false;
            if (_disposed || PlatformView is null) return;
            DoReloadAdapter();
        });
    }

    private void DoReloadAdapter()
    {
        UnsubscribeImages();

        var pager  = PlatformView.Pager;
        var images = VirtualView.Images;

        if (images is null || images.Count == 0)
        {
            pager.Adapter = null;
            SubscribeImages(images);
            UpdateDots();
            return;
        }

        var targetIdx = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);

        pager.Adapter = new ThumbPagerAdapter(
            images:        images.ToArray(),
            isUrl:         VirtualView.IsUrl,
            placeholder:   VirtualView.Placeholder,
            aspectMode:    VirtualView.AspectMode,
            thumbMaxPx:    VirtualView.ThumbMaxPx,
            onPageClick:   OpenFullscreen,
            onImageLoaded: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageLoaded()),
            onImageFailed: () => MainThread.BeginInvokeOnMainThread(() => VirtualView?.RaiseImageFailed()));

        if (targetIdx > 0)
        {
            var observer = new OneShotAdapterObserver(pager, targetIdx);
            pager.Adapter.RegisterAdapterDataObserver(observer);
            pager.Post(observer.Apply);
        }

        SubscribeImages(images);
        UpdateDots();
    }

    private void SubscribeImages(IList<string>? images)
    {
        if (images is INotifyCollectionChanged ncc)
        {
            _imagesChangedSource = ncc;
            ncc.CollectionChanged += OnImagesCollectionChanged;
        }
    }

    private void UnsubscribeImages()
    {
        if (_imagesChangedSource is not null)
        {
            _imagesChangedSource.CollectionChanged -= OnImagesCollectionChanged;
            _imagesChangedSource = null;
        }
    }

    private void OnImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed || PlatformView is null) return;
        MainThread.BeginInvokeOnMainThread(ReloadAdapter);
    }

    private void SyncPage()
    {
        if (_disposed || PlatformView is null || _syncingPage) return;
        var images = VirtualView.Images;
        if (images is null || images.Count == 0) return;
        var idx = Math.Clamp(VirtualView.SelectedIndex, 0, images.Count - 1);
        PlatformView.Pager.SetCurrentItem(idx, false);
    }

    private void OnPageChanged(int index)
    {
        if (_disposed || VirtualView is null) return;
        _syncingPage = true;
        VirtualView.SelectedIndex = index;
        VirtualView.RaiseSelectionChanged(index);
        _syncingPage = false;
        UpdateDots();
    }

    private void UpdateDots()
    {
        if (_disposed || PlatformView is null) return;
        var count    = VirtualView.Images?.Count ?? 0;
        var idx      = Math.Clamp(VirtualView.SelectedIndex, 0, Math.Max(0, count - 1));
        var active   = ToAndroidArgb(VirtualView.IndicatorColor);
        var inactive = ToAndroidArgb(VirtualView.IndicatorInactiveColor);
        PlatformView.Dots.Update(VirtualView.ShowIndicator, count, idx, active, inactive);
    }

    private static int ToAndroidArgb(Microsoft.Maui.Graphics.Color c) =>
        global::Android.Graphics.Color.Argb(
            (int)(c.Alpha * 255),
            (int)(c.Red   * 255),
            (int)(c.Green * 255),
            (int)(c.Blue  * 255));

    private void OpenFullscreen(int startIndex)
    {
        if (_disposed) return;
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
                if (_disposed || VirtualView is null) return;
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

    private float _downX;
    private float _downY;

    public GalleryContainerView(global::Android.Content.Context context) : base(context)
    {
        Pager = new ViewPager2(context) { Orientation = ViewPager2.OrientationHorizontal };

        var recycler = (RecyclerView)Pager.GetChildAt(0)!;
        recycler.SetClipToPadding(true);
        recycler.SetClipChildren(true);
        recycler.SetPadding(0, 0, 0, 0);
        recycler.SetItemViewCacheSize(0);

        Pager.OffscreenPageLimit = 1;
        Pager.SetPageTransformer(null);

        Dots = new DotsView(context);

        AddView(Pager, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));

        var lp = new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent,
                                  GravityFlags.Bottom | GravityFlags.CenterHorizontal);
        lp.BottomMargin = DpToPx(context, 10);
        AddView(Dots, lp);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        switch (ev?.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = ev.GetX();
                _downY = ev.GetY();
                Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
            case MotionEventActions.Move:
                var dx = Math.Abs(ev.GetX() - _downX);
                var dy = Math.Abs(ev.GetY() - _downY);
                var slop = ViewConfiguration.Get(Context!)?.ScaledTouchSlop ?? 8;
                if (dx > dy && dx > slop)
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                break;
        }
        return base.DispatchTouchEvent(ev);
    }

    internal Action? OnLayoutChanged { get; set; }

    protected override void OnLayout(bool changed, int l, int t, int r, int b)
    {
        base.OnLayout(changed, l, t, r, b);
        if (changed && Width > 0 && Height > 0)
            OnLayoutChanged?.Invoke();
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        Pager.Adapter?.NotifyDataSetChanged();
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

    public void Update(bool show, int count, int selected, int activeArgb, int inactiveArgb)
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
            dot.Background = MakeDot(i == selected, activeArgb, inactiveArgb);
            AddView(dot);
        }
        Visibility = ViewStates.Visible;
    }

    private static Drawable MakeDot(bool active, int activeArgb, int inactiveArgb)
    {
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Oval);
        d.SetColor(active ? activeArgb : inactiveArgb);
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
    private readonly int             _thumbMaxPx;
    private readonly Action<int>     _onPageClick;
    private readonly Action          _onImageLoaded;
    private readonly Action          _onImageFailed;

    public ThumbPagerAdapter(
        string[]        images,
        bool            isUrl,
        string?         placeholder,
        ZoomImageAspect aspectMode,
        int             thumbMaxPx,
        Action<int>     onPageClick,
        Action          onImageLoaded,
        Action          onImageFailed)
    {
        _images        = images;
        _isUrl         = isUrl;
        _placeholder   = placeholder;
        _aspectMode    = aspectMode;
        _thumbMaxPx    = thumbMaxPx > 0 ? thumbMaxPx : 720;
        _onPageClick   = onPageClick;
        _onImageLoaded = onImageLoaded;
        _onImageFailed = onImageFailed;
    }

    public override int ItemCount => _images.Length;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var context = parent.Context!;
        var isFit   = _aspectMode != ZoomImageAspect.CenterCrop;

        // FrameLayout raiz necessário para ViewPager2 resolver MATCH_PARENT corretamente.
        var frame = new global::Android.Widget.FrameLayout(context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
        };

        // ImageView MATCH_PARENT em ambos os modos.
        // FitCenter centraliza a imagem dentro da área disponível (letterbox).
        // CenterCrop preenche a área cortando as bordas.
        var imageView = new global::Android.Widget.ImageView(context)
        {
            LayoutParameters = new global::Android.Widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
            Clickable = true,
            Focusable = true,
        };

        imageView.SetScaleType(isFit
            ? global::Android.Widget.ImageView.ScaleType.FitCenter
            : global::Android.Widget.ImageView.ScaleType.CenterCrop);

        frame.AddView(imageView);

        var holder = new ThumbPageViewHolder(frame, imageView);
        imageView.Click += (_, _) =>
        {
            var pos = holder.BindingAdapterPosition;
            if (pos >= 0) _onPageClick(pos);
        };
        return holder;
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is ThumbPageViewHolder vh)
        {
            try { Glide.With(vh.ImageView).Clear(vh.ImageView); } catch { }
            vh.ImageView.SetImageDrawable(null);
        }
        base.OnViewRecycled(holder);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not ThumbPageViewHolder vh) return;

        var source = _images[position];

        try { Glide.With(vh.ImageView).Clear(vh.ImageView); } catch { }
        vh.ImageView.SetImageDrawable(null);

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
            vh.ImageView.SetImageResource(id);
            _onImageLoaded();
        }
    }

    private RequestOptions BuildOptions(global::Android.Content.Context context)
    {
        var o = _aspectMode == ZoomImageAspect.CenterCrop
            ? new RequestOptions().CenterCrop()
            : new RequestOptions().FitCenter();
        o = o.Override(_thumbMaxPx, _thumbMaxPx);
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

    public ThumbPageViewHolder(
        global::Android.Widget.FrameLayout   root,
        global::Android.Widget.ImageView     imageView)
        : base(root)
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
