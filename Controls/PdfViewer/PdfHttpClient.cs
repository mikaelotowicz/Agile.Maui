namespace Agile.Maui;

/// <summary>
/// Fábrica de HttpClient para download de PDFs.
/// Configura headers completos de browser + proxy do sistema + decompressão automática.
/// </summary>
internal static class PdfHttpClient
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression   = System.Net.DecompressionMethods.All,
            AllowAutoRedirect        = true,
            MaxAutomaticRedirections = 10,
            UseCookies               = true,
            UseProxy                 = true,               // respeita proxy do sistema
            Proxy                    = System.Net.WebRequest.GetSystemWebProxy(),
        };

        var client = new HttpClient(handler);
        var h = client.DefaultRequestHeaders;

        h.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        h.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9," +
            "application/pdf,image/webp,image/apng,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language",
            "pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
        h.TryAddWithoutValidation("Connection",                 "keep-alive");
        h.TryAddWithoutValidation("Upgrade-Insecure-Requests",  "1");
        h.TryAddWithoutValidation("Sec-Fetch-Dest",             "document");
        h.TryAddWithoutValidation("Sec-Fetch-Mode",             "navigate");
        h.TryAddWithoutValidation("Sec-Fetch-Site",             "none");
        h.TryAddWithoutValidation("Sec-Fetch-User",             "?1");
        h.TryAddWithoutValidation("Cache-Control",              "max-age=0");

        return client;
    }
}
