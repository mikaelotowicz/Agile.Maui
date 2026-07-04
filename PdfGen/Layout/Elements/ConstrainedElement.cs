using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Impõe largura e/ou altura fixas ao filho.</summary>
public sealed class ConstrainedElement : SingleChildElement, IFlowContainer
{
    readonly float? _width;
    readonly float? _height;

    public ConstrainedElement(ILayoutElement? child, float? width, float? height) : base(child)
    {
        _width = width;
        _height = height;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float availW = _width ?? available.Width;
        float availH = _height ?? available.Height;
        PdfSize childSize = Child?.Measure(new PdfSize(availW, availH)) ?? PdfSize.Zero;

        float w = _width ?? childSize.Width;
        float h = _height ?? childSize.Height;
        return new PdfSize(w, h);
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        if (_height is not null)
        {
            float h = Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            yield return new FlowItem(this, h, width: _width ?? width);
            yield break;
        }

        float effectiveWidth = _width ?? width;
        if (Child is IFlowContainer flow)
        {
            foreach (FlowItem item in flow.Flatten(effectiveWidth))
                yield return item.Width > 0f ? item : new FlowItem(item.Element, item.Height, item.Kind, item.GroupId, item.LeftInset, effectiveWidth);
        }
        else if (Child is not null)
        {
            float h = Child.Measure(new PdfSize(effectiveWidth, PdfSize.Infinity)).Height;
            yield return new FlowItem(Child, h, width: effectiveWidth);
        }
    }
}
