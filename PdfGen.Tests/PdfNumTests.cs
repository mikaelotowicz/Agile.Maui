using System.Globalization;
using Agile.Maui.PdfGen.Pdf;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>Formatação numérica e escape de literais do escritor PDF.</summary>
public class PdfNumTests
{
    [Fact]
    public void Nan_e_infinito_viram_zero()
    {
        Assert.Equal("0", PdfNum.F(float.NaN));
        Assert.Equal("0", PdfNum.F(float.PositiveInfinity));
        Assert.Equal("0", PdfNum.F(float.NegativeInfinity));
    }

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(1f, "1")]
    [InlineData(595f, "595")]
    [InlineData(1.5f, "1.5")]
    [InlineData(-2.25f, "-2.25")]
    [InlineData(0.1234f, "0.123")]   // máximo de 3 casas decimais
    [InlineData(10.5f, "10.5")]
    public void Formata_com_ate_tres_decimais_e_ponto(float value, string expected)
    {
        Assert.Equal(expected, PdfNum.F(value));
    }

    [Fact]
    public void Formato_e_invariante_sob_cultura_ptBR()
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal("1.5", PdfNum.F(1.5f));
            Assert.Equal("-0.25", PdfNum.F(-0.25f));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void EscapeLiteral_escapa_parenteses_e_barra()
    {
        Assert.Equal(@"a\(b\)c\\", PdfNum.EscapeLiteral(@"a(b)c\"));
    }

    [Fact]
    public void EscapeLiteral_escapa_quebras_de_linha()
    {
        Assert.Equal(@"a\r\nb", PdfNum.EscapeLiteral("a\r\nb"));
    }

    [Fact]
    public void EscapeLiteral_mapeia_winansi_fora_do_latin1()
    {
        Assert.Equal((char)0x80, PdfNum.EscapeLiteral("€")[0]);
        Assert.Equal((char)0x97, PdfNum.EscapeLiteral("—")[0]);
        Assert.Equal((char)0x93, PdfNum.EscapeLiteral("“")[0]);
        Assert.Equal((char)0x99, PdfNum.EscapeLiteral("™")[0]);
    }

    [Fact]
    public void EscapeLiteral_preserva_latin1_e_degrada_o_resto()
    {
        Assert.Equal((char)0xE7, PdfNum.EscapeLiteral("ç")[0]);   // ç inalterado
        Assert.Equal('?', PdfNum.EscapeLiteral("中")[0]);          // sem mapa → '?'
        Assert.Equal("abc 123", PdfNum.EscapeLiteral("abc 123"));
    }
}
