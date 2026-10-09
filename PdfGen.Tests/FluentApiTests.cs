using System.IO;
using System.Linq;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Rendering;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>API fluente: documento vazio, reuso sequencial, overloads e escape hatch.</summary>
public class FluentApiTests
{
    static PdfDocument BuildComplex() => PdfDocument.Create(doc =>
    {
        doc.Page(page =>
        {
            page.Margin(25f);
            page.Header().Text("Reuso");
            page.Footer().PageNumber("{0}/{1}");
            page.Content().Column(col =>
            {
                col.Spacing(5f);
                col.Item().Background(GradientBrush.Linear(Colors.Blue, Colors.White, 45f), 4f)
                    .Padding(6f).Text("Topo çãé");
                col.Item().Image(TestImages.Rgba2x2());
                col.Item().Table(t =>
                {
                    t.Columns(c => { c.ConstantColumn(60f); c.RelativeColumn(); });
                    t.Header(h => { h.Cell(Colors.LightGray).Text("Col A"); h.Cell(Colors.LightGray).Text("Col B"); });
                    for (int i = 0; i < 80; i++)
                        t.Row(r => { r.Cell().Text($"{i}"); r.Cell().Text($"valor {i}"); });
                });
            });
        });
    });

    [Fact]
    public void Documento_sem_paginas_gera_pdf_estruturalmente_valido()
    {
        byte[] pdf = PdfDocument.Create(_ => { }).GeneratePdf();

        string raw = PdfMiniParser.AsLatin1(pdf);
        Assert.StartsWith("%PDF-1.7\n", raw);
        Assert.Contains("/Count 0", raw);
        Assert.EndsWith("%%EOF\n", raw);

        var entries = PdfMiniParser.ReadXref(pdf);   // xref válida mesmo sem páginas
        Assert.Equal(3, entries.Length);             // livre + Catalog + Pages
    }

    [Fact]
    public void Pagina_vazia_gera_uma_pagina()
    {
        byte[] pdf = PdfDocument.Create(doc => doc.Page(_ => { })).GeneratePdf();
        Assert.Contains("/Count 1", PdfMiniParser.AsLatin1(pdf));
    }

    [Fact]
    public void Renders_sequenciais_do_mesmo_documento_sao_identicos()
    {
        PdfDocument doc = BuildComplex();

        byte[] first = doc.GeneratePdf();
        byte[] second = doc.GeneratePdf();

        Assert.True(first.SequenceEqual(second), "dois renders sequenciais divergiram");
    }

    [Fact]
    public void Pdf_e_svg_sequenciais_reutilizam_o_mesmo_documento()
    {
        PdfDocument doc = BuildComplex();

        byte[] pdf1 = doc.GeneratePdf();
        byte[] svg = doc.GenerateSvg();
        byte[] pdf2 = doc.GeneratePdf();

        Assert.StartsWith("<?xml", System.Text.Encoding.UTF8.GetString(svg));
        Assert.True(pdf1.SequenceEqual(pdf2), "render PDF depois do SVG divergiu");
    }

    [Fact]
    public void Overload_de_stream_escreve_os_mesmos_bytes()
    {
        PdfDocument doc = BuildComplex();

        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);

        Assert.True(ms.ToArray().SequenceEqual(doc.GeneratePdf()));
    }

    [Fact]
    public void DefaultTextStyle_e_herdado_pelos_blocos()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.DefaultTextStyle(new TextStyle(fontSize: 20f, family: PdfFontFamily.Times));
                page.Content().Text("estilo herdado");
            });
        }).GeneratePdf();

        string content = PdfMiniParser.AllContent(pdf);
        Assert.Contains(" 20 Tf", content);
        Assert.Contains("/BaseFont /Times-Roman", PdfMiniParser.AsLatin1(pdf));
    }

    [Fact]
    public void Ultimo_conteudo_definido_no_container_prevalece()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                IContainer c = page.Content();
                c.Text("primeiro");
                c.Column(col => col.Item().Text("col"));
            });
        }).GeneratePdf();

        string content = PdfMiniParser.AllContent(pdf);
        Assert.Contains("(col) Tj", content);
        Assert.DoesNotContain("(primeiro) Tj", content);
    }

    [Fact]
    public void Elemento_customizado_via_escape_hatch_desenha_no_contexto()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Element(new DiagonalLineElement()));
        }).GeneratePdf();

        string content = PdfMiniParser.AllContent(pdf);
        Assert.Contains(" RG\n", content);   // cor de contorno
        Assert.Contains(" m\n", content);    // moveto
        Assert.Contains(" l\n", content);    // lineto
        Assert.Contains("S\n", content);     // stroke
    }

    sealed class DiagonalLineElement : Element
    {
        public override PdfSize Measure(PdfSize available) => new(50f, 10f);

        public override void Render(IRenderContext context) =>
            context.DrawLine(
                new PdfPoint(Bounds.Left, Bounds.Top),
                new PdfPoint(Bounds.Right, Bounds.Bottom),
                Colors.Black, 1f);
    }
}
