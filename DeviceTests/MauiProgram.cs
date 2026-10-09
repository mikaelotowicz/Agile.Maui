using Agile.Maui;

namespace Agile.Maui.DeviceTests;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseAgileGalleryView()
            .UseAgilePdfViewer()
            .UseAgileVirtualizedCollectionView()
            .UseAgileChipGroup()
            .UseAgileSignaturePad();

        return builder.Build();
    }
}
