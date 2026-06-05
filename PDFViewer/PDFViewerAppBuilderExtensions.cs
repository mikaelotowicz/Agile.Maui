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

public static class PDFViewerAppBuilderExtensions
{
    /// <summary>
    /// Registra PdfViewer e seu handler nativo.
    /// Chamar em MauiProgram.cs: builder.UsePdfViewer()
    /// </summary>
    public static MauiAppBuilder UsePdfViewer(this MauiAppBuilder builder)
    {
        // Fonte de ícones do PdfReaderView (empacotada na lib). Alias próprio p/ não colidir com
        // fontes do app consumidor.
        builder.ConfigureFonts(fonts => fonts.AddFont("AgilePdfIcons.ttf", "AgilePdfIcons"));

        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<PdfViewer, PdfViewerHandler>();
#endif
#if IOS || MACCATALYST
            handlers.AddHandler<PdfViewer, PdfViewerHandler>();
#endif
#if WINDOWS
            handlers.AddHandler<PdfViewer, PdfViewerHandler>();
#endif
        });

        return builder;
    }
}
