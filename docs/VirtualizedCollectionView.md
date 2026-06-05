# Agile.Maui.VirtualizedCollection

Projeto do `VirtualizedCollectionView`, um controle de lista/grade com
virtualizacao nativa e suporte a templates MAUI.

Assembly: `Agile.Maui.VirtualizedCollection`  
Namespace C#: `Agile.Maui`  
Registro: `builder.UseAgileVirtualizedCollectionView()`

## Instalacao

```powershell
dotnet add package Agile.Maui.VirtualizedCollection
```

```csharp
using Agile.Maui;

builder.UseAgileVirtualizedCollectionView();
```

```xml
xmlns:virtualized="clr-namespace:Agile.Maui;assembly=Agile.Maui.VirtualizedCollection"
```

## Exemplo rapido

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Items}"
    ItemTemplate="{StaticResource ProductTemplate}"
    Span="1"
    ItemSizingStrategy="Dynamic"
    ItemHeightRequest="200"
    RemainingItemsThreshold="8"
    RemainingItemsThresholdReached="OnLoadMore"
    Scrolled="OnScrolled" />
```

## Propriedades

| Propriedade | Tipo | Padrao | Descricao |
|---|---|---|---|
| `ItemsSource` | `IEnumerable?` | `null` | Fonte de dados. Suporta `INotifyCollectionChanged`. |
| `ItemTemplate` | `DataTemplate?` | `null` | Template MAUI de cada item. |
| `ItemHeight` | `double` | `-1` | Altura fixa explicita. Quando `> 0`, vence a estrategia de sizing em Android/iOS/Mac. Ignorada no Windows. |
| `ItemHeightRequest` | `double` | `350` | Altura fixa fallback em `Fixed`; estimativa em `Dynamic`. No Windows nao ha equivalente de altura estimada. |
| `Span` | `int` | `1` | Colunas no layout vertical; linhas no layout horizontal. |
| `Orientation` | `VirtualizedOrientation` | `Vertical` | `Vertical` ou `Horizontal`. |
| `ItemSizingStrategy` | `ItemSizingStrategy` | `Fixed` | `Fixed` para altura previsivel; `Dynamic` para altura medida pelo conteudo. |
| `ItemSpacing` | `double` | `0` | Espaco entre itens. |
| `RemainingItemsThreshold` | `int` | `-1` | Dispara carga incremental quando faltam N itens. |
| `RemainingItemsThresholdReachedCommand` | `ICommand?` | `null` | Comando para infinite scroll. |
| `ScrolledCommand` | `ICommand?` | `null` | Recebe `VirtualizedScrolledEventArgs`. |
| `EmptyView` | `object?` | `null` | Conteudo exibido quando vazio. |
| `EmptyViewTemplate` | `DataTemplate?` | `null` | Template para `EmptyView`. |

## Eventos e metodos

| API | Descricao |
|---|---|
| `RemainingItemsThresholdReached` | Evento de infinite scroll. |
| `Scrolled` | Evento de scroll; args possuem `HorizontalOffset` e `VerticalOffset`. |
| `ScrollTo(int index, bool animated = true)` | Rola ate um indice. |

## Enums

```csharp
public enum VirtualizedOrientation
{
    Vertical,
    Horizontal
}

public enum ItemSizingStrategy
{
    Fixed,
    Dynamic
}
```

## Estrategia de tamanho

`Fixed` e o caminho mais rapido. Use quando todos os itens tem altura previsivel.
Se `ItemHeight > 0`, essa altura e usada. Caso contrario, `ItemHeightRequest`
serve como altura fixa de fallback em Android/iOS/Mac.

`Dynamic` mede cada item pelo conteudo. Use para posts, cards expansivos ou
textos com muitas variacoes. Em Android, o caminho dinamico completo so e usado
quando `Span=1`, `Orientation=Vertical` e `ItemHeight <= 0`. Em iOS/Mac,
`Dynamic` usa self-sizing do `UICollectionViewCompositionalLayout`.

No Windows, `ItemSizingStrategy` e mapeado para o `CollectionView` interno:

| Agile | Windows MAUI |
|---|---|
| `Fixed` | `CollectionView.ItemSizingStrategy = MeasureFirstItem` |
| `Dynamic` | `CollectionView.ItemSizingStrategy = MeasureAllItems` |

`ItemHeight` e `ItemHeightRequest` nao tem mapeamento direto no Windows. Para
altura fixa no Windows, defina `HeightRequest` dentro do proprio `DataTemplate`.

## Comportamento por plataforma

| Plataforma | Implementacao |
|---|---|
| Android | `RecyclerView`, `LinearLayoutManager`, `GridLayoutManager` e `CachingLinearLayoutManager` para altura dinamica. |
| iOS/MacCatalyst | `UICollectionView` com `UICollectionViewCompositionalLayout`; `PreferredLayoutAttributesFitting` mede views MAUI. |
| Windows | `ContentView` que hospeda um `CollectionView` MAUI, com drag-to-scroll por mouse e inercia. |

## Exemplo com template inline

```xml
<virtualized:VirtualizedCollectionView
    ItemsSource="{Binding Products}"
    Span="2"
    ItemSpacing="8"
    ItemSizingStrategy="Fixed"
    ItemHeightRequest="160"
    RemainingItemsThreshold="10">

    <virtualized:VirtualizedCollectionView.ItemTemplate>
        <DataTemplate x:DataType="local:Product">
            <Border Padding="12" StrokeShape="RoundRectangle 8">
                <VerticalStackLayout>
                    <Label Text="{Binding Name}" FontAttributes="Bold" />
                    <Label Text="{Binding PriceText}" />
                </VerticalStackLayout>
            </Border>
        </DataTemplate>
    </virtualized:VirtualizedCollectionView.ItemTemplate>
</virtualized:VirtualizedCollectionView>
```

## Recomendacoes de performance

- Prefira `Fixed` quando o card tem altura conhecida.
- Use `Dynamic` apenas quando a altura realmente varia.
- Ajuste `ItemHeightRequest` para algo proximo da media real, principalmente no iOS/Mac.
- Use `x:DataType` nos `DataTemplate`.
- Evite imagens remotas grandes dentro de celulas; prefira thumbnails ou `ImageView` do pacote `GalleryView`.
- Use `ObservableCollection` para atualizacoes incrementais em vez de trocar toda a lista.

Veja tambem [../TUNING.md](../TUNING.md) e [../PROFILING.md](../PROFILING.md).
