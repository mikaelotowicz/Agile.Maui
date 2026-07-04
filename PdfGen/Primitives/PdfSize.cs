namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Dimensão em pontos PDF.</summary>
public readonly struct PdfSize
{
    public readonly float Width;
    public readonly float Height;

    public PdfSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public static readonly PdfSize Zero = new(0f, 0f);

    /// <summary>Espaço "infinito" usado como disponível ao medir sem restrição.</summary>
    public const float Infinity = float.PositiveInfinity;

    public bool IsWidthConstrained => !float.IsPositiveInfinity(Width);
    public bool IsHeightConstrained => !float.IsPositiveInfinity(Height);

    public PdfSize WithWidth(float width) => new(width, Height);
    public PdfSize WithHeight(float height) => new(Width, height);

    public override string ToString() => $"{Width:0.##} x {Height:0.##}";
}
