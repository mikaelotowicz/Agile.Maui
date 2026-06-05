# Agile.Maui.Pdf

Projeto de visualizacao PDF com dois niveis de uso:

- `PdfViewer`: controle base para montar uma interface customizada.
- `PdfReaderView`: leitor pronto, com toolbar, busca, print/share, orientacao, miniaturas, zoom e navegacao.

Plataformas suportadas: Android, iOS, macOS Catalyst e Windows.

Assembly: `Agile.Maui.Pdf`  
Namespace C#: `Agile.Maui`  
Registro: `builder.UseAgilePdfViewer()`

## Visao geral

`Agile.Maui.Pdf` traz visualizacao PDF nativa para apps .NET MAUI sem depender de
`WebView`. O pacote expoe um controle base (`PdfViewer`) para UIs customizadas e
um leitor pronto (`PdfReaderView`) com toolbar, busca, impressao, compartilhamento,
miniaturas e navegacao.

### Por que usar

- Renderizacao nativa por plataforma, sem HTML/JavaScript.
- API consistente para Android, iOS, macOS Catalyst e Windows.
- Duas camadas de uso: controle base ou leitor completo.
- Busca, selecao/copia de texto, zoom e navegacao por pagina.
- Configuracao por `BindableProperty`, adequada para XAML e MVVM.

## Recursos

### Funcionalidade principal

- Fontes por caminho/URL (`Source`), stream (`PdfStream`) e asset empacotado no `PdfReaderView`.
- Senha para PDFs protegidos (`Password`).
- Zoom programatico, pinch quando suportado e limites `MinZoom`/`MaxZoom`.
- Scroll vertical continuo ou horizontal paginado.
- Busca de texto com resultado atual/total.
- Links com evento `LinkTapped` onde suportado pelo handler da plataforma.
- Impressao nativa por plataforma.
- Compartilhamento no `PdfReaderView`.

### Recursos avancados

- Cache LRU e prefetch configuravel de paginas.
- Miniaturas como drawer/overlay no mobile e sidebar fixa no Windows.
- Selecao e copia de texto onde suportado pelo handler.
- Localizacao de textos do leitor pronto.
- Controle de renderizacao por `RenderScale`, `MaxCacheMB`, `PrefetchAbove` e `PrefetchBelow`.

## Requisitos

- .NET MAUI / .NET 10.0.
- Android API 21+.
- iOS 15.0+.
- macOS Catalyst 15.0+.
- Windows 10.0.17763.0+; target Windows recomendado: `net10.0-windows10.0.19041.0`.

## Inicio rapido

### 1. Instale o pacote

```powershell
dotnet add package Agile.Maui.Pdf
```

### 2. Registre o handler

```csharp
using Agile.Maui;

builder
    .UseMauiApp<App>()
    .UseAgilePdfViewer();
```

`UseAgilePdfViewer()` tambem registra a fonte interna `AgilePdfIcons`, usada pelo
`PdfReaderView`.

### 3. Adicione o namespace XAML

```xml
xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf"
```

### 4. Use o leitor pronto

```xml
<pdf:PdfReaderView Source="manual.pdf" />
```

### 5. Ou use o controle base

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    DocumentLoaded="OnDocumentLoaded"
    PageChanged="OnPageChanged" />
```

## PdfViewer

`PdfViewer` e o controle base. Ele renderiza o PDF e expoe propriedades,
eventos e comandos para navegacao, zoom, busca, print e miniaturas.

### Fontes de documento

| Propriedade | Tipo | Descricao |
|---|---|---|
| `Source` | `string?` | Caminho local ou URL. |
| `PdfStream` | `Stream?` | Stream PDF fornecido pela aplicacao. |
| `Password` | `string?` | Senha para PDFs protegidos. |

### Regras de origem

| Entrada | Comportamento |
|---|---|
| `https://...` ou `http://...` | Baixa e abre como URL. |
| Caminho de arquivo existente | Abre o arquivo local. |
| `PdfStream` | Abre os bytes fornecidos pela aplicacao. |
| `PdfReaderView Source="arquivo.pdf"` | Se nao for URL/arquivo local, tenta abrir como MauiAsset e copia para cache. |

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
| `LinkTapped` | `PdfLinkTappedEventArgs`; Android intercepta links internos/externos, iOS/MacCatalyst intercepta URLs externas e Windows ainda nao expõe interceptacao de links. Defina `Handled = true` para impedir a acao padrao quando suportado. |

### Commands

| Command | Args |
|---|---|
| `DocumentLoadedCommand` | `PdfDocumentLoadedEventArgs` |
| `DocumentLoadFailedCommand` | `PdfDocumentLoadFailedEventArgs` |
| `PageChangedCommand` | `PdfPageChangedEventArgs` |

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

## Exemplos

### Leitor completo em uma pagina

```xml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:pdf="clr-namespace:Agile.Maui;assembly=Agile.Maui.Pdf">

    <pdf:PdfReaderView
        Source="manual.pdf"
        MinZoom="1"
        MaxZoom="4"
        SearchPlaceholder="Buscar..."
        LoadingText="Carregando..."
        ThumbnailBarPlacement="Right" />
</ContentPage>
```

### Controle base com eventos

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
    StatusLabel.Text = $"{e.PageCount} paginas";
}

private void OnPageChanged(object sender, PdfPageChangedEventArgs e)
{
    PageLabel.Text = $"{e.Page + 1}/{Viewer.PageCount}";
}

private void OnLinkTapped(object sender, PdfLinkTappedEventArgs e)
{
    if (e.Uri is not null && !e.Uri.StartsWith("https://minhaempresa.com"))
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

### PDF protegido por senha

```xml
<pdf:PdfReaderView
    Source="{Binding SecurePdfPath}"
    Password="{Binding PdfPassword}" />
```

## Cenarios comuns

### Bloquear navegacao externa

Disponivel nos handlers que expõem `LinkTapped` para o tipo de link tocado
(Android para links internos/externos; iOS/MacCatalyst para URLs externas).

```csharp
Viewer.LinkTapped += (sender, e) =>
{
    if (e.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
        e.Handled = true;
};
```

### Abrir links externos com confirmacao

Disponivel no Android, iOS e MacCatalyst para URLs externas.

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

### Ajustar memoria para PDFs grandes

```xml
<pdf:PdfViewer
    Source="{Binding PdfPath}"
    RenderScale="1.5"
    MaxCacheMB="150"
    PrefetchAbove="1"
    PrefetchBelow="2" />
```

## Comportamento por plataforma

| Plataforma | Motor | Observacoes |
|---|---|---|
| Android | `PdfRenderer` + PDFium | Render nativo; PDFium para texto, busca e links. |
| iOS/MacCatalyst | `PdfKit.PdfView` | Zoom, selecao, links nativos; `LinkTapped` cobre URLs externas. |
| Windows | PDFium | Renderizacao, texto e busca via PDFium; impressao e compartilhamento usam APIs WinUI/Windows. |

## Arquitetura

```text
.NET MAUI app
  |
  +-- PdfReaderView (UI pronta: toolbar, busca, share, print)
  |     |
  |     +-- PdfViewer
  |
  +-- PdfViewer (View cross-platform)
        |
        +-- Android handler      -> PdfRenderer + PDFium
        +-- iOS/Mac handler      -> PdfKit.PdfView
        +-- Windows handler      -> PDFium + WinUI
```

## Performance

- O controle renderiza paginas sob demanda e mantem cache configuravel.
- `MaxCacheMB` limita o uso de memoria das paginas renderizadas.
- `PrefetchAbove` e `PrefetchBelow` melhoram a fluidez do scroll ao custo de memoria/CPU.
- `RenderScale` controla a nitidez: valores maiores melhoram qualidade, mas aumentam custo.
- Para documentos muito grandes, reduza prefetch e cache antes de aumentar `RenderScale`.

## Solucao de problemas

### O PDF nao carrega

- Confira se `Source` e URL acessivel ou caminho de arquivo existente.
- Para assets, marque o PDF como `MauiAsset` no projeto do app e use o nome logico correto.
- Se o PDF for protegido, defina `Password`.
- Assine `DocumentLoadFailed` para capturar a mensagem de erro.

### Busca nao encontra texto

- Alguns PDFs sao imagens escaneadas e nao possuem camada de texto pesquisavel.
- Confirme se o termo nao esta vazio e se o documento terminou de carregar.

### Links nao abrem ou precisam ser bloqueados

- Assine `LinkTapped`.
- Defina `e.Handled = true` quando quiser impedir a acao padrao.
- Android intercepta links internos e externos; iOS/MacCatalyst intercepta URLs externas; Windows ainda nao expõe interceptacao de links.

### Compartilhamento no Windows nao abre

- O `PdfReaderView` usa a API WinUI de compartilhamento vinculada ao HWND da janela atual.
- Confirme que existe uma janela MAUI ativa e que o documento veio de arquivo, URL ou `PdfStream` valido.

## Build local

```powershell
dotnet build PDFViewer\PDFViewer.csproj
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-android
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-ios
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-maccatalyst
dotnet build PDFViewer\PDFViewer.csproj -f net10.0-windows10.0.19041.0
```

## Quando usar cada controle

Use `PdfViewer` quando a aplicacao precisa de toolbar propria, comandos em outro
layout, overlays personalizados ou integracao profunda com o fluxo do app.

Use `PdfReaderView` quando voce quer um leitor completo com configuracao por
propriedades e localizacao de textos.

## Licenca

Pacote distribuido sob licenca MIT.

## Suporte

Use o issue tracker do repositorio para bugs, duvidas e pedidos de recurso.
