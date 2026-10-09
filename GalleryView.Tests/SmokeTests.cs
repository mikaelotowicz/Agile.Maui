using Xunit;

namespace Agile.Maui.GalleryTests;

public class SmokeTests
{
    [Fact]
    public void ImageView_pode_ser_instanciado_no_host()
    {
        var image = new ImageView();

        Assert.Null(image.Source);
    }

    [Fact]
    public void GalleryView_pode_ser_instanciado_no_host()
    {
        var gallery = new GalleryView();

        Assert.NotNull(gallery);
    }
}
