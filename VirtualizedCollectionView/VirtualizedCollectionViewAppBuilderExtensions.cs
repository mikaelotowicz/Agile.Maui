using Microsoft.Maui.Hosting;

#if ANDROID
using Agile.Maui.Platforms.Android;
#endif

#if IOS || MACCATALYST
using Agile.Maui.Platforms.iOS;
#endif

namespace Agile.Maui;

public static class VirtualizedCollectionViewAppBuilderExtensions
{
    /// <summary>
    /// Registra VirtualizedCollectionView e seu handler nativo.
    /// Chamar em MauiProgram.cs: builder.UseAgileVirtualizedCollectionView()
    /// </summary>
    public static MauiAppBuilder UseAgileVirtualizedCollectionView(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<VirtualizedCollectionView, VirtualizedCollectionViewHandler>();
#endif
#if IOS || MACCATALYST
            handlers.AddHandler<VirtualizedCollectionView, VirtualizedCollectionViewHandler>();
#endif
            // Windows: VirtualizedCollectionView herda de ContentView e usa CollectionView do MAUI
            // como Content — não precisa de handler customizado.
        });

        return builder;
    }
}
