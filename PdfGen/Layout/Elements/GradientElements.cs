using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Preenche o fundo da área do filho com um gradiente, com cantos arredondados opcionais.</summary>
public sealed class GradientBackgroundElement : SingleChildElement
{
    readonly GradientBrush _brush;
    readonly float _cornerRadius;

    public GradientBackgroundElement(ILayoutElement? child, GradientBrush brush, float cornerRadius = 0f) : base(child)
    {
        _brush = brush;
        _cornerRadius = cornerRadius;
    }

    public override void Render(IRenderContext context)
    {
        context.FillGradient(Bounds, _brush, _cornerRadius);
        base.Render(context);
    }
}

/// <summary>Desenha uma borda em gradiente ao redor do filho, com cantos arredondados opcionais.</summary>
public sealed class GradientBorderElement : SingleChildElement
{
    readonly float _thickness;
    readonly GradientBrush _brush;
    readonly float _cornerRadius;

    public GradientBorderElement(ILayoutElement? child, float thickness, GradientBrush brush, float cornerRadius = 0f) : base(child)
    {
        _thickness = thickness;
        _brush = brush;
        _cornerRadius = cornerRadius;
    }

    public override void Render(IRenderContext context)
    {
        base.Render(context);
        if (_thickness > 0f)
        {
            float half = _thickness / 2f;
            var inset = new PdfRect(
                Bounds.Left + half, Bounds.Top + half,
                MathF.Max(0f, Bounds.Width - _thickness), MathF.Max(0f, Bounds.Height - _thickness));
            context.StrokeGradient(inset, _brush, _thickness, _cornerRadius);
        }
    }
}
