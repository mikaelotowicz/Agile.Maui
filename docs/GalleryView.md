# Agile.Maui.Gallery

Projeto que entrega dois controles visuais para imagens:

- `ImageView`: imagem unica com carregamento nativo, eventos de carga e zoom fullscreen nas plataformas suportadas.
- `GalleryView`: galeria paginada de imagens com selecao, indicadores e fullscreen nas plataformas suportadas.

Assembly: `Agile.Maui.Gallery`  
Namespace C#: `Agile.Maui`  
Registro: `builder.UseAgileGalleryView()`

## Instalacao

```powershell
dotnet add package Agile.Maui.Gallery
```

```csharp
using Agile.Maui;

builder.UseAgileGalleryView();
```

```xml
xmlns:gallery="clr-namespace:Agile.Maui;assembly=Agile.Maui.Gallery"
```

## ImageView

`ImageView` e um `View` MAUI cross-platform. Ele renderiza uma imagem local ou URL
e, quando permitido, abre uma visualizacao fullscreen com zoom.

### Propriedades

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `Source` | `string?` | `null` | Nome do recurso local ou URL completa. |
| `IsUrl` | `bool` | `false` | Indica que `Source` e uma URL HTTP/HTTPS. |
| `Placeholder` | `string?` | `null` | Recurso local exibido durante carga ou erro. |
| `MaxZoom` | `float` | `5` | Zoom maximo do viewer fullscreen. Minimo aceito: `1`. |
| `EnableFullscreen` | `bool` | `true` | Abre fullscreen ao tocar na imagem nas plataformas suportadas. |
| `FullscreenSource` | `string?` | `null` | Fonte de maior qualidade para fullscreen. Se nula, usa `Source`. Ignorada onde fullscreen nao e implementado. |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | `CenterCrop` ou `AspectFit`. |
| `ImageLoadedCommand` | `ICommand?` | `null` | Comando executado ao carregar. |
| `ImageFailedCommand` | `ICommand?` | `null` | Comando executado ao falhar. |

### Eventos

| Evento | Args | Quando dispara |
|---|---|---|
| `ImageLoaded` | `EventArgs` | Quando a imagem carrega com sucesso. |
| `ImageFailed` | `EventArgs` | Quando o carregamento falha ou a fonte nao existe. |

### Exemplo

```xml
<gallery:ImageView
    Source="https://picsum.photos/seed/maui/900/600"
    IsUrl="True"
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    EnableFullscreen="True"
    MaxZoom="6"
    HeightRequest="220" />
```

## GalleryView

`GalleryView` exibe uma lista de imagens em formato paginado. Ele usa o mesmo enum
`ZoomImageAspect` do `ImageView` e pode abrir a galeria em fullscreen com swipe e
zoom nas plataformas que suportam esse fluxo.

### Propriedades

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `Images` | `IList<string>?` | `null` | Lista de URLs ou recursos locais. |
| `IsUrl` | `bool` | `false` | Indica se os itens de `Images` sao URLs. |
| `Placeholder` | `string?` | `null` | Fallback enquanto cada imagem carrega. |
| `SelectedIndex` | `int` | `0` | Indice selecionado. Valor minimo: `0`. |
| `AspectMode` | `ZoomImageAspect` | `CenterCrop` | Como a imagem ocupa o espaco. |
| `MaxZoom` | `float` | `5` | Zoom maximo no fullscreen. |
| `ShowIndicator` | `bool` | `false` | Mostra indicadores de pagina. |
| `IndicatorColor` | `Color` | `White` | Cor do indicador ativo. |
| `IndicatorInactiveColor` | `Color` | branco 50% | Cor dos indicadores inativos. |
| `SelectionChangedCommand` | `ICommand?` | `null` | Recebe o indice selecionado. |
| `ImageLoadedCommand` | `ICommand?` | `null` | Comando ao carregar imagem. |
| `ImageFailedCommand` | `ICommand?` | `null` | Comando ao falhar imagem. |
| `ThumbMaxPx` | `int` | `720` | Limite de decode de thumbnail no Android. Minimo: `64`. |

### Eventos

| Evento | Args | Quando dispara |
|---|---|---|
| `SelectionChanged` | `GalleryIndexChangedEventArgs` | Quando a pagina atual muda. |
| `ImageLoaded` | `EventArgs` | Quando uma imagem carrega. |
| `ImageFailed` | `EventArgs` | Quando uma imagem falha. |

### Exemplo

```xml
<gallery:GalleryView
    Images="{Binding Photos}"
    IsUrl="True"
    Placeholder="dotnet_bot"
    AspectMode="CenterCrop"
    ShowIndicator="True"
    SelectedIndex="{Binding CurrentPhoto, Mode=TwoWay}"
    SelectionChangedCommand="{Binding PhotoChangedCommand}"
    HeightRequest="240" />
```

## Comportamento por plataforma

| Plataforma | `ImageView` | `GalleryView` |
|---|---|---|
| Android | `Android.Widget.ImageView` com Glide; fullscreen via `DialogFragment` e `Matrix`. | `ViewPager2`/`RecyclerView`; fullscreen nativo com swipe e zoom. |
| iOS/MacCatalyst | `UIImageView`; URLs via `NSUrlSession`; fullscreen com `UIScrollView`. | `UIScrollView` paginado + `UIPageControl`; fullscreen com zoom. |
| Windows | `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`; fullscreen nao e implementado. | `FlipView` com indicadores. |

## Recomendacoes

- Use `FullscreenSource` quando a lista mostra thumbnails, mas o fullscreen deve abrir uma imagem de maior qualidade.
- Em listas grandes, prefira URLs ja redimensionadas no servidor.
- No Android, reduza `ThumbMaxPx` quando muitas imagens remotas estiverem vivas ao mesmo tempo.
- Sempre defina `Placeholder` para evitar flashes visuais enquanto a imagem carrega.
