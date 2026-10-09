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
    // There is no explicit teardown: the mapper has no disconnect counterpart, the
    // listener dies with the platform view, and MAUI's handler disconnect severs
    // pad->handler so the peer graph stays collectable.
    private static readonly ConditionalWeakTable<AView, SignatureTouchListener> Attached = new();

    public static void Attach(AView platformView, SignaturePad pad)
    {
        if (Attached.TryGetValue(platformView, out var existing))
        {
            // Handler reuse (e.g. CollectionView recycling) can reconnect the same
            // platform view to a new SignaturePad; retarget instead of keeping the old one.
            existing.SetPad(pad);
            return;
        }

        var listener = new SignatureTouchListener(pad, platformView);
        Attached.Add(platformView, listener);
        platformView.SetOnTouchListener(listener);
    }
}

// Do not use `file sealed class` here: Java-derived/implemented types generate XAJCW7024
// on Windows because Android JCW does not accept angle brackets in generated names.
internal sealed class SignatureTouchListener : Java.Lang.Object, AView.IOnTouchListener
{
    private const int InvalidPointerId = -1;

    private SignaturePad _pad;
    private readonly float _density;

    // Strokes follow a single pointer; indices are resolved from this id because
    // pointer indices are reshuffled when another pointer goes down or up.
    private int _activePointerId = InvalidPointerId;

    public SignatureTouchListener(SignaturePad pad, AView view)
    {
        _pad = pad;
        _density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (_density <= 0)
            _density = 1f;
    }

    public void SetPad(SignaturePad pad) => _pad = pad;

    public bool OnTouch(AView? v, MotionEvent? e)
    {
        if (e is null)
            return false;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                // Prevent ancestors such as ScrollView or CollectionView from intercepting
                // the gesture and stealing Move events.
                v?.Parent?.RequestDisallowInterceptTouchEvent(true);
                _activePointerId = e.GetPointerId(0);
                Emit(e, 0, _pad.OnTouchDown);
                return true;

            case MotionEventActions.PointerDown:
                // Extra pointers (second finger, resting palm) never join the stroke.
                return true;

            case MotionEventActions.Move:
            {
                var index = e.FindPointerIndex(_activePointerId);
                if (index < 0)
                    return true;

                // Replay historical batched points for more faithful strokes.
                var supported = IsStylus(e, index);
                for (var h = 0; h < e.HistorySize; h++)
                {
                    var hx = e.GetHistoricalX(index, h) / _density;
                    var hy = e.GetHistoricalY(index, h) / _density;
                    var hp = e.GetHistoricalPressure(index, h);
                    _pad.OnTouchMove(hx, hy, hp, supported, e.GetHistoricalEventTime(h));
                }
                Emit(e, index, _pad.OnTouchMove);
                return true;
            }

            case MotionEventActions.PointerUp:
                // Finish the stroke when the tracked pointer lifts even if others remain.
                if (e.GetPointerId(e.ActionIndex) == _activePointerId)
                {
                    Emit(e, e.ActionIndex, _pad.OnTouchUp);
                    _activePointerId = InvalidPointerId;
                }
                return true;

            case MotionEventActions.Up:
            {
                var index = e.FindPointerIndex(_activePointerId);
                if (index >= 0)
                    Emit(e, index, _pad.OnTouchUp);
                _activePointerId = InvalidPointerId;
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                return true;
            }

            case MotionEventActions.Cancel:
                _activePointerId = InvalidPointerId;
                _pad.OnTouchCancel();
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                return true;
        }

        return false;
    }

    private void Emit(MotionEvent e, int pointerIndex,
        Action<float, float, float, bool, double> sink)
    {
        var x = e.GetX(pointerIndex) / _density;
        var y = e.GetY(pointerIndex) / _density;
        sink(x, y, e.GetPressure(pointerIndex), IsStylus(e, pointerIndex), e.EventTime);
    }

    private static bool IsStylus(MotionEvent e, int pointerIndex) =>
        e.GetToolType(pointerIndex) is MotionEventToolType.Stylus or MotionEventToolType.Eraser;
}
