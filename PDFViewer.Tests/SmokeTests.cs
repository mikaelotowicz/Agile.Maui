using Xunit;

namespace Agile.Maui.PdfTests;

public class SmokeTests
{
    [Fact]
    public void PdfViewer_pode_ser_instanciado_no_host()
    {
        var viewer = new PdfViewer();

        Assert.NotNull(viewer);
    }
}
