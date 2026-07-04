using Agile.Maui.PdfGen.Primitives;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

public class PrimitivesTests
{
    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("000000", 0, 0, 0, 255)]
    [InlineData("#F00", 255, 0, 0, 255)]
    [InlineData("#12345678", 0x12, 0x34, 0x56, 0x78)]
    public void FromHex_parses(string hex, int r, int g, int b, int a)
    {
        PdfColor c = PdfColor.FromHex(hex);
        Assert.Equal((byte)r, c.R);
        Assert.Equal((byte)g, c.G);
        Assert.Equal((byte)b, c.B);
        Assert.Equal((byte)a, c.A);
    }

    [Fact]
    public void Deflate_reduces_by_edges()
    {
        var rect = new PdfRect(0f, 0f, 100f, 100f);
        PdfRect inner = rect.Deflate(Edges.All(10f));
        Assert.Equal(10f, inner.Left);
        Assert.Equal(10f, inner.Top);
        Assert.Equal(80f, inner.Width);
        Assert.Equal(80f, inner.Height);
    }

    [Fact]
    public void Deflate_never_goes_negative()
    {
        var rect = new PdfRect(0f, 0f, 10f, 10f);
        PdfRect inner = rect.Deflate(Edges.All(20f));
        Assert.Equal(0f, inner.Width);
        Assert.Equal(0f, inner.Height);
    }

    [Fact]
    public void Landscape_swaps_dimensions()
    {
        PdfSize a4 = PageSizes.A4;
        PdfSize land = a4.Landscape();
        Assert.Equal(a4.Height, land.Width);
        Assert.Equal(a4.Width, land.Height);
    }
}
