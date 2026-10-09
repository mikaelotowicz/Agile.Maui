namespace Agile.Maui;

/// <summary>Caminho usado pelo ScrollToStart no Windows.</summary>
internal enum ScrollToStartRoute
{
    /// <summary>Nada a fazer.</summary>
    None,
    /// <summary>ScrollViewer/ScrollView nativo: só move o offset.</summary>
    PlatformScroller,
    /// <summary>ItemsView.ScrollTo(0) do CollectionView do MAUI.</summary>
    ItemsViewScrollTo,
}

internal static class ScrollToStartRouting
{
    /// <summary>
    /// Rota do ScrollToStart no Windows. O scroller nativo só move o offset; o ItemsView.ScrollTo do
    /// MAUI vira StartBringItemIntoView, que no handler Items2 (MAUI 11) realiza os itens de forma
    /// síncrona quando chamado logo após um lote grande, antes do layout — travou a UI com 5.000
    /// itens. Por isso a volta automática ao topo (transição vazio→preenchido) nunca usa o ItemsView:
    /// sem scroller nativo não há o que fazer, porque a lista que estava vazia já está no offset 0.
    /// </summary>
    internal static ScrollToStartRoute Resolve(bool hasPlatformScroller, bool afterDataRefresh) =>
        hasPlatformScroller ? ScrollToStartRoute.PlatformScroller
        : afterDataRefresh ? ScrollToStartRoute.None
        : ScrollToStartRoute.ItemsViewScrollTo;
}
