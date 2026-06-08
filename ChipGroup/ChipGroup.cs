using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly FlexLayout _layout;
    private readonly ScrollView _horizontalScroll;
    private readonly List<ChipItem> _observedChipItems = new();
    private INotifyCollectionChanged? _observedCollection;
    private bool _suppressItemChanged;

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
            ChipSelectionMode.Single, propertyChanged: Redraw);

    public static readonly BindableProperty LayoutModeProperty =
        BindableProperty.Create(nameof(LayoutMode), typeof(ChipGroupLayoutMode), typeof(ChipGroup),
            ChipGroupLayoutMode.Wrap, propertyChanged: (b, _, _) => ((ChipGroup)b).ApplyLayoutMode());

    public static readonly BindableProperty SelectedItemProperty =
        BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(ChipGroup), null,
            BindingMode.TwoWay, propertyChanged: Redraw);

    public static readonly BindableProperty SelectedItemsProperty =
        BindableProperty.Create(nameof(SelectedItems), typeof(IList), typeof(ChipGroup), null,
            BindingMode.TwoWay, propertyChanged: Redraw);

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

    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

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
        => ((ChipGroup)bindable).Rebuild();

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
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged += OnCollectionChanged;

        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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
            Padding = ChipPadding,
            Margin = new Thickness(0, 0, ChipSpacing, RowSpacing),
            BackgroundColor = selected ? SelectedBackgroundColor : UnselectedBackgroundColor,
            Stroke = new SolidColorBrush(selected ? SelectedStrokeColor : UnselectedStrokeColor),
            StrokeThickness = selected ? 1.5 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(CornerRadius) },
            Content = row,
            Opacity = enabled ? 1 : 0.45,
            WidthRequest = ChipWidth > 0 ? ChipWidth : -1,
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

        if (enabled)
        {
            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ToggleSelection(entry)),
            });
        }

        return border;
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
        Rebuild();
    }

    private void SelectSingle(ChipEntry selectedEntry)
    {
        foreach (var item in EnumerateEntries())
            SetEntrySelected(item, false);

        SetEntrySelected(selectedEntry, true);
        SelectedItem = selectedEntry.Value;
        SelectedItems = new List<object?> { selectedEntry.Value };
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
            SelectedItems = selectedValues;
            return;
        }

        SetEntrySelected(entry, !IsEntrySelected(entry));

        var selected = EnumerateEntries()
            .Where(IsEntrySelected)
            .Select(static e => e.Value)
            .ToList();

        SelectedItem = selected.LastOrDefault();
        SelectedItems = selected;
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

        SyncSelectionFromChipItems();
        Rebuild();
    }

    private void SyncSelectionFromChipItems()
    {
        var selected = EnumerateEntries().Where(IsEntrySelected).Select(static e => e.Value).ToList();
        SelectedItems = selected;
        SelectedItem = SelectionMode == ChipSelectionMode.Single
            ? selected.FirstOrDefault()
            : selected.LastOrDefault();
    }

    private void DetachObservedChipItems()
    {
        foreach (var item in _observedChipItems)
            item.PropertyChanged -= OnChipItemPropertyChanged;
        _observedChipItems.Clear();
    }

    private static bool EqualsValue(object? left, object? right)
        => EqualityComparer<object?>.Default.Equals(left, right);

    private sealed record ChipEntry(object? Source, string Text, object? Value, ChipItem? ChipItem);
}
