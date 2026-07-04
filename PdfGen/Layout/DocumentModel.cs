using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Layout;

/// <summary>Definição de uma seção de página (pode gerar várias páginas físicas via paginação).</summary>
public sealed class PageModel
{
    public PdfSize Size { get; set; } = PageSizes.A4;
    public Edges Margin { get; set; } = Edges.All(30f);
    public PdfColor? Background { get; set; }
    public ILayoutElement? Header { get; set; }
    public ILayoutElement? Content { get; set; }
    public ILayoutElement? Footer { get; set; }
}

/// <summary>Documento completo: seções de página + contexto compartilhado.</summary>
public sealed class DocumentModel
{
    public List<PageModel> Pages { get; } = new();
    public PageContext Context { get; } = new();
}
