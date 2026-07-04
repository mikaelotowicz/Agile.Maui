namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Cores comuns pré-definidas.</summary>
public static class Colors
{
    public static PdfColor Transparent => new(0, 0, 0, 0);
    public static PdfColor Black => new(0, 0, 0);
    public static PdfColor White => new(255, 255, 255);
    public static PdfColor Red => new(220, 53, 69);
    public static PdfColor Green => new(40, 167, 69);
    public static PdfColor Blue => new(0, 123, 255);
    public static PdfColor Yellow => new(255, 193, 7);
    public static PdfColor Orange => new(253, 126, 20);
    public static PdfColor Gray => new(108, 117, 125);
    public static PdfColor LightGray => new(222, 226, 230);
    public static PdfColor DarkGray => new(52, 58, 64);
}
