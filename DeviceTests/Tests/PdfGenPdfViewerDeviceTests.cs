#if ANDROID
using Agile.Maui.PdfGen.Api;
using Xunit;
using EmbeddedFont = Agile.Maui.PdfGen.Text.EmbeddedFont;

namespace Agile.Maui.DeviceTests;

/// <summary>
/// Sinergia PdfGen → PdfViewer no Android: gera o PDF no device (escritor gerenciado e
/// renderer nativo/Typeface) e carrega num <see cref="PdfViewer"/> em página real,
/// validando DocumentLoaded, PageCount e a rasterização da primeira página.
/// </summary>
public static class PdfGenPdfViewerDeviceTests
{
    [DeviceFact(Platforms.Android, TimeoutSeconds = 120)]
    public static async Task PdfGen_Gerenciado_Carrega_No_PdfViewer_Com_2_Paginas(TestHost host)
    {
        var pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(24f);
                page.Header().Text("Agile.Maui DeviceTests").Bold().FontSize(18f);
                page.Content().Column(col =>
                {
                    col.Spacing(6f);
                    col.Item().Text("Página 1 — escritor gerenciado, com acentuação: ação, café, çãé.");
                    col.Item().Text("Texto adicional para garantir conteúdo rasterizável.");
                });
                page.Footer().PageNumber("Página {0} de {1}");
            });
            doc.Page(page => page.Content().Text("Página 2 — fim."));
        }).GeneratePdf();

        await LoadPdfAndAssertAsync(host, pdf, expectedPages: 2, "GeneratePdf (gerenciado)");
    }

    [DeviceFact(Platforms.Android, TimeoutSeconds = 120)]
    public static async Task PdfGen_Gerenciado_Com_Fonte_Embutida_Carrega_No_PdfViewer(TestHost host)
    {
        // Fonte TTF real empacotada como MauiAsset (OpenSans-Regular).
        byte[] fontBytes;
        await using (var fontStream = await FileSystem.OpenAppPackageFileAsync("OpenSans-Regular.ttf"))
        using (var ms = new MemoryStream())
        {
            await fontStream.CopyToAsync(ms);
            fontBytes = ms.ToArray();
        }
        var font = EmbeddedFont.Load(fontBytes);

        var pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(24f);
                page.Content().Column(col =>
                {
                    col.Item().Text("Fonte embutida no device: Ágil Ação çãé ÀÈÌÒÙ").Font(font).FontSize(14f);
                    col.Item().Text("Subset TrueType gerado em runtime.").Font(font);
                });
            });
        }).GeneratePdf();

        await LoadPdfAndAssertAsync(host, pdf, expectedPages: 1, "GeneratePdf com EmbeddedFont");
    }

    [DeviceFact(Platforms.Android, TimeoutSeconds = 120)]
    public static async Task PdfGen_Nativo_Typeface_Carrega_No_PdfViewer(TestHost host)
    {
        // GeneratePdfNative no Android usa o renderer nativo (fontes via Typeface).
        var pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(24f);
                page.Content().Column(col =>
                {
                    col.Item().Text("Renderer NATIVO Android (Typeface): ação, café.").FontSize(16f);
                    col.Item().Text("Segunda linha de texto nativo.");
                });
            });
        }).GeneratePdfNative();

        await LoadPdfAndAssertAsync(host, pdf, expectedPages: 1, "GeneratePdfNative (Typeface)");
    }

    private static async Task LoadPdfAndAssertAsync(TestHost host, byte[] pdf, int expectedPages, string origem)
    {
        Assert.True(pdf.Length > 100, $"{origem}: PDF gerado suspeito ({pdf.Length} bytes)");

        var path = Path.Combine(FileSystem.CacheDirectory, $"devicetest-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, pdf);

        var viewer = new PdfViewer { Source = path };

        var loaded = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? failMessage = null;
        viewer.DocumentLoaded += (_, e) => loaded.TrySetResult(e.PageCount);
        viewer.DocumentLoadFailed += (_, e) =>
        {
            failMessage = e.Message;
            loaded.TrySetException(new InvalidOperationException($"DocumentLoadFailed: {e.Message}"));
        };

        try
        {
            await host.MountAsync(viewer);
            await host.WaitForAsync(loaded.Task, 30_000, $"{origem}: DocumentLoaded");

            var pageCount = await loaded.Task;
            Assert.True(pageCount == expectedPages,
                $"{origem}: PageCount do evento = {pageCount}, esperado {expectedPages}");
            Assert.True(viewer.PageCount == expectedPages,
                $"{origem}: viewer.PageCount = {viewer.PageCount}, esperado {expectedPages}");

            // Rasterização real: o handler aplica o bitmap da página numa ImageView nativa
            // (PdfPageImageView). Espera alguma ImageView com drawable não nulo.
            var root = viewer.Handler?.PlatformView as global::Android.Views.View
                ?? throw new InvalidOperationException($"{origem}: platform view nulo após mount");
            await host.WaitForAsync(
                () => AndroidTreeScan.Descendants(root)
                    .OfType<global::Android.Widget.ImageView>()
                    .Any(iv => iv.Drawable is not null),
                20_000,
                $"{origem}: primeira página rasterizada (ImageView com bitmap)");

            Assert.True(failMessage is null, $"{origem}: DocumentLoadFailed disparou: {failMessage}");
        }
        finally
        {
            await host.UnmountAsync();
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
#endif
