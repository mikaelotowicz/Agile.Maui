# sample

Demo application that consumes the active component projects:

- `GalleryView`
- `PDFViewer`
- `VirtualizedCollectionView`
- `ChipGroup`
- `SignaturePad`

It also includes a dedicated benchmark page (`CollectionBenchmarkPage`) that
compares `VirtualizedCollectionView` with the standard MAUI `CollectionView` to
measure behavior, scrolling, and incremental loading.

## Component registration

The sample registers all components in `MauiProgram.cs`:

```csharp
builder
    .UseMauiApp<App>()
    .UseAgileGalleryView()
    .UseAgilePdfViewer()
    .UseAgileVirtualizedCollectionView()
    .UseAgileChipGroup()
    .UseAgileSignaturePad();
```

It also registers the `MaterialDesignIcons` font, used by the sample app's
buttons, and the `IAnchoredMenu` service, used in the custom PDF top menu.

## Pages

| Page | Purpose |
|---|---|
| `MainPage` | Demonstrates `ImageView` and `GalleryView` ("Gallery View" menu entry). |
| `ReaderDemoPage` | Demonstrates `PdfReaderView`, the ready-to-use reader. |
| `VirtualizedListPage` | Demonstrates `VirtualizedCollectionView` with list, grid, search, and metrics. |
| `CollectionBenchmarkPage` | Side-by-side benchmark of `VirtualizedCollectionView` vs. the MAUI `CollectionView`. |
| `ChipGroupPage` | Demonstrates `ChipGroup` in wrap, horizontal scroll, and vertical modes. |
| `SignaturePadPage` | Demonstrates `SignaturePad` with signature capture, undo/redo, metrics, and PNG export preview. |

## XAML namespaces used

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
xmlns:chips="clr-namespace:Agile.Maui;assembly=Agile.Maui.ChipGroup"
xmlns:signature="clr-namespace:Agile.Maui;assembly=Agile.Maui.SignaturePad"
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

`AppShell.xaml` uses a redesigned flyout:

- **Header**: violet gradient card (`#3A1C9E → #512BD4 → #8A6CF2`) with translucent
  decorative circles, `agile.png` in a white rounded tile, a "SAMPLE" badge, and a
  "COMPONENTES" section label.
- **Items**: a custom `Shell.ItemTemplate` renders each entry as a pill with a
  font-glyph icon in a lavender tile (glyphs come from the `Icons` static class,
  read from the item's `FontImageSource`); the `Selected` visual state highlights
  the pill, icon, title, and a side marker.
- **Footer**: divider plus a "Android · iOS · Mac · Windows" caption.

Each menu item is a `ShellContent`:

- `Gallery View`: images and gallery (`MainPage`).
- `PDF Viewer`: ready-to-use UI with `PdfReaderView`.
- `Virtualized Collection`: virtualized list with search, grid, and metrics.
- `Collection Benchmark`: `VirtualizedCollectionView` vs. MAUI `CollectionView`.
- `ChipGroup`: single/multiple chip selection with wrap, horizontal, and vertical layouts.
- `SignaturePad`: freehand signature capture and export preview.

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

## ChipGroupPage

Uses `ChipGroup` with the three `LayoutMode` values:

```xml
<chips:ChipGroup LayoutMode="Wrap" />
<chips:ChipGroup LayoutMode="Horizontal" />
<chips:ChipGroup LayoutMode="Vertical" />
```

`Horizontal` keeps the chips in one row and enables horizontal scrolling, which
is useful for many options on small screens.

## SignaturePadPage

Uses `SignaturePad` with a fixed-height signing area, guide line, prompt,
undo/redo actions, clear action, biometric metrics, and PNG export preview.

```xml
<signature:SignaturePad
    MinStrokeWidth="1.5"
    MaxStrokeWidth="7"
    PromptText="Sign here"
    ShowSignatureLine="True"
    StrokeColor="#111111" />
```

The page calls `GetSignatureData()` for stroke/point/duration metrics and
`GetImageStreamAsync()` to export a cropped transparent PNG.

## Build

The sample targets **.NET 11 preview only** (`net11.0-*`); the libraries keep
their `net10.0` targets for stable consumers.

```powershell
dotnet build sample/sample.csproj
dotnet build sample/sample.csproj -f net11.0-windows10.0.26100.0
dotnet build sample/sample.csproj -f net11.0-android
```
