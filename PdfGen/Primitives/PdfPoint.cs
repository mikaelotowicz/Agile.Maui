namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Ponto em pontos PDF (1 pt = 1/72"). Origem no canto superior esquerdo.</summary>
public readonly struct PdfPoint
{
    public readonly float X;
    public readonly float Y;

    public PdfPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public PdfPoint Offset(float dx, float dy) => new(X + dx, Y + dy);

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}
