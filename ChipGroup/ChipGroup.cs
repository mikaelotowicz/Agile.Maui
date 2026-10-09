using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace Agile.Maui;

/// <summary>
/// Grupo de chips com quebra automatica de linha, selecao unica ou multipla e visual customizavel.
/// </summary>
public sealed class ChipGroup : ContentView
{
    private const double UnselectedStrokeThickness = 1.0;
    private const double SelectedStrokeThickness = 1.5;

    private readonly FlexLayout _layout;
    private readonly ScrollView _horizontalScroll;
    private readonly List<ChipItem> _observedChipItems = new();
    private INotifyCollectionChanged? _observedCollection;
    private bool _suppressItemChanged;
    private bool _syncingSelection;
    private bool _detached;
    private int _rebuildPending;

    public ChipGroup()
    {
        _layout = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Start,
            AlignContent = FlexAlignContent.Start,
        };

        _horizontalScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
        };

        ApplyLayoutMode();
    }

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ChipGroup), null,
            propertyChanged: (b, oldValue, newValue) => ((ChipGroup)b).OnItemsSourceChanged(oldValue, newValue));

    public static readonly BindableProperty SelectionModeProperty =
        BindableProperty.Create(nameof(SelectionMode), typeof(ChipSelectionMode), typeof(ChipGroup),
            ChipSelectionMode.Single, propertyChanged: (b, _, _) => ((ChipGroup)b).OnSelectionModeChanged());

    public static readonly BindableProperty LayoutModeProperty =
        BindableProperty.Create(nameof(LayoutMode), typeof(ChipGroupLayoutMode), typeof(ChipGroup),
            ChipGroupLayoutMode.Wrap, propertyChanged: (b, _, _) => ((ChipGroup)b).ApplyLayoutMode());

    public static readonly BindableProperty SelectedItemProperty =
        BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(ChipGroup), null,
            BindingMode.TwoWay, propertyChanged: (b, _, _) => ((ChipGroup)b).OnSelectionBindableChanged());

    public static readonly BindableProperty SelectedItemsProperty =
        BindableProperty.Create(nameof(SelectedItems), typeof(IList), typeof(ChipGroup), null,
            BindingMode.TwoWay, propertyChanged: (b, _, _) => ((ChipGroup)b).OnSelectionBindableChanged());

    public static readonly BindableProperty DisplayMemberPathProperty =
        BindableProperty.Create(nameof(DisplayMemberPath), typeof(string), typeof(ChipGroup), null,
            propertyChanged: Redraw);

    public static readonly BindableProperty ValueMemberPathProperty =
        BindableProperty.Create(nameof(ValueMemberPath), typeof(string), typeof(ChipGroup), null,
            propertyChanged: Redraw);

    public static readonly BindableProperty SelectionChangedCommandProperty =
        BindableProperty.Create(nameof(SelectionChangedCommand), typeof(ICommand), typeof(ChipGroup));

    public static readonly BindableProperty ChipPaddingProperty =
        BindableProperty.Create(nameof(ChipPadding), typeof(Thickness), typeof(ChipGroup), new Thickness(14, 8),
            propertyChanged: Redraw);

    public static readonly BindableProperty ChipSpacingProperty =
        BindableProperty.Create(nameof(ChipSpacing), typeof(double), typeof(ChipGroup), 8.0,
            propertyChanged: Redraw);

    public static readonly BindableProperty RowSpacingProperty =
        BindableProperty.Create(nameof(RowSpacing), typeof(double), typeof(ChipGroup), 10.0,
            propertyChanged: Redraw);

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(ChipGroup), 18.0,
            propertyChanged: Redraw);

    public static readonly BindableProperty ChipWidthProperty =
        BindableProperty.Create(nameof(ChipWidth), typeof(double), typeof(ChipGroup), -1.0,
            propertyChanged: Redraw);

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(ChipGroup), 13.0,
            propertyChanged: Redraw);

    public static readonly BindableProperty ShowCheckmarkProperty =
        BindableProperty.Create(nameof(ShowCheckmark), typeof(bool), typeof(ChipGroup), true,
            propertyChanged: Redraw);

    public static readonly BindableProperty SelectedBackgroundColorProperty =
        BindableProperty.Create(nameof(SelectedBackgroundColor), typeof(Color), typeof(ChipGroup), Colors.White,
            propertyChanged: Redraw);

    public static readonly BindableProperty UnselectedBackgroundColorProperty =
        BindableProperty.Create(nameof(UnselectedBackgroundColor), typeof(Color), typeof(ChipGroup), Colors.White,
            propertyChanged: Redraw);

    public static readonly BindableProperty SelectedTextColorProperty =
        BindableProperty.Create(nameof(SelectedTextColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#2F6FDB"),
            propertyChanged: Redraw);

    public static readonly BindableProperty UnselectedTextColorProperty =
        BindableProperty.Create(nameof(UnselectedTextColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#40444C"),
            propertyChanged: Redraw);

    public static readonly BindableProperty SelectedStrokeColorProperty =
        BindableProperty.Create(nameof(SelectedStrokeColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#2F6FDB"),
            propertyChanged: Redraw);

    public static readonly BindableProperty UnselectedStrokeColorProperty =
        BindableProperty.Create(nameof(UnselectedStrokeColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#EAECF0"),
            propertyChanged: Redraw);

    public static readonly BindableProperty CheckmarkColorProperty =
        BindableProperty.Create(nameof(CheckmarkColor), typeof(Color), typeof(ChipGroup), Colors.White,
            propertyChanged: Redraw);

    public static readonly BindableProperty CheckmarkBackgroundColorProperty =
        BindableProperty.Create(nameof(CheckmarkBackgroundColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#2F6FDB"),
            propertyChanged: Redraw);

    public static readonly BindableProperty UnselectedIndicatorColorProperty =
        BindableProperty.Create(nameof(UnselectedIndicatorColor), typeof(Color), typeof(ChipGroup), Color.FromArgb("#EEF0F3"),
            propertyChanged: Redraw);

    public static readonly BindableProperty ElevationProperty =
        BindableProperty.Create(nameof(Elevation), typeof(double), typeof(ChipGroup), 0.10,
            propertyChanged: Redraw);

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ChipSelectionMode SelectionMode
    {
        get => (ChipSelectionMode)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public ChipGroupLayoutMode LayoutMode
    {
        get => (ChipGroupLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    /// <summary>Nome da propriedade usada como texto do chip quando o item não é <see cref="ChipItem"/>.</summary>
    /// <remarks>Resolvida por reflection em runtime: com trimming completo ou NativeAOT, preserve a
    /// propriedade no tipo do item (ex.: <c>[DynamicallyAccessedMembers]</c>/<c>DynamicDependency</c>),
    /// senão ela pode ser removida e o texto cai em <c>ToString()</c>.</remarks>
    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>Nome da propriedade usada como valor selecionado quando o item não é <see cref="ChipItem"/>.</summary>
    /// <remarks>Resolvida por reflection em runtime: com trimming completo ou NativeAOT, preserve a
    /// propriedade no tipo do item, senão ela pode ser removida e o valor passa a ser <c>null</c>.</remarks>
    public string? ValueMemberPath
    {
        get => (string?)GetValue(ValueMemberPathProperty);
        set => SetValue(ValueMemberPathProperty, value);
    }

    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)GetValue(SelectionChangedCommandProperty);
        set => SetValue(SelectionChangedCommandProperty, value);
    }

    public Thickness ChipPadding
    {
        get => (Thickness)GetValue(ChipPaddingProperty);
        set => SetValue(ChipPaddingProperty, value);
    }

    public double ChipSpacing
    {
        get => (double)GetValue(ChipSpacingProperty);
        set => SetValue(ChipSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double ChipWidth
    {
        get => (double)GetValue(ChipWidthProperty);
        set => SetValue(ChipWidthProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public bool ShowCheckmark
    {
        get => (bool)GetValue(ShowCheckmarkProperty);
        set => SetValue(ShowCheckmarkProperty, value);
    }

    public Color SelectedBackgroundColor
    {
        get => (Color)GetValue(SelectedBackgroundColorProperty);
        set => SetValue(SelectedBackgroundColorProperty, value);
    }

    public Color UnselectedBackgroundColor
    {
        get => (Color)GetValue(UnselectedBackgroundColorProperty);
        set => SetValue(UnselectedBackgroundColorProperty, value);
    }

    public Color SelectedTextColor
    {
        get => (Color)GetValue(SelectedTextColorProperty);
        set => SetValue(SelectedTextColorProperty, value);
    }

    public Color UnselectedTextColor
    {
        get => (Color)GetValue(UnselectedTextColorProperty);
        set => SetValue(UnselectedTextColorProperty, value);
    }

    public Color SelectedStrokeColor
    {
        get => (Color)GetValue(SelectedStrokeColorProperty);
        set => SetValue(SelectedStrokeColorProperty, value);
    }

    public Color UnselectedStrokeColor
    {
        get => (Color)GetValue(UnselectedStrokeColorProperty);
        set => SetValue(UnselectedStrokeColorProperty, value);
    }

    public Color CheckmarkColor
    {
        get => (Color)GetValue(CheckmarkColorProperty);
        set => SetValue(CheckmarkColorProperty, value);
    }

    public Color CheckmarkBackgroundColor
    {
        get => (Color)GetValue(CheckmarkBackgroundColorProperty);
        set => SetValue(CheckmarkBackgroundColorProperty, value);
    }

    public Color UnselectedIndicatorColor
    {
        get => (Color)GetValue(UnselectedIndicatorColorProperty);
        set => SetValue(UnselectedIndicatorColorProperty, value);
    }

    public double Elevation
    {
        get => (double)GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    public event EventHandler<ChipSelectionChangedEventArgs>? SelectionChanged;

    private static void Redraw(BindableObject bindable, object oldValue, object newValue)
        => ((ChipGroup)bindable).RequestRebuild();

    /// <summary>
    /// Coalesce de rebuilds: N mudanças no mesmo ciclo geram um único <see cref="Rebuild"/>,
    /// sempre despachado para o UI thread (seguro para CollectionChanged em thread de fundo).
    /// </summary>
    private void RequestRebuild()
    {
        if (Interlocked.Exchange(ref _rebuildPending, 1) == 1)
            return;

        Dispatcher.Dispatch(() =>
        {
            Interlocked.Exchange(ref _rebuildPending, 0);
            if (_detached)
                return; // controle fora de cena: o rebuild acontece no re-attach do handler

            Rebuild();
        });
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            // Página saiu de cena: solta CollectionChanged/PropertyChanged para a coleção e os
            // ChipItems do consumidor não manterem o controle vivo (leak) nem rebuildarem controle morto.
            _detached = true;
            if (_observedCollection is not null)
                _observedCollection.CollectionChanged -= OnCollectionChanged;
            DetachObservedChipItems();
            return;
        }

        if (_detached)
        {
            _detached = false;
            if (_observedCollection is not null)
            {
                _observedCollection.CollectionChanged -= OnCollectionChanged; // guarda contra dupla assinatura
                _observedCollection.CollectionChanged += OnCollectionChanged;
            }

            Rebuild();
        }
    }

    private void ApplyLayoutMode()
    {
        _layout.Direction = LayoutMode == ChipGroupLayoutMode.Vertical
            ? FlexDirection.Column
            : FlexDirection.Row;
        _layout.Wrap = LayoutMode == ChipGroupLayoutMode.Wrap
            ? FlexWrap.Wrap
            : FlexWrap.NoWrap;

        if (LayoutMode == ChipGroupLayoutMode.Horizontal)
        {
            if (ReferenceEquals(Content, _layout))
                Content = null;

            if (!ReferenceEquals(_horizontalScroll.Content, _layout))
                _horizontalScroll.Content = _layout;

            if (!ReferenceEquals(Content, _horizontalScroll))
                Content = _horizontalScroll;

            return;
        }

        if (ReferenceEquals(_horizontalScroll.Content, _layout))
            _horizontalScroll.Content = null;

        if (!ReferenceEquals(Content, _layout))
            Content = _layout;
    }

    private void OnItemsSourceChanged(object oldValue, object newValue)
    {
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged -= OnCollectionChanged;

        _observedCollection = newValue as INotifyCollectionChanged;
        if (_observedCollection is not null && !_detached)
            _observedCollection.CollectionChanged += OnCollectionChanged;

        RequestRebuild();
    }

    // RequestRebuild é thread-safe: coalesce + Dispatcher.Dispatch cobrem CollectionChanged fora do UI thread.
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestRebuild();

    private void Rebuild()
    {
        DetachObservedChipItems();
        _layout.Children.Clear();

        if (ItemsSource is null) return;

        foreach (var item in ItemsSource)
        {
            var entry = CreateEntry(item);
            if (entry.ChipItem is not null)
            {
                entry.ChipItem.PropertyChanged += OnChipItemPropertyChanged;
                _observedChipItems.Add(entry.ChipItem);
            }

            _layout.Children.Add(CreateChipView(entry));
        }

        // Estado inicial: ChipItem.IsSelected pré-definido deve refletir em SelectedItem/SelectedItems
        // antes do primeiro toque (SyncSelectionFromChipItems tem guarda de reentrância).
        if (_observedChipItems.Count > 0)
            SyncSelectionFromChipItems();
    }

    private View CreateChipView(ChipEntry entry)
    {
        var selected = IsEntrySelected(entry);
        var enabled = entry.ChipItem?.IsEnabled ?? true;
        var showIndicator = SelectionMode == ChipSelectionMode.Multiple && ShowCheckmark;

        var text = new Label
        {
            Text = entry.Text,
            FontSize = FontSize,
            TextColor = selected ? SelectedTextColor : UnselectedTextColor,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        var row = new HorizontalStackLayout
        {
            Spacing = showIndicator ? 8 : 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
        };

        if (showIndicator)
            row.Children.Add(CreateIndicator(selected));

        row.Children.Add(text);

        var border = new Border
        {
            Padding = GetCompensatedPadding(selected),
            Margin = new Thickness(0, 0, ChipSpacing, RowSpacing),
            BackgroundColor = selected ? SelectedBackgroundColor : UnselectedBackgroundColor,
            Stroke = new SolidColorBrush(selected ? SelectedStrokeColor : UnselectedStrokeColor),
            StrokeThickness = selected ? SelectedStrokeThickness : UnselectedStrokeThickness,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadius) },
            Content = row,
            Opacity = enabled ? 1 : 0.45,
            WidthRequest = ChipWidth > 0 ? ChipWidth : -1,
            // Sem altura mínima: a altura vem de ChipPadding + texto. Os 44pt da 1.1.0 inflavam
            // os chips compactos dos consumidores (ChipPadding 8 vertical ≈ 34pt).
        };

        if (Elevation > 0)
        {
            border.Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = (float)Math.Clamp(Elevation, 0, 1),
            };
        }

        // Acessibilidade: leitores de tela anunciam o texto e o estado do chip.
        SemanticProperties.SetDescription(border, entry.Text);
        SemanticProperties.SetHint(border, selected ? "Selecionado" : "Não selecionado");

        if (enabled)
        {
            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ToggleSelection(entry)),
            });
        }

        return border;
    }

    private Thickness GetCompensatedPadding(bool selected)
    {
        if (!selected)
            return ChipPadding;

        var delta = SelectedStrokeThickness - UnselectedStrokeThickness;
        return new Thickness(
            Math.Max(0, ChipPadding.Left - delta),
            Math.Max(0, ChipPadding.Top - delta),
            Math.Max(0, ChipPadding.Right - delta),
            Math.Max(0, ChipPadding.Bottom - delta));
    }

    private Border CreateIndicator(bool selected)
    {
        return new Border
        {
            WidthRequest = 18,
            HeightRequest = 18,
            BackgroundColor = selected ? CheckmarkBackgroundColor : UnselectedIndicatorColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            Content = new Label
            {
                Text = selected ? "\u2713" : string.Empty,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = CheckmarkColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };
    }

    private void ToggleSelection(ChipEntry entry)
    {
        // Re-tap no chip já selecionado em Single: nada muda, não dispara evento nem rebuild.
        if (SelectionMode == ChipSelectionMode.Single && IsEntrySelected(entry))
            return;

        _suppressItemChanged = true;
        try
        {
            if (SelectionMode == ChipSelectionMode.Single)
                SelectSingle(entry);
            else
                ToggleMultiple(entry);
        }
        finally
        {
            _suppressItemChanged = false;
        }

        RaiseSelectionChanged();
        RequestRebuild();
    }

    private void SelectSingle(ChipEntry selectedEntry)
    {
        foreach (var item in EnumerateEntries())
            SetEntrySelected(item, false);

        SetEntrySelected(selectedEntry, true);
        SelectedItem = selectedEntry.Value;
        SetSelectedValues(new List<object?> { selectedEntry.Value });
    }

    private void ToggleMultiple(ChipEntry entry)
    {
        if (entry.ChipItem is null)
        {
            var selectedValues = SelectedItems?.Cast<object?>().ToList() ?? new List<object?>();
            var index = selectedValues.FindIndex(value => EqualsValue(value, entry.Value));
            if (index >= 0)
                selectedValues.RemoveAt(index);
            else
                selectedValues.Add(entry.Value);

            SelectedItem = selectedValues.LastOrDefault();
            SetSelectedValues(selectedValues);
            return;
        }

        SetEntrySelected(entry, !IsEntrySelected(entry));

        var selected = EnumerateEntries()
            .Where(IsEntrySelected)
            .Select(static e => e.Value)
            .ToList();

        SelectedItem = selected.LastOrDefault();
        SetSelectedValues(selected);
    }

    private void RaiseSelectionChanged()
    {
        var selectedItems = SelectedItems?.Cast<object?>().ToList() ?? new List<object?>();
        var args = new ChipSelectionChangedEventArgs(SelectedItem, selectedItems);
        SelectionChanged?.Invoke(this, args);

        var command = SelectionChangedCommand;
        if (command?.CanExecute(args) == true)
            command.Execute(args);
    }

    private bool IsEntrySelected(ChipEntry entry)
    {
        if (entry.ChipItem is not null)
            return entry.ChipItem.IsSelected;

        if (SelectionMode == ChipSelectionMode.Single)
            return EqualsValue(SelectedItem, entry.Value);

        return SelectedItems?.Cast<object?>().Any(value => EqualsValue(value, entry.Value)) == true;
    }

    private void SetEntrySelected(ChipEntry entry, bool selected)
    {
        if (entry.ChipItem is not null)
            entry.ChipItem.IsSelected = selected;
    }

    private IEnumerable<ChipEntry> EnumerateEntries()
    {
        if (ItemsSource is null) yield break;

        foreach (var item in ItemsSource)
            yield return CreateEntry(item);
    }

    private ChipEntry CreateEntry(object? item)
    {
        if (item is ChipItem chip)
            return new ChipEntry(item, chip.Text, chip.Value ?? chip.Text, chip);

        var text = GetMemberValue(item, DisplayMemberPath)?.ToString() ?? item?.ToString() ?? string.Empty;
        var value = string.IsNullOrWhiteSpace(ValueMemberPath) ? item : GetMemberValue(item, ValueMemberPath);
        return new ChipEntry(item, text, value, null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "DisplayMemberPath/ValueMemberPath resolvem propriedade por nome em tipo do consumidor; " +
            "com trimming completo/NativeAOT o consumidor deve preservar a propriedade no modelo " +
            "(documentado nos remarks das propriedades).")]
    private static object? GetMemberValue(object? item, string? memberPath)
    {
        if (item is null || string.IsNullOrWhiteSpace(memberPath)) return null;

        var property = item.GetType().GetRuntimeProperty(memberPath);
        return property?.GetValue(item);
    }

    private void OnChipItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressItemChanged) return;
        if (e.PropertyName is not nameof(ChipItem.IsSelected)
            and not nameof(ChipItem.Text)
            and not nameof(ChipItem.Value)
            and not nameof(ChipItem.IsEnabled))
            return;

        // ChipItem pode ser alterado fora do UI thread: bindables e árvore visual só no dispatcher.
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(HandleChipItemChanged);
            return;
        }

        HandleChipItemChanged();
    }

    private void HandleChipItemChanged()
    {
        if (_detached) return;

        SyncSelectionFromChipItems();
        RequestRebuild();
    }

    private void SyncSelectionFromChipItems()
    {
        if (_syncingSelection) return;

        _syncingSelection = true;
        try
        {
            var selectedEntries = EnumerateEntries().Where(IsEntrySelected).ToList();
            SetSelectedValues(selectedEntries.Select(static e => e.Value).ToList());

            var current = SelectionMode == ChipSelectionMode.Single
                ? selectedEntries.FirstOrDefault()
                : selectedEntries.LastOrDefault();

            // SelectedItem que já aponta para o chip (ex.: o próprio ChipItem) fica como a VM passou:
            // trocá-lo pelo Value devolveria à VM, pelo TwoWay, um objeto de outro tipo.
            if (current is null || !MatchesEntry(SelectedItem, current))
                SelectedItem = current?.Value;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Atualiza SelectedItems preservando a lista do consumidor: muta a lista existente (Clear/Add)
    /// quando ela é gravável; só substitui a instância quando não há lista utilizável.
    /// </summary>
    private void SetSelectedValues(List<object?> values)
    {
        var current = SelectedItems;
        if (current is { IsReadOnly: false, IsFixedSize: false })
        {
            if (!ReferenceEquals(current, values))
            {
                current.Clear();
                foreach (var value in values)
                    current.Add(value);
            }

            return;
        }

        SelectedItems = values;
    }

    private void OnSelectionBindableChanged()
    {
        ApplySelectionToChipItems();
        RequestRebuild();
    }

    /// <summary>
    /// Two-way VM→controle: reflete SelectedItem/SelectedItems em ChipItem.IsSelected.
    /// Guardas evitam reentrância com o toggle (_suppressItemChanged) e com o sync inverso (_syncingSelection).
    /// </summary>
    private void ApplySelectionToChipItems()
    {
        if (_suppressItemChanged || _syncingSelection) return;

        _suppressItemChanged = true;
        try
        {
            foreach (var entry in EnumerateEntries())
            {
                if (entry.ChipItem is null) continue;

                var selected = SelectionMode == ChipSelectionMode.Single
                    ? MatchesEntry(SelectedItem, entry)
                    : SelectedItems?.Cast<object?>().Any(value => MatchesEntry(value, entry)) == true;
                entry.ChipItem.IsSelected = selected;
            }
        }
        finally
        {
            _suppressItemChanged = false;
        }
    }

    private void OnSelectionModeChanged()
    {
        // Multiple→Single: normaliza a seleção mantendo só o primeiro item selecionado.
        if (SelectionMode == ChipSelectionMode.Single)
        {
            _suppressItemChanged = true;
            try
            {
                var keep = EnumerateEntries().FirstOrDefault(IsEntrySelected);
                foreach (var entry in EnumerateEntries())
                    SetEntrySelected(entry, keep?.ChipItem is not null && ReferenceEquals(entry.ChipItem, keep.ChipItem));

                SelectedItem = keep?.Value;
                SetSelectedValues(keep is null ? new List<object?>() : new List<object?> { keep.Value });
            }
            finally
            {
                _suppressItemChanged = false;
            }
        }

        RequestRebuild();
    }

    private void DetachObservedChipItems()
    {
        foreach (var item in _observedChipItems)
            item.PropertyChanged -= OnChipItemPropertyChanged;
        _observedChipItems.Clear();
    }

    private static bool EqualsValue(object? left, object? right)
        => EqualityComparer<object?>.Default.Equals(left, right);

    // A VM pode selecionar pelo Value ou pelo próprio item da fonte (ex.: o ChipItem de ItemsSource).
    private static bool MatchesEntry(object? selected, ChipEntry entry)
        => EqualsValue(selected, entry.Value) || (selected is not null && ReferenceEquals(selected, entry.Source));

    private sealed record ChipEntry(object? Source, string Text, object? Value, ChipItem? ChipItem);
}
