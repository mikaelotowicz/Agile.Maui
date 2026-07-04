using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Base para elementos que envolvem exatamente um filho.</summary>
public abstract class SingleChildElement : Element
{
    protected ILayoutElement? Child { get; }

    protected SingleChildElement(ILayoutElement? child)
    {
        Child = child;
    }

    public override PdfSize Measure(PdfSize available) =>
        Child?.Measure(available) ?? PdfSize.Zero;

    protected override void ArrangeCore(PdfRect bounds) =>
        Child?.Arrange(bounds);

    public override void Render(IRenderContext context) =>
        Child?.Render(context);
}
