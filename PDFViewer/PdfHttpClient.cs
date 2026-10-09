namespace Agile.Maui;

/// <summary>
/// HttpClient COMPARTILHADO para download de PDFs.
/// Instância única por processo (criar/dispor um client por request esgota portas — guideline
/// oficial: https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines),
/// com PooledConnectionLifetime para respeitar mudanças de DNS e Timeout explícito.
/// Configura headers completos de browser + proxy do sistema + decompressão automática.
/// </summary>
internal static class PdfHttpClient
{
    private static readonly Lazy<HttpClient> _shared = new(CreateClient);

    /// <summary>Instância compartilhada — NÃO dispor.</summary>
    public static HttpClient Shared => _shared.Value;

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // renova conexões (DNS) sem recriar o client
            AutomaticDecompression   = System.Net.DecompressionMethods.All,
            AllowAutoRedirect        = true,
            MaxAutomaticRedirections = 10,
            UseCookies               = true,
            UseProxy                 = true,               // respeita proxy do sistema
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
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
