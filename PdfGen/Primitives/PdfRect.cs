namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Retângulo em pontos PDF. Origem (Left, Top) no canto superior esquerdo, Y crescendo para baixo.</summary>
public readonly struct PdfRect
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Width;
    public readonly float Height;

    public PdfRect(float left, float top, float width, float height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public float Right => Left + Width;
    public float Bottom => Top + Height;
    public PdfPoint TopLeft => new(Left, Top);
    public PdfSize Size => new(Width, Height);

    public static readonly PdfRect Empty = new(0f, 0f, 0f, 0f);

    public PdfRect Deflate(Edges edges) => new(
        Left + edges.Left,
        Top + edges.Top,
        MathF.Max(0f, Width - edges.Left - edges.Right),
        MathF.Max(0f, Height - edges.Top - edges.Bottom));

    public PdfRect Offset(float dx, float dy) => new(Left + dx, Top + dy, Width, Height);

    public override string ToString() => $"[{Left:0.##}, {Top:0.##}, {Width:0.##} x {Height:0.##}]";
}
