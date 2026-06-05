# Agile.Maui.Pdf

Projeto de visualizacao PDF com dois niveis de uso:

- `PdfViewer`: controle base para montar uma interface customizada.
- `PdfReaderView`: leitor pronto, com toolbar, busca, print/share, orientacao, miniaturas, zoom e navegacao.

Assembly: `Agile.Maui.Pdf`  
Namespace C#: `Agile.Maui`  
Registro: `builder.UseAgilePdfViewer()`

## Instalacao

```powershell
dotnet add package Agile.Maui.Pdf
```

```csharp
using Agile.Maui;

builder.UseAgilePdfViewer();
```

```xml
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
```

`UseAgilePdfViewer()` tambem registra a fonte interna `AgilePdfIcons`, usada pelo
`PdfReaderView`.

## PdfViewer

`PdfViewer` e o controle base. Ele renderiza o PDF e expoe propriedades,
eventos e comandos para navegacao, zoom, busca, print e miniaturas.

### Fontes de documento

| Propriedade | Tipo | Descricao |
|---|---|---|
| `Source` | `string?` | Caminho local ou URL. |
| `PdfStream` | `Stream?` | Stream PDF fornecido pela aplicacao. |
| `Password` | `string?` | Senha para PDFs protegidos. |

### Estado e navegacao

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `CurrentPage` | `int` | `0` | Pagina atual, 0-based, `TwoWay`. |
| `PageCount` | `int` | `0` | Total de paginas, definido pelo controle. |
| `ScrollOrientation` | `PdfScrollOrientation` | `Vertical` | Scroll continuo vertical ou horizontal paginado. |

### Zoom e renderizacao

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `ZoomFactor` | `double` | `1.0` | Zoom relativo ao ajuste base. |
| `MinZoom` | `double` | `0.5` | Zoom minimo. |
| `MaxZoom` | `double` | `8.0` | Zoom maximo. |
| `IsPinchZoomEnabled` | `bool` | `true` | Habilita gesto de pinch quando suportado. |
| `RenderScale` | `double` | `1.5` | Escala/DPI de render. |
| `MaxCacheMB` | `int` | `200` | Limite de cache de paginas renderizadas. |
| `EnablePageCaching` | `bool` | `true` | Habilita cache/prefetch. |
| `PrefetchAbove` | `int` | `2` | Paginas acima a pre-carregar. |
| `PrefetchBelow` | `int` | `3` | Paginas abaixo a pre-carregar. |

### Aparencia e textos

| Propriedade | Tipo | Padrao |
|---|---|---|
| `PageBackgroundColor` | `Color` | `White` |
| `PageSpacing` | `double` | `8` |
| `CopyButtonText` | `string` | `Copy` |
| `CopiedMessageText` | `string` | `Copied` |
| `ThumbnailBarTitleText` | `string` | `Pages` |
| `PrintJobName` | `string` | `Document` |

### Miniaturas

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `EnableThumbnailBar` | `bool` | `false` | Sidebar fixa no Windows. |
| `IsThumbnailBarOpen` | `bool` | `false` | Drawer mobile, `TwoWay`. |
| `ThumbnailBarPlacement` | `PdfThumbnailPlacement` | `None` | `None`, `Left` ou `Right`. |

### Eventos

| Evento | Args |
|---|---|
| `DocumentLoaded` | `PdfDocumentLoadedEventArgs` |
| `DocumentLoadFailed` | `PdfDocumentLoadFailedEventArgs` |
| `PageChanged` | `PdfPageChangedEventArgs` |
| `SearchResultChanged` | `PdfSearchResultEventArgs` |
| `LinkTapped` | `PdfLinkTappedEventArgs` |

### Metodos

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

### Exemplo base

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

`PdfReaderView` compoe uma UI completa sobre `PdfViewer`. Ele e indicado quando
voce quer um leitor pronto e customizavel sem recriar toolbar, busca e barra
inferior no app.

### Principais propriedades

`PdfReaderView` repassa a maior parte das propriedades do `PdfViewer`: `Source`,
`PdfStream`, `Password`, `ZoomFactor`, `MinZoom`, `MaxZoom`,
`ScrollOrientation`, `ThumbnailBarPlacement`, `IsThumbnailBarOpen`,
`EnableThumbnailBar`, `RenderScale`, `MaxCacheMB`, `PrefetchAbove`,
`PrefetchBelow`, textos e cores.

Ele tambem expoe:

| Propriedade | Tipo | Descricao |
|---|---|---|
| `ViewerControl` | `PdfViewer` | Acesso ao controle base para cenarios avancados. |
| `ToolbarColor` | `Color` | Cor da barra superior. |
| `BottomBarColor` | `Color` | Cor da barra inferior. |
| `IconColor` | `Color` | Cor dos icones. |
| `CaptionColor` | `Color` | Cor dos titulos e contadores. |
| `LoadingText` | `string` | Texto do carregamento. |
| `SearchPlaceholder` | `string` | Placeholder da busca. |
| `PageCountFormat` | `string` | Formato do total de paginas. |
| `LoadFailedText` | `string` | Texto em falha de carga. |
| `ShowToolbar` | `bool` | Mostra a barra superior. |
| `ShowSearch` | `bool` | Mostra o botao de busca. |
| `ShowPrint` | `bool` | Mostra o botao de impressao. |
| `ShowShare` | `bool` | Mostra o botao de compartilhamento. |
| `ShowOrientationToggle` | `bool` | Mostra alternancia vertical/horizontal. |
| `ShowBottomBar` | `bool` | Mostra a barra inferior. |

### Exemplo pronto

```xml
<pdf:PdfReaderView
    Source="InteligenciaArtificial.pdf"
    LoadingText="Carregando..."
    SearchPlaceholder="Buscar..."
    PageCountFormat="{}{0} paginas"
    CopyButtonText="Copiar"
    CopiedMessageText="Copiado"
    PrintJobName="Documento"
    ThumbnailBarPlacement="Right"
    ShowToolbar="True"
    ShowBottomBar="True" />
```

## Comportamento por plataforma

| Plataforma | Motor |
|---|---|
| Android | Renderizacao via `PdfRenderer`; camada de texto, busca e links via PDFium. |
| iOS/MacCatalyst | `PdfKit.PdfView`, com zoom, selecao, links e virtualizacao nativos. |
| Windows | PDFium para renderizacao e texto. |

## Quando usar cada controle

Use `PdfViewer` quando a aplicacao precisa de toolbar propria, comandos em outro
layout, overlays personalizados ou integracao profunda com o fluxo do app.

Use `PdfReaderView` quando voce quer um leitor completo com configuracao por
propriedades e localizacao de textos.
