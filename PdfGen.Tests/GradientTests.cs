using System.Text.RegularExpressions;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>Shading patterns: funções tipo 2/3, coordenadas, alpha via ExtGState e validação da API.</summary>
public class GradientTests
{
    static byte[] Pdf(GradientBrush brush) => PdfDocument.Create(doc =>
    {
        doc.Page(page => page.Content().Background(brush).Height(40f));
    }).GeneratePdf();

    [Fact]
    public void Duas_paradas_usam_funcao_tipo_2_direta()
    {
        string raw = PdfMiniParser.AsLatin1(Pdf(GradientBrush.Linear(Colors.Blue, Colors.White, 0f)));

        Assert.Contains("/ShadingType 2", raw);
        Assert.Contains("/FunctionType 2", raw);
        Assert.DoesNotContain("/FunctionType 3", raw);
        Assert.Contains("/Extend [true true]", raw);
    }

    [Fact]
    public void Quatro_paradas_geram_stitching_com_tres_funcoes()
    {
        var brush = GradientBrush.Linear(0f,
            new GradientStop(0f, Colors.Red),
            new GradientStop(0.25f, Colors.Yellow),
            new GradientStop(0.6f, Colors.Green),
            new GradientStop(1f, Colors.Blue));

        string raw = PdfMiniParser.AsLatin1(Pdf(brush));

        Assert.Contains("/FunctionType 3", raw);
        Assert.Contains("/Bounds [0.25 0.6 ]", raw);
        Assert.Contains("/Encode [0 1 0 1 0 1 ]", raw);
        Assert.Equal(3, Regex.Matches(raw, Regex.Escape("/FunctionType 2")).Count);
    }

    [Fact]
    public void Radial_usa_shading_tipo_3_com_raio_positivo()
    {
        string raw = PdfMiniParser.AsLatin1(Pdf(GradientBrush.Radial(Colors.White, Colors.Black)));

        Match m = Regex.Match(raw, @"/ShadingType 3 /Coords \[([-\d\. ]+)\]");
        Assert.True(m.Success);
        string[] coords = m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, coords.Length);
        Assert.Equal("0", coords[2]);                                   // raio inicial
        Assert.True(float.Parse(coords[5], System.Globalization.CultureInfo.InvariantCulture) > 0f);
    }

    [Fact]
    public void Angulo_90_gera_eixo_vertical()
    {
        string raw = PdfMiniParser.AsLatin1(Pdf(GradientBrush.Linear(Colors.Red, Colors.Blue, 90f)));

        Match m = Regex.Match(raw, @"/ShadingType 2 /Coords \[(\S+) (\S+) (\S+) (\S+)\]");
        Assert.True(m.Success);
        Assert.Equal(m.Groups[1].Value, m.Groups[3].Value);   // x0 == x1
        Assert.NotEqual(m.Groups[2].Value, m.Groups[4].Value); // y0 != y1
    }

    [Fact]
    public void Alfa_uniforme_das_paradas_vira_extgstate()
    {
        var brush = GradientBrush.Linear(0f,
            new GradientStop(0f, new PdfColor(255, 0, 0, 128)),
            new GradientStop(1f, new PdfColor(0, 0, 255, 128)));

        string raw = PdfMiniParser.AsLatin1(Pdf(brush));
        Assert.Contains("/ExtGState", raw);
        Assert.Contains("/ca 0.502", raw);
        Assert.Contains("/CA 0.502", raw);
    }

    [Fact]
    public void Alfa_misto_entre_paradas_nao_gera_extgstate()
    {
        var brush = GradientBrush.Linear(0f,
            new GradientStop(0f, new PdfColor(255, 0, 0, 255)),
            new GradientStop(1f, new PdfColor(0, 0, 255, 128)));

        string raw = PdfMiniParser.AsLatin1(Pdf(brush));
        Assert.Contains("/ShadingType 2", raw);
        Assert.DoesNotContain("/ExtGState", raw);
    }

    [Fact]
    public void Borda_com_gradiente_usa_pattern_de_contorno()
    {
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content()
                .Border(2f, GradientBrush.Linear(Colors.Red, Colors.Blue, 0f))
                .Height(40f));
        }).GeneratePdf();

        string content = PdfMiniParser.AllContent(pdf);
        Assert.Contains("/Pattern CS", content);   // espaço de cor de contorno
        Assert.Contains(" SCN", content);
        Assert.Contains("S\n", content);           // stroke
    }

    [Fact]
    public void Gradiente_exige_ao_menos_duas_paradas()
    {
        Assert.Throws<System.ArgumentException>(() =>
            GradientBrush.Linear(0f, new GradientStop(0f, Colors.Red)));
    }

    [Fact]
    public void Offsets_das_paradas_sao_grampeados_em_0_e_1()
    {
        Assert.Equal(0f, new GradientStop(-0.5f, Colors.Red).Offset);
        Assert.Equal(1f, new GradientStop(1.5f, Colors.Red).Offset);
        Assert.Equal(0.4f, new GradientStop(0.4f, Colors.Red).Offset, 4);
    }

    [Fact]
    public void Cor_de_fallback_e_a_primeira_parada()
    {
        var brush = GradientBrush.Linear(Colors.Green, Colors.Blue, 0f);
        Assert.Equal(Colors.Green, brush.FallbackColor);
    }
}
