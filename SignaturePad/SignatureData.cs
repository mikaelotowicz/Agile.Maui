namespace Agile.Maui;

/// <summary>
/// Complete biometric-style signature snapshot containing strokes, timing, geometry,
/// and physical pressure when available.
/// </summary>
public sealed class SignatureData
{
    public SignatureData(IReadOnlyList<SignatureStroke> strokes, Size canvasSize)
    {
        Strokes = strokes ?? Array.Empty<SignatureStroke>();
        CanvasSize = canvasSize;
    }

    /// <summary>Strokes in drawing order.</summary>
    public IReadOnlyList<SignatureStroke> Strokes { get; }

    /// <summary>Signature pad size in DIP at capture time, useful for coordinate normalization.</summary>
    public Size CanvasSize { get; }

    /// <summary>Total sample count across all strokes.</summary>
    public int TotalPoints
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Strokes.Count; i++)
                count += Strokes[i].Points.Count;
            return count;
        }
    }

    /// <summary>Total elapsed time between the first and last sample, in milliseconds.</summary>
    public double TotalDurationMs
    {
        get
        {
            if (Strokes.Count == 0)
                return 0;

            var first = Strokes[0].Points;
            var last = Strokes[^1].Points;
            if (first.Count == 0 || last.Count == 0)
                return 0;

            return last[^1].TimestampMs - first[0].TimestampMs;
        }
    }

    /// <summary>True when at least one sample reported real physical pressure from hardware.</summary>
    public bool HasRealPressure
    {
        get
        {
            for (var i = 0; i < Strokes.Count; i++)
            {
                var points = Strokes[i].Points;
                for (var j = 0; j < points.Count; j++)
                    if (points[j].PressureSupported)
                        return true;
            }
            return false;
        }
    }
}
