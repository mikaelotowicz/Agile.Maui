using System.Runtime.CompilerServices;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Agile.Maui.Platforms.iOS;

/// <summary>
/// Connects native iOS/MacCatalyst touch capture, including pressure through
/// <see cref="UITouch.Force"/> and Apple Pencil, to the underlying <see cref="SignaturePad"/> GraphicsView.
/// </summary>
internal static class SignatureTouchInterop
{
    // Avoid attaching the recognizer more than once for the same platform view.
    // There is no explicit teardown: the mapper has no disconnect counterpart, the
    // recognizer dies with the platform view, and MAUI's handler disconnect severs
    // pad->handler so the native<->managed reference cycle stays collectable.
    private static readonly ConditionalWeakTable<UIView, SignatureGestureRecognizer> Attached = new();

    public static void Attach(UIView platformView, SignaturePad pad)
    {
        if (Attached.TryGetValue(platformView, out var existing))
        {
            // Handler reuse (e.g. CollectionView recycling) can reconnect the same
            // platform view to a new SignaturePad; retarget instead of keeping the old one.
            existing.SetPad(pad);
            return;
        }

        platformView.UserInteractionEnabled = true;

        var recognizer = new SignatureGestureRecognizer(pad)
        {
            CancelsTouchesInView = false,
        };
        Attached.Add(platformView, recognizer);
        platformView.AddGestureRecognizer(recognizer);
    }
}

internal sealed class SignatureGestureRecognizer : UIGestureRecognizer
{
    private SignaturePad _pad;

    // Strokes follow a single touch; extra fingers (e.g. a resting palm) are ignored,
    // otherwise each new TouchesBegan would discard the in-progress stroke.
    private UITouch? _trackedTouch;

    public SignatureGestureRecognizer(SignaturePad pad) => _pad = pad;

    public void SetPad(SignaturePad pad) => _pad = pad;

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        base.TouchesBegan(touches, evt);
        if (_trackedTouch != null)
            return;

        if (touches.AnyObject is UITouch touch)
        {
            _trackedTouch = touch;
            Emit(touch, _pad.OnTouchDown);
            State = UIGestureRecognizerState.Began;
        }
    }

    public override void TouchesMoved(NSSet touches, UIEvent evt)
    {
        base.TouchesMoved(touches, evt);
        var touch = FindTracked(touches);
        if (touch == null)
            return;

        // Replay coalesced touches so high-frequency input (Apple Pencil reports at
        // 120-240 Hz, while TouchesMoved fires at ~60 Hz) is captured faithfully instead
        // of dropping the in-between samples. Mirrors the historical-point replay on Android.
        var coalesced = evt.GetCoalescedTouches(touch);
        if (coalesced is { Length: > 0 })
        {
            foreach (var ct in coalesced)
                Emit(ct, _pad.OnTouchMove);
        }
        else
        {
            Emit(touch, _pad.OnTouchMove);
        }

        State = UIGestureRecognizerState.Changed;
    }

    public override void TouchesEnded(NSSet touches, UIEvent evt)
    {
        base.TouchesEnded(touches, evt);
        var touch = FindTracked(touches);
        if (touch == null)
            return;

        _trackedTouch = null;
        Emit(touch, _pad.OnTouchUp);
        State = UIGestureRecognizerState.Ended;
    }

    public override void TouchesCancelled(NSSet touches, UIEvent evt)
    {
        base.TouchesCancelled(touches, evt);
        if (FindTracked(touches) == null)
            return;

        _trackedTouch = null;
        _pad.OnTouchCancel();
        State = UIGestureRecognizerState.Cancelled;
    }

    private UITouch? FindTracked(NSSet touches)
    {
        var tracked = _trackedTouch;
        if (tracked == null)
            return null;

        // UIKit keeps the same UITouch instance alive for the whole contact, so the
        // native handle identifies the tracked finger across events.
        foreach (var obj in touches)
        {
            if (obj is UITouch touch && touch.Handle == tracked.Handle)
                return touch;
        }

        return null;
    }

    private void Emit(UITouch touch, Action<float, float, float, bool, double> sink)
    {
        CGPoint p = touch.LocationInView(View);

        var maxForce = touch.MaximumPossibleForce;
        var supported = maxForce > 0 || touch.Type == UITouchType.Stylus;
        var pressure = maxForce > 0 ? (float)(touch.Force / maxForce) : 0f;

        // UITouch.Timestamp is seconds since boot, converted to ms.
        sink((float)p.X, (float)p.Y, pressure, supported, touch.Timestamp * 1000.0);
    }
}
