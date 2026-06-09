using Microsoft.Maui.Graphics;

namespace Agile.Maui;

/// <summary>
/// In-progress or completed render stroke: biometric-style samples plus calculated width per sample.
/// </summary>
internal sealed class RenderStroke
{
    public RenderStroke(Color color)
    {
        Color = color;
    }

    public List<SignaturePoint> Points { get; } = new();
    public List<float> Widths { get; } = new();
    public Color Color { get; }

    public SignatureStroke ToPublic() => new(Points.ToArray(), Color);
}

/// <summary>
/// Draws variable-width strokes using quadratic curves between segment midpoints,
/// with signature_pad-style smoothing, plus the guide line and empty prompt.
/// </summary>
internal sealed class SignaturePadDrawable : IDrawable
{
    private readonly SignaturePad _owner;

    public SignaturePadDrawable(SignaturePad owner) => _owner = owner;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // Guides are only shown on screen and are never exported.
        if (_owner.IsEmpty && !_owner.HasActiveStroke)
            DrawGuides(canvas, dirtyRect);

        DrawStrokes(canvas, strokeColorOverride: null);
    }

    /// <summary>Draws only strokes, without guides. Also used for image export.</summary>
    public void DrawStrokes(ICanvas canvas, Color? strokeColorOverride) =>
        DrawStrokes(canvas, _owner.AllStrokesForRender, strokeColorOverride);

    /// <summary>
    /// Draws a stroke list without guides. Static so export can run on a background
    /// thread from an immutable snapshot without touching control state.
    /// </summary>
    public static void DrawStrokes(ICanvas canvas, IReadOnlyList<RenderStroke> strokes, Color? strokeColorOverride)
    {
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.Antialias = true;

        for (var i = 0; i < strokes.Count; i++)
            DrawStroke(canvas, strokes[i], strokeColorOverride);
    }

    private static void DrawStroke(ICanvas canvas, RenderStroke stroke, Color? overrideColor)
    {
        var pts = stroke.Points;
        var widths = stroke.Widths;
        var n = pts.Count;
        if (n == 0)
            return;

        var color = overrideColor ?? stroke.Color;
        canvas.StrokeColor = color;
        canvas.FillColor = color;

        if (n == 1)
        {
            var r = Math.Max(widths[0], 0.5f) / 2f;
            canvas.FillCircle(pts[0].X, pts[0].Y, r);
            return;
        }

        if (n == 2)
        {
            canvas.StrokeSize = (widths[0] + widths[1]) / 2f;
            canvas.DrawLine(pts[0].X, pts[0].Y, pts[1].X, pts[1].Y);
            return;
        }

        // Initial cap: first point to the first segment midpoint.
        canvas.StrokeSize = Math.Max(widths[0], 0.5f);
        var firstMid = Mid(pts[0], pts[1]);
        canvas.DrawLine(pts[0].X, pts[0].Y, firstMid.X, firstMid.Y);

        // Middle section: quadratic curve from mid(i-1,i) to pts[i] to mid(i,i+1).
        for (var i = 1; i < n - 1; i++)
        {
            var m1 = Mid(pts[i - 1], pts[i]);
            var m2 = Mid(pts[i], pts[i + 1]);

            var path = new PathF();
            path.MoveTo(m1.X, m1.Y);
            path.QuadTo(pts[i].X, pts[i].Y, m2.X, m2.Y);

            canvas.StrokeSize = Math.Max(widths[i], 0.5f);
            canvas.DrawPath(path);
        }

        // Final cap: last segment midpoint to the last point.
        var lastMid = Mid(pts[n - 2], pts[n - 1]);
        canvas.StrokeSize = Math.Max(widths[n - 1], 0.5f);
        canvas.DrawLine(lastMid.X, lastMid.Y, pts[n - 1].X, pts[n - 1].Y);
    }

    private void DrawGuides(ICanvas canvas, RectF rect)
    {
        if (_owner.ShowSignatureLine && rect.Height > 0)
        {
            var y = rect.Height * 0.82f;
            var left = rect.Left + 24f;
            var right = rect.Right - 24f;

            canvas.StrokeColor = _owner.SignatureLineColor;
            canvas.StrokeSize = 1f;
            canvas.StrokeDashPattern = null;
            canvas.DrawLine(left, y, right, y);

            // Marks the left side of the line with X.
            canvas.FontColor = _owner.SignatureLineColor;
            canvas.FontSize = 14f;
            canvas.DrawString("X", left, y - 20f, 20f, 16f,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        if (!string.IsNullOrEmpty(_owner.PromptText))
        {
            canvas.FontColor = _owner.PromptTextColor;
            canvas.FontSize = 16f;
            canvas.DrawString(_owner.PromptText, rect.Left, rect.Top, rect.Width, rect.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private static PointF Mid(SignaturePoint a, SignaturePoint b) =>
        new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
}
