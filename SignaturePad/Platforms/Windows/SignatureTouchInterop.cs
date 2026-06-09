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
    private static readonly ConditionalWeakTable<UIElement, SignaturePad> Attached = new();

    public static void Attach(UIElement platformView, SignaturePad pad)
    {
        if (Attached.TryGetValue(platformView, out _))
            return;

        Attached.Add(platformView, pad);

        platformView.PointerPressed += (s, e) => Handle(platformView, pad, e, Phase.Down);
        platformView.PointerMoved += (s, e) => Handle(platformView, pad, e, Phase.Move);
        platformView.PointerReleased += (s, e) => Handle(platformView, pad, e, Phase.Up);
        platformView.PointerCanceled += (s, e) => pad.OnTouchCancel();
        platformView.PointerCaptureLost += (s, e) => pad.OnTouchCancel();
    }

    private enum Phase { Down, Move, Up }

    private static void Handle(UIElement view, SignaturePad pad, PointerRoutedEventArgs e, Phase phase)
    {
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
                view.CapturePointer(e.Pointer);
                pad.OnTouchDown(x, y, pressure, supported, timestampMs);
                break;
            case Phase.Move:
                // Replay intermediate points coalesced between PointerMoved events so
                // high-frequency pen input is captured faithfully instead of dropping the
                // in-between samples. Mirrors the historical-point replay on Android.
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

                        pad.OnTouchMove(
                            (float)ip.Position.X,
                            (float)ip.Position.Y,
                            ip.Properties.Pressure,
                            supported,
                            ip.Timestamp / 1000.0);
                    }
                }
                else
                {
                    pad.OnTouchMove(x, y, pressure, supported, timestampMs);
                }
                break;
            case Phase.Up:
                pad.OnTouchUp(x, y, pressure, supported, timestampMs);
                view.ReleasePointerCapture(e.Pointer);
                break;
        }

        e.Handled = true;
    }
}
