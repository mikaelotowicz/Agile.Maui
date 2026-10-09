using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// Os handlers nativos decidem se calculam offsets/threshold consultando
// HasScrolledObservers e CanRaiseRemainingItemsThresholdReached, e disparam os
// eventos públicos via RaiseScrolled/RaiseRemainingItemsThresholdReached (internals).
public class VirtualizedCollectionViewEventosTests
{
    // ── HasScrolledObservers ─────────────────────────────────────────────────

    [Fact]
    public void HasScrolledObservers_padrao_e_falso()
    {
        var view = new VirtualizedCollectionView();

        Assert.False(view.HasScrolledObservers);
    }

    [Fact]
    public void HasScrolledObservers_com_evento_inscrito_e_verdadeiro_e_volta_a_falso_ao_remover()
    {
        var view = new VirtualizedCollectionView();
        EventHandler<VirtualizedScrolledEventArgs> handler = (_, _) => { };

        view.Scrolled += handler;
        Assert.True(view.HasScrolledObservers);

        view.Scrolled -= handler;
        Assert.False(view.HasScrolledObservers);
    }

    [Fact]
    public void HasScrolledObservers_com_comando_e_verdadeiro()
    {
        var view = new VirtualizedCollectionView { ScrolledCommand = new ComandoDeTeste() };

        Assert.True(view.HasScrolledObservers);
    }

    // ── RaiseScrolled ────────────────────────────────────────────────────────

    [Fact]
    public void RaiseScrolled_dispara_o_evento_com_os_offsets()
    {
        var view = new VirtualizedCollectionView();
        VirtualizedScrolledEventArgs? recebido = null;
        view.Scrolled += (sender, e) =>
        {
            Assert.Same(view, sender);
            recebido = e;
        };

        view.RaiseScrolled(10.0, 25.5);

        Assert.NotNull(recebido);
        Assert.Equal(10.0, recebido.HorizontalOffset);
        Assert.Equal(25.5, recebido.VerticalOffset);
    }

    [Fact]
    public void RaiseScrolled_executa_o_comando_com_os_mesmos_args_do_evento()
    {
        var view = new VirtualizedCollectionView();
        var comando = new ComandoDeTeste();
        view.ScrolledCommand = comando;
        VirtualizedScrolledEventArgs? doEvento = null;
        view.Scrolled += (_, e) => doEvento = e;

        view.RaiseScrolled(1.0, 2.0);

        var parametro = Assert.Single(comando.Execucoes);
        Assert.Same(doEvento, parametro);   // uma única instância de args para ambos
        Assert.Same(parametro, Assert.Single(comando.ConsultasCanExecute));
    }

    [Fact]
    public void RaiseScrolled_nao_executa_comando_com_CanExecute_falso_mas_evento_dispara()
    {
        var view = new VirtualizedCollectionView();
        var comando = new ComandoDeTeste { PodeExecutar = false };
        view.ScrolledCommand = comando;
        var eventos = 0;
        view.Scrolled += (_, _) => eventos++;

        view.RaiseScrolled(0, 100);

        Assert.Equal(1, eventos);
        Assert.Empty(comando.Execucoes);
        Assert.Single(comando.ConsultasCanExecute);
    }

    [Fact]
    public void RaiseScrolled_sem_observadores_nao_lanca()
    {
        var view = new VirtualizedCollectionView();

        view.RaiseScrolled(3, 4);
    }

    // ── CanRaiseRemainingItemsThresholdReached ───────────────────────────────

    [Fact]
    public void CanRaise_threshold_padrao_e_falso()
    {
        var view = new VirtualizedCollectionView();

        Assert.False(view.CanRaiseRemainingItemsThresholdReached);
    }

    [Fact]
    public void CanRaise_threshold_com_evento_inscrito_e_verdadeiro()
    {
        var view = new VirtualizedCollectionView();
        view.RemainingItemsThresholdReached += (_, _) => { };

        Assert.True(view.CanRaiseRemainingItemsThresholdReached);
    }

    [Fact]
    public void CanRaise_threshold_depende_do_CanExecute_do_comando()
    {
        var comando = new ComandoDeTeste { PodeExecutar = true };
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };

        Assert.True(view.CanRaiseRemainingItemsThresholdReached);

        comando.PodeExecutar = false;
        Assert.False(view.CanRaiseRemainingItemsThresholdReached);
    }

    [Fact]
    public void CanRaise_threshold_consulta_CanExecute_com_parametro_nulo()
    {
        var comando = new ComandoDeTeste();
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };

        _ = view.CanRaiseRemainingItemsThresholdReached;

        Assert.Null(Assert.Single(comando.ConsultasCanExecute));
    }

    [Fact]
    public void CanRaise_threshold_com_comando_bloqueado_mas_evento_inscrito_e_verdadeiro()
    {
        var view = new VirtualizedCollectionView
        {
            RemainingItemsThresholdReachedCommand = new ComandoDeTeste { PodeExecutar = false },
        };
        view.RemainingItemsThresholdReached += (_, _) => { };

        Assert.True(view.CanRaiseRemainingItemsThresholdReached);
    }

    // ── RaiseRemainingItemsThresholdReached ──────────────────────────────────

    [Fact]
    public void RaiseThreshold_dispara_o_evento()
    {
        var view = new VirtualizedCollectionView();
        var disparos = 0;
        view.RemainingItemsThresholdReached += (sender, _) =>
        {
            Assert.Same(view, sender);
            disparos++;
        };

        view.RaiseRemainingItemsThresholdReached();

        Assert.Equal(1, disparos);
    }

    [Fact]
    public void RaiseThreshold_executa_o_comando_com_parametro_nulo()
    {
        var comando = new ComandoDeTeste();
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };

        view.RaiseRemainingItemsThresholdReached();

        Assert.Null(Assert.Single(comando.Execucoes));
    }

    [Fact]
    public void RaiseThreshold_evento_e_comando_disparam_juntos()
    {
        var comando = new ComandoDeTeste();
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };
        var eventos = 0;
        view.RemainingItemsThresholdReached += (_, _) => eventos++;

        view.RaiseRemainingItemsThresholdReached();

        Assert.Equal(1, eventos);
        Assert.Single(comando.Execucoes);
    }

    [Fact]
    public void RaiseThreshold_com_comando_bloqueado_e_sem_evento_nao_executa_nada()
    {
        var comando = new ComandoDeTeste { PodeExecutar = false };
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };

        view.RaiseRemainingItemsThresholdReached();

        Assert.Empty(comando.Execucoes);
    }

    [Fact]
    public void RaiseThreshold_com_comando_bloqueado_mas_evento_inscrito_dispara_so_o_evento()
    {
        var comando = new ComandoDeTeste { PodeExecutar = false };
        var view = new VirtualizedCollectionView { RemainingItemsThresholdReachedCommand = comando };
        var eventos = 0;
        view.RemainingItemsThresholdReached += (_, _) => eventos++;

        view.RaiseRemainingItemsThresholdReached();

        Assert.Equal(1, eventos);
        Assert.Empty(comando.Execucoes);
    }

    [Fact]
    public void RaiseThreshold_sem_observadores_nao_lanca()
    {
        var view = new VirtualizedCollectionView();

        view.RaiseRemainingItemsThresholdReached();
    }
}
