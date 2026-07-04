using System.Text;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class ManagedPdfTests
{
    static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public void Generates_valid_pdf_header_and_trailer()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(30f);
                page.Content().Text("Olá, PDF!");
            });
        }).GeneratePdf();

        Assert.True(pdf.Length > 200);
        string content = AsLatin1(pdf);
        Assert.StartsWith("%PDF-1.", content);
        Assert.Contains("/Type /Catalog", content);
        Assert.Contains("/Type /Pages", content);
        Assert.Contains("/Type /Page", content);
        Assert.Contains("xref", content);
        Assert.Contains("trailer", content);
        Assert.EndsWith("%%EOF\n", content);
    }

    [Fact]
    public void Embeds_font_and_draws_text()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("Fatura").Bold().FontSize(20f));
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.Contains("/BaseFont /Helvetica-Bold", content);
        Assert.Contains("(Fatura) Tj", content);
    }

    [Fact]
    public void Accented_characters_are_encoded_as_winansi_bytes()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("Endereço"));
        }).GeneratePdf();

        // 'ç' = 0xE7 em WinAnsi/Latin-1; deve aparecer como byte literal, não escapado.
        Assert.Contains((byte)0xE7, pdf);
    }

    [Fact]
    public void Page_count_matches_planned_pages()
    {
        PdfDocument doc = PdfDocument.Create(d =>
        {
            d.Page(page =>
            {
                page.Margin(30f);
                page.Content().Column(col =>
                {
                    for (int i = 0; i < 400; i++)
                        col.Item().Text($"linha {i}");
                });
            });
        });

        byte[] pdf = doc.GeneratePdf();
        string content = AsLatin1(pdf);

        int pageCount = System.Text.RegularExpressions.Regex.Matches(content, "/Type /Page(?!s)").Count;
        Assert.True(pageCount > 1);
        Assert.Contains($"/Count {pageCount}", content);
    }

    [Fact]
    public void PageNumber_resolves_total_pages()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(30f);
                page.Footer().AlignCenter().PageNumber("Página {0} de {1}");
                page.Content().Column(col =>
                {
                    for (int i = 0; i < 400; i++)
                        col.Item().Text($"linha {i}");
                });
            });
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        // "Página 1 de N": o 'á' vira o byte WinAnsi 0xE1 (não escapado); o restante é ASCII.
        Assert.Contains("gina 1 de ", content);
    }

    [Fact]
    public void Table_and_shapes_render()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(30f);
                page.Content().Column(col =>
                {
                    col.Spacing(10f);
                    col.Item().Background(Colors.Blue, 6f).Padding(8f).Text("Cabeçalho").FontColor(Colors.White);
                    col.Item().Border(1f, Colors.Gray, 4f).Padding(8f).Text("Caixa com borda");
                    col.Item().Table(t =>
                    {
                        t.Columns(c => { c.RelativeColumn(2f); c.RelativeColumn(); });
                        t.Header(h =>
                        {
                            h.Cell(Colors.LightGray).Text("Item").Bold();
                            h.Cell(Colors.LightGray).Text("Valor").Bold();
                        });
                        t.Row(r => { r.Cell().Text("Produto A"); r.Cell().Text("R$ 10,00"); });
                        t.Row(r => { r.Cell().Text("Produto B"); r.Cell().Text("R$ 20,00"); });
                    });
                });
            });
        }).GeneratePdf();

        string content = AsLatin1(pdf);
        Assert.Contains(" re\n", content);   // retângulos (fundo/borda/células)
        Assert.Contains(" c\n", content);    // curvas (cantos arredondados)
        Assert.Contains("(Produto A) Tj", content);
    }

    [Fact]
    public void Saves_to_disk()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agile_pdfgen_test.pdf");
        PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("arquivo"));
        }).Save(path);

        Assert.True(System.IO.File.Exists(path));
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        System.IO.File.Delete(path);
    }
}
