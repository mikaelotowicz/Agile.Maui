using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Regressão do vazamento de Typeface no AndroidPdfRenderer: o objeto nativo da fonte embutida
/// deve ser criado uma vez por conteúdo de fonte no processo, não uma vez por documento (o
/// Android nunca libera Typeface de CreateFromFile). Pula graciosamente sem fonte de sistema.
/// </summary>
public class EmbeddedFontCacheTests
{
    static byte[]? FontBytes()
    {
        string[] candidates =
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        };

        string? path = candidates.FirstOrDefault(File.Exists);
        return path is null ? null : File.ReadAllBytes(path);
    }

    // Mesma fonte com padding no fim: continua válida, mas com conteúdo (e hash) diferente.
    static byte[] Variant(byte[] data) => data.Concat(new byte[4]).ToArray();

    [Fact]
    public void Mesma_instancia_em_varios_documentos_cria_uma_vez()
    {
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        var font = EmbeddedFont.Load(data);
        int created = 0;

        object? first = null;
        for (int doc = 0; doc < 20; doc++)
        {
            object? got = cache.GetOrCreate(font, _ => { created++; return new object(); });
            first ??= got;
            Assert.Same(first, got);
        }

        Assert.Equal(1, created);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Instancias_distintas_com_os_mesmos_bytes_compartilham_a_entrada()
    {
        // App que recarrega a fonte (EmbeddedFont.FromFile) a cada documento.
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        int created = 0;

        object? a = cache.GetOrCreate(EmbeddedFont.Load(data), _ => { created++; return new object(); });
        object? b = cache.GetOrCreate(EmbeddedFont.Load(data.ToArray()), _ => { created++; return new object(); });

        Assert.Same(a, b);
        Assert.Equal(1, created);
    }

    [Fact]
    public void Conteudos_diferentes_criam_entradas_separadas()
    {
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        int created = 0;

        object? a = cache.GetOrCreate(EmbeddedFont.Load(data), _ => { created++; return new object(); });
        object? b = cache.GetOrCreate(EmbeddedFont.Load(Variant(data)), _ => { created++; return new object(); });

        Assert.NotSame(a, b);
        Assert.Equal(2, created);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Falha_fica_cacheada_sem_nova_tentativa()
    {
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        var font = EmbeddedFont.Load(data);
        int attempts = 0;

        Assert.Null(cache.GetOrCreate(font, _ => { attempts++; return null; }));
        Assert.Null(cache.GetOrCreate(font, _ => { attempts++; return new object(); }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Excecao_da_fabrica_nao_envenena_o_cache()
    {
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        var font = EmbeddedFont.Load(data);

        Assert.Throws<IOException>(() => cache.GetOrCreate(font, _ => throw new IOException("disco cheio")));

        var created = new object();
        Assert.Same(created, cache.GetOrCreate(font, _ => created));
    }

    [Fact]
    public void Renderizacoes_concorrentes_criam_uma_vez()
    {
        byte[]? data = FontBytes();
        if (data is null)
            return;

        var cache = new EmbeddedFontCache<object>();
        int created = 0;
        var fonts = Enumerable.Range(0, 8).Select(_ => EmbeddedFont.Load(data)).ToArray();

        object?[] results = new object?[64];
        Parallel.For(0, results.Length, i =>
            results[i] = cache.GetOrCreate(fonts[i % fonts.Length], _ =>
            {
                Interlocked.Increment(ref created);
                return new object();
            }));

        Assert.Equal(1, created);
        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
