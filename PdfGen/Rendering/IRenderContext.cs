using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Rendering;

/// <summary>
/// Superfície de desenho de uma página. Implementada por cada backend (escritor gerenciado,
/// Android PdfDocument, iOS UIGraphicsPDFRenderer). Coordenadas em pontos PDF, origem no
/// canto superior esquerdo e Y crescendo para baixo.
/// </summary>
public interface IRenderContext
{
    /// <summary>Desenha uma linha de texto com a baseline na posição informada.</summary>
    void DrawText(string text, PdfPoint baselineOrigin, TextStyle style);

    void DrawImage(PdfImage image, PdfRect destination);

    void DrawLine(PdfPoint from, PdfPoint to, PdfColor color, float thickness);

    /// <summary>Desenha o contorno de um retângulo (com cantos arredondados se radius &gt; 0).</summary>
    void DrawRectangle(PdfRect rect, PdfColor color, float thickness, float cornerRadius = 0f);

    /// <summary>Preenche um retângulo (com cantos arredondados se radius &gt; 0).</summary>
    void FillRectangle(PdfRect rect, PdfColor color, float cornerRadius = 0f);

    /// <summary>
    /// Preenche um retângulo com um gradiente. A implementação padrão recai na cor da primeira
    /// parada — backends que suportam gradiente (escritor gerenciado) sobrescrevem este método.
    /// </summary>
    void FillGradient(PdfRect rect, GradientBrush brush, float cornerRadius = 0f) =>
        FillRectangle(rect, brush.FallbackColor, cornerRadius);

    /// <summary>Desenha o contorno de um retângulo com um gradiente (padrão: cor da primeira parada).</summary>
    void StrokeGradient(PdfRect rect, GradientBrush brush, float thickness, float cornerRadius = 0f) =>
        DrawRectangle(rect, brush.FallbackColor, thickness, cornerRadius);

    void SaveState();

    void RestoreState();

    /// <summary>Recorta o desenho subsequente à área informada (até o próximo RestoreState).</summary>
    void ClipRectangle(PdfRect rect);
}
