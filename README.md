# Agile.Maui

Biblioteca de componentes .NET MAUI com implementacoes nativas para Android, iOS,
macOS Catalyst e Windows. A solucao atual e modular: cada componente vive em seu
proprio projeto/pacote, mas todos compartilham o namespace C# `Agile.Maui`.

## Projetos

| Projeto | Pacote / Assembly | Componentes | Documentacao |
|---|---|---|---|
| `GalleryView` | `Agile.Maui.Gallery` | `ImageView`, `GalleryView` | [docs/GalleryView.md](docs/GalleryView.md) |
| `PDFViewer` | `Agile.Maui.Pdf` | `PdfViewer`, `PdfReaderView` | [docs/PDFViewer.md](docs/PDFViewer.md) |
| `VirtualizedCollectionView` | `Agile.Maui.VirtualizedCollection` | `VirtualizedCollectionView` | [docs/VirtualizedCollectionView.md](docs/VirtualizedCollectionView.md) |
| `sample` | aplicativo de exemplo | demos de todos os componentes | [docs/Sample.md](docs/Sample.md) |

`Controls_old` e uma copia legada do antigo projeto monolitico e nao faz parte da
solucao ativa.

## Instalacao

Instale somente os pacotes que sua aplicacao realmente usa:

```powershell
dotnet add package Agile.Maui.Gallery
dotnet add package Agile.Maui.Pdf
dotnet add package Agile.Maui.VirtualizedCollection
```

Depois registre os handlers no `MauiProgram.cs`:

```csharp
using Agile.Maui;

builder
    .UseMauiApp<App>()
    .UseAgileGalleryView()
    .UseAgilePdfViewer()
    .UseAgileVirtualizedCollectionView();
```

Cada metodo e independente. Se a aplicacao usa apenas PDF, por exemplo, chame
somente `UseAgilePdfViewer()`.

## XAML

O namespace C# e o mesmo, mas o assembly XAML muda por pacote:

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
```

Exemplo:

```xml
<gallery:ImageView Source="photo" />
<gallery:GalleryView Images="{Binding Photos}" />
<pdf:PdfViewer Source="{Binding PdfPath}" />
<pdf:PdfReaderView Source="manual.pdf" />
<virtualized:VirtualizedCollectionView ItemsSource="{Binding Items}" />
```

## Plataformas

| Componente | Android | iOS / MacCatalyst | Windows |
|---|---|---|---|
| `ImageView` | `Android.Widget.ImageView` + Glide + zoom fullscreen nativo | `UIImageView` + `UIScrollView` fullscreen | `Microsoft.UI.Xaml.Controls.Image` |
| `GalleryView` | `ViewPager2` + `RecyclerView` | `UIScrollView` paginado + `UIPageControl` | `FlipView` |
| `PdfViewer` | `PdfRenderer` para render + PDFium para texto/busca | `PdfKit.PdfView` | PDFium |
| `VirtualizedCollectionView` | `RecyclerView` | `UICollectionViewCompositionalLayout` | MAUI `CollectionView` |

## Estrutura

```text
Agile.Maui.slnx
GalleryView/
PDFViewer/
VirtualizedCollectionView/
sample/
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
dotnet build -f net10.0-windows10.0.19041.0
```

## Documentacao adicional

- [GalleryView e ImageView](docs/GalleryView.md)
- [PDFViewer e PdfReaderView](docs/PDFViewer.md)
- [VirtualizedCollectionView](docs/VirtualizedCollectionView.md)
- [Aplicativo sample](docs/Sample.md)
- [Tuning de performance](TUNING.md)
- [Profiling Android do VirtualizedCollectionView](PROFILING.md)
