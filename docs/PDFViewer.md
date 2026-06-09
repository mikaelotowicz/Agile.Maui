# Agile.Maui.Pdf

PDF viewing project with two levels of use:

- `PdfViewer`: base control for building a custom interface.
- `PdfReaderView`: ready-to-use reader, with toolbar, search, print/share, orientation, thumbnails, zoom and navigation.

Supported platforms: Android, iOS, macOS Catalyst and Windows.

Assembly: `Agile.Maui.Pdf`  
C# namespace: `Agile.Maui`  
Registration: `builder.UseAgilePdfViewer()`

## Overview

`Agile.Maui.Pdf` brings native PDF viewing to .NET MAUI apps without relying on
`WebView`. The package exposes a base control (`PdfViewer`) for custom UIs and
a ready-to-use reader (`PdfReaderView`) with toolbar, search, printing, sharing,
thumbnails and navigation.

### Why use it

- Native rendering per platform, without HTML/JavaScript.
- Consistent API for Android, iOS, macOS Catalyst and Windows.
- Two layers of use: base control or full reader.
- Search, text selection/copy, zoom and page navigation.
- Configuration via `BindableProperty`, suitable for XAML and MVVM.

## Features

### Core functionality

- Sources by path/URL (`Source`), stream (`PdfStream`) and bundled asset in the `PdfReaderView`.
- Password for protected PDFs (`Password`).
- Programmatic zoom, pinch when supported and `MinZoom`/`MaxZoom` limits.
- Continuous vertical scrolling or paged horizontal scrolling.
- Text search with current/total result.
- Links with `LinkTapped` event where supported by the platform handler.
- Native printing per platform.
- Sharing in the `PdfReaderView`.

### Advanced features

- LRU cache and configurable page prefetch.
- Thumbnails as drawer/overlay on mobile and fixed sidebar on Windows.
- Text selection and copy where supported by the handler.
- Localization of the ready-to-use reader's text.
- Render control via `RenderScale`, `MaxCacheMB`, `PrefetchAbove` and `PrefetchBelow`.

## Requirements

- .NET MAUI / .NET 10.0.
- Android API 21+.
- iOS 15.0+.
- macOS Catalyst 15.0+.
- Windows 10.0.17763.0+; recommended Windows target: `net10.0-windows10.0.19041.0`.

## Quick start

### 1. Install the package

```powershell
dotnet add package Agile.Maui.Pdf
```

### 2. Register the handler

```csharp
using Agile.Maui;

builder
    .UseMauiApp<App>()
    .UseAgilePdfViewer();
```

`UseAgilePdfViewer()` also registers the internal `AgilePdfIcons` font, used by the
`PdfReaderView`.

### 3. Add the XAML namespace

```xml
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
```

### 4. Use the ready-to-use reader

```xml
<pdf:PdfReaderView Source="manual.pdf" />
```

### 5. Or use the base control

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    DocumentLoaded="OnDocumentLoaded"
    PageChanged="OnPageChanged" />
```

## PdfViewer

`PdfViewer` is the base control. It renders the PDF and exposes properties,
events and commands for navigation, zoom, search, print and thumbnails.

### Document sources

| Property | Type | Description |
|---|---|---|
| `Source` | `string?` | Local path or URL. |
| `PdfStream` | `Stream?` | PDF stream provided by the application. |
| `Password` | `string?` | Password for protected PDFs. |

### Source rules

| Input | Behavior |
|---|---|
| `https://...` or `http://...` | Downloads and opens as a URL. |
| Existing file path | Opens the local file. |
| `PdfStream` | Opens the bytes provided by the application. |
| `PdfReaderView Source="file.pdf"` | If it is not a URL/local file, tries to open it as a MauiAsset and copies it to the cache. |

### State and navigation

| Property | Type | Default | Description |
|---|---|---|---|
| `CurrentPage` | `int` | `0` | Current page, 0-based, `TwoWay`. |
| `PageCount` | `int` | `0` | Total number of pages, set by the control. |
| `ScrollOrientation` | `PdfScrollOrientation` | `Vertical` | Continuous vertical scroll or paged horizontal scroll. |

### Zoom and rendering

| Property | Type | Default | Description |
|---|---|---|---|
| `ZoomFactor` | `double` | `1.0` | Zoom relative to the base fit. |
| `MinZoom` | `double` | `0.5` | Minimum zoom. |
| `MaxZoom` | `double` | `8.0` | Maximum zoom. |
| `IsPinchZoomEnabled` | `bool` | `true` | Enables the pinch gesture when supported. |
| `RenderScale` | `double` | `1.5` | Render scale/DPI. |
| `MaxCacheMB` | `int` | `200` | Limit for the rendered page cache. |
| `EnablePageCaching` | `bool` | `true` | Enables cache/prefetch. |
| `PrefetchAbove` | `int` | `2` | Pages above to prefetch. |
| `PrefetchBelow` | `int` | `3` | Pages below to prefetch. |

### Appearance and text

| Property | Type | Default |
|---|---|---|
| `PageBackgroundColor` | `Color` | `White` |
| `PageSpacing` | `double` | `8` |
| `CopyButtonText` | `string` | `Copy` |
| `CopiedMessageText` | `string` | `Copied` |
| `ThumbnailBarTitleText` | `string` | `Pages` |
| `PrintJobName` | `string` | `Document` |

### Thumbnails

| Property | Type | Default | Description |
|---|---|---|---|
| `EnableThumbnailBar` | `bool` | `false` | Fixed sidebar on Windows. |
| `IsThumbnailBarOpen` | `bool` | `false` | Mobile drawer, `TwoWay`. |
| `ThumbnailBarPlacement` | `PdfThumbnailPlacement` | `None` | `None`, `Left` or `Right`. |

### Events

| Event | Args |
|---|---|
| `DocumentLoaded` | `PdfDocumentLoadedEventArgs` |
| `DocumentLoadFailed` | `PdfDocumentLoadFailedEventArgs` |
| `PageChanged` | `PdfPageChangedEventArgs` |
| `SearchResultChanged` | `PdfSearchResultEventArgs` |
| `LinkTapped` | `PdfLinkTappedEventArgs`; Android intercepts internal/external links, iOS/MacCatalyst intercepts external URLs and Windows does not yet expose link interception. Set `Handled = true` to prevent the default action when supported. |

### Commands

| Command | Args |
|---|---|
| `DocumentLoadedCommand` | `PdfDocumentLoadedEventArgs` |
| `DocumentLoadFailedCommand` | `PdfDocumentLoadFailedEventArgs` |
| `PageChangedCommand` | `PdfPageChangedEventArgs` |

### Methods

```csharp
await Viewer.GoToPageAsync(10);
await Viewer.ZoomInAsync();
await Viewer.ZoomOutAsync();
await Viewer.ResetZoomAsync();
await Viewer.PrintAsync();

Viewer.Search("termo");
Viewer.FindNext();
Viewer.FindPrevious();
Viewer.ClearSearch();
```

### Enums

```csharp
public enum PdfScrollOrientation
{
    Vertical,
    Horizontal
}

public enum PdfThumbnailPlacement
{
    None,
    Left,
    Right
}
```

### Base example

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    MinZoom="1"
    MaxZoom="4"
    RenderScale="2"
    MaxCacheMB="350"
    ScrollOrientation="Vertical"
    ThumbnailBarPlacement="Right"
    CopyButtonText="Copiar"
    CopiedMessageText="Copiado" />
```

## PdfReaderView

`PdfReaderView` composes a complete UI on top of `PdfViewer`. It is suitable when
you want a ready-to-use, customizable reader without recreating the toolbar, search and bottom
bar in the app.

### Main properties

`PdfReaderView` forwards most of the `PdfViewer` properties: `Source`,
`PdfStream`, `Password`, `ZoomFactor`, `MinZoom`, `MaxZoom`,
`ScrollOrientation`, `ThumbnailBarPlacement`, `IsThumbnailBarOpen`,
`EnableThumbnailBar`, `RenderScale`, `MaxCacheMB`, `PrefetchAbove`,
`PrefetchBelow`, text and colors.

It also exposes:

| Property | Type | Description |
|---|---|---|
| `ViewerControl` | `PdfViewer` | Access to the base control for advanced scenarios. |
| `ToolbarColor` | `Color` | Top bar color. |
| `BottomBarColor` | `Color` | Bottom bar color. |
| `IconColor` | `Color` | Icon color. |
| `CaptionColor` | `Color` | Color of titles and counters. |
| `LoadingText` | `string` | Loading text. |
| `SearchPlaceholder` | `string` | Search placeholder. |
| `PageCountFormat` | `string` | Format for the total page count. |
| `LoadFailedText` | `string` | Text on load failure. |
| `SearchBarMaxWidth` | `double` | Maximum search bar width. Mobile fills the toolbar by default; Windows uses a compact width. |
| `NavigationButtonMode` | `PdfReaderNavigationButtonMode` | Shows a navigation button at the start of the toolbar. `None`, `Auto`, `Menu`, or `Back`. |
| `NavigationButtonCommand` | `ICommand?` | Optional command that replaces the default navigation action. |
| `NavigationButtonCommandParameter` | `object?` | Optional parameter for `NavigationButtonCommand`. |
| `ShowToolbar` | `bool` | Shows the top bar. |
| `ShowSearch` | `bool` | Shows the search button. |
| `ShowPrint` | `bool` | Shows the print button. |
| `ShowShare` | `bool` | Shows the share button. |
| `ShowOrientationToggle` | `bool` | Shows the vertical/horizontal toggle. |
| `ShowBottomBar` | `bool` | Shows the bottom bar. |

### Ready-to-use example

```xml
<pdf:PdfReaderView
    Source="InteligenciaArtificial.pdf"
    LoadingText="Loading..."
    SearchPlaceholder="Search..."
    PageCountFormat="{}{0} pages"
    CopyButtonText="Copy"
    CopiedMessageText="Copied"
    PrintJobName="Document"
    ThumbnailBarPlacement="Right"
    ShowToolbar="True"
    ShowBottomBar="True" />
```

### Navigation button

`PdfReaderView` can show a navigation button in its custom toolbar. This is useful
when `Shell.NavBarIsVisible="False"` or when the app uses `FlyoutPage` and the
reader toolbar occupies the navigation bar area.

```xml
<pdf:PdfReaderView
    Source="manual.pdf"
    NavigationButtonMode="Auto" />
```

Modes:

| Mode | Behavior |
|---|---|
| `None` | Default. Does not show a navigation button. |
| `Auto` | Shows back when the page can navigate back; otherwise shows menu when a Shell flyout or `FlyoutPage` is available. |
| `Menu` | Shows a menu button and opens `Shell.Current.FlyoutIsPresented` or `FlyoutPage.IsPresented`. |
| `Back` | Shows a back button and tries modal/pop navigation, then Shell relative navigation. |

For custom navigation, bind a command:

```xml
<pdf:PdfReaderView
    NavigationButtonMode="Menu"
    NavigationButtonCommand="{Binding OpenMenuCommand}" />
```

## Examples

### Full reader on a page

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf">

    <pdf:PdfReaderView
        Source="manual.pdf"
        MinZoom="1"
        MaxZoom="4"
        SearchPlaceholder="Search..."
        LoadingText="Loading..."
        ThumbnailBarPlacement="Right" />
</ContentPage>
```

### Base control with events

```xml
<pdf:PdfViewer
    x:Name="Viewer"
    Source="{Binding PdfPath}"
    DocumentLoaded="OnDocumentLoaded"
    DocumentLoadFailed="OnDocumentLoadFailed"
    PageChanged="OnPageChanged"
    LinkTapped="OnLinkTapped" />
```

```csharp
private void OnDocumentLoaded(object sender, PdfDocumentLoadedEventArgs e)
{
    StatusLabel.Text = $"{e.PageCount} pages";
}

private void OnPageChanged(object sender, PdfPageChangedEventArgs e)
{
    PageLabel.Text = $"{e.Page + 1}/{Viewer.PageCount}";
}

private void OnLinkTapped(object sender, PdfLinkTappedEventArgs e)
{
    if (e.Uri is not null && !e.Uri.StartsWith("https://mycompany.com"))
        e.Handled = true;
}
```

### MVVM

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    CurrentPage="{Binding CurrentPage}"
    DocumentLoadedCommand="{Binding DocumentLoadedCommand}"
    PageChangedCommand="{Binding PageChangedCommand}" />
```

### Password-protected PDF

```xml
<pdf:PdfReaderView
    Source="{Binding SecurePdfPath}"
    Password="{Binding PdfPassword}" />
```

## Common scenarios

### Block external navigation

Available on the handlers that expose `LinkTapped` for the type of link tapped
(Android for internal/external links; iOS/MacCatalyst for external URLs).

```csharp
Viewer.LinkTapped += (sender, e) =>
{
    if (e.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
        e.Handled = true;
};
```

### Open external links with confirmation

Available on Android, iOS and MacCatalyst for external URLs.

```csharp
Viewer.LinkTapped += async (sender, e) =>
{
    if (e.Uri is null) return;

    e.Handled = true;
    bool open = await DisplayAlert("Abrir link?", e.Uri, "Abrir", "Cancelar");
    if (open)
        await Launcher.OpenAsync(e.Uri);
};
```

### Tune memory for large PDFs

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    RenderScale="1.5"
    MaxCacheMB="150"
    PrefetchAbove="1"
    PrefetchBelow="2" />
```

## Platform behavior

| Platform | Engine | Notes |
|---|---|---|
| Android | `PdfRenderer` + PDFium | Native rendering; PDFium for text, search and links. |
| iOS/MacCatalyst | `PdfKit.PdfView` | Native zoom, selection, links; `LinkTapped` covers external URLs. |
| Windows | PDFium | Rendering, text and search via PDFium; printing and sharing use WinUI/Windows APIs. |

## Architecture

```text
.NET MAUI app
  |
  +-- PdfReaderView (ready-to-use UI: toolbar, search, share, print)
  |     |
  |     +-- PdfViewer
  |
  +-- PdfViewer (cross-platform View)
        |
        +-- Android handler      -> PdfRenderer + PDFium
        +-- iOS/Mac handler      -> PdfKit.PdfView
        +-- Windows handler      -> PDFium + WinUI
```

## Performance

- The control renders pages on demand and keeps a configurable cache.
- `MaxCacheMB` limits the memory used by rendered pages.
- `PrefetchAbove` and `PrefetchBelow` improve scroll smoothness at the cost of memory/CPU.
- `RenderScale` controls sharpness: higher values improve quality but increase cost.
- For very large documents, reduce prefetch and cache before increasing `RenderScale`.

## Troubleshooting

### The PDF does not load

- Check that `Source` is an accessible URL or an existing file path.
- For assets, mark the PDF as `MauiAsset` in the app project and use the correct logical name.
- If the PDF is protected, set `Password`.
- Subscribe to `DocumentLoadFailed` to capture the error message.

### Search does not find text

- Some PDFs are scanned images and have no searchable text layer.
- Confirm that the term is not empty and that the document has finished loading.

### Links do not open or need to be blocked

- Subscribe to `LinkTapped`.
- Set `e.Handled = true` when you want to prevent the default action.
- Android intercepts internal and external links; iOS/MacCatalyst intercepts external URLs; Windows does not yet expose link interception.

### Sharing on Windows does not open

- The `PdfReaderView` uses the WinUI sharing API bound to the current window's HWND.
- Confirm that there is an active MAUI window and that the document came from a valid file, URL or `PdfStream`.

## Local build

```powershell
dotnet build PDFViewer\PDFViewer.csproj
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-android
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-ios
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-maccatalyst
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-windows10.0.19041.0
```

## When to use each control

Use `PdfViewer` when the application needs its own toolbar, commands in a different
layout, custom overlays or deep integration with the app flow.

Use `PdfReaderView` when you want a complete reader with property-based
configuration and text localization.

## License

Package distributed under the MIT license.

## Support

Use the repository's issue tracker for bugs, questions and feature requests.
