using System.Collections.Generic;
using System.IO;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Layout.Elements;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>Medição e quebra de texto: LineHeight, espaços, quebras explícitas e fonte embutida.</summary>
public class TextMeasureTests
{
    static string? FindTestFontPath()
    {
        string[] candidates =
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    [Fact]
    public void LineHeight_multiplica_o_espacamento_de_linha()
    {
        var style = new TextStyle(fontSize: 10f, lineHeight: 2f);
        Assert.Equal(20f, style.LineSpacing, 3);

        var element = new TextElement("um\ndois\ntres", style);
        PdfSize size = element.Measure(new PdfSize(500f, PdfSize.Infinity));
        Assert.Equal(60f, size.Height, 2);   // 3 linhas × 20pt
    }

    [Fact]
    public void Espacos_consecutivos_sao_preservados()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("a  b", style, 500f);

        Assert.Single(lines);
        Assert.Equal("a  b", lines[0].Text);
    }

    [Fact]
    public void Crlf_vale_uma_unica_quebra_sem_carriage_return_residual()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("a\r\nb", style, 500f);

        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Text);
        Assert.Equal("b", lines[1].Text);
    }

    [Fact]
    public void Texto_vazio_vira_uma_linha_vazia()
    {
        List<TextLine> lines = TextLayout.Wrap("", new TextStyle(), 500f);
        Assert.Single(lines);
        Assert.Equal("", lines[0].Text);
        Assert.Equal(0f, lines[0].Width);
    }

    [Fact]
    public void Quebra_no_final_gera_linha_vazia_extra()
    {
        List<TextLine> lines = TextLayout.Wrap("a\n", new TextStyle(), 500f);
        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Text);
        Assert.Equal("", lines[1].Text);
    }

    [Fact]
    public void Quebra_de_palavra_longa_respeita_a_largura_maxima()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap(new string('W', 40), style, 40f);

        Assert.True(lines.Count > 1);
        foreach (TextLine line in lines)
            Assert.True(line.Width <= 40f + 0.5f, $"linha de {line.Width}pt excede 40pt");
    }

    [Fact]
    public void Char_fora_da_tabela_usa_largura_padrao_da_familia()
    {
        Assert.Equal(556, StandardFont.Get(PdfFontFamily.Helvetica, false, false).GlyphWidth('中'));
        Assert.Equal(500, StandardFont.Get(PdfFontFamily.Times, false, false).GlyphWidth('中'));
        Assert.Equal(600, StandardFont.Get(PdfFontFamily.Courier, false, false).GlyphWidth('中'));
    }

    [Fact]
    public void Medida_da_fonte_embutida_soma_os_avancos()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        int mWidth = font.GlyphWidth('m');
        Assert.True(mWidth > 0);

        float expected = (mWidth + mWidth) / 1000f * 12f;
        Assert.Equal(expected, font.MeasureWidth("mm", 12f), 3);
    }

    [Fact]
    public void TextStyle_com_fonte_embutida_usa_a_medida_dela()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        var style = new TextStyle(fontSize: 14f, embedded: font);

        Assert.Equal(font.MeasureWidth("Ação ágil", 14f), style.MeasureWidth("Ação ágil"), 3);
        Assert.Equal(font.Ascent, style.Ascent, 4);
    }

    [Fact]
    public void Wrap_com_fonte_embutida_quebra_pelas_larguras_dela()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        var style = new TextStyle(fontSize: 12f, embedded: font);
        string text = string.Join(" ", System.Linq.Enumerable.Repeat("palavra", 30));

        List<TextLine> lines = TextLayout.Wrap(text, style, 90f);
        Assert.True(lines.Count > 1);
        foreach (TextLine line in lines)
            Assert.True(line.Width <= 90f + 0.5f, $"linha de {line.Width}pt excede 90pt");
    }
}
