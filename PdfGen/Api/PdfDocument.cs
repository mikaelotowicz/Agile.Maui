using System;
using System.IO;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Pdf;
using Agile.Maui.PdfGen.Rendering;

namespace Agile.Maui.PdfGen.Api;

/// <summary>
/// Ponto de entrada da biblioteca. Descreva o documento com a API fluente e gere o PDF.
/// <code>
/// byte[] pdf = PdfDocument.Create(doc =>
/// {
///     doc.Page(page =>
///     {
///         page.Margin(20);
///         page.Header().Text("Pedido").Bold().FontSize(20);
///         page.Content().Column(col =>
///         {
///             col.Item().Text("Cliente");
///             col.Item().Table(t => { ... });
///         });
///         page.Footer().AlignCenter().PageNumber("Página {0} de {1}");
///     });
/// }).GeneratePdf();
/// </code>
/// </summary>
public sealed class PdfDocument
{
    readonly DocumentModel _model;

    private PdfDocument(DocumentModel model)
    {
        _model = model;
    }

    /// <summary>Descreve um documento. Nada é renderizado até chamar GeneratePdf/Render/Save.</summary>
    public static PdfDocument Create(Action<IDocumentContainer> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var container = new DocumentContainer();
        build(container);
        return new PdfDocument(container.Build());
    }

    /// <summary>Gera o PDF com o escritor gerenciado (funciona em qualquer plataforma).</summary>
    public byte[] GeneratePdf() => Render(new ManagedPdfRenderer());

    /// <summary>
    /// Gera o PDF usando o renderer nativo da plataforma corrente (Android/iOS/Mac); nas demais,
    /// recai no escritor gerenciado. Prefira este método em app MAUI para aproveitar as fontes e
    /// o suporte a PNG do sistema.
    /// </summary>
    public byte[] GeneratePdfNative() => Render(PlatformRenderer.Create());

    /// <summary>Gera o PDF (escritor gerenciado) diretamente em um stream.</summary>
    public void GeneratePdf(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = GeneratePdf();
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Exporta o mesmo documento como SVG (todas as páginas empilhadas verticalmente num único SVG).
    /// Usa o mesmo motor de layout do PDF, apenas trocando o backend de renderização.
    /// </summary>
    public byte[] GenerateSvg() => Render(new Svg.SvgRenderer());

    /// <summary>Exporta o documento como SVG diretamente em um stream.</summary>
    public void GenerateSvg(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = GenerateSvg();
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Gera e salva o PDF em disco (escritor gerenciado).</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllBytes(path, GeneratePdf());
    }

    /// <summary>
    /// Renderiza usando um backend específico (ex.: renderer nativo Android/iOS).
    /// O motor de layout planeja as páginas e as desenha no renderer informado.
    /// </summary>
    public byte[] Render(IPdfRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return LayoutEngine.Render(_model, renderer);
    }

    /// <summary>Acesso interno ao modelo (para renderers de plataforma e testes).</summary>
    internal DocumentModel Model => _model;
}
