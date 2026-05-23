// MauiAppBuilderExtensions.cs
using Microsoft.Maui.Hosting;

#if ANDROID
using Controls.Platforms.Android;
#endif

#if IOS || MACCATALYST
using Controls.Platforms.iOS;
#endif

#if WINDOWS
using Controls.Platforms.Windows;
#endif

namespace Controls;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registra o ZoomImageView e seus handlers nativos.
    /// Chamar em MauiProgram.cs: builder.UseZoomImageView()
    /// </summary>
    public static MauiAppBuilder UseZoomImageView(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<ImageView, ImageViewHandler>();
#endif
#if IOS || MACCATALYST
            handlers.AddHandler<ImageView, ImageViewHandler>();
#endif
#if WINDOWS
            handlers.AddHandler<ImageView, ImageViewHandler>();
#endif
        });

        return builder;
    }
}
