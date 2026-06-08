# Agile.Maui.ChipGroup

Project for `ChipGroup`, a reusable chip selection control for filters, tags,
statuses, categories, and compact option lists.

Assembly: `Agile.Maui.ChipGroup`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileChipGroup()`

## Installation

```powershell
dotnet add package Agile.Maui.ChipGroup
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
| `ItemsSource` | `IEnumerable?` | `null` | Data source. Supports `INotifyCollectionChanged`. |
| `SelectionMode` | `ChipSelectionMode` | `Single` | `Single` or `Multiple`. |
| `LayoutMode` | `ChipGroupLayoutMode` | `Wrap` | `Wrap`, `Horizontal`, or `Vertical`. |
| `SelectedItem` | `object?` | `null` | Selected value for single selection; last selected value for multiple selection. |
| `SelectedItems` | `IList?` | `null` | Selected values. |
| `DisplayMemberPath` | `string?` | `null` | Property used as chip text when the item is not a `ChipItem`. |
| `ValueMemberPath` | `string?` | `null` | Property used as selected value when the item is not a `ChipItem`. |
| `SelectionChangedCommand` | `ICommand?` | `null` | Command executed when selection changes. |
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

## Notes

`ChipGroup` is intended for compact option sets. It does not virtualize items.
For hundreds or thousands of entries, prefer a virtualized list control.
