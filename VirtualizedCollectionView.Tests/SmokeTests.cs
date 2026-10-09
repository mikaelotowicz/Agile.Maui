using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

public class SmokeTests
{
    [Fact]
    public void VirtualizedCollectionView_pode_ser_instanciado_no_host()
    {
        var view = new VirtualizedCollectionView();

        Assert.Null(view.ItemsSource);
    }

    [Fact]
    public void ObservableRangeCollection_pode_ser_instanciada_no_host()
    {
        var collection = new ObservableRangeCollection<int>();

        Assert.Empty(collection);
    }
}
