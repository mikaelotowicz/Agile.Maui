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

public static class GalleryViewAppBuilderExtensions
{
    /// <summary>
    /// Registra ImageView e GalleryView e seus handlers nativos.
    /// Chamar em MauiProgram.cs: builder.UseGalleryView()
    /// </summary>
    public static MauiAppBuilder UseGalleryView(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
#endif
#if IOS || MACCATALYST
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
#endif
#if WINDOWS
            handlers.AddHandler<ImageView, ImageViewHandler>();
            handlers.AddHandler<GalleryView, GalleryViewHandler>();
#endif
        });

        return builder;
    }
}
