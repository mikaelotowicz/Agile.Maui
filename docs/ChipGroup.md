# Agile.Maui.ChipGroup

Project for `ChipGroup`, a reusable chip selection control for filters, tags,
statuses, categories, and compact option lists.

Assembly: `Agile.Maui.ChipGroup`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileChipGroup()`

## Requirements

- .NET MAUI / .NET 10.0 or 11.0 with package `Agile.Maui.ChipGroup` `1.2.0` (depends on `Microsoft.Maui.Controls` `10.0.90` or later on .NET 10 and `11.0.0` or later on .NET 11).
- Android, iOS, macOS Catalyst, or Windows.

## Installation

```powershell
dotnet add package Agile.Maui.ChipGroup --version 1.2.0
```

The same version serves .NET 10 and .NET 11 projects.

```csharp
using Agile.Maui;

builder.UseAgileChipGroup();
```

```xml
xmlns:chips="clr-namespace:Agile.Maui;assembly=Agile.Maui.ChipGroup"
```

## Quick example

```xml
<chips:ChipGroup
    ItemsSource="{Binding Categories}"
    LayoutMode="Horizontal"
    SelectionMode="Single"
    ShowCheckmark="False" />
```

## Layout modes

`LayoutMode` controls how chips are distributed on screen.

| Value | Behavior | Best for |
|---|---|---|
| `Wrap` | Default. Chips flow in rows and automatically wrap when there is no space. A chip wider than the whole row shrinks and its text is truncated with an ellipsis. | Forms, filters, small and medium option sets. |
| `Horizontal` | Chips stay in one row inside a horizontal scroll; they never shrink, the row scrolls. | Mobile screens with many options where the user swipes to find the option. |
| `Vertical` | Chips are stacked vertically. | Status lists, menus, or narrow layouts. |

```xml
<chips:ChipGroup LayoutMode="Wrap" />
<chips:ChipGroup LayoutMode="Horizontal" />
<chips:ChipGroup LayoutMode="Vertical" />
```

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` | Data source. Supports `INotifyCollectionChanged`, including notifications raised off the UI thread. |
| `SelectionMode` | `ChipSelectionMode` | `Single` | `Single` or `Multiple`. Switching from `Multiple` to `Single` keeps only the first selected chip. |
| `LayoutMode` | `ChipGroupLayoutMode` | `Wrap` | `Wrap`, `Horizontal`, or `Vertical`. |
| `SelectedItem` | `object?` | `null` | `TwoWay`. Selected value for single selection; for multiple selection, the last value in `SelectedItems` (with `ChipItem` sources that list follows `ItemsSource` order, so this is not necessarily the chip tapped last). Setting it from the view model selects the matching chip. |
| `SelectedItems` | `IList?` | `null` | `TwoWay`. Selected values. A writable list supplied by the app is updated in place (`Clear`/`Add`) instead of being replaced; a read-only or fixed-size list (such as an array) is replaced. |
| `DisplayMemberPath` | `string?` | `null` | Property used as chip text when the item is not a `ChipItem`. Resolved by reflection (see [Trimming](#trimming-and-nativeaot)). |
| `ValueMemberPath` | `string?` | `null` | Property used as selected value when the item is not a `ChipItem`. Resolved by reflection (see [Trimming](#trimming-and-nativeaot)). |
| `SelectionChangedCommand` | `ICommand?` | `null` | Command executed when the user changes the selection. Receives `ChipSelectionChangedEventArgs`. |
| `ChipPadding` | `Thickness` | `14,8` | Inner padding of each chip. |
| `ChipSpacing` | `double` | `8` | Horizontal spacing after each chip. |
| `RowSpacing` | `double` | `10` | Vertical spacing after each chip. Not applied in `Horizontal` mode, which has a single row. |
| `CornerRadius` | `double` | `18` | Rounded corner radius. |
| `ChipWidth` | `double` | `-1` | Minimum chip width when greater than zero. The chip grows when the text needs more room (for example, with a larger system font). Before `1.2.0` it was a fixed width and longer text was truncated with an ellipsis. |
| `FontSize` | `double` | `13` | Chip text size. |
| `ShowCheckmark` | `bool` | `true` | Shows the circular indicator/checkmark in multiple selection mode. |
| `SelectedBackgroundColor` | `Color` | `White` | Selected chip background. |
| `UnselectedBackgroundColor` | `Color` | `White` | Unselected chip background. |
| `SelectedTextColor` | `Color` | `#2F6FDB` | Selected chip text color. |
| `UnselectedTextColor` | `Color` | `#40444C` | Unselected chip text color. |
| `SelectedStrokeColor` | `Color` | `#2F6FDB` | Selected border color. |
| `UnselectedStrokeColor` | `Color` | `#EAECF0` | Unselected border color. |
| `CheckmarkColor` | `Color` | `White` | Checkmark color. |
| `CheckmarkBackgroundColor` | `Color` | `#2F6FDB` | Selected indicator background. |
| `UnselectedIndicatorColor` | `Color` | `#EEF0F3` | Unselected indicator background. |
| `Elevation` | `double` | `0.10` | Shadow opacity. Use `0` to remove shadow. |

## Events

| Event | Args | When it fires |
|---|---|---|
| `SelectionChanged` | `ChipSelectionChangedEventArgs` (`SelectedItem`, `SelectedItems`) | When the user taps a chip and the selection changes. Tapping the chip that is already selected in `Single` mode does not raise it. |

## Selection

Use `ChipItem` when you want each item to carry its own selected/enabled state:

```csharp
public ObservableCollection<ChipItem> Interests { get; } =
[
    new() { Text = "Photography", Value = "Photography" },
    new() { Text = "Video", Value = "Video", IsSelected = true },
    new() { Text = "Music", Value = "Music" },
];
```

`ChipItem` exposes `Text`, `Value` (when `null`, `Text` is used as the value),
`IsSelected`, and `IsEnabled` (default `true`; a disabled chip is dimmed and
ignores taps). It raises `PropertyChanged`, so later changes to these properties,
even off the UI thread, update the chips.

For plain models, set `DisplayMemberPath` and optionally `ValueMemberPath`.
Without them, the chip text is the item's `ToString()` and the value is the item
itself.

Selection is synchronized in both directions:

- `ChipItem.IsSelected` set before the first tap is reflected in `SelectedItem`
  and `SelectedItems` as soon as the chips are built.
- Setting `SelectedItem` (single) or `SelectedItems` (multiple) from the view
  model selects the matching chips; values are compared with `Equals`. With
  `ChipItem` sources, the `ChipItem` instance itself is also accepted, and the
  control keeps it in `SelectedItem` instead of replacing it with the value.
  For plain models, pass the value (the item itself only when `ValueMemberPath`
  is not set).
- When the app binds a writable list to `SelectedItems`, the control updates that
  list in place. Observe `SelectionChanged`, `SelectionChangedCommand` or the
  list itself (for example an `ObservableCollection<object>`) instead of waiting
  for the property setter to be called again.

```xml
<chips:ChipGroup
    ItemsSource="{Binding Categories}"
    SelectionMode="Single"
    SelectedItem="{Binding SelectedCategory, Mode=TwoWay}"
    SelectionChangedCommand="{Binding CategoryChangedCommand}" />
```

## Accessibility

- Chips have no minimum height: the height comes from `ChipPadding` and the
  text. For a 44pt touch target, size `ChipPadding` and `FontSize` accordingly.
- Each chip exposes `SemanticProperties.Description` with its text and
  `SemanticProperties.Hint` with its state. The hint text is fixed in Portuguese
  (`Selecionado` / `Não selecionado`).

## Trimming and NativeAOT

`DisplayMemberPath` and `ValueMemberPath` are resolved by reflection at runtime.
With full trimming or NativeAOT, preserve the referenced properties on the item
type (for example with `[DynamicallyAccessedMembers]` or `DynamicDependency`);
otherwise the text falls back to `ToString()` and the value becomes `null`.
`ChipItem` does not depend on reflection.

## Notes

The chips are rebuilt asynchronously on the UI dispatcher, and several changes in
the same cycle produce a single rebuild. When the control leaves the visual tree
(its handler is removed), it unsubscribes from the collection and `ChipItem`
events, so it does not keep the page alive; it subscribes again when it returns.

`ChipGroup` is intended for compact option sets. It does not virtualize items.
For hundreds or thousands of entries, prefer a virtualized list control.
