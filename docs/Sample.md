# sample

Aplicativo de demonstracao que consome os tres projetos ativos:

- `GalleryView`
- `PDFViewer`
- `VirtualizedCollectionView`

Ele tambem contem uma comparacao com o `CollectionView` padrao do MAUI para medir
comportamento, scroll e carga incremental.

## Registro dos componentes

O sample registra todos os componentes no `MauiProgram.cs`:

```csharp
builder
    .UseMauiApp<App>()
    .UseAgileGalleryView()
    .UseAgilePdfViewer()
    .UseAgileVirtualizedCollectionView();
```

Tambem registra a fonte `MaterialDesignIcons`, usada pelos botoes do app de
exemplo, e o servico `IAnchoredMenu`, usado no menu superior do PDF customizado.

## Paginas

| Pagina | Finalidade |
|---|---|
| `MainPage` | Demonstra `ImageView` e `GalleryView`. |
| `ReaderDemoPage` | Demonstra o `PdfReaderView`, o leitor pronto. |
| `VirtualizedListPage` | Demonstra `VirtualizedCollectionView` com lista, grade, busca e metricas. |

## XAML namespaces usados

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
```

## Assets

| Asset | Uso |
|---|---|
| `Resources/Images/agile.png` | Cabecalho do menu Shell. |
| `Resources/Images/InteligenciaArtificial.pdf` | PDF empacotado para as demos. |
| `Resources/Images/dotnet_bot.png` | Placeholder de imagem. |
| `Resources/Fonts/materialdesignicons-webfont.ttf` | Icones do sample. |

PDFs em `Resources/Images` sao removidos de `MauiImage` e incluidos como
`MauiAsset`, para evitar problemas do Resizetizer com arquivos PDF.

## Menu Shell

O `AppShell.xaml` usa um flyout com cabecalho contendo `agile.png`. Cada item do
menu e um `ShellContent`:

- `Gallery View`: imagens e galeria.
- `PDF Viewer`: UI pronta com `PdfReaderView`.
- `Virtualized Collection`: lista virtualizada com busca, grade e metricas.

## ReaderDemoPage

Usa `PdfReaderView` com toolbar, busca, impressao, compartilhamento, alternancia
vertical/horizontal, miniaturas, zoom e navegacao por paginas.

## VirtualizedListPage

Usa:

```xml
<virtualized:VirtualizedCollectionView
    ItemHeightRequest="200"
    ItemSizingStrategy="Dynamic"
    RemainingItemsThreshold="8" />
```

Tambem permite alternar entre lista/grade e altura fixa/dinamica para observar
impacto de performance.

## Build

```powershell
dotnet build sample/sample.csproj
dotnet build sample/sample.csproj -f net10.0-windows10.0.19041.0
dotnet build sample/sample.csproj -f net10.0-android
```
