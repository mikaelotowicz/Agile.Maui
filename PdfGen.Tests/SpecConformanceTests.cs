using System.Globalization;
using System.Text.RegularExpressions;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Invariantes de conformidade com a spec PDF (ISO 32000) verificadas byte a byte no arquivo
/// gerado: xref, /Length, operadores balanceados, WinAnsi, gradientes, SMask e cultura invariante.
/// </summary>
public class SpecConformanceTests
{
    // Documento "rico" compartilhado: multi-página, com acentos, escapes, gradientes, PNG com alfa
    // e número de página no rodapé. Gerado uma única vez (determinístico).
    static readonly Lazy<byte[]> Rich = new(BuildRich);

    static byte[] BuildRich() => PdfDocument.Create(doc =>
    {
        doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30f);
            page.Header().Text("CABEÇALHO ESPEC");
            page.Footer().AlignRight().PageNumber("Página {0} de {1}");
            page.Content().Column(col =>
            {
                col.Spacing(4f);
                col.Item().Text("Endereço: ç ã é — € “aspas”");
                col.Item().Text(@"escape a(b)c\d e paren");
                col.Item().Background(GradientBrush.Linear(30f,
                        new GradientStop(0f, Colors.Red),
                        new GradientStop(0.33f, Colors.Yellow),
                        new GradientStop(1f, Colors.Blue)), 4f)
                    .Padding(6f).Text("gradiente");
                col.Item().Background(GradientBrush.Radial(Colors.White, Colors.Blue)).Height(30f);
                col.Item().Image(TestImages.Rgba2x2());
                for (int i = 0; i < 150; i++)
                    col.Item().Text($"linha de paginação {i}");
            });
        });
    }).GeneratePdf();

    static int Count(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle)).Count;

    // ---- xref / estrutura do arquivo ----

    [Fact]
    public void Xref_tem_entradas_de_20_bytes_e_tamanho_do_trailer()
    {
        byte[] pdf = Rich.Value;
        var entries = PdfMiniParser.ReadXref(pdf);     // valida o formato linha a linha
        Assert.Equal(PdfMiniParser.TrailerSize(pdf), entries.Length);
        Assert.Equal((0L, 65535, 'f'), entries[0]);    // entrada livre obrigatória
    }

    [Fact]
    public void Xref_offsets_em_bytes_apontam_para_cada_objeto()
    {
        byte[] pdf = Rich.Value;
        var entries = PdfMiniParser.ReadXref(pdf);

        for (int id = 1; id < entries.Length; id++)
        {
            Assert.Equal('n', entries[id].type);
            string expected = $"{id} 0 obj";
            string actual = System.Text.Encoding.ASCII.GetString(pdf, (int)entries[id].offset, expected.Length);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Length_de_cada_stream_e_exato_em_bytes()
    {
        byte[] pdf = Rich.Value;
        var streams = PdfMiniParser.Streams(pdf);
        Assert.NotEmpty(streams);

        foreach (var (_, dataStart, length) in streams)
        {
            string after = System.Text.Encoding.ASCII.GetString(pdf, dataStart + length, 11);
            Assert.StartsWith("\nendstream", after);
        }
    }

    [Fact]
    public void Cabecalho_com_versao_e_comentario_binario()
    {
        byte[] pdf = Rich.Value;
        Assert.StartsWith("%PDF-1.7\n", PdfMiniParser.AsLatin1(pdf));
        // Segunda linha: comentário com 4 bytes >= 0x80 (arquivo tratado como binário).
        Assert.Equal((byte)'%', pdf[9]);
        for (int i = 10; i < 14; i++)
            Assert.True(pdf[i] >= 0x80, $"byte {i} do comentário binário é {pdf[i]:X2}");
    }

    [Fact]
    public void Arvore_de_paginas_consistente_com_kids_e_count()
    {
        byte[] pdf = Rich.Value;
        string raw = PdfMiniParser.AsLatin1(pdf);

        int count = int.Parse(Regex.Match(raw, @"/Count (\d+)").Groups[1].Value);
        Assert.True(count > 1, "o documento rico deveria paginar");
        Assert.Equal(count, Count(raw, "/Type /Page /Parent"));

        string kids = Regex.Match(raw, @"/Kids \[([^\]]*)\]").Groups[1].Value;
        Assert.Equal(count, Regex.Matches(kids, @"\d+ 0 R").Count);
    }

    // ---- content streams ----

    [Fact]
    public void Operadores_q_Q_e_BT_ET_balanceados_por_pagina()
    {
        foreach (string cs in PdfMiniParser.ContentStreams(Rich.Value))
        {
            Assert.Equal(
                Regex.Matches(cs, @"(?<![A-Za-z])q\n").Count,
                Regex.Matches(cs, @"(?<![A-Za-z])Q\n").Count);
            Assert.Equal(Count(cs, "BT\n"), Count(cs, "ET\n"));
        }
    }

    [Fact]
    public void Texto_winansi_mapeado_byte_a_byte()
    {
        string content = PdfMiniParser.AllContent(Rich.Value);
        Assert.Contains('ç', content);   // ç
        Assert.Contains('\u0097', content);   // — (em dash, WinAnsi 0x97)
        Assert.Contains('\u0080', content);   // € (WinAnsi 0x80)
        Assert.Contains('\u0093', content);   // “
        Assert.Contains('\u0094', content);   // ”
    }

    [Fact]
    public void Parenteses_e_barra_invertida_escapados_no_literal()
    {
        string content = PdfMiniParser.AllContent(Rich.Value);
        Assert.Contains(@"(escape a\(b\)c\\d e paren) Tj", content);
    }

    [Fact]
    public void Caractere_sem_mapa_winansi_vira_interrogacao()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("中文"));
        }).GeneratePdf();

        Assert.Contains("(??) Tj", PdfMiniParser.AllContent(pdf));
    }

    [Fact]
    public void Numero_de_pagina_resolve_total_em_duas_passadas()
    {
        byte[] pdf = Rich.Value;
        int pages = int.Parse(Regex.Match(PdfMiniParser.AsLatin1(pdf), @"/Count (\d+)").Groups[1].Value);
        string content = PdfMiniParser.AllContent(pdf);

        Assert.Contains($"Página 1 de {pages}", content);
        Assert.Contains($"Página {pages} de {pages}", content);
        Assert.DoesNotContain($"de {pages + 1}", content);
    }

    [Fact]
    public void Cabecalho_desenhado_exatamente_uma_vez_por_pagina()
    {
        byte[] pdf = Rich.Value;
        int pages = int.Parse(Regex.Match(PdfMiniParser.AsLatin1(pdf), @"/Count (\d+)").Groups[1].Value);
        Assert.Equal(pages, Count(PdfMiniParser.AllContent(pdf), "(CABEÇALHO ESPEC) Tj"));
    }

    // ---- gradientes e imagens ----

    [Fact]
    public void Gradiente_de_3_paradas_gera_stitching_com_bounds_e_encode()
    {
        string raw = PdfMiniParser.AsLatin1(Rich.Value);
        Assert.Contains("/ShadingType 2", raw);
        Assert.Contains("/Extend [true true]", raw);
        Assert.Contains("/FunctionType 3", raw);
        Assert.Contains("/Bounds [0.33 ]", raw);
        Assert.Contains("/Encode [0 1 0 1 ]", raw);
    }

    [Fact]
    public void Gradiente_radial_tem_seis_coordenadas()
    {
        string raw = PdfMiniParser.AsLatin1(Rich.Value);
        Match m = Regex.Match(raw, @"/ShadingType 3 /Coords \[([-\d\. ]+)\]");
        Assert.True(m.Success, "shading radial não encontrado");
        Assert.Equal(6, m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Png_com_alfa_gera_smask_em_devicegray()
    {
        string raw = PdfMiniParser.AsLatin1(Rich.Value);
        Assert.Matches(@"/SMask \d+ 0 R", raw);
        Assert.Contains("/ColorSpace /DeviceGray", raw);
    }

    [Fact]
    public void Mediabox_a4_sem_decimais_espurios()
    {
        Assert.Contains("/MediaBox [0 0 595 842]", PdfMiniParser.AsLatin1(Rich.Value));
    }

    // ---- cultura ----

    [Fact]
    public void Numeros_invariantes_sob_cultura_ptBR()
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        CultureInfo prevUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

            byte[] pdf = PdfDocument.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Margin(31.5f);
                    page.Content().Padding(7.25f).Text("cultura invariante 1,5").FontSize(10.5f);
                });
            }).GeneratePdf();

            string content = PdfMiniParser.AllContent(pdf);
            Assert.Contains("10.5 Tf", content);

            // Vírgula entre dígitos fora de literais de texto seria vírgula decimal vazada.
            string noLiterals = Regex.Replace(content, @"\((?:[^()\\]|\\.)*\)", "()");
            Assert.DoesNotMatch(@"\d,\d", noLiterals);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }
}
