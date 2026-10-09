using Xunit;

namespace Agile.Maui.DeviceTests;

public static class ChipGroupDeviceTests
{
    [DeviceFact(Platforms.All, TimeoutSeconds = 60)]
    public static async Task ChipGroup_Renderiza_Chips_E_Tap_Programatico_Seleciona(TestHost host)
    {
        var chips = new ChipGroup
        {
            ItemsSource = new List<string> { "Um", "Dois", "Três" },
            SelectionMode = ChipSelectionMode.Single,
        };

        object? eventSelectedItem = null;
        var selectionRaised = false;
        chips.SelectionChanged += (_, e) =>
        {
            selectionRaised = true;
            eventSelectedItem = e.SelectedItem;
        };

        await host.MountAsync(chips);

        // O rebuild dos chips é coalescido e despachado na UI thread: espera os 3 Borders
        // existirem e terem tamanho real.
        await host.WaitForAsync(
            () => FindChipBorders(chips).Count == 3,
            5_000,
            "3 chips (Borders) montados no FlexLayout");
        await host.WaitForAsync(
            () => FindChipBorders(chips).All(b => b.Width > 0 && b.Height > 0),
            5_000,
            "chips com largura/altura > 0 após layout");

        var borders = FindChipBorders(chips);
        foreach (var border in borders)
            Assert.True(border.Width > 0 && border.Height > 0,
                $"chip sem tamanho real: {border.Width}x{border.Height}");

        // Tap programático no 2º chip: executa o Command do TapGestureRecognizer,
        // o mesmo caminho do toque real.
        var tap = borders[1].GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
        Assert.True(tap?.Command is not null, "chip habilitado deveria ter TapGestureRecognizer com Command");
        tap!.Command!.Execute(null);

        await host.WaitForAsync(() => selectionRaised, 5_000, "evento SelectionChanged após o tap");
        Assert.Equal("Dois", chips.SelectedItem);
        Assert.Equal("Dois", eventSelectedItem);

        // O rebuild pós-seleção não pode derrubar os chips.
        await host.PumpAsync();
        Assert.Equal(3, FindChipBorders(chips).Count);

        await host.UnmountAsync();
    }

    /// <summary>Chips são Borders filhos do FlexLayout (Content no modo Wrap; dentro do ScrollView no Horizontal).</summary>
    private static List<Border> FindChipBorders(ChipGroup chips)
    {
        var layout = chips.Content as FlexLayout
            ?? (chips.Content as ScrollView)?.Content as FlexLayout;
        return layout?.Children.OfType<Border>().ToList() ?? new List<Border>();
    }
}
