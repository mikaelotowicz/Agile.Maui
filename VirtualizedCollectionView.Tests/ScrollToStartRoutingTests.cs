using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// Rota do ScrollToStart no Windows. Regressão: a volta automática ao topo após vazio→preenchido
// caía no ItemsView.ScrollTo(0) quando a lista nasceu oculta (scroller nativo ainda não achado);
// no handler Items2 do MAUI 11 isso realiza os itens de forma síncrona e travou a UI com 5.000 itens.
public class ScrollToStartRoutingTests
{
    [Fact]
    public void Volta_automatica_ao_topo_sem_scroller_nativo_nao_usa_o_ItemsView()
    {
        Assert.Equal(ScrollToStartRoute.None,
            ScrollToStartRouting.Resolve(hasPlatformScroller: false, afterDataRefresh: true));
    }

    [Fact]
    public void Volta_automatica_ao_topo_com_scroller_nativo_so_move_o_offset()
    {
        Assert.Equal(ScrollToStartRoute.PlatformScroller,
            ScrollToStartRouting.Resolve(hasPlatformScroller: true, afterDataRefresh: true));
    }

    [Fact]
    public void ScrollToStart_explicito_prefere_o_scroller_nativo()
    {
        Assert.Equal(ScrollToStartRoute.PlatformScroller,
            ScrollToStartRouting.Resolve(hasPlatformScroller: true, afterDataRefresh: false));
    }

    [Fact]
    public void ScrollToStart_explicito_sem_scroller_nativo_recai_no_ItemsView()
    {
        Assert.Equal(ScrollToStartRoute.ItemsViewScrollTo,
            ScrollToStartRouting.Resolve(hasPlatformScroller: false, afterDataRefresh: false));
    }
}
