<div align="center">

# Agile.Maui

<img src="agile.png" alt="Agile.Maui" width="160" />

**Native, modular components for .NET MAUI**

[![Gallery NuGet](https://img.shields.io/nuget/v/Agile.Maui.Gallery?label=Agile.Maui.Gallery)](https://www.nuget.org/packages/Agile.Maui.Gallery)
[![PDF NuGet](https://img.shields.io/nuget/v/Agile.Maui.Pdf?label=Agile.Maui.Pdf)](https://www.nuget.org/packages/Agile.Maui.Pdf)
[![PdfGen NuGet](https://img.shields.io/nuget/v/Agile.Maui.PdfGen?label=Agile.Maui.PdfGen)](https://www.nuget.org/packages/Agile.Maui.PdfGen)
[![Virtualized NuGet](https://img.shields.io/nuget/v/Agile.Maui.VirtualizedCollection?label=Agile.Maui.VirtualizedCollection)](https://www.nuget.org/packages/Agile.Maui.VirtualizedCollection)
[![ChipGroup NuGet](https://img.shields.io/nuget/v/Agile.Maui.ChipGroup?label=Agile.Maui.ChipGroup)](https://www.nuget.org/packages/Agile.Maui.ChipGroup)
[![SignaturePad NuGet](https://img.shields.io/nuget/v/Agile.Maui.SignaturePad?label=Agile.Maui.SignaturePad)](https://www.nuget.org/packages/Agile.Maui.SignaturePad)

![.NET](https://img.shields.io/badge/.NET-10.0%20%7C%2011.0--preview-512BD4)
![MAUI](https://img.shields.io/badge/MAUI-supported-512BD4)
![Platforms](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20macOS%20Catalyst%20%7C%20Windows-blue)
![License](https://img.shields.io/badge/license-MIT-green)

`ImageView` / `GalleryView` | `PdfViewer` / `PdfReaderView` | `PdfGen` | `VirtualizedCollectionView` | `ChipGroup` | `SignaturePad`

[Overview](#overview) | [Features](#features) | [What's new](#whats-new) | [Projects](#projects) | [Platforms](#platforms) | [Documentation](#additional-documentation)

</div>

A .NET MAUI component library with native implementations for Android, iOS,
macOS Catalyst, and Windows. The current solution is modular: each component lives in its
own project/package, but they all share the C# namespace `Agile.Maui`.

## Overview

Agile.Maui is a collection of native MAUI controls and companion document tooling for common
mobile and desktop app scenarios: zoomable images, galleries, PDF reading, PDF generation,
virtualized lists, chip-based selections, and signature capture.
Each module can be installed separately, so the app consumes only what it uses.

### Why use it

- Native controls per platform; the PDF viewer does not rely on `WebView` and images use native views.
- Independent packages sharing the same C# namespace.
- Bindable APIs for XAML and MVVM.
- Platform-specific handlers for Android, iOS, macOS Catalyst, and Windows.
- Sample app and per-component documentation.

## Features

| Module | Key features |
|---|---|
| `Agile.Maui.Gallery` | `ImageView` with native loading, bounded decode, load state, zoom/fullscreen, and `GalleryView` with image navigation and vertical image alignment. |
| `Agile.Maui.Pdf` | Base `PdfViewer` and a ready-to-use `PdfReaderView` with search, print/share, zoom, thumbnails, and navigation. |
| `Agile.Maui.PdfGen` | Fluent PDF generation with a managed backend for MAUI, WinForms, Blazor, services, and native MAUI renderers where useful. |
| `Agile.Maui.VirtualizedCollection` | High-performance virtualized list for large volumes of items, with header/footer and `DataTemplateSelector` support. |
| `Agile.Maui.ChipGroup` | Chip selection control with single/multiple two-way selection and wrap, horizontal, or vertical layout modes. |
| `Agile.Maui.SignaturePad` | Freehand signature capture with vector strokes, pressure metadata, undo/redo, and PNG/JPEG export. |

## What's new

Release of 2026-10-09. Each package keeps its own version; behavior changes are
marked as such.

| Package | Version | Highlights |
|---|---|---|
| `Agile.Maui.Gallery` | `1.1.0` | New `GalleryView.VerticalImageAlignment` (`Start`/`Center`/`End`) for `AspectFit` on Android, iOS and Mac Catalyst. Fixes: Android gallery pages fill the gallery, `GalleryView` on iOS/Mac Catalyst takes the offered height like Android (it stayed at `MinimumHeightRequest`), pinch on images smaller than the screen no longer crashes, Windows decode keeps the aspect ratio, fullscreen decode sized by the screen, iOS/Mac Catalyst fullscreen shows the photo instead of the placeholder. |
| `Agile.Maui.Pdf` | `1.1.0` | Behavior change: switching orientation (all platforms) and double-tap zoom-out (Android) return to 100% instead of `MinZoom`. iOS minimum raised to 15.0. The background behind the pages is now light gray `#EDEDEF` on Android, iOS and Mac Catalyst (was dark `#525659` on iOS/Mac Catalyst). Fixes: two-axis pan when zoomed on Android, reliable `PdfStream` reload with `Password`, cancellable search, shared `HttpClient` with timeout, thumbnails button on Mac Catalyst, `PdfReaderView` icon font now ships in the NuGet package (icons were blank when installed from NuGet). |
| `Agile.Maui.PdfGen` | `1.2.0` | First version published on nuget.org. Embedded fonts in `GeneratePdfNative()`, per-page rounded decorations over paginated content, page numbers that follow container alignment, grayscale/CMYK JPEG. See the package `CHANGELOG.md`. |
| `Agile.Maui.VirtualizedCollection` | `1.1.0` | `ItemTemplate` accepts a `DataTemplateSelector`. Behavior change: the list scrolls back to the first item when it goes from empty to non-empty. Fixes: collection changes from background threads, iOS cell leaks/crashes, header/footer kept across recycling, no UI freeze on Windows with MAUI 11 when a hidden list is filled with a large batch. |
| `Agile.Maui.ChipGroup` | `1.1.0` | Two-way selection with the view model and initial state from `ChipItem.IsSelected`. Behavior changes: a bound `SelectedItems` list is updated in place, and chips are rebuilt asynchronously on the UI dispatcher. |
| `Agile.Maui.SignaturePad` | `1.1.0` | Behavior change: white "paper" `BackgroundColor` by default (override it, including with `Transparent`). Fixes: export scale on Android/iOS/Mac, trimming-safe JSON, palm/second touch ignored, Windows pen eraser and right button no longer draw. |

The five control packages now depend on `Microsoft.Maui.Controls` `10.0.90`
(stable) or `11.0.0-preview.7.26406.9` (preview); `Agile.Maui.PdfGen` does not
depend on MAUI. All six packages include XML documentation for IntelliSense.
Release notes per version are shown on each package page.

## Requirements

- .NET MAUI / .NET 10.0 for the stable packages, with `Microsoft.Maui.Controls` `10.0.90` or later.
- .NET MAUI / .NET 11.0 preview for the `-preview.1` packages.
- Android 7.0 (API 24)+, iOS 15.0+, macOS Catalyst 15.0+, or Windows 10.0.17763.0+.
- Registration of the visual MAUI control packages used in `MauiProgram.cs`. `Agile.Maui.PdfGen` is a generator library and does not require handler registration.

## Projects

| Project | Package / Assembly | Components | Documentation |
|---|---|---|---|
| `GalleryView` | `Agile.Maui.Gallery` | `ImageView`, `GalleryView` | [docs/GalleryView.md](docs/GalleryView.md) |
| `PDFViewer` | `Agile.Maui.Pdf` | `PdfViewer`, `PdfReaderView` | [docs/PDFViewer.md](docs/PDFViewer.md) |
| `PdfGen` | `Agile.Maui.PdfGen` | `PdfDocument` fluent PDF generator | [docs/PdfGen.md](docs/PdfGen.md) |
| `VirtualizedCollectionView` | `Agile.Maui.VirtualizedCollection` | `VirtualizedCollectionView` | [docs/VirtualizedCollectionView.md](docs/VirtualizedCollectionView.md) |
| `ChipGroup` | `Agile.Maui.ChipGroup` | `ChipGroup` | [docs/ChipGroup.md](docs/ChipGroup.md) |
| `SignaturePad` | `Agile.Maui.SignaturePad` | `SignaturePad` | [docs/SignaturePad.md](docs/SignaturePad.md) |
| `sample` | sample application | demos of all components | [docs/Sample.md](docs/Sample.md) |

## Installation

Install only the packages your application actually uses:

```powershell
dotnet add package Agile.Maui.Gallery --version 1.1.0
dotnet add package Agile.Maui.Pdf --version 1.1.0
dotnet add package Agile.Maui.PdfGen --version 1.2.0
dotnet add package Agile.Maui.VirtualizedCollection --version 1.1.0
dotnet add package Agile.Maui.ChipGroup --version 1.1.0
dotnet add package Agile.Maui.SignaturePad --version 1.1.0
```

For .NET 11 preview projects, use the preview package channel:

```powershell
dotnet add package Agile.Maui.Gallery --version 1.1.0-preview.1
dotnet add package Agile.Maui.Pdf --version 1.1.0-preview.1
dotnet add package Agile.Maui.PdfGen --version 1.2.0-preview.1
dotnet add package Agile.Maui.VirtualizedCollection --version 1.1.0-preview.1
dotnet add package Agile.Maui.ChipGroup --version 1.1.0-preview.1
dotnet add package Agile.Maui.SignaturePad --version 1.1.0-preview.1
```

Then register the handlers in `MauiProgram.cs`:

```csharp
using Agile.Maui;

builder
    .UseMauiApp<App>()
    .UseAgileGalleryView()
    .UseAgilePdfViewer()
    .UseAgileVirtualizedCollectionView()
    .UseAgileChipGroup()
    .UseAgileSignaturePad();
```

Each method is independent. If the application uses only PDF, for example, call
only `UseAgilePdfViewer()`.

## XAML

The C# namespace is the same, but the XAML assembly changes per package:

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
xmlns:chips="clr-namespace:Agile.Maui;assembly=Agile.Maui.ChipGroup"
xmlns:signature="clr-namespace:Agile.Maui;assembly=Agile.Maui.SignaturePad"
```

Example:

```xml
<gallery:ImageView Source="photo" DecodeMaxPx="256" />
<gallery:GalleryView Images="{Binding Photos}" ThumbMaxPx="512" />
<pdf:PdfViewer Source="{Binding PdfPath}" />
<pdf:PdfReaderView Source="manual.pdf" />
<virtualized:VirtualizedCollectionView ItemsSource="{Binding Items}" />
<chips:ChipGroup ItemsSource="{Binding Categories}" LayoutMode="Horizontal" />
<signature:SignaturePad StrokeColor="#111111" />
```

## Platforms

| Component | Android | iOS / MacCatalyst | Windows |
|---|---|---|---|
| `ImageView` | `Android.Widget.ImageView` + Glide + bounded decode + native fullscreen zoom | `UIImageView` + bounded decode + `UIScrollView` fullscreen | `Microsoft.UI.Xaml.Controls.Image` + bounded decode |
| `GalleryView` | `ViewPager2` + `RecyclerView` | paginated `UIScrollView` + `UIPageControl` | `FlipView` |
| `PdfViewer` | `PdfRenderer` for rendering + PDFium for text/search | `PdfKit.PdfView` | PDFium |
| `VirtualizedCollectionView` | `RecyclerView` | `UICollectionViewCompositionalLayout` | MAUI `CollectionView` |
| `ChipGroup` | MAUI `FlexLayout` / horizontal `ScrollView` | MAUI `FlexLayout` / horizontal `ScrollView` | MAUI `FlexLayout` / horizontal `ScrollView` |
| `SignaturePad` | MAUI `GraphicsView` + native `MotionEvent` pressure input | MAUI `GraphicsView` + native `UITouch` pressure input | MAUI `GraphicsView` + native pointer pressure input |

## Performance

- `VirtualizedCollectionView` uses native handlers and virtualization to reduce cost on large lists.
- `PdfViewer` renders pages on demand and provides a configurable cache/prefetch.
- `GalleryView` and `ImageView` use native per-platform loading and cache where applicable.
- `ImageView.DecodeMaxPx` and `GalleryView.ThumbMaxPx` limit thumbnail decode size; use `FullscreenSource` for high-detail fullscreen images.
- `ImageView.IsLoading` is a read-only state from the platform handler and can drive indicators or fade-in behaviors.
- `SignaturePad` stores strokes as vector data and exports images on demand.

## Troubleshooting

- If a control does not render, confirm that the corresponding `UseAgile...()` method was called.
- If XAML cannot find the control, check the assembly in the namespace, such as `Agile.Maui.Pdf`, `Agile.Maui.Gallery`, `Agile.Maui.VirtualizedCollection`, `Agile.Maui.ChipGroup`, or `Agile.Maui.SignaturePad`.
- For bundled PDFs, use `MauiAsset` and refer to [docs/PDFViewer.md](docs/PDFViewer.md).
- If `Agile.Maui.PdfGen` code fails with CS0104 (ambiguous reference) in a MAUI app, some PdfGen type names (`Colors`, `IContainer`, `EmbeddedFont`, `GradientBrush`, `HorizontalAlignment` and others) clash with the MAUI global usings; see the aliases in [docs/PdfGen.md](docs/PdfGen.md#use-in-maui-apps).

## Structure

```text
Agile.Maui.slnx
GalleryView/
PDFViewer/
PdfGen/
VirtualizedCollectionView/
ChipGroup/
SignaturePad/
sample/
*.Tests/            (host test projects, one per component)
.github/workflows/  (CI tests and NuGet publish)
docs/
TUNING.md
PROFILING.md
```

## Build

```powershell
dotnet build
dotnet build -f net10.0-android
dotnet build -f net10.0-ios
dotnet build -f net10.0-maccatalyst
dotnet build -f net11.0-maccatalyst
dotnet build -f net10.0-windows10.0.19041.0
dotnet build -f net11.0-windows10.0.26100.0
```

## Tests

Each component has a host test project (xunit, platform-neutral `net10.0` target,
no device or emulator required): `ChipGroup.Tests`, `GalleryView.Tests`,
`PDFViewer.Tests`, `PdfGen.Tests`, `SignaturePad.Tests`, and
`VirtualizedCollectionView.Tests`.

```powershell
dotnet test ChipGroup.Tests    # same for the other <Project>.Tests
```

The GitHub Actions workflow in `.github/workflows/ci.yml` runs all six suites on
every push and pull request, on a Windows runner.

## Package generation

Generate the stable .NET 10 packages and the .NET 11 preview packages with one command:

```powershell
dotnet pack Agile.Maui.PackAll.proj -c Release
```

Packages are written to `nupkgs/` at the repository root. Each project defines its own
version in its `.csproj` (currently `1.1.0` for the five control packages and `1.2.0`
for `Agile.Maui.PdfGen`). Packing must run on Windows, the only OS that builds every
target framework. The command produces:

- stable packages for .NET 10 projects.
- `-preview.1` packages for .NET 10 and .NET 11 preview projects.

Besides the platform assemblies, each package contains a platform-neutral `net10.0`
assembly (plus `net11.0` in the preview channel) with only the cross-platform code,
without handlers; it is what the host test projects run against.

The publish workflow pushes with `--skip-duplicate`, so a package only reaches
nuget.org when its version in the `.csproj` changes.

## Additional documentation

- [GalleryView and ImageView](docs/GalleryView.md)
- [PDFViewer and PdfReaderView](docs/PDFViewer.md)
- [PdfGen PDF generation](docs/PdfGen.md)
- [VirtualizedCollectionView](docs/VirtualizedCollectionView.md)
- [ChipGroup](docs/ChipGroup.md)
- [SignaturePad](docs/SignaturePad.md)
- [Sample application](docs/Sample.md)
- [Performance tuning](TUNING.md)
- [Android profiling of VirtualizedCollectionView](PROFILING.md)

## License

Distributed under the MIT license.

## Support

Use the repository issue tracker to report bugs, ask questions, or suggest
new features.
