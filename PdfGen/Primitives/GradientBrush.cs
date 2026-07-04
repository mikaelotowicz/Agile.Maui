using System.Collections.Generic;

namespace Agile.Maui.PdfGen.Primitives;

public enum GradientKind
{
    /// <summary>Interpolação ao longo de um eixo (definido por um ângulo).</summary>
    Linear,
    /// <summary>Interpolação radial do centro para as bordas.</summary>
    Radial
}

/// <summary>Uma parada de cor do gradiente: posição em [0,1] e a cor naquele ponto.</summary>
public readonly struct GradientStop
{
    public readonly float Offset;
    public readonly PdfColor Color;

    public GradientStop(float offset, PdfColor color)
    {
        Offset = offset < 0f ? 0f : (offset > 1f ? 1f : offset);
        Color = color;
    }
}

/// <summary>
/// Preenchimento em gradiente independente de plataforma. No escritor gerenciado vira um shading
/// pattern PDF; nos renderers nativos que não o suportam, degrada para a cor da primeira parada.
/// </summary>
public sealed class GradientBrush
{
    public GradientKind Kind { get; }
    /// <summary>Ângulo do gradiente linear em graus (0 = esquerda→direita, 90 = cima→baixo).</summary>
    public float AngleDegrees { get; }
    public IReadOnlyList<GradientStop> Stops { get; }

    public GradientBrush(GradientKind kind, float angleDegrees, IReadOnlyList<GradientStop> stops)
    {
        if (stops is null || stops.Count < 2)
            throw new System.ArgumentException("Um gradiente precisa de ao menos duas paradas de cor.", nameof(stops));
        Kind = kind;
        AngleDegrees = angleDegrees;
        Stops = stops;
    }

    /// <summary>Cor usada quando o backend não suporta gradiente (primeira parada).</summary>
    public PdfColor FallbackColor => Stops[0].Color;

    /// <summary>Gradiente linear entre duas cores no ângulo informado (graus).</summary>
    public static GradientBrush Linear(PdfColor from, PdfColor to, float angleDegrees = 0f) =>
        new(GradientKind.Linear, angleDegrees, new[] { new GradientStop(0f, from), new GradientStop(1f, to) });

    /// <summary>Gradiente linear com paradas arbitrárias.</summary>
    public static GradientBrush Linear(float angleDegrees, params GradientStop[] stops) =>
        new(GradientKind.Linear, angleDegrees, stops);

    /// <summary>Gradiente radial do centro (primeira cor) para a borda (última cor).</summary>
    public static GradientBrush Radial(PdfColor center, PdfColor edge) =>
        new(GradientKind.Radial, 0f, new[] { new GradientStop(0f, center), new GradientStop(1f, edge) });

    /// <summary>Gradiente radial com paradas arbitrárias.</summary>
    public static GradientBrush Radial(params GradientStop[] stops) =>
        new(GradientKind.Radial, 0f, stops);
}
