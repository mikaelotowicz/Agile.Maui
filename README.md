# <img src="Controls/agile.png" width="118" height="118" align="center" /> Agile.Maui

.NET MAUI component library with **native** implementations for Android, iOS, macOS Catalyst, and Windows. Every control maps directly to the platform's own scrolling and rendering infrastructure — no WebView, no abstraction layers.

| Control | Android | iOS / macOS Catalyst | Windows |
|---|---|---|---|
| `ImageView` | `Android.Widget.ImageView` + Glide + Matrix zoom | `UIImageView` + `UIScrollView` zoom | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage` |
| `GalleryView` | `ViewPager2` + `RecyclerView` | `UIScrollView` paging + `UIPageControl` | `FlipView` |
| `VirtualizedCollectionView` | `RecyclerView` + `LinearLayoutManager` | `UICollectionView` + `UICollectionViewCompositionalLayout` | MAUI `CollectionView` (built-in virtualization) |

---

## Installation

```bash
dotnet add package Agile.Maui
```

### Register in `MauiProgram.cs`

```csharp
using Agile.Maui;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseAgileMaui();   // registers all handlers

    return builder.Build();
}
```

### XAML namespace

```xml
xmlns:agile="clr-namespace:Agile.Maui;assembly=Agile.Maui"
```

---

## ImageView

Displays a single image with optional **zoom** and **fullscreen** viewer. When `EnableFullscreen` is `true` and the user taps the image, a full-screen overlay opens with pinch-to-zoom, double-tap, and single-tap-to-dismiss.

### How it works

| Platform | Thumbnail | Fullscreen viewer |
|---|---|---|
| **Android** | `Android.Widget.ImageView` loaded by **Glide** (memory + disk cache, `RequestOptions.Override` for bounded decode) | `FullscreenZoomDialogFragment` — pure native Matrix zoom via `ScaleGestureDetector`, `GestureDetector`, and `ValueAnimator`. No external dependencies. |
| **iOS / macOS** | `UIImageView` loaded by `NSUrlSession` with a `CancellationTokenSource` per load; placeholder shown immediately | `FullscreenZoomViewController` — `UIScrollView` with built-in pinch zoom (`minimumZoomScale` / `maximumZoomScale`) + `UITapGestureRecognizer` for double-tap and dismiss |
| **Windows** | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`; `ImageOpened` / `ImageFailed` events forwarded to MAUI | No fullscreen viewer (fullscreen is a no-op on Windows) |

Image loading in all handlers is fully cancellable: loading a new `Source` while a previous load is in-flight cancels the previous operation before starting the next.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Source` | `string?` | `null` | Local resource name or full HTTP/HTTPS URL |
| `IsUrl` | `bool` | `false` | Set to `true` when `Source` is a URL |
| `Placeholder` | `string?` | `null` | Local resource shown while loading and on error |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` fills and crops; `AspectFit` letterboxes |
| `MaxZoom` | `float` | `5` | Maximum pinch scale in the fullscreen viewer (minimum: `1`) |
| `EnableFullscreen` | `bool` | `true` | Enables the fullscreen viewer on tap |
| `FullscreenSource` | `string?` | `null` | High-quality URL or resource to load in the fullscreen viewer. When `null`, falls back to `Source` |
| `ImageLoadedCommand` | `ICommand?` | `null` | Executed when the image loads successfully |
| `ImageFailedCommand` | `ICommand?` | `null` | Executed when loading fails or the resource is not found |

### Events

| Event | Args | Description |
|---|---|---|
| `ImageLoaded` | `EventArgs` | Raised when the image loads successfully |
| `ImageFailed` | `EventArgs` | Raised when loading fails or the resource is not found |

### XAML examples

**URL with CenterCrop and max zoom 6×:**
```xml
<agile:ImageView
    Source="https://example.com/photo.jpg"
    IsUrl="True"
    AspectMode="CenterCrop"
    MaxZoom="6"
    Placeholder="placeholder"
    HeightRequest="220" />
```

**High-quality fullscreen source:**
```xml
<agile:ImageView
    Source="https://cdn.example.com/thumb.jpg"
    FullscreenSource="https://cdn.example.com/full.jpg"
    IsUrl="True"
    AspectMode="CenterCrop"
    MaxZoom="8"
    HeightRequest="220" />
```

**Local image with AspectFit:**
```xml
<agile:ImageView
    Source="my_image"
    IsUrl="False"
    AspectMode="AspectFit"
    MaxZoom="4"
    HeightRequest="180" />
```

**Fullscreen disabled:**
```xml
<agile:ImageView
    Source="https://example.com/photo.jpg"
    IsUrl="True"
    EnableFullscreen="False"
    Placeholder="placeholder"
    HeightRequest="140" />
```

**Code-behind events:**
```xml
<agile:ImageView
    Source="{Binding ImageUrl}"
    IsUrl="True"
    ImageLoaded="OnImageLoaded"
    ImageFailed="OnImageFailed"
    HeightRequest="220" />
```

```csharp
private void OnImageLoaded(object sender, EventArgs e) { /* image ready */ }
private void OnImageFailed(object sender, EventArgs e) { /* show error UI */ }
```

**MVVM commands:**
```xml
<agile:ImageView
    Source="{Binding ImageUrl}"
    IsUrl="True"
    ImageLoadedCommand="{Binding OnLoadedCommand}"
    ImageFailedCommand="{Binding OnFailedCommand}"
    HeightRequest="220" />
```

### Local image path conventions

| Platform | Location | Value for `Source` |
|---|---|---|
| Android | `Resources/drawable/` | Filename without extension (`my_photo`) |
| iOS / macOS | `Resources/Images/` | Asset name without extension (`my_photo`) |
| Windows | `Resources/Images/` | Filename with or without `.png` extension |

---

## GalleryView

Horizontal image gallery with swipe navigation, page indicator, and a **fullscreen viewer** that preserves the swipe gesture between pages and adds pinch-to-zoom. Supports `ObservableCollection<string>` — add or remove items at runtime and the gallery updates automatically.

### How it works

| Platform | Gallery | Fullscreen viewer |
|---|---|---|
| **Android** | `ViewPager2` (backed by `RecyclerView`) with a `ThumbPagerAdapter`. Each page is an `Android.Widget.ImageView` loaded by Glide. Indicator is a `LinearLayout` of `ShapeDrawable` dots. | `FullscreenGalleryFragment` — another `ViewPager2` with `ZoomTouchHandler` on each page (same Matrix-based zoom as `ImageView`). Page position is passed from thumbnail to fullscreen and back. |
| **iOS / macOS** | `UIScrollView` with `isPagingEnabled = true`. Each page is a `UIImageView` loaded asynchronously by `NSUrlSession`, cancellable via a per-page `CancellationTokenSource`. Indicator is a `UIPageControl`. | `FullscreenGalleryViewController` — another paging `UIScrollView` with a `UIScrollView`-based zoom per page. |
| **Windows** | `FlipView` with a `BitmapImage` per item. Indicator is a `StackPanel` of `Ellipse` dots. Image events (`ImageOpened` / `ImageFailed`) are forwarded to MAUI. | No fullscreen viewer. |

Thumbnail download is bounded by `ThumbMaxPx`: Glide (Android) uses `RequestOptions.Override(ThumbMaxPx, ThumbMaxPx)` to avoid decoding the full-resolution bitmap into memory.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Images` | `IList<string>?` | `null` | List of URLs or local resource names |
| `IsUrl` | `bool` | `false` | `true` when sources are HTTP/HTTPS URLs |
| `Placeholder` | `string?` | `null` | Local resource shown while loading and on error |
| `SelectedIndex` | `int` | `0` | Index of the currently visible page (bindable, two-way) |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` fills and crops; `AspectFit` letterboxes |
| `MaxZoom` | `float` | `5` | Maximum pinch scale in the fullscreen viewer (minimum: `1`) |
| `ShowIndicator` | `bool` | `false` | Shows page indicator dots below the gallery |
| `IndicatorColor` | `Color` | `Colors.White` | Color of the active dot |
| `IndicatorInactiveColor` | `Color` | `rgba(255,255,255,0.5)` | Color of inactive dots |
| `ThumbMaxPx` | `int` | `720` | Maximum pixel dimension for thumbnail decode (minimum: `64`). Lower values reduce memory on large galleries. |
| `SelectionChangedCommand` | `ICommand?` | `null` | Executed on page change; receives the new index (`int`) as parameter |
| `ImageLoadedCommand` | `ICommand?` | `null` | Executed when any image in the gallery loads |
| `ImageFailedCommand` | `ICommand?` | `null` | Executed when any image in the gallery fails to load |

### Events

| Event | Args | Description |
|---|---|---|
| `SelectionChanged` | `GalleryIndexChangedEventArgs` | Raised on page change; `e.Index` is the new page index |
| `ImageLoaded` | `EventArgs` | Raised when an image in the gallery loads successfully |
| `ImageFailed` | `EventArgs` | Raised when an image fails to load |

### XAML examples

**URL gallery with indicator:**
```xml
<agile:GalleryView
    Images="{Binding ImageUrls}"
    IsUrl="True"
    AspectMode="CenterCrop"
    MaxZoom="6"
    ShowIndicator="True"
    Placeholder="placeholder"
    HeightRequest="220" />
```

**Custom indicator colors:**
```xml
<agile:GalleryView
    Images="{Binding ImageUrls}"
    IsUrl="True"
    ShowIndicator="True"
    IndicatorColor="#FF6200EE"
    IndicatorInactiveColor="#66000000"
    HeightRequest="220" />
```

**Memory-optimized gallery with large image lists:**
```xml
<agile:GalleryView
    Images="{Binding ImageUrls}"
    IsUrl="True"
    ThumbMaxPx="480"
    AspectMode="CenterCrop"
    HeightRequest="220" />
```

**Local image gallery with AspectFit:**
```xml
<agile:GalleryView
    Images="{Binding LocalImages}"
    IsUrl="False"
    AspectMode="AspectFit"
    MaxZoom="4"
    ShowIndicator="True"
    HeightRequest="200" />
```

**Two-way index binding with events:**
```xml
<agile:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    SelectedIndex="{Binding CurrentIndex, Mode=TwoWay}"
    SelectionChanged="OnSelectionChanged"
    ShowIndicator="True"
    HeightRequest="240" />
```

```csharp
private void OnSelectionChanged(object sender, GalleryIndexChangedEventArgs e)
{
    Console.WriteLine($"Current page: {e.Index}");
}
```

**MVVM:**
```xml
<agile:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    SelectedIndex="{Binding CurrentIndex, Mode=TwoWay}"
    SelectionChangedCommand="{Binding PageChangedCommand}"
    ShowIndicator="True"
    HeightRequest="240" />
```

### Reactive updates with ObservableCollection

```csharp
var photos = new ObservableCollection<string>
{
    "https://example.com/photo1.jpg",
    "https://example.com/photo2.jpg",
};
GalleryControl.Images = photos;

photos.Add("https://example.com/photo3.jpg"); // gallery updates automatically
photos.RemoveAt(0);                            // and on removal too
```

---

## VirtualizedCollectionView

High-performance list or grid with **native virtualization** on all platforms. Renders only the items visible on screen and recycles views as the user scrolls — independent of collection size.

### How it works

| Platform | Engine | Layout | Item measurement |
|---|---|---|---|
| **Android** | `RecyclerView` | `CachingLinearLayoutManager` (single column) or `GridLayoutManager` (multi-column). Heights cached in a `SparseIntArray` to avoid re-measuring items already seen. | `ItemHeight > 0` → `setHasFixedSize(true)` + `MeasureSpec.EXACTLY`. `ItemHeight = -1` → `wrap_content`. |
| **iOS / macOS** | `UICollectionView` with `UICollectionViewCompositionalLayout` | `ItemSizeStrategy.Fixed` → `NSCollectionLayoutSize.CreateAbsolute`. `ItemSizeStrategy.Dynamic` → `NSCollectionLayoutSize.CreateEstimated` + self-sizing via MAUI `Measure/Arrange`. | Self-sizing uses `((IView)mauiView).Measure(width, ∞)` — not Auto Layout — because MAUI views don't expose `intrinsicContentSize`. |
| **Windows** | MAUI built-in `CollectionView` (backed by `ItemsRepeater` with virtualization) | `GridItemsLayout` (multi-column) or `LinearItemsLayout` | Delegated to the MAUI layout engine. `ItemHeight` is ignored on Windows. |

**Android — memory tuning:** `ItemViewCacheSize` and `RecycledViewPool` sizes are calculated from the device's available RAM at handler connect time, so the pool is larger on high-RAM devices.

**iOS — event coalescing:** `INotifyCollectionChanged` events are queued and flushed in a single `PerformBatchUpdates` call. When a batch exceeds 30 operations, or contains a mix of inserts, deletes, and moves, the handler falls back to `ReloadData` to avoid index conflicts.

**Android — async diffing:** When `ItemsSource` is replaced entirely, `DiffUtil.CalculateDiff` runs on a background thread pool and is cancelled if a newer update arrives before the diff completes.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` | Data source; supports `ObservableCollection` |
| `ItemTemplate` | `DataTemplate?` | `null` | Template rendered for each item |
| `ItemHeight` | `double` | `-1` | Fixed item height in DIPs for Android/iOS. `-1` = measure each item. Ignored on Windows. |
| `ItemSizeStrategy` | `ItemSizeStrategy` | `Fixed` | `Fixed`: use `ItemHeight` (faster). `Dynamic`: self-sizing cells, iOS only (see note below). |
| `ItemHeightRequest` | `double` | `350` | Estimated height hint used when `ItemSizeStrategy = Dynamic` (iOS). Should be close to the real height to avoid layout thrashing. |
| `ColumnCount` | `int` | `1` | Number of grid columns (minimum: `1`) |
| `Orientation` | `VirtualizedOrientation` | `Vertical` | `Vertical` or `Horizontal` |
| `ItemSpacing` | `double` | `0` | Gap between items in DIPs |
| `RemainingItemsThreshold` | `int` | `-1` | Fires `RemainingItemsThresholdReached` when N items remain before the end. `-1` disables. |
| `RemainingItemsThresholdReachedCommand` | `ICommand?` | `null` | Executed when the threshold is reached (infinite scroll) |
| `ScrolledCommand` | `ICommand?` | `null` | Executed on scroll; receives `VirtualizedScrolledEventArgs` |
| `EmptyView` | `object?` | `null` | View shown when `ItemsSource` is empty or null |
| `EmptyViewTemplate` | `DataTemplate?` | `null` | Template for the empty state |

### Events

| Event | Args | Description |
|---|---|---|
| `RemainingItemsThresholdReached` | `EventArgs` | Raised when `RemainingItemsThreshold` items remain before the end of the list |
| `Scrolled` | `VirtualizedScrolledEventArgs` | Raised on each scroll frame; `e.ScrollX` and `e.ScrollY` in DIPs |

### Methods

| Method | Description |
|---|---|
| `ScrollTo(int index, bool animated = true)` | Scrolls to the item at `index` |

### XAML examples

**Simple list with automatic height:**
```xml
<agile:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    RemainingItemsThreshold="10"
    RemainingItemsThresholdReached="OnLoadMore">
    <agile:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate>
            <Label Text="{Binding Name}" Padding="16,12" />
        </DataTemplate>
    </agile:VirtualizedCollectionView.ItemTemplate>
</agile:VirtualizedCollectionView>
```

**2-column grid with fixed height (best performance):**
```xml
<agile:VirtualizedCollectionView
    ItemsSource="{Binding Products}"
    ColumnCount="2"
    ItemHeight="120"
    ItemSpacing="4"
    RemainingItemsThreshold="8"
    RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">
    <agile:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:Product">
            <Grid Padding="8">
                <Label Text="{Binding Name}" />
            </Grid>
        </DataTemplate>
    </agile:VirtualizedCollectionView.ItemTemplate>
</agile:VirtualizedCollectionView>
```

**Dynamic height with estimated size (iOS self-sizing):**
```xml
<agile:VirtualizedCollectionView
    ItemsSource="{Binding Feed}"
    ItemSizeStrategy="Dynamic"
    ItemHeightRequest="200"
    RemainingItemsThreshold="5"
    RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">
    <agile:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:Post">
            <StackLayout Padding="16,12">
                <Label Text="{Binding Title}" FontSize="16" FontAttributes="Bold" />
                <Label Text="{Binding Body}" />
            </StackLayout>
        </DataTemplate>
    </agile:VirtualizedCollectionView.ItemTemplate>
</agile:VirtualizedCollectionView>
```

**Infinite scroll with scroll monitoring (MVVM):**
```xml
<agile:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    ItemHeight="80"
    RemainingItemsThreshold="15"
    RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}"
    ScrolledCommand="{Binding ScrolledCommand}">
    <agile:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:MyItem">
            <Label Text="{Binding Title}" Padding="16,12" />
        </DataTemplate>
    </agile:VirtualizedCollectionView.ItemTemplate>
    <agile:VirtualizedCollectionView.EmptyView>
        <Label Text="No items found." HorizontalOptions="Center" VerticalOptions="Center" />
    </agile:VirtualizedCollectionView.EmptyView>
</agile:VirtualizedCollectionView>
```

**Programmatic scroll:**
```csharp
MyList.ScrollTo(index: 50, animated: true);
```

**Reading scroll position:**
```csharp
private void OnScrolled(object sender, VirtualizedScrolledEventArgs e)
{
    Console.WriteLine($"ScrollY: {e.ScrollY:F0} dp");
}
```

### `ItemSizeStrategy` guide

| Strategy | When to use | Platforms |
|---|---|---|
| `Fixed` | Item template has a known, constant height. Set `ItemHeight` to that value. | Android, iOS, macOS, Windows |
| `Dynamic` | Template height varies per item (e.g. feed with variable-length text). Set `ItemHeightRequest` to an estimate close to the average height. | iOS, macOS. On Android, set `ItemHeight = -1` instead (same effect). |

> **iOS tip:** When using `ItemSizeStrategy.Dynamic`, set `ItemHeightRequest` as close to the real average height as possible. UIKit uses this estimate to allocate the initial cell pool. A very small estimate (e.g. 44) causes UIKit to create many extra cells upfront, driving memory usage significantly higher.

### Performance tips

- **Always set `ItemHeight`** when the template has a predictable height. This lets RecyclerView and UICollectionView skip individual item measurement during scroll, reducing CPU usage and jank.
- **Set `ItemSizeStrategy = Fixed`** (the default) unless items genuinely vary in height.
- **Use `x:DataType`** in `DataTemplate` to enable compiled bindings — avoids reflection overhead per item.
- **Prefer `ObservableCollection`** over replacing the entire `ItemsSource` for incremental updates. On Android, incremental changes run `DiffUtil` which animates and avoids full redraw. On iOS, changes are batched into a single `PerformBatchUpdates` call.

---

## Enums

### `ZoomImageAspect`

Used by `ImageView.AspectMode` and `GalleryView.AspectMode`.

| Value | Behavior | Equivalent |
|---|---|---|
| `CenterCrop` | Fills the view, cropping edges | Android `centerCrop` / iOS `ScaleAspectFill` / WinUI `UniformToFill` |
| `AspectFit` | Fits the entire image, letterboxing if needed | Android `fitCenter` / iOS `ScaleAspectFit` / WinUI `Uniform` |

### `VirtualizedOrientation`

Used by `VirtualizedCollectionView.Orientation`.

| Value | Description |
|---|---|
| `Vertical` | Items flow top to bottom (default) |
| `Horizontal` | Items flow left to right |

### `ItemSizeStrategy`

Used by `VirtualizedCollectionView.ItemSizeStrategy`.

| Value | Description |
|---|---|
| `Fixed` | Each item has the height set by `ItemHeight`. Fastest option. |
| `Dynamic` | Items measure themselves. Use `ItemHeightRequest` as the initial size hint. |

---

## Requirements

| Requirement | Version |
|---|---|
| .NET | 10 |
| .NET MAUI | 10 |
| Android | API 21+ (Android 5.0) |
| iOS | 15.0+ |
| macOS Catalyst | 15.0+ |
| Windows | 10 build 19041+ (Windows 10 2004) |

---

## Performance tuning

For a detailed reference on internal cache sizes, layout manager selection, DiffUtil behavior, iOS coalescing thresholds, and memory guidelines for images inside cells, see **[TUNING.md](TUNING.md)**.

---

## License

MIT © 2025 Micael Otowicz
