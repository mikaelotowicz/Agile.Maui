namespace Agile.Maui;

/// <summary>
/// Glifos (Material Design Icons) usados pelo <see cref="PdfReaderView"/>. A fonte é empacotada na
/// biblioteca como <c>AgilePdfIcons</c> e registrada por <c>builder.UseAgilePdfViewer()</c>.
/// </summary>
public static class PdfReaderIcons
{
    /// <summary>Nome (alias) da fonte registrada pela biblioteca.</summary>
    public const string FontFamily = "AgilePdfIcons";

    public const string Search    = "\U000f0349";   // magnify
    public const string Print     = "\U000f1786";   // printer-outline
    public const string Share     = "\U000f1514";   // share-variant-outline
    public const string Thumbnails = "\U000f11d9";  // view-grid-outline
    public const string Horizontal = "\U000f0b63";  // book-open-outline (ir para horizontal)
    public const string Vertical   = "\U000f148a";  // view-day-outline (ir para vertical)
    public const string ZoomIn    = "\U000f06ed";   // magnify-plus-outline
    public const string ZoomOut   = "\U000f06ec";   // magnify-minus-outline
    public const string Prev      = "\U000f0141";   // chevron-left
    public const string Next      = "\U000f0142";   // chevron-right
    public const string Up        = "\U000f0143";   // chevron-up
    public const string Down      = "\U000f0140";   // chevron-down
    public const string Close     = "\U000f0156";   // close
}
