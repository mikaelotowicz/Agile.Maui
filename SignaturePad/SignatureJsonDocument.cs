using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// Source-generated System.Text.Json contract. Reflection-based serialization is
/// disabled by default in trimmed/AOT published apps (always the case on
/// iOS/MacCatalyst), so the signature JSON APIs must not depend on it.
/// </summary>
[JsonSerializable(typeof(SignatureJsonDocument))]
internal partial class SignatureJsonContext : JsonSerializerContext
{
    /// <summary>Shared context variant that writes indented JSON.</summary>
    public static SignatureJsonContext Indented { get; } =
        new(new JsonSerializerOptions { WriteIndented = true });
}
