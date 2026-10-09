# Agile.Maui.ChipGroup

Project for `ChipGroup`, a reusable chip selection control for filters, tags,
statuses, categories, and compact option lists.

Assembly: `Agile.Maui.ChipGroup`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileChipGroup()`

## Requirements

- .NET MAUI / .NET 10.0 with package `Agile.Maui.ChipGroup` `1.1.0` (depends on `Microsoft.Maui.Controls` `10.0.90` or later).
- .NET MAUI / .NET 11.0 preview with package `Agile.Maui.ChipGroup` `1.1.0-preview.1`.
- Android, iOS, macOS Catalyst, or Windows.

## Installation

```powershell
dotnet add package Agile.Maui.ChipGroup --version 1.1.0
```

For .NET 11 preview projects:

```powershell
dotnet add package Agile.Maui.ChipGroup --version 1.1.0-preview.1
```

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
| `Wrap` | Default. Chips flow in rows and automatically wrap when there is no space. | Forms, filters, small and medium option sets. |
| `Horizontal` | Chips stay in one row inside a horizontal scroll. | Mobile screens with many options where the user swipes to find the option. |
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
| `SelectedItem` | `object?` | `null` | `TwoWay`. Selected value for single selection; last selected value for multiple selection. Setting it from the view model selects the matching chip. |
| `SelectedItems` | `IList?` | `null` | `TwoWay`. Selected values. A writable list supplied by the app is updated in place (`Clear`/`Add`) instead of being replaced. |
| `DisplayMemberPath` | `string?` | `null` | Property used as chip text when the item is not a `ChipItem`. Resolved by reflection (see [Trimming](#trimming-and-nativeaot)). |
| `ValueMemberPath` | `string?` | `null` | Property used as selected value when the item is not a `ChipItem`. Resolved by reflection (see [Trimming](#trimming-and-nativeaot)). |
| `SelectionChangedCommand` | `ICommand?` | `null` | Command executed when the user changes the selection. Receives `ChipSelectionChangedEventArgs`. |
| `ChipPadding` | `Thickness` | `14,8` | Inner padding of each chip. |
| `ChipSpacing` | `double` | `8` | Horizontal spacing after each chip. |
| `RowSpacing` | `double` | `10` | Vertical spacing after each chip. |
| `CornerRadius` | `double` | `18` | Rounded corner radius. |
| `ChipWidth` | `double` | `-1` | Fixed chip width when greater than zero. |
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

For plain models, set `DisplayMemberPath` and optionally `ValueMemberPath`.

Selection is synchronized in both directions:

- `ChipItem.IsSelected` set before the first tap is reflected in `SelectedItem`
  and `SelectedItems` as soon as the chips are built.
- Setting `SelectedItem` (single) or `SelectedItems` (multiple) from the view
  model selects the matching chips; values are compared with `Equals`.
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

- Each chip has a minimum height of 44 (Apple HIG touch target), even with a
  small `FontSize` or `ChipPadding`.
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
