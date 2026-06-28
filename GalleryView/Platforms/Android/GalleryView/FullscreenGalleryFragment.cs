// Platforms/Android/GalleryView/FullscreenGalleryFragment.cs
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Bumptech.Glide;
using Bumptech.Glide.Load.Engine;
using Bumptech.Glide.Request;
using Color = Android.Graphics.Color;

using AndroidView        = Android.Views.View;
using AndroidProgressBar = Android.Widget.ProgressBar;

namespace Agile.Maui.Platforms.Android;

public sealed class FullscreenGalleryFragment : DialogFragment
{
    public new const string Tag = nameof(FullscreenGalleryFragment);

    private const string KeyImages      = "gallery_images";
    private const string KeyIsUrl       = "gallery_is_url";
    private const string KeyPlaceholder = "gallery_placeholder";
    private const string KeyMaxZoom     = "gallery_max_zoom";
    private const string KeyStartIndex  = "gallery_start_index";

    private string[]?  _images;
    private bool       _isUrl;
    private string?    _placeholder;
    private float      _maxZoom;
    private int        _startIndex;
    private Action<int>? _onIndexChanged;

    private AndroidX.ViewPager2.Widget.ViewPager2?              _viewPager;
    private TextView?                                           _indicator;
    private FrameLayout?                                        _root;
    private AndroidX.ViewPager2.Widget.ViewPager2.OnPageChangeCallback? _pageCallback;

    public FullscreenGalleryFragment(
        string[]  images,
        bool      isUrl,
        string?   placeholder,
        float     maxZoom,
        int       startIndex,
        Action<int>? onIndexChanged)
    {
        _images         = images;
        _isUrl          = isUrl;
        _placeholder    = placeholder;
        _maxZoom        = Math.Max(1f, maxZoom);
        _startIndex     = Math.Clamp(startIndex, 0, Math.Max(0, images.Length - 1));
        _onIndexChanged = onIndexChanged;
    }

    public FullscreenGalleryFragment() { }

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (savedInstanceState is not null)
        {
            _images      = savedInstanceState.GetStringArray(KeyImages);
            _isUrl       = savedInstanceState.GetBoolean(KeyIsUrl);
            _placeholder = savedInstanceState.GetString(KeyPlaceholder);
            _maxZoom     = Math.Max(1f, savedInstanceState.GetFloat(KeyMaxZoom, 5f));
            _startIndex  = savedInstanceState.GetInt(KeyStartIndex, 0);
        }

        SetStyle(StyleNoTitle,
            global::Android.Resource.Style.ThemeBlackNoTitleBarFullScreen);
    }

    public override AndroidView? OnCreateView(
        LayoutInflater inflater,
        ViewGroup? container,
        Bundle? savedInstanceState)
    {
        _root = new FrameLayout(RequireContext());
        _root.SetBackgroundColor(Color.Black);

        _viewPager = new AndroidX.ViewPager2.Widget.ViewPager2(RequireContext());
        _viewPager.Orientation = AndroidX.ViewPager2.Widget.ViewPager2.OrientationHorizontal;
        _root.AddView(_viewPager, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var adapter = new GalleryPagerAdapter(
            _images ?? Array.Empty<string>(),
            _isUrl,
            _placeholder,
            _maxZoom,
            RequireContext(),
            isZoomed => _viewPager.UserInputEnabled = !isZoomed);
        _viewPager.Adapter = adapter;

        _pageCallback = new GalleryPageCallback(index =>
        {
            UpdateIndicator(index);
            _onIndexChanged?.Invoke(index);
        });
        _viewPager.RegisterOnPageChangeCallback(_pageCallback);

        if (_startIndex > 0 && _images?.Length > _startIndex)
            _viewPager.SetCurrentItem(_startIndex, false);

        AddCloseButton();
        AddIndicator();

        return _root;
    }

    public override void OnDestroyView()
    {
        if (_pageCallback is not null)
        {
            _viewPager?.UnregisterOnPageChangeCallback(_pageCallback);
            _pageCallback = null;
        }
        _onIndexChanged = null;
        if (_viewPager is not null)
            _viewPager.Adapter = null;
        base.OnDestroyView();
    }

    public override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutStringArray(KeyImages,      _images ?? Array.Empty<string>());
        outState.PutBoolean(KeyIsUrl,           _isUrl);
        outState.PutString(KeyPlaceholder,      _placeholder ?? string.Empty);
        outState.PutFloat(KeyMaxZoom,           _maxZoom);
        outState.PutInt(KeyStartIndex,          _viewPager?.CurrentItem ?? _startIndex);
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Dialog?.Window is { } window)
        {
            window.SetLayout(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent);
            window.SetBackgroundDrawableResource(
                global::Android.Resource.Color.Black);

            // Garante que o dialog cobre 100% da tela, inclusive atrás da status bar.
            // Sem LayoutNoLimits + LayoutInScreen o dialog herda o bounds do container MAUI
            // e aparece deslocado/centralizado apenas na área do componente.
            window.AddFlags(
                WindowManagerFlags.LayoutInScreen |
                WindowManagerFlags.LayoutNoLimits);

            var attrs = window.Attributes!;
            attrs.Gravity = GravityFlags.Fill;
            attrs.X = 0;
            attrs.Y = 0;
            window.Attributes = attrs;
        }
    }

    private void AddCloseButton()
    {
        var density  = RequireContext().Resources?.DisplayMetrics?.Density ?? 1f;
        var btnPx    = (int)(40 * density);
        var marginPx = (int)(16 * density);
        var topPx    = (int)(44 * density);

        var bg = new global::Android.Graphics.Drawables.GradientDrawable();
        bg.SetShape(global::Android.Graphics.Drawables.ShapeType.Rectangle);
        bg.SetCornerRadius(btnPx / 2f);
        bg.SetColor(Color.Argb(160, 0, 0, 0));

        var closeBtn = new global::Android.Widget.TextView(RequireContext())
        {
            Text      = "✕",
            TextSize  = 18f,
            Gravity   = GravityFlags.Center,
            Clickable = true,
            Focusable = true,
            Background = bg,
        };
        closeBtn.SetTextColor(Color.White);
        closeBtn.Click += (_, _) => DismissAllowingStateLoss();

        _root!.AddView(closeBtn, new FrameLayout.LayoutParams(btnPx, btnPx)
        {
            Gravity     = GravityFlags.Top | GravityFlags.Right,
            TopMargin   = topPx,
            RightMargin = marginPx,
        });
    }

    private void AddIndicator()
    {
        if (_images is null || _images.Length <= 1) return;

        var density  = RequireContext().Resources?.DisplayMetrics?.Density ?? 1f;
        var marginPx = (int)(16 * density);
        var topPx    = (int)(44 * density);

        _indicator = new TextView(RequireContext())
        {
            TextSize  = 14f,
            Gravity   = GravityFlags.Center,
        };
        _indicator.SetTextColor(Color.White);
        UpdateIndicator(_startIndex);

        _root!.AddView(_indicator, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity    = GravityFlags.Top | GravityFlags.Left,
            TopMargin  = topPx,
            LeftMargin = marginPx,
        });
    }

    private void UpdateIndicator(int index)
    {
        if (_indicator is null || _images is null) return;
        _indicator.Text = $"{index + 1} / {_images.Length}";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryPagerAdapter
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class GalleryPagerAdapter : RecyclerView.Adapter
{
    private readonly string[]    _images;
    private readonly bool        _isUrl;
    private readonly string?     _placeholder;
    private readonly float       _maxZoom;
    private readonly Action<bool> _onZoomStateChanged;
    private readonly RequestOptions _requestOptions;

    public GalleryPagerAdapter(
        string[]     images,
        bool         isUrl,
        string?      placeholder,
        float        maxZoom,
        global::Android.Content.Context context,
        Action<bool> onZoomStateChanged)
    {
        _images             = images;
        _isUrl              = isUrl;
        _placeholder        = placeholder;
        _maxZoom            = maxZoom;
        _onZoomStateChanged = onZoomStateChanged;
        _requestOptions     = BuildOptions(context);
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
        };
        imageView.SetBackgroundColor(Color.Black);
        imageView.SetScaleType(global::Android.Widget.ImageView.ScaleType.Matrix);

        var container = new FrameLayout(context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent),
        };
        container.SetBackgroundColor(Color.Black);
        container.AddView(imageView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var density  = context.Resources?.DisplayMetrics?.Density ?? 1f;
        var progress = new AndroidProgressBar(context)
        {
            Visibility = ViewStates.Visible
        };
        container.AddView(progress, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        { Gravity = GravityFlags.Center });

        return new GalleryPageViewHolder(container, imageView, progress);
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is GalleryPageViewHolder vh)
        {
            vh.BindToken++;
            try { Glide.With(vh.ImageView).Clear(vh.ImageView); } catch { }
            vh.ImageView.SetImageDrawable(null);
            vh.ImageView.SetOnTouchListener(null);
            vh.Progress.Visibility = ViewStates.Gone;
        }
        base.OnViewRecycled(holder);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not GalleryPageViewHolder vh) return;

        var token  = ++vh.BindToken;
        var source = _images[position];

        // Clear previous request
        try { Glide.With(vh.ImageView).Clear(vh.ImageView); } catch { }
        vh.ImageView.SetImageDrawable(null);
        vh.ImageView.Visibility = ViewStates.Invisible; // oculta até a matrix estar correta
        vh.Progress.Visibility  = ViewStates.Visible;

        var mediumScale = Math.Min(2.5f, _maxZoom * 0.55f);
        var zoomHandler = new ZoomTouchHandler(
            imageView:          vh.ImageView,
            mediumScale:        mediumScale,
            maxScale:           _maxZoom,
            onDismiss:          () => { }, // no-op: gallery uses close button
            onZoomStateChanged: _onZoomStateChanged);

        vh.ImageView.SetOnTouchListener(zoomHandler);

        var context = vh.ImageView.Context!;

        void OnReady()
        {
            if (vh.BindToken != token) return;
            vh.ImageView.Post(() =>
            {
                if (vh.BindToken != token) return;
                zoomHandler.InitMatrix();
                vh.ImageView.Visibility = ViewStates.Visible;
                MainThread.BeginInvokeOnMainThread(
                    () => vh.Progress.Visibility = ViewStates.Gone);
            });
        }

        void OnFail()
        {
            if (vh.BindToken != token) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (vh.BindToken != token) return;
                vh.ImageView.Visibility = ViewStates.Visible;
                vh.Progress.Visibility  = ViewStates.Gone;
            });
        }

        var listener = new ZoomGlideRequestListener(onReady: OnReady, onFail: OnFail);

        AndroidImageLoader.LoadInto(vh.ImageView, source, _requestOptions, listener, _isUrl);
    }

    private RequestOptions BuildOptions(global::Android.Content.Context context)
    {
        var metrics = context.Resources?.DisplayMetrics;
        var maxScreenPx = Math.Max(metrics?.WidthPixels ?? 0, metrics?.HeightPixels ?? 0);
        var decodePx = Math.Clamp(
            (int)(maxScreenPx * Math.Max(1f, Math.Min(_maxZoom, 3f))),
            720,
            4096);

        var o = new RequestOptions()
            .FitCenter()
            .Override(decodePx, decodePx)
            .DontAnimate();
        o.SetDiskCacheStrategy(DiskCacheStrategy.Automatic!);
        var ph = AndroidImageLoader.ResolveDrawable(context, _placeholder);
        if (ph != 0) o = o.Clone().Placeholder(ph).Error(ph);
        return o;
    }

    private static int ResolveDrawable(global::Android.Content.Context context, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        return AndroidImageLoader.ResolveDrawable(context, name);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryPageViewHolder
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class GalleryPageViewHolder : RecyclerView.ViewHolder
{
    public global::Android.Widget.ImageView ImageView  { get; }
    public AndroidProgressBar               Progress   { get; }
    internal int                            BindToken;

    public GalleryPageViewHolder(
        AndroidView                          root,
        global::Android.Widget.ImageView     imageView,
        AndroidProgressBar                   progress)
        : base(root)
    {
        ImageView = imageView;
        Progress  = progress;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GalleryPageCallback
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class GalleryPageCallback : AndroidX.ViewPager2.Widget.ViewPager2.OnPageChangeCallback
{
    private readonly Action<int> _onPageSelected;

    public GalleryPageCallback(Action<int> onPageSelected)
        => _onPageSelected = onPageSelected;

    public override void OnPageSelected(int position)
        => _onPageSelected(position);
}
