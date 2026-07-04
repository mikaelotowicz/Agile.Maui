using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>
/// Renderiza o número da página corrente. O texto é resolvido em tempo de renderização a partir
/// do PageContext, então o mesmo elemento serve para todas as páginas.
/// </summary>
public sealed class PageNumberElement : Element
{
    readonly PageContext _context;
    readonly string _format;   // {0}=página atual, {1}=total
    readonly TextStyle _style;
    readonly TextAlign _align;

    public PageNumberElement(PageContext context, string format, TextStyle style, TextAlign align = TextAlign.Left)
    {
        _context = context;
        _format = format;
        _style = style;
        _align = align;
    }

    string CurrentText() => string.Format(_format, _context.PageNumber, _context.TotalPages);

    public override PdfSize Measure(PdfSize available)
    {
        float width = _style.MeasureWidth(CurrentText());
        return new PdfSize(available.IsWidthConstrained ? available.Width : width, _style.LineSpacing);
    }

    public override void Render(IRenderContext context)
    {
        string text = CurrentText();
        float w = _style.MeasureWidth(text);
        var line = new TextLine(text, w);
        SingleLineElement.DrawLine(context, line, _style, _align, Bounds.Left, Bounds.Top, Bounds.Width);
    }
}
