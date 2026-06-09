namespace Agile.Maui;

/// <summary>
/// Continuous stroke from pointer down to pointer up, composed of several
/// <see cref="SignaturePoint"/> samples and the color used to draw it.
/// </summary>
public sealed class SignatureStroke
{
    public SignatureStroke(IReadOnlyList<SignaturePoint> points, Color color)
    {
        Points = points ?? Array.Empty<SignaturePoint>();
        Color = color;
    }

    /// <summary>Stroke samples in temporal order.</summary>
    public IReadOnlyList<SignaturePoint> Points { get; }

    /// <summary>Color used to draw the stroke.</summary>
    public Color Color { get; }

    /// <summary>Stroke duration in milliseconds, based on the first and last point timestamps.</summary>
    public double DurationMs =>
        Points.Count < 2 ? 0 : Points[^1].TimestampMs - Points[0].TimestampMs;
}
