using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class TextLayoutTests
{
    [Fact]
    public void MeasureWidth_grows_with_text()
    {
        StandardFont font = StandardFont.Get(PdfFontFamily.Helvetica, false, false);
        float w1 = font.MeasureWidth("a", 12f);
        float w2 = font.MeasureWidth("aaaa", 12f);
        Assert.True(w2 > w1);
        Assert.True(w1 > 0f);
    }

    [Fact]
    public void Bold_is_wider_than_regular_for_typical_text()
    {
        StandardFont regular = StandardFont.Get(PdfFontFamily.Helvetica, false, false);
        StandardFont bold = StandardFont.Get(PdfFontFamily.Helvetica, true, false);
        Assert.True(bold.MeasureWidth("Relatório", 12f) >= regular.MeasureWidth("Relatório", 12f));
    }

    [Fact]
    public void Courier_is_monospaced()
    {
        StandardFont c = StandardFont.Get(PdfFontFamily.Courier, false, false);
        Assert.Equal(c.GlyphWidth('i'), c.GlyphWidth('W'));
    }
}
