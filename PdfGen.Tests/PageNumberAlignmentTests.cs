using System.Globalization;
using System.Text.RegularExpressions;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Regressão: o PageNumberElement reportava a largura disponível no Measure, então o AlignRight/
/// AlignCenter do contêiner não tinha efeito e o número ficava colado à esquerda.
/// </summary>
public class PageNumberAlignmentTests
{
    const float PageWidth = 595f;   // A4
    const float Margin = 30f;
    const float Tolerance = 0.01f;

    static float Width(string text) => TextStyle.Default.MeasureWidth(text);

    /// <summary>Posição x (Td) de cada desenho do texto informado, na ordem do conteúdo.</summary>
    static List<float> XsOf(byte[] pdf, string text)
    {
        string content = PdfMiniParser.AllContent(pdf);
        var xs = new List<float>();
        foreach (Match m in Regex.Matches(content, @"(-?[\d.]+) (-?[\d.]+) Td\n\((.*?)\) Tj"))
        {
            if (m.Groups[3].Value == text)
                xs.Add(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return xs;
    }

    static byte[] FooterPdf(Action<IContainer> footer) =>
        PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(Margin);
                footer(page.Footer());
                page.Content().Text("conteudo");
            });
        }).GeneratePdf();

    [Fact]
    public void Footer_AlignRight_encosta_o_numero_na_borda_direita()
    {
        byte[] pdf = FooterPdf(f => f.AlignRight().PageNumber("Pag. {0} de {1}"));

        float x = Assert.Single(XsOf(pdf, "Pag. 1 de 1"));
        Assert.Equal(PageWidth - Margin, x + Width("Pag. 1 de 1"), Tolerance);
    }

    [Fact]
    public void Footer_AlignCenter_centraliza_o_numero()
    {
        byte[] pdf = FooterPdf(f => f.AlignCenter().PageNumber("Pag. {0} de {1}"));

        float x = Assert.Single(XsOf(pdf, "Pag. 1 de 1"));
        Assert.Equal(PageWidth / 2f, x + Width("Pag. 1 de 1") / 2f, Tolerance);
    }

    [Fact]
    public void RelativeItem_AlignRight_encosta_o_numero_na_borda_da_celula()
    {
        byte[] pdf = FooterPdf(f => f.Row(row =>
        {
            row.RelativeItem().Text("Empresa");
            row.RelativeItem().AlignRight().PageNumber("Pag. {0} de {1}");
        }));

        float x = Assert.Single(XsOf(pdf, "Pag. 1 de 1"));
        Assert.Equal(PageWidth - Margin, x + Width("Pag. 1 de 1"), Tolerance);
    }

    [Fact]
    public void Alinhamento_do_proprio_texto_continua_funcionando()
    {
        byte[] right = FooterPdf(f => f.PageNumber("{0}/{1}").AlignRight());
        byte[] center = FooterPdf(f => f.PageNumber("{0}/{1}").AlignCenter());

        float w = Width("1/1");
        Assert.Equal(PageWidth - Margin, Assert.Single(XsOf(right, "1/1")) + w, Tolerance);
        Assert.Equal(PageWidth / 2f, Assert.Single(XsOf(center, "1/1")) + w / 2f, Tolerance);
    }

    [Fact]
    public void AlignRight_fica_rente_em_todas_as_paginas_mesmo_com_larguras_diferentes()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(Margin);
                page.Footer().AlignRight().PageNumber("{0} de {1}");
                page.Content().Column(col =>
                {
                    for (int i = 0; i < 600; i++)
                        col.Item().Text($"linha {i}");
                });
            });
        }).GeneratePdf();

        int total = Regex.Matches(PdfTestHelpers.AsLatin1(pdf), "/Type /Page(?!s)").Count;
        Assert.True(total >= 10, $"esperava ao menos 10 páginas (dígitos variando), veio {total}");

        foreach (int page in new[] { 1, total })
        {
            string text = $"{page} de {total}";
            float x = Assert.Single(XsOf(pdf, text));
            Assert.Equal(PageWidth - Margin, x + Width(text), Tolerance);
        }
    }
}
