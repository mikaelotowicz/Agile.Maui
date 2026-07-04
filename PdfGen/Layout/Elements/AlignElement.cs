using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Alinha o filho (que ocupa apenas o tamanho desejado) dentro da área disponível.</summary>
public sealed class AlignElement : SingleChildElement
{
    readonly HorizontalAlignment? _horizontal;
    readonly VerticalAlignment? _vertical;

    public AlignElement(ILayoutElement? child, HorizontalAlignment? horizontal, VerticalAlignment? vertical) : base(child)
    {
        _horizontal = horizontal;
        _vertical = vertical;
    }

    protected override void ArrangeCore(PdfRect bounds)
    {
        if (Child is null)
            return;

        PdfSize desired = Child.Measure(bounds.Size);
        float w = _horizontal is null ? bounds.Width : MathF.Min(desired.Width, bounds.Width);
        float h = _vertical is null ? bounds.Height : MathF.Min(desired.Height, bounds.Height);

        float x = _horizontal switch
        {
            HorizontalAlignment.Center => bounds.Left + (bounds.Width - w) / 2f,
            HorizontalAlignment.Right => bounds.Right - w,
            _ => bounds.Left,
        };
        float y = _vertical switch
        {
            VerticalAlignment.Middle => bounds.Top + (bounds.Height - h) / 2f,
            VerticalAlignment.Bottom => bounds.Bottom - h,
            _ => bounds.Top,
        };

        Child.Arrange(new PdfRect(x, y, w, h));
    }
}
