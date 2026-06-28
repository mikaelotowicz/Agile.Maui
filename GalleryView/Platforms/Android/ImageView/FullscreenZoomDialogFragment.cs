// Platforms/Android/FullscreenZoomDialogFragment.cs
using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using AndroidX.Fragment.App;
using Bumptech.Glide;
using Bumptech.Glide.Load.Engine;
using Bumptech.Glide.Request;
using Color = Android.Graphics.Color;

// Aliases para resolver ambiguidades com Microsoft.Maui.Controls.*
using AndroidView        = Android.Views.View;
using AndroidProgressBar = Android.Widget.ProgressBar;

namespace Agile.Maui.Platforms.Android;

public sealed class FullscreenZoomDialogFragment : DialogFragment
{
    public new const string Tag = nameof(FullscreenZoomDialogFragment);

    private const string KeySource      = "source";
    private const string KeyIsUrl       = "is_url";
    private const string KeyPlaceholder = "placeholder";
    private const string KeyMaxZoom     = "max_zoom";
    private const float  DismissScaleThreshold = 1.05f;

    private string?  _source;
    private bool     _isUrl;
    private string?  _placeholder;
    private float    _maxZoom;

    private global::Android.Widget.ImageView? _imageView;
    private AndroidProgressBar?               _progressBar;
    private FrameLayout?                      _root;
    private ZoomTouchHandler?                 _zoomHandler;
    private ZoomKeyCallback?                  _keyCallback;

    private float MediumScale => Math.Min(2.5f, _maxZoom * 0.55f);

    public FullscreenZoomDialogFragment(
        string  source,
        bool    isUrl,
        string? placeholder,
        float   maxZoom)
    {
        _source      = source;
        _isUrl       = isUrl;
        _placeholder = placeholder;
        _maxZoom     = Math.Max(1f, maxZoom);
    }

    public FullscreenZoomDialogFragment() { }

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (savedInstanceState is not null)
        {
            _source      = savedInstanceState.GetString(KeySource);
            _isUrl       = savedInstanceState.GetBoolean(KeyIsUrl);
            _placeholder = savedInstanceState.GetString(KeyPlaceholder);
            _maxZoom     = Math.Max(1f, savedInstanceState.GetFloat(KeyMaxZoom, 5f));
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

        _imageView = new global::Android.Widget.ImageView(RequireContext());
        _imageView.SetBackgroundColor(Color.Black);
        _imageView.SetScaleType(global::Android.Widget.ImageView.ScaleType.Matrix);

        _root.AddView(_imageView,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        _progressBar = new AndroidProgressBar(RequireContext())
        {
            Visibility = ViewStates.Visible
        };
        var pbParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        { Gravity = GravityFlags.Center };
        _root.AddView(_progressBar, pbParams);

        AddCloseButton();

        _zoomHandler = new ZoomTouchHandler(
            imageView:   _imageView,
            mediumScale: MediumScale,
            maxScale:    _maxZoom,
            onDismiss:   DismissAllowingStateLoss);

        _imageView.SetOnTouchListener(_zoomHandler);

        LoadImage();
        return _root;
    }

    public override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutString(KeySource,      _source);
        outState.PutBoolean(KeyIsUrl,      _isUrl);
        outState.PutString(KeyPlaceholder, _placeholder ?? string.Empty);
        outState.PutFloat(KeyMaxZoom,      _maxZoom);
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
        }
    }

    public override void OnResume()
    {
        base.OnResume();
        _keyCallback ??= new ZoomKeyCallback(keyCode =>
        {
            if (keyCode != Keycode.Back) return false;
            if (_zoomHandler is not null && _zoomHandler.Scale > DismissScaleThreshold)
            {
                _zoomHandler.ResetZoom();
                return true;
            }
            return false;
        });
        Dialog?.SetOnKeyListener(_keyCallback);
    }

    public override void OnDestroyView()
    {
        if (_imageView is not null)
        {
            _imageView.SetOnTouchListener(null);
            try { Glide.With(this).Clear(_imageView); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FullscreenZoom] Glide.Clear error: {ex.Message}");
            }
            _imageView.SetImageDrawable(null);
            _imageView = null;
        }

        _progressBar = null;
        _zoomHandler = null;
        base.OnDestroyView();
    }

    private void AddCloseButton()
    {
        var density  = RequireContext().Resources?.DisplayMetrics?.Density ?? 1f;
        var btnPx    = (int)(40 * density);
        var marginPx = (int)(16 * density);
        var topPx    = (int)(44 * density); // folga para a status bar

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

    private void LoadImage()
    {
        if (_imageView is null) return;

        var options = BuildRequestOptions();
        AndroidImageLoader.LoadInto(
            _imageView,
            _source,
            options,
            new ZoomGlideRequestListener(
                onReady: OnImageReady,
                onFail:  HideProgress),
            _isUrl);
    }

    // Post() garante que a view ja esta layoutada antes de calcular a matrix
    private void OnImageReady()
    {
        _imageView?.Post(() =>
        {
            _zoomHandler?.InitMatrix();
            HideProgress();
        });
    }

    private RequestOptions BuildRequestOptions()
    {
        var metrics = RequireContext().Resources?.DisplayMetrics;
        var maxScreenPx = Math.Max(metrics?.WidthPixels ?? 0, metrics?.HeightPixels ?? 0);
        var decodePx = Math.Clamp(
            (int)(maxScreenPx * Math.Max(1f, Math.Min(_maxZoom, 3f))),
            720,
            4096);

        var options = new RequestOptions()
            .FitCenter()
            .Override(decodePx, decodePx)
            .DontAnimate();
        options.SetDiskCacheStrategy(DiskCacheStrategy.Automatic!);

        var placeholderId = AndroidImageLoader.ResolveDrawable(RequireContext(), _placeholder);
        if (placeholderId != 0)
            options = options.Clone().Placeholder(placeholderId).Error(placeholderId);

        return options;
    }

    private void HideProgress()
    {
        if (_progressBar is null) return;
        if (MainThread.IsMainThread)
            _progressBar.Visibility = ViewStates.Gone;
        else
            MainThread.BeginInvokeOnMainThread(
                () => _progressBar.Visibility = ViewStates.Gone);
    }

    private int ResolveDrawable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        return AndroidImageLoader.ResolveDrawable(RequireContext(), name);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ZoomTouchHandler
// Implementa pinch-to-zoom, pan e double-tap usando Matrix nativa do Android.
// Tecnica: Matrix transformation — a mesma usada no Maps e na galeria do Android.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ZoomTouchHandler
    : Java.Lang.Object,
      AndroidView.IOnTouchListener,
      ScaleGestureDetector.IOnScaleGestureListener
{
    // Indices das celulas na array float[9] retornada por Matrix.GetValues()
    // [ ScaleX  SkewX  TransX ]   0  1  2
    // [ SkewY   ScaleY TransY ]   3  4  5
    // [ Persp0  Persp1 Persp2 ]   6  7  8
    private const int IdxScaleX = 0;
    private const int IdxTransX = 2;
    private const int IdxTransY = 5;

    private const long AnimMs = 250L;

    private readonly global::Android.Widget.ImageView _imageView;
    private readonly ScaleGestureDetector             _scaleDetector;
    private readonly GestureDetector                  _gestureDetector;
    private readonly Action                           _onDismiss;
    private readonly Action<bool>?                    _onZoomStateChanged;
    private readonly Matrix                           _matrix = new();
    private readonly float                            _mediumScale;
    private readonly float                            _maxScale;

    private float _minScale;
    private float _currentScale;
    private float _lastX;
    private float _lastY;

    public float Scale => _currentScale;

    public ZoomTouchHandler(
        global::Android.Widget.ImageView imageView,
        float mediumScale,
        float maxScale,
        Action onDismiss,
        Action<bool>? onZoomStateChanged = null)
    {
        _imageView          = imageView;
        _mediumScale        = mediumScale;
        _maxScale           = maxScale;
        _onDismiss          = onDismiss;
        _onZoomStateChanged = onZoomStateChanged;
        _minScale           = 1f;
        _currentScale       = 1f;

        var ctx          = imageView.Context!;
        _scaleDetector   = new ScaleGestureDetector(ctx, this);
        _gestureDetector = new GestureDetector(ctx, new ZoomTapListener(this));
    }

    /// <summary>
    /// Calcula a matrix fit-center a partir das dimensoes reais.
    /// Deve ser chamado via Post() apos o layout estar completo.
    /// </summary>
    public void InitMatrix()
    {
        if (_imageView.Drawable is null) return;

        var viewW = (float)_imageView.Width;
        var viewH = (float)_imageView.Height;
        var imgW  = (float)_imageView.Drawable.IntrinsicWidth;
        var imgH  = (float)_imageView.Drawable.IntrinsicHeight;

        if (viewW <= 0 || viewH <= 0 || imgW <= 0 || imgH <= 0) return;

        var scale     = Math.Min(viewW / imgW, viewH / imgH); // fit-center
        _minScale     = scale;
        _currentScale = scale;

        _matrix.Reset();
        _matrix.PostScale(scale, scale);
        _matrix.PostTranslate(
            (viewW - imgW * scale) / 2f,
            (viewH - imgH * scale) / 2f);

        _imageView.ImageMatrix = _matrix;
        _onZoomStateChanged?.Invoke(false); // always at min scale after init
    }

    public void ResetZoom()
        => AnimateToMatrix(BuildFitMatrix(), _minScale);

    // ── IOnTouchListener ─────────────────────────────────────────────────

    public bool OnTouch(AndroidView? v, MotionEvent? e)
    {
        if (e is null) return false;

        _scaleDetector.OnTouchEvent(e);
        _gestureDetector.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _lastX = e.GetX(0);
                _lastY = e.GetY(0);
                break;

            case MotionEventActions.Move:
                // Pan: 1 dedo, fora de pinch ativo, com zoom aplicado
                if (!_scaleDetector.IsInProgress
                    && e.PointerCount == 1
                    && _currentScale > _minScale + 0.01f)
                {
                    _matrix.PostTranslate(e.GetX(0) - _lastX, e.GetY(0) - _lastY);
                    ConstrainTranslation();
                    _imageView.ImageMatrix = _matrix;
                }
                _lastX = e.GetX(0);
                _lastY = e.GetY(0);
                break;
        }

        return true;
    }

    // ── IOnScaleGestureListener — pinch-to-zoom ──────────────────────────

    public bool OnScale(ScaleGestureDetector detector)
    {
        var newScale  = Math.Clamp(_currentScale * detector.ScaleFactor, _minScale, _maxScale);
        var delta     = newScale / _currentScale;
        _currentScale = newScale;

        _matrix.PostScale(delta, delta, detector.FocusX, detector.FocusY);
        ConstrainTranslation();
        _imageView.ImageMatrix = _matrix;
        _onZoomStateChanged?.Invoke(_currentScale > _minScale + 0.01f);
        return true;
    }

    public bool OnScaleBegin(ScaleGestureDetector detector) => true;
    public void OnScaleEnd(ScaleGestureDetector detector)   { }

    // ── Chamados pelo ZoomTapListener ────────────────────────────────────

    internal void OnSingleTap()
    {
        if (_currentScale <= _minScale * 1.05f)
            _onDismiss();
        else
            ResetZoom();
    }

    internal void OnDoubleTap(float x, float y)
    {
        if (_currentScale > _minScale + 0.01f)
            AnimateToMatrix(BuildFitMatrix(), _minScale);
        else
            AnimateToMatrix(BuildZoomedMatrix(_mediumScale, x, y), _mediumScale);
    }

    // ── Logica de Matrix ─────────────────────────────────────────────────

    private void ConstrainTranslation()
    {
        if (_imageView.Drawable is null) return;
        ConstrainMatrix(
            _matrix, _currentScale,
            (float)_imageView.Width,
            (float)_imageView.Height,
            (float)_imageView.Drawable.IntrinsicWidth,
            (float)_imageView.Drawable.IntrinsicHeight);
    }

    // Clampa a translacao: imagem nunca deixa espaco vazio nas bordas
    private static void ConstrainMatrix(
        Matrix matrix, float scale,
        float viewW, float viewH,
        float imgW, float imgH)
    {
        var v  = new float[9];
        matrix.GetValues(v);

        var sw = imgW * scale;
        var sh = imgH * scale;

        v[IdxTransX] = sw <= viewW
            ? (viewW - sw) / 2f
            : Math.Clamp(v[IdxTransX], viewW - sw, 0f);

        v[IdxTransY] = sh <= viewH
            ? (viewH - sh) / 2f
            : Math.Clamp(v[IdxTransY], viewH - sh, 0f);

        matrix.SetValues(v);
    }

    private Matrix BuildFitMatrix()
    {
        var m = new Matrix();
        if (_imageView.Drawable is null) return m;

        var viewW = (float)_imageView.Width;
        var viewH = (float)_imageView.Height;
        var imgW  = (float)_imageView.Drawable.IntrinsicWidth;
        var imgH  = (float)_imageView.Drawable.IntrinsicHeight;

        m.PostScale(_minScale, _minScale);
        m.PostTranslate(
            (viewW - imgW * _minScale) / 2f,
            (viewH - imgH * _minScale) / 2f);
        return m;
    }

    private Matrix BuildZoomedMatrix(float targetScale, float focusX, float focusY)
    {
        if (_imageView.Drawable is null) return new Matrix(_matrix);

        var m     = new Matrix(_matrix);
        var delta = targetScale / _currentScale;
        m.PostScale(delta, delta, focusX, focusY);
        ConstrainMatrix(
            m, targetScale,
            (float)_imageView.Width,
            (float)_imageView.Height,
            (float)_imageView.Drawable.IntrinsicWidth,
            (float)_imageView.Drawable.IntrinsicHeight);
        return m;
    }

    // ── Animacao suave via lerp entre duas matrizes ───────────────────────

    private void AnimateToMatrix(Matrix target, float targetScale)
    {
        var startV = new float[9];
        var endV   = new float[9];
        _matrix.GetValues(startV);
        target.GetValues(endV);

        var anim = ValueAnimator.OfFloat(0f, 1f)!;
        anim.SetDuration(AnimMs);
        anim.SetInterpolator(new DecelerateInterpolator());

        anim.Update += (_, args) =>
        {
            var t = (float)(args.Animation?.AnimatedValue ?? 0f);
            var v = new float[9];
            for (var i = 0; i < 9; i++)
                v[i] = startV[i] + (endV[i] - startV[i]) * t;
            _currentScale = startV[IdxScaleX] + (endV[IdxScaleX] - startV[IdxScaleX]) * t;
            _matrix.SetValues(v);
            _imageView.ImageMatrix = _matrix;
        };

        anim.AnimationEnd += (_, _) =>
        {
            _currentScale = targetScale;
            _matrix.SetValues(endV);
            _imageView.ImageMatrix = _matrix;
            _onZoomStateChanged?.Invoke(_currentScale > _minScale + 0.01f);
        };

        anim.Start();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers internos — nomes prefixados com Zoom para evitar colisão no assembly
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ZoomTapListener : GestureDetector.SimpleOnGestureListener
{
    private readonly ZoomTouchHandler _handler;

    public ZoomTapListener(ZoomTouchHandler handler) => _handler = handler;

    // Dispara somente apos confirmar que nao e double-tap
    public override bool OnSingleTapConfirmed(MotionEvent? e)
    {
        _handler.OnSingleTap();
        return true;
    }

    public override bool OnDoubleTap(MotionEvent? e)
    {
        if (e is null) return false;
        _handler.OnDoubleTap(e.GetX(), e.GetY());
        return true;
    }
}

internal sealed class ZoomKeyCallback : Java.Lang.Object, IDialogInterfaceOnKeyListener
{
    private readonly Func<Keycode, bool> _handler;
    public ZoomKeyCallback(Func<Keycode, bool> handler) => _handler = handler;

    public bool OnKey(IDialogInterface? dialog, Keycode keyCode, KeyEvent? e)
    {
        if (e?.Action != KeyEventActions.Up) return false;
        return _handler(keyCode);
    }
}

internal sealed class ZoomGlideRequestListener
    : Java.Lang.Object, Bumptech.Glide.Request.IRequestListener
{
    private readonly Action _onReady;
    private readonly Action _onFail;

    public ZoomGlideRequestListener(Action onReady, Action onFail)
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
