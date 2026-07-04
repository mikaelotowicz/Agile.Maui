using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Layout;

/// <summary>
/// Contrato de todo elemento do layout. O ciclo é sempre: Measure (descobrir o tamanho desejado
/// dado o espaço disponível) → Arrange (fixar a posição final) → Render (desenhar). Todo o layout
/// é calculado antes de qualquer renderização.
/// </summary>
public interface ILayoutElement
{
    /// <summary>Tamanho desejado dado o espaço disponível (use PdfSize.Infinity onde não há limite).</summary>
    PdfSize Measure(PdfSize available);

    /// <summary>Fixa a posição/dimensão final do elemento (e posiciona filhos).</summary>
    void Arrange(PdfRect bounds);

    /// <summary>Desenha o elemento já posicionado.</summary>
    void Render(IRenderContext context);
}
