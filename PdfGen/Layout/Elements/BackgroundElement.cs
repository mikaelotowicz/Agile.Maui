using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Preenche o fundo da área do filho, com cantos arredondados opcionais.</summary>
public sealed class BackgroundElement : SingleChildElement
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
}
