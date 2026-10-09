using System.Collections.Concurrent;
using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// Configuração do <see cref="PdfHttpClient"/> (sem rede real — apenas o contrato da instância).
/// </summary>
public class PdfHttpClientTests
{
    [Fact]
    public void Shared_e_singleton()
    {
        var a = PdfHttpClient.Shared;
        var b = PdfHttpClient.Shared;
        Assert.Same(a, b);
    }

    [Fact]
    public void Shared_tem_timeout_explicito_de_60s()
        => Assert.Equal(TimeSpan.FromSeconds(60), PdfHttpClient.Shared.Timeout);

    [Fact]
    public void Shared_e_thread_safe_na_inicializacao()
    {
        // Lazy<T> (ExecutionAndPublication): acesso concorrente sempre entrega a MESMA instância.
        var instancias = new ConcurrentBag<HttpClient>();
        Parallel.For(0, 64, _ => instancias.Add(PdfHttpClient.Shared));

        Assert.Equal(64, instancias.Count);
        Assert.All(instancias, c => Assert.Same(PdfHttpClient.Shared, c));
    }

    [Fact]
    public void Shared_envia_User_Agent_de_browser()
    {
        var ua = PdfHttpClient.Shared.DefaultRequestHeaders.TryGetValues("User-Agent", out var valores)
            ? string.Join(" ", valores)
            : null;
        Assert.NotNull(ua);
        Assert.Contains("Mozilla/5.0", ua);
    }

    [Fact]
    public void Shared_aceita_application_pdf()
    {
        var accept = PdfHttpClient.Shared.DefaultRequestHeaders.TryGetValues("Accept", out var valores)
            ? string.Join(",", valores)
            : null;
        Assert.NotNull(accept);
        Assert.Contains("application/pdf", accept);
    }
}
