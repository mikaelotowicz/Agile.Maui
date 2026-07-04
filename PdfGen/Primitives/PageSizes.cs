namespace Agile.Maui.PdfGen.Primitives;

/// <summary>Tamanhos de página em pontos PDF (1 pt = 1/72"). Retrato por padrão.</summary>
public static class PageSizes
{
    public static PdfSize A4 => new(595f, 842f);
    public static PdfSize A5 => new(420f, 595f);
    public static PdfSize A3 => new(842f, 1191f);
    public static PdfSize Letter => new(612f, 792f);
    public static PdfSize Legal => new(612f, 1008f);

    /// <summary>Gira para paisagem (troca largura/altura).</summary>
    public static PdfSize Landscape(this PdfSize size) => new(size.Height, size.Width);
}
