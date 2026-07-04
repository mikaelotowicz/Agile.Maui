namespace Agile.Maui.PdfGen.Rendering;

/// <summary>
/// Seleciona o renderer nativo da plataforma corrente (Android <c>PdfDocument</c>, iOS/Mac
/// <c>CGContextPDF</c>) e, onde não houver backend nativo, usa o escritor PDF gerenciado.
/// </summary>
public static class PlatformRenderer
{
    public static IPdfRenderer Create()
    {
#if ANDROID
        return new Platforms.Android.AndroidPdfRenderer();
#elif IOS || MACCATALYST
        return new Platforms.iOS.ApplePdfRenderer();
#else
        return new Pdf.ManagedPdfRenderer();
#endif
    }
}
