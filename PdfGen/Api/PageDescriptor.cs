using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

/// <summary>Configuração fluente de uma seção de página.</summary>
public interface IPageDescriptor
{
    void Size(PdfSize size);
    void Size(float width, float height);
    void Margin(float all);
    void Margin(float horizontal, float vertical);
    void Margin(float left, float top, float right, float bottom);
    void BackgroundColor(PdfColor color);
    /// <summary>Estilo de texto herdado pelos blocos desta página.</summary>
    void DefaultTextStyle(TextStyle style);

    IContainer Header();
    IContainer Content();
    IContainer Footer();
}

internal sealed class PageDescriptor : IPageDescriptor
{
    readonly PageContext _ctx;
    TextStyle _defaultStyle = TextStyle.Default;

    PdfSize _size = PageSizes.A4;
    Edges _margin = Edges.All(30f);
    PdfColor? _background;

    Container? _header;
    Container? _content;
    Container? _footer;

    public PageDescriptor(PageContext ctx)
    {
        _ctx = ctx;
    }

    public void Size(PdfSize size) => _size = size;
    public void Size(float width, float height) => _size = new PdfSize(width, height);
    public void Margin(float all) => _margin = Edges.All(all);
    public void Margin(float horizontal, float vertical) => _margin = Edges.Symmetric(horizontal, vertical);
    public void Margin(float left, float top, float right, float bottom) => _margin = new Edges(left, top, right, bottom);
    public void BackgroundColor(PdfColor color) => _background = color;
    public void DefaultTextStyle(TextStyle style) => _defaultStyle = style;

    public IContainer Header() => _header ??= new Container(_defaultStyle, _ctx);
    public IContainer Content() => _content ??= new Container(_defaultStyle, _ctx);
    public IContainer Footer() => _footer ??= new Container(_defaultStyle, _ctx);

    internal PageModel Build() => new()
    {
        Size = _size,
        Margin = _margin,
        Background = _background,
        Header = _header?.Build(),
        Content = _content?.Build(),
        Footer = _footer?.Build(),
    };
}
