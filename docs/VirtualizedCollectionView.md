# Agile.Maui.VirtualizedCollection

Project for `VirtualizedCollectionView`, a list/grid control with
native virtualization and support for MAUI templates.

Assembly: `Agile.Maui.VirtualizedCollection`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileVirtualizedCollectionView()`

## Installation

```powershell
dotnet add package Agile.Maui.VirtualizedCollection
```

```csharp
using Agile.Maui;

builder.UseAgileVirtualizedCollectionView();
```

```xml
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
```

## Quick example

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    ItemTemplate="{StaticResource ProductTemplate}"
    Span="1"
    ItemSizingStrategy="Dynamic"
    ItemHeightRequest="200"
    RemainingItemsThreshold="8"
    RemainingItemsThresholdReached="OnLoadMore"
    Scrolled="OnScrolled" />
```

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` | Data source. Supports `INotifyCollectionChanged`. |
| `ItemTemplate` | `DataTemplate?` | `null` | MAUI template for each item. |
| `ItemHeight` | `double` | `-1` | Explicit fixed height. When `> 0`, it overrides the sizing strategy on Android/iOS/Mac. Ignored on Windows. |
| `ItemHeightRequest` | `double` | `350` | Fixed fallback height in `Fixed`; estimate in `Dynamic`. On Windows there is no equivalent for estimated height. |
| `Span` | `int` | `1` | Columns in vertical layout; rows in horizontal layout. |
| `Orientation` | `VirtualizedOrientation` | `Vertical` | `Vertical` or `Horizontal`. |
| `ItemSizingStrategy` | `ItemSizingStrategy` | `Fixed` | `Fixed` for predictable height; `Dynamic` for height measured from content. |
| `ItemSpacing` | `double` | `0` | Space between items. |
| `RemainingItemsThreshold` | `int` | `-1` | Triggers incremental loading when N items remain. |
| `RemainingItemsThresholdReachedCommand` | `ICommand?` | `null` | Command for infinite scroll. |
| `ScrolledCommand` | `ICommand?` | `null` | Receives `VirtualizedScrolledEventArgs`. |
| `EmptyView` | `object?` | `null` | Content displayed when empty. |
| `EmptyViewTemplate` | `DataTemplate?` | `null` | Template for `EmptyView`. |

## Events and methods

| API | Description |
|---|---|
| `RemainingItemsThresholdReached` | Infinite scroll event. |
| `Scrolled` | Scroll event; args expose `HorizontalOffset` and `VerticalOffset`. |
| `ScrollTo(int index, bool animated = true)` | Scrolls to an index. |

## Enums

```csharp
public enum VirtualizedOrientation
{
    Vertical,
    Horizontal
}

public enum ItemSizingStrategy
{
    Fixed,
    Dynamic
}
```

## Sizing strategy

`Fixed` is the fastest path. Use it when all items have a predictable height.
If `ItemHeight > 0`, that height is used. Otherwise, `ItemHeightRequest`
serves as the fixed fallback height on Android/iOS/Mac.

`Dynamic` measures each item from its content. Use it for posts, expandable cards, or
text with many variations. On Android, the full dynamic path is only used
when `Span=1`, `Orientation=Vertical`, and `ItemHeight <= 0`. On iOS/Mac,
`Dynamic` uses self-sizing from `UICollectionViewCompositionalLayout`.

On Windows, `ItemSizingStrategy` is mapped to the internal `CollectionView`:

| Agile | Windows MAUI |
|---|---|
| `Fixed` | `CollectionView.ItemSizingStrategy = MeasureFirstItem` |
| `Dynamic` | `CollectionView.ItemSizingStrategy = MeasureAllItems` |

`ItemHeight` and `ItemHeightRequest` have no direct mapping on Windows. For
fixed height on Windows, set `HeightRequest` within the `DataTemplate` itself.

## Platform behavior

| Platform | Implementation |
|---|---|
| Android | `RecyclerView`, `LinearLayoutManager`, `GridLayoutManager`, and `CachingLinearLayoutManager` for dynamic height. |
| iOS/MacCatalyst | `UICollectionView` with `UICollectionViewCompositionalLayout`; `PreferredLayoutAttributesFitting` measures MAUI views. |
| Windows | `ContentView` hosting a MAUI `CollectionView`, with mouse drag-to-scroll and inertia. |

## Example with inline template

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Products}"
    Span="2"
    ItemSpacing="8"
    ItemSizingStrategy="Fixed"
    ItemHeightRequest="160"
    RemainingItemsThreshold="10">

    <virtualized:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:Product">
            <Border Padding="12" StrokeShape="RoundRectangle 8">
                <VerticalStackLayout>
                    <Label Text="{Binding Name}" FontAttributes="Bold" />
                    <Label Text="{Binding PriceText}" />
                </VerticalStackLayout>
            </Border>
        </DataTemplate>
    </virtualized:VirtualizedCollectionView.ItemTemplate>
</virtualized:VirtualizedCollectionView>
```

## Performance recommendations

- Prefer `Fixed` when the card has a known height.
- Use `Dynamic` only when the height genuinely varies.
- Tune `ItemHeightRequest` to something close to the real average, especially on iOS/Mac.
- Use `x:DataType` in your `DataTemplate`.
- Avoid large remote images inside cells; prefer thumbnails or the `ImageView` from the `GalleryView` package.
- Use `ObservableCollection` for incremental updates instead of swapping the entire list.

See also [../TUNING.md](../TUNING.md) and [../PROFILING.md](../PROFILING.md).
