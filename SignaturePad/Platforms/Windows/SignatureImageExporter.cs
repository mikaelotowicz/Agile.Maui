using System.IO;
using Microsoft.Graphics.Canvas;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Win2D;
using WColor = Windows.UI.Color;

namespace Agile.Maui.Platforms.Windows;

/// <summary>
/// Windows image export through Win2D directly (CanvasRenderTarget + SaveAsync).
/// The cross-platform path (PlatformBitmapExportService/WriteToStream) can throw
/// NotImplementedException on MAUI Graphics' Win2D backend, so this uses the native API.
/// </summary>
internal static class SignatureImageExporter
{
    public static async Task<Stream> ExportAsync(
        IReadOnlyList<RenderStroke> strokes,
        RectF bounds,
        float scale,
        Color? background,
        Color? strokeOverride,
        bool jpeg,
        float jpegQuality)
    {
        var device = CanvasDevice.GetSharedDevice();

        // dpi = 96*scale makes the render target (width*scale) x (height*scale) pixels,
        // while keeping drawing coordinates in DIP.
        using var renderTarget = new CanvasRenderTarget(device, bounds.Width, bounds.Height, 96f * scale);

        using (var session = renderTarget.CreateDrawingSession())
        {
            session.Clear(background is { } bg ? ToWindowsColor(bg)
                : jpeg ? Microsoft.UI.Colors.White
                : WColor.FromArgb(0, 0, 0, 0));

            var canvas = new W2DCanvas { Session = session };
            canvas.Translate(-bounds.Left, -bounds.Top);
            SignaturePadDrawable.DrawStrokes(canvas, strokes, strokeOverride);
        }

        var stream = new MemoryStream();
        if (jpeg)
            await renderTarget.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Jpeg, jpegQuality);
        else
            await renderTarget.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);

        stream.Position = 0;
        return stream;
    }

    private static WColor ToWindowsColor(Color c) =>
        WColor.FromArgb(
            (byte)(c.Alpha * 255),
            (byte)(c.Red * 255),
            (byte)(c.Green * 255),
            (byte)(c.Blue * 255));
}
