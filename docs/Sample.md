# sample

Demo application that consumes the three active projects:

- `GalleryView`
- `PDFViewer`
- `VirtualizedCollectionView`

It also includes a comparison with the standard MAUI `CollectionView` to measure
behavior, scrolling, and incremental loading.

## Component registration

The sample registers all components in `MauiProgram.cs`:

```csharp
builder
    .UseMauiApp<App>()
    .UseAgileGalleryView()
    .UseAgilePdfViewer()
    .UseAgileVirtualizedCollectionView();
```

It also registers the `MaterialDesignIcons` font, used by the sample app's
buttons, and the `IAnchoredMenu` service, used in the custom PDF top menu.

## Pages

| Page | Purpose |
|---|---|
| `MainPage` | Demonstrates `ImageView` and `GalleryView`. |
| `ReaderDemoPage` | Demonstrates `PdfReaderView`, the ready-to-use reader. |
| `VirtualizedListPage` | Demonstrates `VirtualizedCollectionView` with list, grid, search, and metrics. |

## XAML namespaces used

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
```

## Assets

| Asset | Usage |
|---|---|
| `Resources/Images/agile.png` | Shell menu header. |
| `Resources/Images/InteligenciaArtificial.pdf` | PDF bundled for the demos. |
| `Resources/Images/dotnet_bot.png` | Image placeholder. |
| `Resources/Fonts/materialdesignicons-webfont.ttf` | Sample icons. |

PDFs in `Resources/Images` are removed from `MauiImage` and included as
`MauiAsset`, to avoid Resizetizer issues with PDF files.

## Shell menu

`AppShell.xaml` uses a flyout with a header containing `agile.png`. Each menu
item is a `ShellContent`:

- `Gallery View`: images and gallery.
- `PDF Viewer`: ready-to-use UI with `PdfReaderView`.
- `Virtualized Collection`: virtualized list with search, grid, and metrics.

## ReaderDemoPage

Uses `PdfReaderView` with toolbar, search, printing, sharing, vertical/horizontal
toggling, thumbnails, zoom, and page navigation.

## VirtualizedListPage

Uses:

```xml
<virtualized:VirtualizedCollectionView
    ItemHeightRequest="200"
    ItemSizingStrategy="Dynamic"
    RemainingItemsThreshold="8" />
```

It also allows toggling between list/grid and fixed/dynamic height to observe
performance impact.

## Build

```powershell
dotnet build sample/sample.csproj
dotnet build sample/sample.csproj -f net10.0-windows10.0.19041.0
dotnet build sample/sample.csproj -f net10.0-android
```
