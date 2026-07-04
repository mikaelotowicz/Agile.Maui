using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout;

/// <summary>Base dos elementos: guarda os bounds definidos no Arrange.</summary>
public abstract class Element : ILayoutElement
{
    /// <summary>Área final ocupada pelo elemento, definida no Arrange.</summary>
    protected PdfRect Bounds { get; private set; }

    public abstract PdfSize Measure(PdfSize available);

    public virtual void Arrange(PdfRect bounds)
    {
        Bounds = bounds;
        ArrangeCore(bounds);
    }

    /// <summary>Posiciona filhos relativamente aos bounds. Padrão: nada.</summary>
    protected virtual void ArrangeCore(PdfRect bounds) { }

    public abstract void Render(IRenderContext context);
}
