using Microsoft.Maui.Handlers;

namespace Agile.Maui;

public static class SignaturePadAppBuilderExtensions
{
    private const string TouchMapperKey = "AgileSignaturePadTouch";

    // The mapping goes on the static GraphicsViewHandler.Mapper, so a second call
    // must not stack a duplicate entry.
    private static bool _registered;

    /// <summary>
    /// Registers SignaturePad. Drawing and export are cross-platform; this method only
    /// connects native touch and physical pressure capture to the underlying GraphicsView.
    /// Call in MauiProgram.cs: builder.UseAgileSignaturePad()
    /// </summary>
    public static MauiAppBuilder UseAgileSignaturePad(this MauiAppBuilder builder)
    {
        if (_registered)
            return builder;
        _registered = true;

        GraphicsViewHandler.Mapper.AppendToMapping(TouchMapperKey, (handler, view) =>
        {
            if (view is not SignaturePad pad)
                return;

#if ANDROID
            Platforms.Android.SignatureTouchInterop.Attach(handler.PlatformView, pad);
#elif IOS || MACCATALYST
            Platforms.iOS.SignatureTouchInterop.Attach(handler.PlatformView, pad);
#elif WINDOWS
            Platforms.Windows.SignatureTouchInterop.Attach(handler.PlatformView, pad);
#endif
        });

        return builder;
    }
}
