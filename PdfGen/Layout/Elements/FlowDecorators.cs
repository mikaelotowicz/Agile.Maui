using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

internal static class FlowDecorators
{
    public static FlowItem Decorate(FlowItem item, float outerWidth, Func<ILayoutElement, ILayoutElement> wrap)
    {
        ILayoutElement child = item.Element;
        if (item.LeftInset != 0f || item.Width > 0f)
            child = new FlowFragmentChildElement(item.Element, item.LeftInset, item.Width);

        return new FlowItem(wrap(child), item.Height, item.Kind, item.GroupId, width: outerWidth);
    }
}

internal sealed class FlowFragmentChildElement : Element
{
    readonly ILayoutElement _child;
    readonly float _leftInset;
    readonly float _width;

    public FlowFragmentChildElement(ILayoutElement child, float leftInset, float width)
    {
        _child = child;
        _leftInset = leftInset;
        _width = width;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float childWidth = _width > 0f ? _width : MathF.Max(0f, available.Width - _leftInset);
        return _child.Measure(new PdfSize(childWidth, available.Height));
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        float childWidth = _width > 0f ? _width : MathF.Max(0f, bounds.Width - _leftInset);
        _child.Arrange(new PdfRect(bounds.Left + _leftInset, bounds.Top, childWidth, bounds.Height));
    }

    public override void Render(IRenderContext context) => _child.Render(context);
}
