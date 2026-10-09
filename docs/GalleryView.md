# Agile.Maui.Gallery

Project that delivers two visual controls for images:

- `ImageView`: single image with native loading, bounded decode, load state, load events, and fullscreen zoom on the supported platforms.
- `GalleryView`: paged image gallery with selection, indicators, bounded thumbnail decode, and fullscreen on the supported platforms.

Assembly: `Agile.Maui.Gallery`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgileGalleryView()`

## Requirements

- .NET MAUI / .NET 10.0 with package `Agile.Maui.Gallery` `1.1.0` (depends on `Microsoft.Maui.Controls` `10.0.90` or later).
- .NET MAUI / .NET 11.0 preview with package `Agile.Maui.Gallery` `1.1.0-preview.1`.
- Android 7.0 (API 24)+, iOS 15.0+, macOS Catalyst 15.0+, or Windows 10.0.17763.0+.

## Installation

```powershell
dotnet add package Agile.Maui.Gallery --version 1.1.0
```

For .NET 11 preview projects:

```powershell
dotnet add package Agile.Maui.Gallery --version 1.1.0-preview.1
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
remote images are downsampled through `AppleImageCache` to the view's actual
bounds times the screen scale; `DecodeMaxPx` is the fallback used before the
view has been laid out. The Apple decode cache is capped at 96 MB. On Windows,
it maps to `BitmapImage.DecodePixelWidth` only, so non-square images keep their
aspect ratio.

The fullscreen viewers (Android, iOS, MacCatalyst) decode at the largest screen
dimension times `min(MaxZoom, 2)`, clamped between 720 and 4096 px. On iOS and
MacCatalyst, remote fullscreen images are decoded off the main thread.

### Fullscreen zoom

Pinch zooms between the fitted scale and `MaxZoom`; double-tap toggles between
the fitted scale and an intermediate zoom relative to it. When an image is small
enough that fitting it to the screen already needs a scale above `MaxZoom`, the
effective maximum is raised to the fit scale, so pinch and double-tap keep
working on small images. In the single-image viewer, a tap while not zoomed in
(or Back on Android) closes the viewer; the fullscreen gallery closes through
its close button. The same zoom rules apply to `GalleryView` fullscreen pages.

### Loading state and fade-in

`IsLoading` is read-only and reflects the real platform load cycle. It becomes
`true` before the native request/decode starts and returns to `false` on success,
failure, empty source, cancellation, or handler teardown.

Changing `Source` (or clearing it) replaces the previous image immediately:
with a `Placeholder` set, the placeholder shows while the new image loads;
without one, the control clears to empty instead of keeping the previous photo —
relevant for recycled cells.

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
| `VerticalImageAlignment` | `ImageAlignment` | `Center` | Since `1.1.0`. With `AspectFit`, where each image sits when the page has leftover height: `Start` (top, leftover below), `Center` (split above and below) or `End` (bottom, leftover above). Android, iOS and MacCatalyst; always centered on Windows. Tapping the leftover area also opens fullscreen. |
| `MaxZoom` | `float` | `5` | Maximum zoom in fullscreen. |
| `ShowIndicator` | `bool` | `false` | Shows page indicators. |
| `IndicatorColor` | `Color` | `White` | Color of the active indicator. |
| `IndicatorInactiveColor` | `Color` | white 50% | Color of the inactive indicators. |
| `SelectionChangedCommand` | `ICommand?` | `null` | Receives the selected index. |
| `ImageLoadedCommand` | `ICommand?` | `null` | Command when an image loads. |
| `ImageFailedCommand` | `ICommand?` | `null` | Command when an image fails. |
| `ThumbMaxPx` | `int` | `720` | Thumbnail decode limit. On Android, iOS, and MacCatalyst pages decode at their measured size once laid out (following rotation/resize) and `ThumbMaxPx` is the fallback before that; on Windows it is the decode width. Minimum: `64`. |

Without `HeightRequest`, the gallery takes all the height offered by its parent,
capped by `MaximumHeightRequest`, on Android, iOS and macOS Catalyst. Before `1.1.0`, iOS and
macOS Catalyst stayed at `MinimumHeightRequest` while Android took the maximum.

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

Product detail with the photo pinned to the top of the page and the leftover
space below it, next to the indicators:

```xml
<gallery:GalleryView
    Images="{Binding Photos}"
    AspectMode="AspectFit"
    VerticalImageAlignment="Start"
    ShowIndicator="True"
    HeightRequest="360" />
```

```csharp
public enum ImageAlignment
{
    Center,
    Start,
    End
}
```

## Per-platform behavior

| Platform | `ImageView` | `GalleryView` |
|---|---|---|
| Android | `Android.Widget.ImageView` with Glide, disk/memory cache, bounded decode, and fullscreen via `DialogFragment`/`Matrix`. | `ViewPager2`/`RecyclerView`; each page fills the whole gallery; native fullscreen with swipe and zoom. |
| iOS/MacCatalyst | `UIImageView`; local/remote decode through `AppleImageCache`; fullscreen with `UIScrollView`. | Paged `UIScrollView` + `UIPageControl`; fullscreen with zoom. |
| Windows | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage` with decode limits; fullscreen is not implemented. | `FlipView` with indicators. |

## Recommendations

- Use `FullscreenSource` when the list shows thumbnails but fullscreen should open a higher-quality image.
- In large lists, prefer URLs already resized on the server and set `ImageView.DecodeMaxPx` to the rendered thumbnail size.
- Lower `GalleryView.ThumbMaxPx` or `ImageView.DecodeMaxPx` when many remote images are alive at the same time.
- Always set `Placeholder` to avoid visual flashes while the image loads.
- Keep `DecodeMaxPx` at or above `64`. Very small debug values are rejected/clamped and are not representative of production behavior.
