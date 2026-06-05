<div align="center">

# Agile.Maui

<img src="agile.png" alt="Agile.Maui" width="160" />

**Componentes nativos e modulares para .NET MAUI**

[![Gallery NuGet](https://img.shields.io/nuget/v/Agile.Maui.Gallery?label=Agile.Maui.Gallery)](https://www.nuget.org/packages/Agile.Maui.Gallery)
[![PDF NuGet](https://img.shields.io/nuget/v/Agile.Maui.Pdf?label=Agile.Maui.Pdf)](https://www.nuget.org/packages/Agile.Maui.Pdf)
[![Virtualized NuGet](https://img.shields.io/nuget/v/Agile.Maui.VirtualizedCollection?label=Agile.Maui.VirtualizedCollection)](https://www.nuget.org/packages/Agile.Maui.VirtualizedCollection)

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![MAUI](https://img.shields.io/badge/MAUI-supported-512BD4)
![Platforms](https://img.shields.io/badge/platforms-Android%20%7C%20iOS%20%7C%20macOS%20Catalyst%20%7C%20Windows-blue)
![License](https://img.shields.io/badge/license-MIT-green)

`ImageView` / `GalleryView` | `PdfViewer` / `PdfReaderView` | `VirtualizedCollectionView`

[Visao geral](#visao-geral) | [Recursos](#recursos) | [Inicio rapido](#inicio-rapido) | [Projetos](#projetos) | [Plataformas](#plataformas) | [Documentacao](#documentacao-adicional)

</div>

Biblioteca de componentes .NET MAUI com implementacoes nativas para Android, iOS,
macOS Catalyst e Windows. A solucao atual e modular: cada componente vive em seu
proprio projeto/pacote, mas todos compartilham o namespace C# `Agile.Maui`.

## Visao geral

Agile.Maui e uma colecao de controles MAUI nativos para cenarios comuns de apps
mobile e desktop: imagens com zoom, galerias, leitura de PDF e listas virtualizadas.
Cada modulo pode ser instalado separadamente, entao o app consome apenas o que usa.

### Por que usar

- Controles nativos por plataforma; o PDF nao depende de `WebView` e imagens usam views nativas.
- Pacotes independentes com o mesmo namespace C#.
- APIs bindaveis para XAML e MVVM.
- Handlers especificos para Android, iOS, macOS Catalyst e Windows.
- App sample e documentacao por componente.

## Recursos

| Modulo | Recursos principais |
|---|---|
| `Agile.Maui.Gallery` | `ImageView` com zoom/fullscreen nas plataformas suportadas e `GalleryView` com navegacao por imagens. |
| `Agile.Maui.Pdf` | `PdfViewer` base e `PdfReaderView` pronto com busca, print/share, zoom, miniaturas e navegacao. |
| `Agile.Maui.VirtualizedCollection` | Lista virtualizada de alto desempenho para grandes volumes de itens. |

## Requisitos

- .NET MAUI / .NET 10.0.
- Android, iOS, macOS Catalyst ou Windows.
- Registro do pacote usado no `MauiProgram.cs`.

## Inicio rapido

Instale o pacote do componente que voce precisa:

```powershell
dotnet add package Agile.Maui.Pdf
```

Registre o handler:

```csharp
using Agile.Maui;

builder
    .UseMauiApp<App>()
    .UseAgilePdfViewer();
```

Use no XAML:

```xml
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"

<pdf:PdfReaderView Source="manual.pdf" />
```

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

## Performance

- `VirtualizedCollectionView` usa handlers nativos e virtualizacao para reduzir custo em listas grandes.
- `PdfViewer` renderiza paginas sob demanda e possui cache/prefetch configuravel.
- `GalleryView` e `ImageView` usam carregamento nativo por plataforma e cache onde aplicavel.

## Solucao de problemas

- Se um controle nao renderizar, confirme se o metodo `UseAgile...()` correspondente foi chamado.
- Se XAML nao encontrar o controle, confira o assembly no namespace (`Agile.Maui.Pdf`, `Agile.Maui.Gallery` ou `Agile.Maui.VirtualizedCollection`).
- Para PDFs empacotados, use `MauiAsset` e consulte [docs/PDFViewer.md](docs/PDFViewer.md).

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

## Licenca

Distribuido sob licenca MIT.

## Suporte

Use o issue tracker do repositorio para relatar bugs, tirar duvidas ou sugerir
novos recursos.
