using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Empilha filhos verticalmente com espaçamento opcional. Paginável (fatiável em fluxo).</summary>
public sealed class ColumnElement : Element, IFlowContainer
{
    readonly List<ILayoutElement> _children;
    readonly float _spacing;
    float[] _heights = System.Array.Empty<float>();

    public ColumnElement(List<ILayoutElement> children, float spacing)
    {
        _children = children;
        _spacing = spacing;
    }

    public override PdfSize Measure(PdfSize available)
    {
        _heights = new float[_children.Count];
        float width = 0f;
        float height = 0f;

        for (int i = 0; i < _children.Count; i++)
        {
            PdfSize childAvail = new(available.Width, PdfSize.Infinity);
            PdfSize size = _children[i].Measure(childAvail);
            _heights[i] = size.Height;
            if (size.Width > width)
                width = size.Width;
            height += size.Height;
            if (i < _children.Count - 1)
                height += _spacing;
        }

        float finalWidth = available.IsWidthConstrained ? available.Width : width;
        return new PdfSize(finalWidth, height);
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        if (_heights.Length != _children.Count)
            Measure(new PdfSize(bounds.Width, PdfSize.Infinity));

        float y = bounds.Top;
        for (int i = 0; i < _children.Count; i++)
        {
            _children[i].Arrange(new PdfRect(bounds.Left, y, bounds.Width, _heights[i]));
            y += _heights[i] + _spacing;
        }
    }

    public override void Render(IRenderContext context)
    {
        foreach (ILayoutElement child in _children)
            child.Render(context);
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        for (int i = 0; i < _children.Count; i++)
        {
            ILayoutElement child = _children[i];

            if (child is IFlowContainer flow)
            {
                foreach (FlowItem item in flow.Flatten(width))
                    yield return item;
            }
            else
            {
                float h = child.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
                yield return new FlowItem(child, h, width: width);
            }

            if (_spacing > 0f && i < _children.Count - 1)
                yield return new FlowItem(new SpacerElement(_spacing), _spacing);
        }
    }
}
