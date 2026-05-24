// MauiAppBuilderExtensions.cs
using Microsoft.Maui.Hosting;

#if ANDROID
using Agile.Maui.Platforms.Android;
#endif

#if IOS || MACCATALYST
using Agile.Maui.Platforms.iOS;
#endif

#if WINDOWS
using Agile.Maui.Platforms.Windows;
#endif

namespace Agile.Maui;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registra os controles Agile.Maui (ImageView, GalleryView, VirtualizedCollectionView) e seus handlers nativos.
    /// Chamar em MauiProgram.cs: builder.UseAgileMaui()
    /// </summary>
    public static MauiAppBuilder UseAgileMaui(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
            handlers.AddHandler<VirtualizedCollectionView, VirtualizedCollectionViewHandler>();
#endif
#if IOS || MACCATALYST
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
            handlers.AddHandler<VirtualizedCollectionView, VirtualizedCollectionViewHandler>();
#endif
#if WINDOWS
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
            // VirtualizedCollectionView não precisa de handler customizado no Windows:
            // herda de ContentView e usa CollectionView do MAUI como Content.
#endif
        });

        return builder;
    }
}
