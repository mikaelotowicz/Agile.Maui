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
| `BenchmarkPage` | Compara `CollectionView` nativa e `VirtualizedCollectionView` com tempo, memoria, scroll e log de iteracoes. |

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
- `Benchmark`: comparacao lado a lado com `CollectionView` nativa.

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

## BenchmarkPage

Compara `CollectionView` nativa e `VirtualizedCollectionView` usando o mesmo
`ProductItem` e o mesmo template principal da `VirtualizedListPage`, incluindo
imagem, textos e secoes expansivas. A pagina mede, por iteracao:

- tempo para trocar `ItemsSource` e aguardar a primeira estabilizacao visual;
- delta de memoria gerenciada reportado por `GC.GetTotalMemory`;
- proxy de quadros perdidos durante rolagem programatica em passos;
- quantidade de views nativas realizadas quando a plataforma permite contar;
- medias, desvio e log de cada rodada.

Os resultados sao uma medicao pratica dentro do app sample, nao um benchmark de
laboratorio.

## Build

```powershell
dotnet build sample/sample.csproj
dotnet build sample/sample.csproj -f net10.0-windows10.0.19041.0
dotnet build sample/sample.csproj -f net10.0-android
```
