# Agile.Maui.VirtualizedCollection

Project for `VirtualizedCollectionView`, a list/grid control with
native virtualization and support for MAUI templates.

Assembly: `Agile.Maui.VirtualizedCollection`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileVirtualizedCollectionView()`

## Requirements

- .NET MAUI / .NET 10.0 with package `Agile.Maui.VirtualizedCollection` `1.1.0` (depends on `Microsoft.Maui.Controls` `10.0.90` or later).
- .NET MAUI / .NET 11.0 preview with package `Agile.Maui.VirtualizedCollection` `1.1.0-preview.1`.
- Android 7.0 (API 24)+, iOS 15.0+, macOS Catalyst 15.0+, or Windows 10.0.17763.0+.

## Installation

```powershell
dotnet add package Agile.Maui.VirtualizedCollection --version 1.1.0
```

For .NET 11 preview projects:

```powershell
dotnet add package Agile.Maui.VirtualizedCollection --version 1.1.0-preview.1
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
| `ItemsSource` | `IEnumerable?` | `null` | Data source. Supports `INotifyCollectionChanged`; see [Collection changes](#collection-changes). |
| `ItemTemplate` | `DataTemplate?` | `null` | MAUI template for each item. Accepts a `DataTemplateSelector`; see [Template selector](#template-selector). |
| `Header` | `object?` | `null` | Content shown before the items. Without `HeaderTemplate`, a `View` is used directly and any other value is shown as text. A `View` without its own `BindingContext` inherits the control's. |
| `HeaderTemplate` | `DataTemplate?` | `null` | Template for `Header`; the created content receives `Header` as its `BindingContext`. |
| `Footer` | `object?` | `null` | Content shown after the items. Same rules as `Header`. |
| `FooterTemplate` | `DataTemplate?` | `null` | Template for `Footer`. |
| `ItemHeight` | `double` | `-1` | Explicit fixed height. When `> 0`, it overrides the sizing strategy on Android/iOS/Mac. Ignored on Windows. |
| `ItemHeightRequest` | `double` | `350` | Fixed item height in `Fixed`; scroll estimate in `Dynamic` and `MeasureFirst`. On Windows there is no equivalent for estimated height. |
| `ItemWidthRequest` | `double` | `-1` | Item width for `Orientation=Horizontal`: absolute column width on iOS/Mac and size estimate on Android. `-1` falls back to the height values. No effect on Windows. |
| `Span` | `int` | `1` | Columns in vertical layout; rows in horizontal layout. |
| `Orientation` | `VirtualizedOrientation` | `Vertical` | `Vertical` or `Horizontal`. |
| `ItemSizingStrategy` | `ItemSizingStrategy` | `Fixed` | `Fixed` (predictable height), `Dynamic` (height measured per item from content), or `MeasureFirst` (measure the first item and apply its height to all). |
| `ItemSpacing` | `double` | `0` | Space between items. |
| `RemainingItemsThreshold` | `int` | `-1` | Triggers incremental loading when N items remain. |
| `RemainingItemsThresholdReachedCommand` | `ICommand?` | `null` | Command for infinite scroll. |
| `ScrolledCommand` | `ICommand?` | `null` | Receives `VirtualizedScrolledEventArgs`. |
| `EmptyView` | `object?` | `null` | Content displayed when empty. |
| `EmptyViewTemplate` | `DataTemplate?` | `null` | Template for `EmptyView`. |
| `VerticalScrollBarVisibility` | `ScrollBarVisibility` | `Default` | Vertical scroll bar visibility. See [Scroll bar visibility](#scroll-bar-visibility). |
| `HorizontalScrollBarVisibility` | `ScrollBarVisibility` | `Default` | Horizontal scroll bar visibility (applies when `Orientation=Horizontal`). |

## Events and methods

| API | Description |
|---|---|
| `RemainingItemsThresholdReached` | Infinite scroll event. |
| `Scrolled` | Scroll event; args expose `HorizontalOffset` and `VerticalOffset`. |
| `ScrollTo(int index, bool animated = true)` | Scrolls to an index. |
| `ScrollToStart(bool animated = true)` | Scrolls back to the first item. |

## Template selector

`ItemTemplate` accepts a `DataTemplateSelector`. On Android and iOS/Mac the
template is resolved per item, and a recycled cell whose item maps to a different
template recreates its content on rebind. On Windows the internal MAUI
`CollectionView` resolves the selector natively. With `MeasureFirst`, switching
the template re-measures the first item.

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Feed}"
    ItemTemplate="{StaticResource FeedTemplateSelector}"
    ItemSizingStrategy="Dynamic" />
```

## Collection changes

- On Android and iOS/Mac, `INotifyCollectionChanged` notifications are queued
  under a lock and coalesced through the view's dispatcher (on iOS/Mac, one batch
  update per dispatch), so batches raised from a background thread are applied on
  the UI thread.
- When the list goes from empty to non-empty (first load, or a refresh after an
  empty result), the view scrolls back to the first item on every platform.

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
    Dynamic,
    MeasureFirst
}
```

## Sizing strategy

`Fixed` is the fastest path. Use it when all items have a predictable height.
`ItemHeightRequest` is the height applied to every item on Android/iOS/Mac.

> **Platform note — `ItemHeight` in `Fixed` mode:** there is a behavior difference.
> On **iOS/Mac**, `ItemHeight > 0` takes precedence and overrides `ItemHeightRequest`.
> On **Android**, `ItemHeight` is **ignored** in `Fixed` mode — `ItemHeightRequest` always wins
> (`ItemHeight` only takes effect on Android in `Dynamic`/`MeasureFirst`, where `> 0` forces a fixed height).
> For consistent results in `Fixed`, leave `ItemHeight = -1` (default) and set only `ItemHeightRequest`.

`Dynamic` measures each item from its content. Use it for posts, expandable cards, or
text with many variations. On Android, the full dynamic path is only used
when `Span=1`, `Orientation=Vertical`, and `ItemHeight <= 0`. On iOS/Mac,
`Dynamic` uses self-sizing from `UICollectionViewCompositionalLayout`. Cost: each item
is measured the first time it appears.

`MeasureFirst` measures the **first** displayed item and applies that height to **all**
items — scroll performance matches `Fixed` (no per-item measurement), but it assumes a
**uniform** item height; items taller than the first are clipped, just like `Fixed`.
Use it instead of `Fixed` when items are uniform and you want the height detected
automatically rather than hardcoding `ItemHeightRequest`. It costs a single measurement
(the first item) on load. Behavior per platform:

- **Android:** items start as `WrapContent`; the first bound item is measured, its height
  becomes fixed for all (new view holders are created with it).
- **iOS/Mac:** the compositional layout starts `Estimated`; once the first cell is measured
  it is rebuilt as `Absolute(measuredHeight)`.
- A new list load re-measures the first item (a small reflow on load).

On Windows, `ItemSizingStrategy` is mapped to the internal `CollectionView`:

| Agile | Windows MAUI |
|---|---|
| `Fixed` | `CollectionView.ItemSizingStrategy = MeasureFirstItem` |
| `MeasureFirst` | `CollectionView.ItemSizingStrategy = MeasureFirstItem` |
| `Dynamic` | `CollectionView.ItemSizingStrategy = MeasureAllItems` |

`ItemHeight` and `ItemHeightRequest` have no direct mapping on Windows. For
fixed height on Windows, set `HeightRequest` within the `DataTemplate` itself.

## Scroll bar visibility

`VerticalScrollBarVisibility` and `HorizontalScrollBarVisibility` (type
`Microsoft.Maui.ScrollBarVisibility`) control the native scroll bar / indicator.

| Value | Meaning |
|---|---|
| `Default` | Platform default — **Android: hidden** (preserves the control's original behavior); **iOS/Mac:** native indicator that auto-fades after the gesture. |
| `Always` | Scroll bar shown. **Android:** persistent (never fades). **iOS/Mac:** native indicator (always auto-fades — there is no native "pinned" mode). |
| `Never` | Hidden. |

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    VerticalScrollBarVisibility="Always" />
```

> **Platform note — Android requires API 29+.** The `RecyclerView` is created in code
> without an `AttributeSet`, so its internal `ScrollBarDrawable` is only initialized via
> `setVerticalScrollbarThumbDrawable`, available from **API 29 (Android 10)**. On API 24–28
> the scroll bar stays hidden (it degrades gracefully — no crash) even with `Always`.
> When shown on Android, the thumb is a translucent gray rounded drawable.
>
> Only `Always` shows the bar on Android; `Default` keeps it hidden to preserve the prior
> behavior of existing screens.

## Platform behavior

| Platform | Implementation |
|---|---|
| Android | `RecyclerView`, `LinearLayoutManager`, `GridLayoutManager`, and `CachingLinearLayoutManager` for dynamic height. View holders discarded by the recycled-view pool are tracked by weak reference and can be collected. |
| iOS/MacCatalyst | `UICollectionView` with `UICollectionViewCompositionalLayout`; `PreferredLayoutAttributesFitting` measures MAUI views. Cells hold the collection view by weak reference, so closing the page releases the list, cells, and views. |
| Windows | `ContentView` hosting a MAUI `CollectionView`, with mouse drag-to-scroll and inertia. The automatic scroll back to the first item (empty → non-empty) only moves the native scroller offset, never `ItemsView.ScrollTo`, which can freeze the UI on MAUI 11 right after a large batch. |

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
- Use `MeasureFirst` for uniform items when you want `Fixed` performance without hardcoding the height.
- Use `Dynamic` only when the height genuinely varies between items (it is the only mode that never clips).
- Tune `ItemHeightRequest` to something close to the real average, especially on iOS/Mac (it is the scroll estimate in `Dynamic`/`MeasureFirst`).
- Use `x:DataType` in your `DataTemplate`.
- Avoid large remote images inside cells; prefer thumbnails or the `ImageView` from the `GalleryView` package.
- Use `ObservableRangeCollection<T>` for pagination (`AddRange`) and refresh/filter (`ReplaceAll`). `ObservableCollection<T>` works, but one `Add` per item generates more UI notifications and more work during scroll/load.

See also [../TUNING.md](../TUNING.md) and [../PROFILING.md](../PROFILING.md).
