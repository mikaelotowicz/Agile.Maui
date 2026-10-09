using Xunit;

namespace Agile.Maui.ChipGroupTests;

public class SmokeTests
{
    [Fact]
    public void ChipGroup_pode_ser_instanciado_no_host()
    {
        var chips = new ChipGroup();

        Assert.Null(chips.SelectedItem);
        Assert.Null(chips.ItemsSource);
    }
}
