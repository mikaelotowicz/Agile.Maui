// Controls/AgilePdfViewer/IPdfEngine.cs
namespace Agile.Maui;

/// <summary>
/// Abstração sobre o motor de renderização de PDF (PDFium via APIs nativas do SO).
/// Android: android.graphics.pdf.PdfRenderer (internamente PDFium).
/// iOS/Mac: CoreGraphics.CGPDFDocument.
/// Windows: Windows.Data.Pdf.PdfDocument.
/// </summary>
internal interface IPdfEngine : IDisposable
{
    bool IsOpen { get; }
    int  PageCount { get; }

    Task<bool> OpenAsync(string path,  string? password = null, CancellationToken ct = default);
    Task<bool> OpenAsync(Stream stream, string? password = null, CancellationToken ct = default);
    void Close();

    /// <summary>Tamanho da página em pontos PDF (72 DPI), sem zoom.</summary>
    SizeF GetPageSize(int pageIndex);

    /// <summary>
    /// Renderiza uma página para pixels BGRA (4 bytes/pixel, pré-multiplicado).
    /// Retorna null se cancelado ou falhou.
    /// </summary>
    Task<byte[]?> RenderPageAsync(
        int pageIndex, int widthPx, int heightPx,
        uint backgroundColor = 0xFFFFFFFF,
        CancellationToken ct = default);

    /// <summary>Renderiza thumbnail em baixa resolução. Usado pelo ThumbnailBar.</summary>
    Task<byte[]?> RenderThumbnailAsync(
        int pageIndex, int targetWidth, int targetHeight,
        CancellationToken ct = default);

    /// <summary>Extrai texto da página (base para busca futura).</summary>
    Task<string> ExtractTextAsync(int pageIndex, CancellationToken ct = default);
}
