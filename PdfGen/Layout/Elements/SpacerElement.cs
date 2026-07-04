using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Espaço vazio de tamanho fixo (gap entre itens).</summary>
public sealed class SpacerElement : Element
{
    readonly float _size;

    public SpacerElement(float size)
    {
        _size = size;
    }

    public override PdfSize Measure(PdfSize available) => new(_size, _size);

    public override void Render(IRenderContext context) { }
}
