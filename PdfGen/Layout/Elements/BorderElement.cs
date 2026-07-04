using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Desenha uma borda uniforme ao redor do filho, com cantos arredondados opcionais.</summary>
public sealed class BorderElement : SingleChildElement, IFlowContainer
{
    readonly float _thickness;
    readonly PdfColor _color;
    readonly float _cornerRadius;

    public BorderElement(ILayoutElement? child, float thickness, PdfColor color, float cornerRadius = 0f) : base(child)
    {
        _thickness = thickness;
        _color = color;
        _cornerRadius = cornerRadius;
    }

    public override void Render(IRenderContext context)
    {
        base.Render(context);
        if (_thickness > 0f && !_color.IsTransparent)
        {
            // Contorno desenhado sobre a meia-espessura para ficar dentro dos bounds.
            float half = _thickness / 2f;
            var inset = new PdfRect(
                Bounds.Left + half, Bounds.Top + half,
                MathF.Max(0f, Bounds.Width - _thickness), MathF.Max(0f, Bounds.Height - _thickness));
            context.DrawRectangle(inset, _color, _thickness, _cornerRadius);
        }
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        if (Child is IFlowContainer flow)
        {
            foreach (FlowItem item in flow.Flatten(width))
                yield return FlowDecorators.Decorate(item, width, child => new BorderElement(child, _thickness, _color, _cornerRadius));
        }
        else if (Child is not null)
        {
            float h = Child.Measure(new PdfSize(width, PdfSize.Infinity)).Height;
            yield return new FlowItem(this, h, width: width);
        }
    }
}
