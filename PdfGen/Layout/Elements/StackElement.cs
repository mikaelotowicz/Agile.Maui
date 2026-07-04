using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Sobrepõe filhos na mesma área (z-order na ordem de inserção).</summary>
public sealed class StackElement : Element
{
    readonly List<ILayoutElement> _children;

    public StackElement(List<ILayoutElement> children)
    {
        _children = children;
    }

    public override PdfSize Measure(PdfSize available)
    {
        float w = 0f;
        float h = 0f;
        foreach (ILayoutElement child in _children)
        {
            PdfSize size = child.Measure(available);
            if (size.Width > w)
                w = size.Width;
            if (size.Height > h)
                h = size.Height;
        }

        return new PdfSize(available.IsWidthConstrained ? available.Width : w, h);
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        foreach (ILayoutElement child in _children)
            child.Arrange(bounds);
    }

    public override void Render(IRenderContext context)
    {
        foreach (ILayoutElement child in _children)
            child.Render(context);
    }
}
