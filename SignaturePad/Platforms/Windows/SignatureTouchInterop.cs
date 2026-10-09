using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Agile.Maui.Platforms.Windows;

/// <summary>
/// Connects native Windows pointer capture, including pressure through
/// <c>PointerPoint.Properties.Pressure</c> for pen input, to the underlying
/// <see cref="SignaturePad"/> GraphicsView.
/// </summary>
internal static class SignatureTouchInterop
{
    // Avoid attaching the handlers more than once for the same platform view.
    // There is no explicit teardown: the mapper has no disconnect counterpart and
    // the subscriptions die with the platform view.
    private static readonly ConditionalWeakTable<UIElement, PointerState> Attached = new();

    public static void Attach(UIElement platformView, SignaturePad pad)
    {
        if (Attached.TryGetValue(platformView, out var existing))
        {
            // Handler reuse (e.g. CollectionView recycling) can reconnect the same
            // platform view to a new SignaturePad; retarget instead of keeping the old one.
            existing.Pad = pad;
            return;
        }

        var state = new PointerState(pad);
        Attached.Add(platformView, state);

        platformView.PointerPressed += (s, e) => Handle(platformView, state, e, Phase.Down);
        platformView.PointerMoved += (s, e) => Handle(platformView, state, e, Phase.Move);
        platformView.PointerReleased += (s, e) => Handle(platformView, state, e, Phase.Up);
        platformView.PointerCanceled += (s, e) => state.Cancel(e.Pointer.PointerId);
        platformView.PointerCaptureLost += (s, e) => state.Cancel(e.Pointer.PointerId);
    }

    private enum Phase { Down, Move, Up }

    // Mutable holder so handler reuse can retarget the pad, plus single-pointer tracking.
    private sealed class PointerState
    {
        public PointerState(SignaturePad pad) => Pad = pad;

        public SignaturePad Pad { get; set; }

        public uint? ActivePointerId { get; set; }

        public void Cancel(uint pointerId)
        {
            if (ActivePointerId != pointerId)
                return;

            ActivePointerId = null;
            Pad.OnTouchCancel();
        }
    }

    private static void Handle(UIElement view, PointerState state, PointerRoutedEventArgs e, Phase phase)
    {
        // Strokes follow a single pointer: a second finger or resting palm would
        // otherwise reset the stroke or interleave its coordinates.
        var pointerId = e.Pointer.PointerId;
        if (phase == Phase.Down)
        {
            if (state.ActivePointerId != null)
                return;
        }
        else if (state.ActivePointerId != pointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(view);

        // Process Move only while a button/contact is active to avoid mouse/pen hover.
        if (phase == Phase.Move && !point.IsInContact)
            return;

        var supported = e.Pointer.PointerDeviceType == PointerDeviceType.Pen;
        var pressure = point.Properties.Pressure; // 0..1
        var timestampMs = point.Timestamp / 1000.0; // microseconds -> ms
        var x = (float)point.Position.X;
        var y = (float)point.Position.Y;

        switch (phase)
        {
            case Phase.Down:
                // Draw only with primary contact: ignore right/middle mouse buttons,
                // the pen barrel button and the inverted pen/eraser.
                var props = point.Properties;
                if (props.IsRightButtonPressed || props.IsMiddleButtonPressed
                    || props.IsBarrelButtonPressed || props.IsEraser)
                    return;

                // Touch panning runs via direct manipulation, so an ancestor ScrollViewer
                // would take the pointer mid-stroke (PointerCaptureLost); opt out first.
                if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
                    view.CancelDirectManipulations();

                view.CapturePointer(e.Pointer);
                state.ActivePointerId = pointerId;
                state.Pad.OnTouchDown(x, y, pressure, supported, timestampMs);
                break;
            case Phase.Move:
                // Replay intermediate points coalesced between PointerMoved events so
                // high-frequency pen input is captured faithfully instead of dropping the
                // in-between samples. Mirrors the historical-point replay on Android,
                // invalidating only once per native event.
                // GetIntermediatePoints returns newest-first, so iterate in reverse for
                // chronological order; it includes the current point.
                var intermediates = e.GetIntermediatePoints(view);
                if (intermediates is { Count: > 0 })
                {
                    for (var i = intermediates.Count - 1; i >= 0; i--)
                    {
                        var ip = intermediates[i];
                        if (!ip.IsInContact)
                            continue;

                        state.Pad.OnTouchMove(
                            (float)ip.Position.X,
                            (float)ip.Position.Y,
                            ip.Properties.Pressure,
                            supported,
                            ip.Timestamp / 1000.0,
                            invalidate: false);
                    }

                    state.Pad.Invalidate();
                }
                else
                {
                    state.Pad.OnTouchMove(x, y, pressure, supported, timestampMs);
                }
                break;
            case Phase.Up:
                // Clear the id before releasing capture so the resulting
                // PointerCaptureLost does not cancel the just-committed stroke.
                state.ActivePointerId = null;
                state.Pad.OnTouchUp(x, y, pressure, supported, timestampMs);
                view.ReleasePointerCapture(e.Pointer);
                break;
        }

        e.Handled = true;
    }
}
