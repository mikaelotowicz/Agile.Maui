using Xunit;

namespace Agile.Maui.SignaturePadTests;

public class SmokeTests
{
    [Fact]
    public void SignaturePad_pode_ser_instanciado_no_host()
    {
        var pad = new SignaturePad();

        Assert.NotNull(pad);
    }
}
