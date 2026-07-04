namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Espessura por lado (margem, padding, borda). Em pontos PDF.</summary>
public readonly struct Edges
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Right;
    public readonly float Bottom;

    public Edges(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static readonly Edges Zero = new(0f, 0f, 0f, 0f);

    public static Edges All(float value) => new(value, value, value, value);

    public static Edges Symmetric(float horizontal, float vertical) =>
        new(horizontal, vertical, horizontal, vertical);

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public Edges WithLeft(float v) => new(v, Top, Right, Bottom);
    public Edges WithTop(float v) => new(Left, v, Right, Bottom);
    public Edges WithRight(float v) => new(Left, Top, v, Bottom);
    public Edges WithBottom(float v) => new(Left, Top, Right, v);

    public override string ToString() => $"L{Left:0.#} T{Top:0.#} R{Right:0.#} B{Bottom:0.#}";
}
