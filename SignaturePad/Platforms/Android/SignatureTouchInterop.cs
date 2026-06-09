using System.Runtime.CompilerServices;
using Android.Views;
using AView = Android.Views.View;

namespace Agile.Maui.Platforms.Android;

/// <summary>
/// Connects native Android touch capture, including pressure through
/// <see cref="MotionEvent.GetPressure"/>, to the underlying <see cref="SignaturePad"/> GraphicsView.
/// </summary>
internal static class SignatureTouchInterop
{
    // Avoid attaching the listener more than once for the same platform view.
    private static readonly ConditionalWeakTable<AView, SignatureTouchListener> Attached = new();

    public static void Attach(AView platformView, SignaturePad pad)
    {
        if (Attached.TryGetValue(platformView, out _))
            return;

        var listener = new SignatureTouchListener(pad, platformView);
        Attached.Add(platformView, listener);
        platformView.SetOnTouchListener(listener);
    }
}

// Do not use `file sealed class` here: Java-derived/implemented types generate XAJCW7024
// on Windows because Android JCW does not accept angle brackets in generated names.
internal sealed class SignatureTouchListener : Java.Lang.Object, AView.IOnTouchListener
{
    private readonly SignaturePad _pad;
    private readonly float _density;

    public SignatureTouchListener(SignaturePad pad, AView view)
    {
        _pad = pad;
        _density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (_density <= 0)
            _density = 1f;
    }

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (e is null)
            return false;

        var supported = e.GetToolType(0) is MotionEventToolType.Stylus or MotionEventToolType.Eraser;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                // Prevent ancestors such as ScrollView or CollectionView from intercepting
                // the gesture and stealing Move events.
                v?.Parent?.RequestDisallowInterceptTouchEvent(true);
                Emit(e, supported, _pad.OnTouchDown);
                return true;

            case MotionEventActions.Move:
                // Replay historical batched points for more faithful strokes.
                for (var h = 0; h < e.HistorySize; h++)
                {
                    var hx = e.GetHistoricalX(h) / _density;
                    var hy = e.GetHistoricalY(h) / _density;
                    var hp = e.GetHistoricalPressure(h);
                    _pad.OnTouchMove(hx, hy, hp, supported, e.GetHistoricalEventTime(h));
                }
                Emit(e, supported, _pad.OnTouchMove);
                return true;

            case MotionEventActions.Up:
                Emit(e, supported, _pad.OnTouchUp);
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                return true;

            case MotionEventActions.Cancel:
                _pad.OnTouchCancel();
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                return true;
        }

        return false;
    }

    private void Emit(MotionEvent e, bool supported,
        Action<float, float, float, bool, double> sink)
    {
        var x = e.GetX() / _density;
        var y = e.GetY() / _density;
        sink(x, y, e.GetPressure(0), supported, e.EventTime);
    }
}
