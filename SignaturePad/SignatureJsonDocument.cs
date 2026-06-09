namespace Agile.Maui;

internal sealed class SignatureJsonDocument
{
    public int Version { get; set; }

    public double CanvasWidth { get; set; }

    public double CanvasHeight { get; set; }

    public List<SignatureJsonStroke> Strokes { get; set; } = new();
}

internal sealed class SignatureJsonStroke
{
    public string Color { get; set; } = "#FF000000";

    public List<SignatureJsonPoint> Points { get; set; } = new();
}

internal sealed class SignatureJsonPoint
{
    public float X { get; set; }

    public float Y { get; set; }

    public double TimestampMs { get; set; }

    public float Pressure { get; set; }

    public bool PressureSupported { get; set; }
}
