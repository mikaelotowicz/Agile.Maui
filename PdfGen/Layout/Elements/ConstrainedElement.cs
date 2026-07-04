using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Impõe largura e/ou altura fixas ao filho.</summary>
public sealed class ConstrainedElement : SingleChildElement
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
}
