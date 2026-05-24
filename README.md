# <img src="Constrols/agile.png" width="118" height="118" align="center" /> Agile.Maui

.NET MAUI component library with native implementations for Android, iOS, macOS Catalyst, and Windows.

| Control | Android | iOS / macOS | Windows |
|---|---|---|---|
| `ImageView` | Glide + Matrix zoom | UIScrollView zoom | BitmapImage |
| `GalleryView` | ViewPager2 | UIScrollView paging | FlipView |
| `VirtualizedCollectionView` | RecyclerView | UICollectionView | CollectionView (MAUI) |

---

## Installation

```bash
dotnet add package Agile.Maui
```

### Register in `MauiProgram.cs`

```csharp
using Agile.Maui;

builder.UseAgileMaui();
```

### XAML namespace

```xml
xmlns:controls="clr-namespace:Agile.Maui;assembly=Agile.Maui"
```

---

## ImageView

Displays a single image with **zoom** and **fullscreen** support. Tapping the image (when `EnableFullscreen="True"`) opens a full-screen viewer with pinch-to-zoom and double-tap.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Source` | `string?` | `null` | Local resource name or full URL |
| `IsUrl` | `bool` | `false` | `true` when `Source` is an HTTP/HTTPS URL |
| `Placeholder` | `string?` | `null` | Local resource shown while loading and on error |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` or `AspectFit` |
| `MaxZoom` | `float` | `5` | Maximum scale in fullscreen (minimum: `1`) |
| `EnableFullscreen` | `bool` | `true` | Enables the fullscreen viewer on tap |
| `ImageLoadedCommand` | `ICommand?` | `null` | Executed when the image loads successfully |
| `ImageFailedCommand` | `ICommand?` | `null` | Executed when loading fails |

### Events

| Event | Description |
|---|---|
| `ImageLoaded` | Raised when the image loads successfully |
| `ImageFailed` | Raised when loading fails or the resource is not found |

### XAML examples

**URL with CenterCrop and max zoom 6×:**
```xml
<controls:ImageView
    Source="https://example.com/image.jpg"
    IsUrl="True"
    AspectMode="CenterCrop"
    MaxZoom="6"
    Placeholder="placeholder"
    HeightRequest="220" />
```

**Local image with AspectFit:**
```xml
<controls:ImageView
    Source="my_image"
    IsUrl="False"
    AspectMode="AspectFit"
    MaxZoom="4"
    HeightRequest="180" />
```

**Fullscreen disabled:**
```xml
<controls:ImageView
    Source="https://example.com/image.jpg"
    IsUrl="True"
    EnableFullscreen="False"
    Placeholder="placeholder"
    HeightRequest="140" />
```

**With events:**
```xml
<controls:ImageView
    Source="{Binding ImageUrl}"
    IsUrl="True"
    ImageLoaded="OnImageLoaded"
    ImageFailed="OnImageFailed"
    HeightRequest="220" />
```

**With commands (MVVM):**
```xml
<controls:ImageView
    Source="{Binding ImageUrl}"
    IsUrl="True"
    ImageLoadedCommand="{Binding OnLoadedCommand}"
    ImageFailedCommand="{Binding OnFailedCommand}"
    HeightRequest="220" />
```

### Local images

- **Android:** drawable name under `Resources/drawable/` (no extension)
- **iOS / macOS:** asset name under `Resources/Images/` (no extension)
- **Windows:** relative path under `Resources/Images/` (with or without `.png` extension)

---

## GalleryView

Image gallery with **horizontal swipe**, page indicator, and **fullscreen viewer with zoom and swipe**. Supports `ObservableCollection<string>` — the gallery updates automatically when the collection changes.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Images` | `IList<string>?` | `null` | List of sources (URLs or local resource names) |
| `IsUrl` | `bool` | `false` | `true` when sources are HTTP/HTTPS URLs |
| `Placeholder` | `string?` | `null` | Local resource shown while loading and on error |
| `SelectedIndex` | `int` | `0` | Index of the visible image (bindable, two-way) |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` or `AspectFit` |
| `MaxZoom` | `float` | `5` | Maximum scale in fullscreen (minimum: `1`) |
| `ShowIndicator` | `bool` | `false` | Shows page indicator dots |
| `SelectionChangedCommand` | `ICommand?` | `null` | Executed on page change; receives the new index (`int`) as parameter |
| `ImageLoadedCommand` | `ICommand?` | `null` | Executed when an image loads successfully |
| `ImageFailedCommand` | `ICommand?` | `null` | Executed when an image fails to load |

### Events

| Event | Args | Description |
|---|---|---|
| `SelectionChanged` | `GalleryIndexChangedEventArgs` | Raised on page change; `e.Index` holds the current index |
| `ImageLoaded` | `EventArgs` | Raised when an image loads successfully |
| `ImageFailed` | `EventArgs` | Raised when an image fails to load |

### XAML examples

**URL gallery with indicator:**
```xml
<controls:GalleryView
    Images="{Binding ImageUrls}"
    IsUrl="True"
    AspectMode="CenterCrop"
    MaxZoom="6"
    ShowIndicator="True"
    Placeholder="placeholder"
    HeightRequest="220" />
```

**Local image gallery with AspectFit:**
```xml
<controls:GalleryView
    Images="{Binding LocalImages}"
    IsUrl="False"
    AspectMode="AspectFit"
    MaxZoom="4"
    ShowIndicator="True"
    HeightRequest="200" />
```

**With two-way index binding and events:**
```xml
<controls:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    SelectedIndex="{Binding CurrentIndex}"
    SelectionChanged="OnSelectionChanged"
    ImageLoaded="OnImageLoaded"
    ShowIndicator="True"
    HeightRequest="240" />
```

**With commands (MVVM):**
```xml
<controls:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    SelectedIndex="{Binding CurrentIndex, Mode=TwoWay}"
    SelectionChangedCommand="{Binding PageChangedCommand}"
    ShowIndicator="True"
    HeightRequest="240" />
```

### Reading the selected index in code-behind

```csharp
private void OnSelectionChanged(object sender, GalleryIndexChangedEventArgs e)
{
    Console.WriteLine($"Current page: {e.Index}");
}
```

### ObservableCollection — reactive updates

```csharp
// The gallery updates automatically when items are added or removed
var photos = new ObservableCollection<string>
{
    "https://example.com/photo1.jpg",
    "https://example.com/photo2.jpg",
};
GalleryControl.Images = photos;

photos.Add("https://example.com/photo3.jpg"); // gallery updates automatically
```

---

## VirtualizedCollectionView

List or grid with **native virtualization** on all platforms. Supports fixed height (better performance) or automatic height, multiple columns, infinite scroll, and scroll events.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` | Data source (supports `ObservableCollection`) |
| `ItemTemplate` | `DataTemplate?` | `null` | Template for each item |
| `ItemHeight` | `double` | `-1` | Fixed item height in DIPs. `-1` = automatic (wrap_content) |
| `ColumnCount` | `int` | `1` | Number of columns (minimum: `1`) |
| `Orientation` | `VirtualizedOrientation` | `Vertical` | `Vertical` or `Horizontal` |
| `ItemSpacing` | `double` | `0` | Spacing between items in DIPs |
| `RemainingItemsThreshold` | `int` | `-1` | Triggers `RemainingItemsThresholdReached` when N items remain. `-1` = disabled |
| `RemainingItemsThresholdReachedCommand` | `ICommand?` | `null` | Executed when the threshold is reached (infinite scroll) |
| `ScrolledCommand` | `ICommand?` | `null` | Executed on each scroll event; receives `VirtualizedScrolledEventArgs` |
| `EmptyView` | `object?` | `null` | Content shown when `ItemsSource` is empty |
| `EmptyViewTemplate` | `DataTemplate?` | `null` | Template for the empty state |

### Events

| Event | Args | Description |
|---|---|---|
| `RemainingItemsThresholdReached` | `EventArgs` | Raised when `RemainingItemsThreshold` items remain |
| `Scrolled` | `VirtualizedScrolledEventArgs` | Raised on each scroll; `e.ScrollX` and `e.ScrollY` in DIPs |

### Methods

| Method | Description |
|---|---|
| `ScrollTo(int index, bool animated = true)` | Scrolls the list to the item at the given index |

### XAML examples

**Simple list with automatic height:**
```xml
<controls:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    RemainingItemsThreshold="10"
    RemainingItemsThresholdReached="OnLoadMore">
    <controls:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate>
            <Label Text="{Binding Name}" Padding="16,12" />
        </DataTemplate>
    </controls:VirtualizedCollectionView.ItemTemplate>
</controls:VirtualizedCollectionView>
```

**2-column grid with fixed height (better performance):**
```xml
<controls:VirtualizedCollectionView
    ItemsSource="{Binding Products}"
    ColumnCount="2"
    ItemHeight="120"
    ItemSpacing="4"
    RemainingItemsThreshold="8"
    RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">
    <controls:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate>
            <Grid Padding="8">
                <Label Text="{Binding Name}" />
            </Grid>
        </DataTemplate>
    </controls:VirtualizedCollectionView.ItemTemplate>
</controls:VirtualizedCollectionView>
```

**Infinite scroll with scroll monitoring (MVVM):**
```xml
<controls:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    ItemHeight="80"
    RemainingItemsThreshold="15"
    RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}"
    ScrolledCommand="{Binding ScrolledCommand}">
    <controls:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:MyItem">
            <Label Text="{Binding Title}" Padding="16,12" />
        </DataTemplate>
    </controls:VirtualizedCollectionView.ItemTemplate>
    <controls:VirtualizedCollectionView.EmptyView>
        <Label Text="No items found." HorizontalOptions="Center" />
    </controls:VirtualizedCollectionView.EmptyView>
</controls:VirtualizedCollectionView>
```

**Programmatic scroll:**
```csharp
MyList.ScrollTo(index: 50, animated: true);
```

### `VirtualizedScrolledEventArgs`

```csharp
private void OnScrolled(object sender, VirtualizedScrolledEventArgs e)
{
    Console.WriteLine($"ScrollY: {e.ScrollY:F0} dp");
}
```

### Performance tip

Set `ItemHeight` to a fixed value whenever the template has a predictable height. This allows RecyclerView (Android) and UICollectionView (iOS) to calculate layout without measuring each item individually, reducing scroll time and CPU usage.

---

## Enum `ZoomImageAspect`

Used by `ImageView.AspectMode` and `GalleryView.AspectMode`.

| Value | Behavior |
|---|---|
| `CenterCrop` | Fills the available space, cropping the edges (equivalent to `ScaleAspectFill` / `UniformToFill`) |
| `AspectFit` | Shows the entire image within the space, with letterboxing if needed (equivalent to `ScaleAspectFit` / `Uniform`) |

---

## Requirements

- .NET 10
- .NET MAUI 10
- Android API 21+
- iOS 15+
- macOS Catalyst 15+
- Windows 10 build 17763+

---

## License

MIT © 2025 Micael Otowicz
