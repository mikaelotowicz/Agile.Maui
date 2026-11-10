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

    // Visto no sample em net11 (Android): no modo Horizontal os chips saíam cortados pela metade na
    // vertical — o FlexLayout do MAUI 11 sem quebra, num ScrollView horizontal, descontava a margem
    // inferior (RowSpacing) da altura do chip. O chip da faixa tem de ter a mesma altura do chip em
    // Wrap, e a faixa (ScrollView) tem de caber o chip inteiro. Em net10 passava antes do fix.
    [DeviceFact(Platforms.All, TimeoutSeconds = 60)]
    public static async Task ChipGroup_Horizontal_Nao_Corta_A_Altura_Dos_Chips(TestHost host)
    {
        var items = new List<string> { "All", "Popular", "Recent", "Design", "Development", "Marketing" };
        var wrap = new ChipGroup { ItemsSource = items, ShowCheckmark = false };
        var horizontal = new ChipGroup
        {
            ItemsSource = items,
            LayoutMode = ChipGroupLayoutMode.Horizontal,
            ShowCheckmark = false,
        };

        await host.MountAsync(new VerticalStackLayout { Spacing = 14, Children = { wrap, horizontal } });

        await host.WaitForAsync(
            () => FindChipBorders(wrap).Count == items.Count && FindChipBorders(horizontal).Count == items.Count
                  && FindChipBorders(wrap).All(b => b.Height > 0) && FindChipBorders(horizontal).All(b => b.Height > 0),
            5_000,
            "chips dos dois modos montados e com altura");
        await host.PumpAsync(300);

        var alturaWrap = FindChipBorders(wrap)[0].Height;
        var chip = FindChipBorders(horizontal)[0];
        var faixa = (ScrollView)horizontal.Content;

        var flex = (FlexLayout)faixa.Content;
        var label = chip.Content is Layout row ? row.Children.OfType<Label>().FirstOrDefault() : null;
        var medidas = $"group={horizontal.Width:F1}x{horizontal.Height:F1} scroll={faixa.Width:F1}x{faixa.Height:F1} " +
                      $"flex={flex.Width:F1}x{flex.Height:F1} chip={chip.Width:F1}x{chip.Height:F1} " +
                      $"chipDesired={chip.DesiredSize.Width:F1}x{chip.DesiredSize.Height:F1} " +
                      $"label={label?.Width:F1}x{label?.Height:F1} wrapChip={FindChipBorders(wrap)[0].Width:F1}x{alturaWrap:F1}";

        Assert.True(Math.Abs(chip.Height - alturaWrap) <= 1,
            $"chip horizontal com altura {chip.Height:F1}, em Wrap {alturaWrap:F1} — {medidas}");
        Assert.True(faixa.Height + 0.5 >= chip.Height,
            $"faixa horizontal ({faixa.Height:F1}) menor que o chip ({chip.Height:F1}): chip cortado");
        Assert.True(horizontal.Height + 0.5 >= chip.Height,
            $"ChipGroup horizontal ({horizontal.Height:F1}) menor que o chip ({chip.Height:F1}): chip cortado");

        await host.UnmountAsync();
    }

    // Visto no sample (Android, MAUI 10 e 11): no modo Vertical a base do texto saía cortada — com o
    // Shrink padrão a coluna descontava ~3 da altura do chip por causa da margem de RowSpacing.
    [DeviceFact(Platforms.All, TimeoutSeconds = 60)]
    public static async Task ChipGroup_Vertical_Nao_Encolhe_A_Altura_Dos_Chips(TestHost host)
    {
        var items = new List<string> { "Open", "In progress", "Waiting", "Closed" };
        var wrap = new ChipGroup { ItemsSource = items, ShowCheckmark = false };
        var vertical = new ChipGroup { ItemsSource = items, LayoutMode = ChipGroupLayoutMode.Vertical, ShowCheckmark = false };

        await host.MountAsync(new VerticalStackLayout { Spacing = 14, Children = { wrap, vertical } });
        await host.WaitForAsync(
            () => FindChipBorders(wrap).Count == items.Count && FindChipBorders(vertical).Count == items.Count
                  && FindChipBorders(wrap).All(b => b.Height > 0) && FindChipBorders(vertical).All(b => b.Height > 0),
            5_000,
            "chips dos dois modos montados e com altura");
        await host.PumpAsync(300);

        var wrapChips = FindChipBorders(wrap);
        var verticalChips = FindChipBorders(vertical);
        for (var i = 0; i < items.Count; i++)
        {
            Assert.True(Math.Abs(verticalChips[i].Height - wrapChips[i].Height) <= 1,
                $"'{items[i]}' na coluna com altura {verticalChips[i].Height:F1}, em Wrap {wrapChips[i].Height:F1}");
        }

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
