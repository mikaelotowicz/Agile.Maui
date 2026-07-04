using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout.Elements;

/// <summary>Elemento vazio (sem conteúdo). Usado quando um container não recebe filho.</summary>
public sealed class EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();

    public override PdfSize Measure(PdfSize available) => PdfSize.Zero;

    public override void Render(IRenderContext context) { }
}
