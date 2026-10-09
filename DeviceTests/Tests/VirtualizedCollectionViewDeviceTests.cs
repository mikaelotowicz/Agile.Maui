using Xunit;

namespace Agile.Maui.DeviceTests;

public static class VirtualizedCollectionViewDeviceTests
{
#if ANDROID
    /// <summary>
    /// Ciclo de vida completo no Android: montar com 500 itens, rolar até o fim, trocar o
    /// template em runtime, esvaziar/repopular e desmontar (DisconnectHandler) SEM exceção —
    /// regressão do teardown (NotSupportedException "Unable to activate instance ... from
    /// native handle" quando o adapter era disposto antes de SetAdapter(null)).
    /// </summary>
    [DeviceFact(Platforms.Android, TimeoutSeconds = 120)]
    public static async Task VirtualizedCollectionView_Monta_Rola_TrocaTemplate_Repopula_E_Desmonta(TestHost host)
    {
        var items = new ObservableRangeCollection<string>();
        items.AddRange(Enumerable.Range(0, 500).Select(i => $"Item {i}"));

        var vcv = new VirtualizedCollectionView
        {
            ItemsSource = items,
            ItemSizingStrategy = ItemSizingStrategy.Fixed,
            ItemHeight = 48,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { VerticalOptions = LayoutOptions.Center, Margin = new Thickness(12, 0) };
                label.SetBinding(Label.TextProperty, ".");
                return new Grid { HeightRequest = 48, Children = { label } };
            }),
        };

        await host.MountAsync(vcv);

        var rv = FindRecyclerView(vcv)
            ?? throw new InvalidOperationException("RecyclerView nativo não encontrado no platform view");

        // Células criadas, mas virtualizadas (muito menos que os 500 itens).
        await host.WaitForAsync(() => rv.ChildCount > 0, 10_000, "RecyclerView criar células visíveis");
        Assert.Equal(500, rv.GetAdapter()?.ItemCount ?? -1);
        Assert.True(rv.ChildCount < 100,
            $"virtualização quebrada: {rv.ChildCount} células nativas para 500 itens");

        // Scroll programático até o fim.
        vcv.ScrollTo(499, animated: false);
        await host.WaitForAsync(
            () => LastVisible(rv) >= 499,
            10_000,
            $"última célula visível == 499 (atual: {LastVisible(rv)})");

        // Troca de ItemTemplate em runtime.
        vcv.ItemTemplate = new DataTemplate(() =>
        {
            var label = new Label { FontAttributes = FontAttributes.Bold, Margin = new Thickness(24, 0) };
            label.SetBinding(Label.TextProperty, ".");
            return new Grid { HeightRequest = 48, Children = { label } };
        });
        await host.PumpAsync(300);
        await host.WaitForAsync(() => rv.ChildCount > 0, 10_000, "células recriadas após troca de template");

        // Esvaziar e repopular.
        items.ReplaceAll(Array.Empty<string>());
        await host.WaitForAsync(() => (rv.GetAdapter()?.ItemCount ?? -1) == 0, 10_000, "adapter zerado após ReplaceAll vazio");

        items.AddRange(Enumerable.Range(0, 100).Select(i => $"Repop {i}"));
        await host.WaitForAsync(() => (rv.GetAdapter()?.ItemCount ?? -1) == 100, 10_000, "adapter com 100 itens após repopular");
        await host.WaitForAsync(() => rv.ChildCount > 0, 10_000, "células visíveis após repopular");

        // Teardown: remover da árvore + DisconnectHandler não pode lançar.
        await host.UnmountAsync();
        Assert.Null(vcv.Handler);
    }

    private static AndroidX.RecyclerView.Widget.RecyclerView? FindRecyclerView(VirtualizedCollectionView vcv)
    {
        var root = vcv.Handler?.PlatformView as global::Android.Views.View;
        return root is null
            ? null
            : AndroidTreeScan.Descendants(root).OfType<AndroidX.RecyclerView.Widget.RecyclerView>().FirstOrDefault();
    }

    private static int LastVisible(AndroidX.RecyclerView.Widget.RecyclerView rv) =>
        (rv.GetLayoutManager() as AndroidX.RecyclerView.Widget.LinearLayoutManager)
            ?.FindLastVisibleItemPosition() ?? -1;
#endif

#if WINDOWS
    /// <summary>
    /// Smoke test Windows: no Windows o controle usa o CollectionView do MAUI como Content
    /// (sem handler customizado) — monta com itens, verifica layout real e desmonta sem exceção.
    /// </summary>
    [DeviceFact(Platforms.Windows, TimeoutSeconds = 60)]
    public static async Task VirtualizedCollectionView_Windows_Monta_E_Desmonta_Sem_Excecao(TestHost host)
    {
        var items = new ObservableRangeCollection<string>();
        items.AddRange(Enumerable.Range(0, 200).Select(i => $"Item {i}"));

        var vcv = new VirtualizedCollectionView
        {
            ItemsSource = items,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { Margin = new Thickness(12, 4) };
                label.SetBinding(Label.TextProperty, ".");
                return label;
            }),
        };

        await host.MountAsync(vcv);

        var cv = vcv.Content as CollectionView;
        Assert.True(cv is not null, "no Windows o Content deveria ser o CollectionView do MAUI");
        Assert.True(cv!.Width > 0 && cv.Height > 0, $"CollectionView sem tamanho real: {cv.Width}x{cv.Height}");

        await host.UnmountAsync();
        Assert.Null(vcv.Handler);
    }
#endif
}
