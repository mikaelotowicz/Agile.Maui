using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

namespace Agile.Maui.PdfGen.Api;

/// <summary>Configuração encadeável de estilo de um bloco de texto (ou número de página).</summary>
public interface ITextStyleDescriptor
{
    ITextStyleDescriptor FontSize(float size);
    ITextStyleDescriptor FontFamily(PdfFontFamily family);
    /// <summary>Usa uma fonte TrueType/OTF embutida (suporta Unicode completo).</summary>
    ITextStyleDescriptor Font(EmbeddedFont font);
    ITextStyleDescriptor Bold();
    ITextStyleDescriptor Italic();
    ITextStyleDescriptor FontColor(PdfColor color);
    ITextStyleDescriptor LineHeight(float ratio);
    ITextStyleDescriptor AlignLeft();
    ITextStyleDescriptor AlignCenter();
    ITextStyleDescriptor AlignRight();
    ITextStyleDescriptor AlignJustify();
}
