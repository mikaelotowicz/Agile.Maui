namespace Agile.Maui.PdfGen.Layout;

/// <summary>
/// Estado da página corrente, compartilhado pelo motor com os elementos que dependem dele
/// (ex.: número de página). Mutável: o motor atualiza antes de renderizar cada página física.
/// </summary>
public sealed class PageContext
{
    public int PageNumber { get; internal set; } = 1;
    public int TotalPages { get; internal set; } = 1;
}
