using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Agile.Maui.PdfGen.Text;

/// <summary>
/// Cache por processo de objetos nativos derivados de uma <see cref="EmbeddedFont"/>, chaveado
/// pelo conteúdo da fonte: instâncias distintas carregadas dos mesmos bytes (ex.: o app chama
/// <see cref="EmbeddedFont.FromFile"/> a cada documento) compartilham a mesma entrada.
/// Existe para backends cujo objeto nativo a plataforma nunca libera — o Typeface do Android
/// criado por CreateFromFile —, em que criar um por documento cresce sem limite; aqui o custo
/// fica limitado ao número de fontes distintas. Falhas (fábrica retornando null) também ficam
/// cacheadas: a fonte recai na do sistema sem nova tentativa.
/// </summary>
internal sealed class EmbeddedFontCache<T> where T : class
{
    readonly object _gate = new();
    readonly Dictionary<string, T?> _byContent = new(StringComparer.Ordinal);
    // Atalho por instância: o hash dos bytes (centenas de KB) é calculado uma vez por EmbeddedFont.
    readonly ConditionalWeakTable<EmbeddedFont, string> _keys = new();

    /// <summary>
    /// Devolve o objeto da fonte, criando-o com <paramref name="factory"/> só na primeira vez
    /// que esse conteúdo aparece no processo. A fábrica roda sob lock (no máximo uma criação
    /// por conteúdo, mesmo com renderizações concorrentes); se ela lançar, nada é cacheado.
    /// </summary>
    public T? GetOrCreate(EmbeddedFont font, Func<EmbeddedFont, T?> factory)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(factory);

        string key = _keys.GetValue(font, static f => Convert.ToHexString(SHA256.HashData(f.FontData)));

        lock (_gate)
        {
            if (_byContent.TryGetValue(key, out T? cached))
                return cached;

            T? created = factory(font);
            _byContent[key] = created;
            return created;
        }
    }

    /// <summary>Número de conteúdos de fonte distintos já resolvidos (inclui falhas).</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
                return _byContent.Count;
        }
    }
}
