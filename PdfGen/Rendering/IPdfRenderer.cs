using Agile.Maui.PdfGen.Primitives;

namespace Agile.Maui.PdfGen.Rendering;

/// <summary>
/// Backend de renderização de um documento inteiro. Cada plataforma implementa uma vez.
/// O motor chama BeginDocument, depois BeginPage/EndPage por página, e por fim EndDocument.
/// </summary>
public interface IPdfRenderer
{
    void BeginDocument();

    /// <summary>Inicia uma nova página física e devolve o contexto de desenho dela.</summary>
    IRenderContext BeginPage(PdfSize size);

    void EndPage();

    /// <summary>Finaliza e devolve os bytes do PDF.</summary>
    byte[] EndDocument();
}
