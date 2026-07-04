using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Preenche o fundo da área do filho, com cantos arredondados opcionais.</summary>
public sealed class BackgroundElement : SingleChildElement, IFlowContainer
{
    readonly PdfColor _color;
    readonly float _cornerRadius;

    public BackgroundElement(ILayoutElement? child, PdfColor color, float cornerRadius = 0f) : base(child)
    {
        _color = color;
        _cornerRadius = cornerRadius;
    }

    public override void Render(IRenderContext context)
    {
        if (!_color.IsTransparent)
            context.FillRectangle(Bounds, _color, _cornerRadius);
        base.Render(context);
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        if (Child is IFlowContainer flow)
        {
            foreach (FlowItem item in flow.Flatten(width))
                yield return FlowDecorators.Decorate(item, width, child => new BackgroundElement(child, _color, _cornerRadius));
        }
        else if (Child is not null)
        {
            float h = Child.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            yield return new FlowItem(this, h, width: width);
        }
    }
}
