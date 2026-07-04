using System.Collections.Generic;
using Agile.Maui.PdfGen.Layout;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class WrapTests
{
    [Fact]
    public void Wrap_short_text_single_line()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("Olá mundo", style, 500f);
        Assert.Single(lines);
        Assert.Equal("Olá mundo", lines[0].Text);
    }

    [Fact]
    public void Wrap_long_text_multiple_lines()
    {
        var style = new TextStyle(fontSize: 12f);
        string text = string.Join(" ", System.Linq.Enumerable.Repeat("palavra", 50));
        List<TextLine> lines = TextLayout.Wrap(text, style, 100f);
        Assert.True(lines.Count > 1);
        foreach (TextLine line in lines)
            Assert.True(line.Width <= 100f + 0.5f, $"linha excede a largura: {line.Width}");
    }

    [Fact]
    public void Wrap_respects_explicit_newlines()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("linha1\nlinha2\nlinha3", style, 500f);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void Wrap_breaks_word_longer_than_width()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", style, 40f);
        Assert.True(lines.Count > 1);
    }

    [Fact]
    public void Wrap_marks_only_wrapped_lines_as_justifiable()
    {
        var style = new TextStyle(fontSize: 12f);
        List<TextLine> lines = TextLayout.Wrap("um dois tres quatro cinco", style, 55f);

        Assert.True(lines.Count > 1);
        Assert.Contains(lines.Take(lines.Count - 1), line => line.CanJustify);
        Assert.False(lines[^1].CanJustify);
    }

    [Fact]
    public void Wrap_does_not_split_surrogate_pairs()
    {
        var style = new TextStyle(fontSize: 12f);
        string emoji = char.ConvertFromUtf32(0x1F600);
        List<TextLine> lines = TextLayout.Wrap(string.Concat(System.Linq.Enumerable.Repeat(emoji, 4)), style, 7f);

        Assert.Equal(4, lines.Count);
        foreach (TextLine line in lines)
            Assert.Equal(emoji, line.Text);
    }
}
