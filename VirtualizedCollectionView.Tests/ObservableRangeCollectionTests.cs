using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// A ObservableRangeCollection é a fonte que os handlers Android/iOS traduzem evento a
// evento (Add → InsertRange+notify range; Reset → recarga completa). Estes testes fixam
// o contrato de que essas traduções dependem: UM evento por lote, índices em coordenadas
// finais e estado da coleção já atualizado dentro do handler.
public class ObservableRangeCollectionTests
{
    private static List<NotifyCollectionChangedEventArgs> CapturarEventos(
        ObservableRangeCollection<int> colecao)
    {
        var eventos = new List<NotifyCollectionChangedEventArgs>();
        colecao.CollectionChanged += (_, e) => eventos.Add(e);
        return eventos;
    }

    private static List<string?> CapturarPropertyChanged(INotifyPropertyChanged fonte)
    {
        var nomes = new List<string?>();
        fonte.PropertyChanged += (_, e) => nomes.Add(e.PropertyName);
        return nomes;
    }

    // ── AddRange ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddRange_em_colecao_vazia_emite_um_unico_Add_com_indice_zero_e_itens_na_ordem()
    {
        var colecao = new ObservableRangeCollection<int>();
        var eventos = CapturarEventos(colecao);

        colecao.AddRange([10, 20, 30]);

        var evento = Assert.Single(eventos);
        Assert.Equal(NotifyCollectionChangedAction.Add, evento.Action);
        Assert.Equal(0, evento.NewStartingIndex);
        Assert.Equal(new object[] { 10, 20, 30 }, evento.NewItems!.Cast<object>().ToArray());
        Assert.Equal([10, 20, 30], colecao);
    }

    [Fact]
    public void AddRange_em_colecao_preenchida_usa_o_Count_anterior_como_NewStartingIndex()
    {
        var colecao = new ObservableRangeCollection<int> { 1, 2 };
        var eventos = CapturarEventos(colecao);

        colecao.AddRange([3, 4]);

        var evento = Assert.Single(eventos);
        Assert.Equal(2, evento.NewStartingIndex);
        Assert.Equal([1, 2, 3, 4], colecao);
    }

    [Fact]
    public void AddRange_emite_exatamente_um_evento_para_muitos_itens()
    {
        var colecao = new ObservableRangeCollection<int>();
        var eventos = CapturarEventos(colecao);

        colecao.AddRange(Enumerable.Range(0, 500));

        Assert.Single(eventos);
        Assert.Equal(500, colecao.Count);
    }

    [Fact]
    public void AddRange_vazio_nao_emite_evento_nenhum()
    {
        var colecao = new ObservableRangeCollection<int> { 1 };
        var eventos = CapturarEventos(colecao);
        var nomes = CapturarPropertyChanged(colecao);

        colecao.AddRange([]);

        Assert.Empty(eventos);
        Assert.Empty(nomes);
        Assert.Equal([1], colecao);
    }

    [Fact]
    public void AddRange_nulo_lanca_ArgumentNullException()
    {
        var colecao = new ObservableRangeCollection<int>();

        Assert.Throws<ArgumentNullException>(() => colecao.AddRange(null!));
    }

    [Fact]
    public void AddRange_itens_ficam_nas_coordenadas_finais_indicadas_pelo_evento()
    {
        // Invariante dos handlers: colecao[NewStartingIndex + i] == NewItems[i]
        // (iOS InsertItems usa coordenadas finais; Android InsertRange + notify range).
        var colecao = new ObservableRangeCollection<string> { "a", "b" };
        NotifyCollectionChangedEventArgs? capturado = null;
        colecao.CollectionChanged += (_, e) => capturado = e;

        colecao.AddRange(["c", "d", "e"]);

        Assert.NotNull(capturado);
        for (var i = 0; i < capturado.NewItems!.Count; i++)
            Assert.Equal(capturado.NewItems[i], colecao[capturado.NewStartingIndex + i]);
    }

    [Fact]
    public void AddRange_Count_ja_reflete_os_novos_itens_dentro_do_handler()
    {
        // Os handlers tiram snapshot da fonte DURANTE o evento (ReloadItems) — o estado
        // precisa estar completo antes do CollectionChanged disparar.
        var colecao = new ObservableRangeCollection<int> { 1 };
        var countNoHandler = -1;
        colecao.CollectionChanged += (_, _) => countNoHandler = colecao.Count;

        colecao.AddRange([2, 3]);

        Assert.Equal(3, countNoHandler);
    }

    [Fact]
    public void AddRange_notifica_Count_e_indexador()
    {
        var colecao = new ObservableRangeCollection<int>();
        var nomes = CapturarPropertyChanged(colecao);

        colecao.AddRange([1, 2]);

        Assert.Contains(nameof(colecao.Count), nomes);
        Assert.Contains("Item[]", nomes);
    }

    [Fact]
    public void AddRange_copia_a_entrada_mutacao_posterior_nao_afeta_a_colecao()
    {
        var colecao = new ObservableRangeCollection<int>();
        var entrada = new List<int> { 1, 2 };

        colecao.AddRange(entrada);
        entrada.Add(99);
        entrada[0] = -1;

        Assert.Equal([1, 2], colecao);
    }

    [Fact]
    public void AddRange_enumera_a_entrada_uma_unica_vez()
    {
        var colecao = new ObservableRangeCollection<int>();
        var fonte = new FonteContada([1, 2, 3]);

        colecao.AddRange(fonte);

        Assert.Equal(1, fonte.Enumeracoes);
        Assert.Equal([1, 2, 3], colecao);
    }

    [Fact]
    public void AddRange_evento_tem_acao_Add_e_OldItems_nulo()
    {
        var colecao = new ObservableRangeCollection<int>();
        NotifyCollectionChangedEventArgs? capturado = null;
        colecao.CollectionChanged += (_, e) => capturado = e;

        colecao.AddRange([7]);

        Assert.NotNull(capturado);
        Assert.Equal(NotifyCollectionChangedAction.Add, capturado.Action);
        Assert.Null(capturado.OldItems);
        Assert.Equal(-1, capturado.OldStartingIndex);
    }

    // ── ReplaceAll ───────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_de_vazio_para_vazio_nao_emite_evento()
    {
        var colecao = new ObservableRangeCollection<int>();
        var eventos = CapturarEventos(colecao);
        var nomes = CapturarPropertyChanged(colecao);

        colecao.ReplaceAll([]);

        Assert.Empty(eventos);
        Assert.Empty(nomes);
    }

    [Fact]
    public void ReplaceAll_de_vazio_para_itens_emite_Add_em_zero_e_nao_Reset()
    {
        // Contrato: a transição 0→N vira Add com range — os handlers aplicam
        // incrementalmente (com animação) em vez de recarregar tudo.
        var colecao = new ObservableRangeCollection<int>();
        var eventos = CapturarEventos(colecao);

        colecao.ReplaceAll([1, 2, 3]);

        var evento = Assert.Single(eventos);
        Assert.Equal(NotifyCollectionChangedAction.Add, evento.Action);
        Assert.Equal(0, evento.NewStartingIndex);
        Assert.Equal(3, evento.NewItems!.Count);
        Assert.Equal([1, 2, 3], colecao);
    }

    [Fact]
    public void ReplaceAll_com_conteudo_anterior_emite_um_unico_Reset()
    {
        var colecao = new ObservableRangeCollection<int> { 1, 2 };
        var eventos = CapturarEventos(colecao);

        colecao.ReplaceAll([3, 4, 5]);

        var evento = Assert.Single(eventos);
        Assert.Equal(NotifyCollectionChangedAction.Reset, evento.Action);
        Assert.Equal([3, 4, 5], colecao);
    }

    [Fact]
    public void ReplaceAll_para_vazio_emite_Reset_e_esvazia()
    {
        var colecao = new ObservableRangeCollection<int> { 1, 2 };
        var eventos = CapturarEventos(colecao);

        colecao.ReplaceAll([]);

        var evento = Assert.Single(eventos);
        Assert.Equal(NotifyCollectionChangedAction.Reset, evento.Action);
        Assert.Empty(colecao);
    }

    [Fact]
    public void ReplaceAll_conteudo_final_ja_esta_na_colecao_dentro_do_handler_do_Reset()
    {
        // Os handlers respondem a Reset com recarga total a partir da fonte viva —
        // o conteúdo novo precisa estar lá quando o evento dispara.
        var colecao = new ObservableRangeCollection<int> { 1 };
        int[]? vistoNoHandler = null;
        colecao.CollectionChanged += (_, _) => vistoNoHandler = [.. colecao];

        colecao.ReplaceAll([8, 9]);

        Assert.NotNull(vistoNoHandler);
        Assert.Equal([8, 9], vistoNoHandler);
    }

    [Fact]
    public void ReplaceAll_nulo_lanca_ArgumentNullException()
    {
        var colecao = new ObservableRangeCollection<int>();

        Assert.Throws<ArgumentNullException>(() => colecao.ReplaceAll(null!));
    }

    [Fact]
    public void ReplaceAll_com_a_propria_colecao_mantem_o_conteudo()
    {
        // O snapshot (ToList) acontece antes do Clear — passar a própria coleção é seguro.
        var colecao = new ObservableRangeCollection<int> { 1, 2, 3 };

        colecao.ReplaceAll(colecao);

        Assert.Equal([1, 2, 3], colecao);
    }

    [Fact]
    public void ReplaceAll_com_mesmo_conteudo_ainda_emite_Reset()
    {
        // Contrato atual: não há comparação de igualdade — Reset sempre que havia itens.
        var colecao = new ObservableRangeCollection<int> { 1, 2 };
        var eventos = CapturarEventos(colecao);

        colecao.ReplaceAll([1, 2]);

        var evento = Assert.Single(eventos);
        Assert.Equal(NotifyCollectionChangedAction.Reset, evento.Action);
    }

    [Fact]
    public void ReplaceAll_notifica_Count_e_indexador()
    {
        var colecao = new ObservableRangeCollection<int> { 1 };
        var nomes = CapturarPropertyChanged(colecao);

        colecao.ReplaceAll([2, 3]);

        Assert.Contains(nameof(colecao.Count), nomes);
        Assert.Contains("Item[]", nomes);
    }

    // ── Reentrância ──────────────────────────────────────────────────────────

    [Fact]
    public void Reentrancia_com_dois_inscritos_e_bloqueada()
    {
        // Contrato herdado de ObservableCollection: mutar dentro do handler com 2+
        // inscritos lança InvalidOperationException (CheckReentrancy).
        var colecao = new ObservableRangeCollection<int>();
        var verificou = false;
        colecao.CollectionChanged += (_, _) =>
        {
            if (verificou) return;
            verificou = true;
            Assert.Throws<InvalidOperationException>(() => colecao.AddRange([99]));
        };
        colecao.CollectionChanged += (_, _) => { };   // 2º inscrito ativa o guard

        colecao.AddRange([1]);

        Assert.True(verificou);
        Assert.Equal([1], colecao);
    }

    [Fact]
    public void Reentrancia_com_um_unico_inscrito_e_permitida()
    {
        var colecao = new ObservableRangeCollection<int>();
        var profundidade = 0;
        colecao.CollectionChanged += (_, _) =>
        {
            if (profundidade++ == 0)
                colecao.AddRange([2]);
        };

        colecao.AddRange([1]);

        Assert.Equal([1, 2], colecao);
    }

    // ── Consistência da tradução dos handlers ────────────────────────────────

    [Fact]
    public void Espelho_que_segue_a_traducao_dos_handlers_permanece_consistente()
    {
        // Reproduz a tabela de tradução dos handlers (Android: mutação+notify por evento;
        // iOS: um batch por evento): se o espelho divergir em qualquer passo, a tradução
        // nativa produziria IndexOutOfBounds/NSInternalInconsistencyException.
        var colecao = new ObservableRangeCollection<string>();
        var espelho = new List<string>();

        colecao.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    espelho.InsertRange(e.NewStartingIndex, e.NewItems!.Cast<string>());
                    break;
                case NotifyCollectionChangedAction.Remove:
                    espelho.RemoveRange(e.OldStartingIndex, e.OldItems!.Count);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    for (var i = 0; i < e.NewItems!.Count; i++)
                        espelho[e.NewStartingIndex + i] = (string)e.NewItems[i]!;
                    break;
                case NotifyCollectionChangedAction.Move:
                    var movido = espelho[e.OldStartingIndex];
                    espelho.RemoveAt(e.OldStartingIndex);
                    espelho.Insert(e.NewStartingIndex, movido);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    espelho.Clear();
                    espelho.AddRange(colecao);
                    break;
            }

            Assert.Equal(colecao, espelho);
        };

        colecao.AddRange(["a", "b", "c"]);      // Add range em vazio
        colecao.Add("d");                        // Add unitário da base
        colecao.Insert(1, "e");                  // Add no meio
        colecao.AddRange(["f", "g"]);            // Add range no fim
        colecao.RemoveAt(0);                     // Remove unitário
        colecao.Move(0, 3);                      // Move
        colecao[2] = "z";                        // Replace via indexador
        colecao.Remove("z");                     // Remove por valor
        colecao.ReplaceAll(["x", "y"]);          // Reset
        colecao.AddRange(["w"]);                 // Add depois do Reset
        colecao.Clear();                         // Reset da base

        Assert.Equal(colecao, espelho);
        Assert.Empty(colecao);
    }

    [Fact]
    public void Sequencia_de_lotes_mantem_Count_e_indices_consistentes()
    {
        var colecao = new ObservableRangeCollection<int>();
        var indices = new List<int>();
        colecao.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                indices.Add(e.NewStartingIndex);
        };

        colecao.AddRange([1, 2]);    // índice 0
        colecao.AddRange([3]);       // índice 2
        colecao.RemoveAt(0);         // sobra [2,3]
        colecao.AddRange([4, 5]);    // índice 2

        Assert.Equal([0, 2, 2], indices);
        Assert.Equal([2, 3, 4, 5], colecao);
    }

    /// <summary>IEnumerable que conta quantas vezes foi enumerado.</summary>
    private sealed class FonteContada(int[] itens) : IEnumerable<int>
    {
        public int Enumeracoes { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            Enumeracoes++;
            return ((IEnumerable<int>)itens).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
