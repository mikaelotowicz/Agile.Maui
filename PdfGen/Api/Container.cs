using System;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

/// <summary>Implementação do container fluente. Constrói a árvore de elementos preguiçosamente.</summary>
internal sealed class Container : IContainer
{
    readonly TextStyle _defaultStyle;
    readonly PageContext _pageContext;

    Container? _child;
    Func<ILayoutElement, ILayoutElement>? _wrap;
    Func<ILayoutElement>? _leaf;

    public Container(TextStyle defaultStyle, PageContext pageContext)
    {
        _defaultStyle = defaultStyle;
        _pageContext = pageContext;
    }

    IContainer Decorate(Func<ILayoutElement, ILayoutElement> wrap)
    {
        var child = new Container(_defaultStyle, _pageContext);
        _wrap = wrap;
        _child = child;
        return child;
    }

    public IContainer Padding(float all) => Decorate(inner => new PaddingElement(inner, Edges.All(all)));
    public IContainer Padding(float horizontal, float vertical) =>
        Decorate(inner => new PaddingElement(inner, Edges.Symmetric(horizontal, vertical)));
    public IContainer Padding(float left, float top, float right, float bottom) =>
        Decorate(inner => new PaddingElement(inner, new Edges(left, top, right, bottom)));

    public IContainer Background(PdfColor color, float cornerRadius = 0f) =>
        Decorate(inner => new BackgroundElement(inner, color, cornerRadius));
    public IContainer Background(GradientBrush brush, float cornerRadius = 0f) =>
        Decorate(inner => new GradientBackgroundElement(inner, brush, cornerRadius));
    public IContainer Border(float thickness, PdfColor color, float cornerRadius = 0f) =>
        Decorate(inner => new BorderElement(inner, thickness, color, cornerRadius));
    public IContainer Border(float thickness, GradientBrush brush, float cornerRadius = 0f) =>
        Decorate(inner => new GradientBorderElement(inner, thickness, brush, cornerRadius));

    public IContainer Width(float width) => Decorate(inner => new ConstrainedElement(inner, width, null));
    public IContainer Height(float height) => Decorate(inner => new ConstrainedElement(inner, null, height));

    public IContainer AlignLeft() => Decorate(inner => new AlignElement(inner, HorizontalAlignment.Left, null));
    public IContainer AlignCenter() => Decorate(inner => new AlignElement(inner, HorizontalAlignment.Center, null));
    public IContainer AlignRight() => Decorate(inner => new AlignElement(inner, HorizontalAlignment.Right, null));
    public IContainer AlignTop() => Decorate(inner => new AlignElement(inner, null, VerticalAlignment.Top));
    public IContainer AlignMiddle() => Decorate(inner => new AlignElement(inner, null, VerticalAlignment.Middle));
    public IContainer AlignBottom() => Decorate(inner => new AlignElement(inner, null, VerticalAlignment.Bottom));

    public ITextStyleDescriptor Text(string text)
    {
        var d = new TextBlockDescriptor(text, _defaultStyle);
        _leaf = d.Build;
        return d;
    }

    public ITextStyleDescriptor PageNumber(string format = "{0}")
    {
        var d = new PageNumberDescriptor(_pageContext, format, _defaultStyle);
        _leaf = d.Build;
        return d;
    }

    public void Image(PdfImage image, ImageFit fit = ImageFit.FitWidth, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        _leaf = () => new ImageElement(image, fit, align);
    }

    public void Image(byte[] data, ImageFit fit = ImageFit.FitWidth, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        PdfImage image = PdfImage.FromBytes(data);
        _leaf = () => new ImageElement(image, fit, align);
    }

    public void Column(Action<IColumnDescriptor> build)
    {
        var d = new ColumnDescriptor(_defaultStyle, _pageContext);
        build(d);
        _leaf = d.Build;
    }

    public void Row(Action<IRowDescriptor> build)
    {
        var d = new RowDescriptor(_defaultStyle, _pageContext);
        build(d);
        _leaf = d.Build;
    }

    public void Stack(Action<IStackDescriptor> build)
    {
        var d = new StackDescriptor(_defaultStyle, _pageContext);
        build(d);
        _leaf = d.Build;
    }

    public void Table(Action<ITableDescriptor> build)
    {
        var d = new TableDescriptor(_defaultStyle, _pageContext);
        build(d);
        _leaf = d.Build;
    }

    public void Element(ILayoutElement element) => _leaf = () => element;

    internal ILayoutElement Build()
    {
        if (_leaf is not null)
            return _leaf();
        if (_child is not null && _wrap is not null)
            return _wrap(_child.Build());
        return EmptyElement.Instance;
    }
}
