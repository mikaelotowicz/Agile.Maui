namespace Agile.Maui;

/// <summary>
/// Individual sample captured during a signature stroke.
/// Coordinates are device-independent units (DIP), relative to the pad.
/// </summary>
public readonly struct SignaturePoint
{
    public SignaturePoint(float x, float y, double timestampMs, float pressure, bool pressureSupported)
    {
        X = x;
        Y = y;
        TimestampMs = timestampMs;
        Pressure = pressure;
        PressureSupported = pressureSupported;
    }

    /// <summary>X coordinate in DIP, relative to the top-left corner of the pad.</summary>
    public float X { get; }

    /// <summary>Y coordinate in DIP, relative to the top-left corner of the pad.</summary>
    public float Y { get; }

    /// <summary>Milliseconds since the start of the first stroke in the signature.</summary>
    public double TimestampMs { get; }

    /// <summary>
    /// Normalized pressure from 0 to 1 reported by the platform when available.
    /// When <see cref="PressureSupported"/> is false, this contains a velocity-derived value.
    /// </summary>
    public float Pressure { get; }

    /// <summary>
    /// True when the platform/hardware reported real physical pressure for this sample,
    /// such as stylus or Apple Pencil input. False when pressure is derived from velocity.
    /// </summary>
    public bool PressureSupported { get; }
}
