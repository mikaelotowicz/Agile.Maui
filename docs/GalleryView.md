# Agile.Maui.Gallery

Project that delivers two visual controls for images:

- `ImageView`: single image with native loading, load events, and fullscreen zoom on the supported platforms.
- `GalleryView`: paged image gallery with selection, indicators, and fullscreen on the supported platforms.

Assembly: `Agile.Maui.Gallery`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileGalleryView()`

## Installation

```powershell
dotnet add package Agile.Maui.Gallery
```

```csharp
using Agile.Maui;

builder.UseAgileGalleryView();
```

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
```

## ImageView

`ImageView` is a cross-platform MAUI `View`. It renders a local image or URL
and, when allowed, opens a fullscreen view with zoom.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Source` | `string?` | `null` | Local resource name or full URL. |
| `IsUrl` | `bool` | `false` | Indicates that `Source` is an HTTP/HTTPS URL. |
| `Placeholder` | `string?` | `null` | Local resource shown during loading or on error. |
| `MaxZoom` | `float` | `5` | Maximum zoom of the fullscreen viewer. Minimum accepted: `1`. |
| `EnableFullscreen` | `bool` | `true` | Opens fullscreen when the image is tapped on the supported platforms. |
| `FullscreenSource` | `string?` | `null` | Higher-quality source for fullscreen. If null, uses `Source`. Ignored where fullscreen is not implemented. |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` or `AspectFit`. |
| `ImageLoadedCommand` | `ICommand?` | `null` | Command executed on load. |
| `ImageFailedCommand` | `ICommand?` | `null` | Command executed on failure. |

### Events

| Event | Args | When it fires |
|---|---|---|
| `ImageLoaded` | `EventArgs` | When the image loads successfully. |
| `ImageFailed` | `EventArgs` | When loading fails or the source does not exist. |

### Example

```xml
<gallery:ImageView
    Source="https://picsum.photos/seed/maui/900/600"
    IsUrl="True"
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    EnableFullscreen="True"
    MaxZoom="6"
    HeightRequest="220" />
```

## GalleryView

`GalleryView` displays a list of images in a paged format. It uses the same
`ZoomImageAspect` enum as `ImageView` and can open the gallery in fullscreen with swipe and
zoom on the platforms that support this flow.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Images` | `IList<string>?` | `null` | List of URLs or local resources. |
| `IsUrl` | `bool` | `false` | Indicates whether the `Images` items are URLs. |
| `Placeholder` | `string?` | `null` | Fallback while each image loads. |
| `SelectedIndex` | `int` | `0` | Selected index. Minimum value: `0`. |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | How the image fills the space. |
| `MaxZoom` | `float` | `5` | Maximum zoom in fullscreen. |
| `ShowIndicator` | `bool` | `false` | Shows page indicators. |
| `IndicatorColor` | `Color` | `White` | Color of the active indicator. |
| `IndicatorInactiveColor` | `Color` | white 50% | Color of the inactive indicators. |
| `SelectionChangedCommand` | `ICommand?` | `null` | Receives the selected index. |
| `ImageLoadedCommand` | `ICommand?` | `null` | Command when an image loads. |
| `ImageFailedCommand` | `ICommand?` | `null` | Command when an image fails. |
| `ThumbMaxPx` | `int` | `720` | Thumbnail decode limit on Android. Minimum: `64`. |

### Events

| Event | Args | When it fires |
|---|---|---|
| `SelectionChanged` | `GalleryIndexChangedEventArgs` | When the current page changes. |
| `ImageLoaded` | `EventArgs` | When an image loads. |
| `ImageFailed` | `EventArgs` | When an image fails. |

### Example

```xml
<gallery:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    ShowIndicator="True"
    SelectedIndex="{Binding CurrentPhoto, Mode=TwoWay}"
    SelectionChangedCommand="{Binding PhotoChangedCommand}"
    HeightRequest="240" />
```

## Per-platform behavior

| Platform | `ImageView` | `GalleryView` |
|---|---|---|
| Android | `Android.Widget.ImageView` with Glide; fullscreen via `DialogFragment` and `Matrix`. | `ViewPager2`/`RecyclerView`; native fullscreen with swipe and zoom. |
| iOS/MacCatalyst | `UIImageView`; URLs via `NSUrlSession`; fullscreen with `UIScrollView`. | Paged `UIScrollView` + `UIPageControl`; fullscreen with zoom. |
| Windows | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`; fullscreen is not implemented. | `FlipView` with indicators. |

## Recommendations

- Use `FullscreenSource` when the list shows thumbnails but fullscreen should open a higher-quality image.
- In large lists, prefer URLs already resized on the server.
- On Android, lower `ThumbMaxPx` when many remote images are alive at the same time.
- Always set `Placeholder` to avoid visual flashes while the image loads.
