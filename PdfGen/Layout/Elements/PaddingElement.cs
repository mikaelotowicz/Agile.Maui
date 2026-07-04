using System.Collections.Generic;
using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>
/// Adiciona espaçamento interno ao redor do filho. Transparente ao fluxo: se o filho é paginável,
/// o padding vertical vira espaçadores e o padding horizontal desloca os itens — assim
/// "Content().Padding(x).Column(...)" continua quebrando entre páginas.
/// </summary>
public sealed class PaddingElement : SingleChildElement, IFlowContainer
{
    readonly Edges _padding;

    public PaddingElement(ILayoutElement? child, Edges padding) : base(child)
    {
        _padding = padding;
    }

    public IEnumerable<FlowItem> Flatten(float width)
    {
        float innerWidth = MathF.Max(0f, width - _padding.Horizontal);

        if (_padding.Top > 0f)
            yield return new FlowItem(new SpacerElement(_padding.Top), _padding.Top);

        if (Child is IFlowContainer flow)
        {
            foreach (FlowItem item in flow.Flatten(innerWidth))
                yield return item.ShiftLeft(_padding.Left, innerWidth);
        }
        else if (Child is not null)
        {
            float h = Child.Measure(new PdfSize(innerWidth, PdfSize.Infinity)).Height;
            yield return new FlowItem(Child, h, leftInset: _padding.Left, width: innerWidth);
        }

        if (_padding.Bottom > 0f)
            yield return new FlowItem(new SpacerElement(_padding.Bottom), _padding.Bottom);
    }

    public override PdfSize Measure(PdfSize available)
    {
        PdfSize inner = new(
            available.IsWidthConstrained ? MathF.Max(0f, available.Width - _padding.Horizontal) : available.Width,
            available.IsHeightConstrained ? MathF.Max(0f, available.Height - _padding.Vertical) : available.Height);

        PdfSize childSize = Child?.Measure(inner) ?? PdfSize.Zero;
        return new PdfSize(childSize.Width + _padding.Horizontal, childSize.Height + _padding.Vertical);
    }

    protected override void ArrangeCore(PdfRect bounds) =>
        Child?.Arrange(bounds.Deflate(_padding));
}
