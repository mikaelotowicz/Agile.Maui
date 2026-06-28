# Agile.Maui.Gallery

Project that delivers two visual controls for images:

- `ImageView`: single image with native loading, bounded decode, load state, load events, and fullscreen zoom on the supported platforms.
- `GalleryView`: paged image gallery with selection, indicators, bounded thumbnail decode, and fullscreen on the supported platforms.

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

`ImageView` is a cross-platform MAUI `View`. It renders a local image path,
MAUI image resource, Android drawable/mipmap, or HTTP/HTTPS URL and, when
allowed, opens a fullscreen view with zoom.

`IsUrl` is no longer required. Remote sources are detected automatically when
`Source` starts with `http://` or `https://`. The property is still mapped for
backward compatibility, but new XAML should omit it.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Source` | `string?` | `null` | Local resource name, local file path, file URI, or HTTP/HTTPS URL. |
| `IsUrl` | `bool` | `false` | Obsolete compatibility flag. URL detection is automatic for HTTP/HTTPS. |
| `Placeholder` | `string?` | `null` | Local resource shown during loading or on error. |
| `MaxZoom` | `float` | `5` | Maximum zoom of the fullscreen viewer. Minimum accepted: `1`. |
| `EnableFullscreen` | `bool` | `true` | Opens fullscreen when the image is tapped on the supported platforms. When `false`, the image does not consume tap/ripple interaction. |
| `FullscreenSource` | `string?` | `null` | Higher-quality source for fullscreen. If null, uses `Source`. Ignored where fullscreen is not implemented. |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` or `AspectFit`. |
| `DecodeMaxPx` | `int` | `720` | Maximum decode size used by thumbnail loaders. Minimum accepted: `64`. |
| `IsLoading` | `bool` | `false` | Read-only load state set by the platform handler. Useful for indicators or fade behaviors. |
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
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    DecodeMaxPx="512"
    EnableFullscreen="True"
    MaxZoom="6"
    HeightRequest="220" />
```

### Decode size

`DecodeMaxPx` limits the decoded thumbnail bitmap, not the visual size of the
control. The control still measures and renders at its requested layout size.
Values below `64` are rejected/clamped internally. For virtualized lists, use a
small but realistic value such as `128`, `192`, or `256`; use a higher
`FullscreenSource` when fullscreen should show more detail.

On Android, `DecodeMaxPx` is passed to Glide through
`RequestOptions.Override(width, height)`. On iOS and MacCatalyst, local and
remote images are downsampled through `AppleImageCache`. On Windows, it maps to
`BitmapImage.DecodePixelWidth` and `DecodePixelHeight`.

### Loading state and fade-in

`IsLoading` is read-only and reflects the real platform load cycle. It becomes
`true` before the native request/decode starts and returns to `false` on success,
failure, empty source, cancellation, or handler teardown.

The application can attach a behavior to animate images after loading:

```xml
<gallery:ImageView
    Source="{Binding Produto.path_imagem}"
    Placeholder="sem_imagem"
    DecodeMaxPx="256"
    EnableFullscreen="False">
    <gallery:ImageView.Behaviors>
        <behaviors:FadeInImageBehavior Duration="300" />
    </gallery:ImageView.Behaviors>
</gallery:ImageView>
```

## GalleryView

`GalleryView` displays a list of images in a paged format. It uses the same
`ZoomImageAspect` enum as `ImageView` and can open the gallery in fullscreen with swipe and
zoom on the platforms that support this flow.

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Images` | `IList<string>?` | `null` | List of URLs, local resources, local paths, or file URIs. |
| `IsUrl` | `bool` | `false` | Obsolete compatibility flag. URL detection is automatic for HTTP/HTTPS. |
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
| `ThumbMaxPx` | `int` | `720` | Thumbnail decode limit. Minimum: `64`. |

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
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    ThumbMaxPx="512"
    ShowIndicator="True"
    SelectedIndex="{Binding CurrentPhoto, Mode=TwoWay}"
    SelectionChangedCommand="{Binding PhotoChangedCommand}"
    HeightRequest="240" />
```

## Per-platform behavior

| Platform | `ImageView` | `GalleryView` |
|---|---|---|
| Android | `Android.Widget.ImageView` with Glide, disk/memory cache, bounded decode, and fullscreen via `DialogFragment`/`Matrix`. | `ViewPager2`/`RecyclerView`; native fullscreen with swipe and zoom. |
| iOS/MacCatalyst | `UIImageView`; local/remote decode through `AppleImageCache`; fullscreen with `UIScrollView`. | Paged `UIScrollView` + `UIPageControl`; fullscreen with zoom. |
| Windows | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage` with decode limits; fullscreen is not implemented. | `FlipView` with indicators. |

## Recommendations

- Use `FullscreenSource` when the list shows thumbnails but fullscreen should open a higher-quality image.
- In large lists, prefer URLs already resized on the server and set `ImageView.DecodeMaxPx` to the rendered thumbnail size.
- Lower `GalleryView.ThumbMaxPx` or `ImageView.DecodeMaxPx` when many remote images are alive at the same time.
- Always set `Placeholder` to avoid visual flashes while the image loads.
- Keep `DecodeMaxPx` at or above `64`. Very small debug values are rejected/clamped and are not representative of production behavior.
