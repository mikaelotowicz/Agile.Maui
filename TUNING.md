# TUNING — VirtualizedCollectionView (Android)

Guia de referência para ajuste de performance do `VirtualizedCollectionView` no Android.

---

## ItemSizeStrategy

| Propriedade | Padrão | Descrição |
|---|---|---|
| `ItemSizeStrategy` | `Fixed` | `Fixed` = altura fixa, `Dynamic` = auto-medição por conteúdo |
| `ItemHeightRequest` | `350` | Altura em DP usada em `Fixed` e como fallback inicial em `Dynamic` |

### Fixed

Melhor performance bruta. Todos os itens têm `LayoutParams.height = ItemHeightRequest px`.  
Use quando os itens têm altura uniforme (cards, listas simples).

```xml
<agile:VirtualizedCollectionView
    ItemSizeStrategy="Fixed"
    ItemHeightRequest="120" />
```

### Dynamic

Itens usam `WrapContent` — expanders, textos variáveis e grids heterogêneos funcionam corretamente.  
Um `CachingLinearLayoutManager` substitui o `LinearLayoutManager` padrão para eliminar os saltos de scroll bar causados por estimativas erradas de altura.

Restrições do modo Dynamic:
- Apenas em layout de coluna única vertical (`ColumnCount=1`, `Orientation=Vertical`).
- Grade (`ColumnCount > 1`) sempre usa `Fixed`.

```xml
<agile:VirtualizedCollectionView
    ItemSizeStrategy="Dynamic"
    ItemHeightRequest="350" />
```

---

## Cache e Pool de Views

Os tamanhos são calculados automaticamente via `ActivityManager.MemoryInfo` ao conectar o handler e a cada mudança de `ColumnCount`.

| RAM do dispositivo | ViewCache | PoolMax (por viewType) |
|---|---|---|
| ≥ 6 GB | 8 | 20 |
| ≥ 3 GB | 5 | 12 |
| ≥ 1,5 GB | 3 | 8 |
| < 1,5 GB | 2 | 5 |

Em grids (`ColumnCount > 1`) os valores são multiplicados por `columnCount / 2`.

O log `VrHandler` imprime a decisão a cada reconfiguração:
```
D/VrHandler: RAM=4096MB cols=2 → cache=5 pool=12
```

**ViewCache** (`SetItemViewCacheSize`): views retiradas da tela ficam *bound* sem rebind até que o limite seja atingido. Aumentar o ViewCache reduz rebinds ao rolar para cima/baixo, mas mantém objetos MAUI vivos na memória.

**Pool** (`RecycledViewPool.SetMaxRecycledViews`): views não-bound aguardam reuso aqui. Diminuir o pool força criação de novas views; aumentar reduz inflação mas cresce o heap.

---

## Prefetch GapWorker

```csharp
// CachingLinearLayoutManager (Dynamic, 1 coluna)
InitialPrefetchItemCount = 4;   // pré-cria 4 itens durante frames ociosos

// LinearLayoutManager (Fixed, 1 coluna)
InitialPrefetchItemCount = 6;

// GridLayoutManager (Fixed, N colunas)
InitialPrefetchItemCount = columns * 3;
```

Aumentar `InitialPrefetchItemCount` reduz frames de inflate visíveis ao usuário,  
ao custo de mais CPU em background durante a renderização do primeiro quadro.

---

## CancellationTokenSource por ViewHolder (Dynamic)

No modo Dynamic cada `OnBindViewHolder` emite um `Post()` para medir a altura real após o layout.  
O `BindCts` por holder garante que o callback não leia a altura de um item diferente se o holder for reciclado antes do `Post` disparar.

`VrRecyclerListener.OnViewRecycled` cancela o CTS, limpa Glide e nula o `BindingContext` antes de o holder ir para o pool.

---

## Imagens dentro de células (iOS)

O `VirtualizedCollectionView` envolve MAUI Views via `DataTemplate` — o handler da lista não controla o decode de imagens dentro das células. O limite de resolução deve ser garantido pelo consumidor ou pelo servidor.

Recomendações para listas com muitas imagens no iOS:

| Abordagem | Benefício |
|---|---|
| Usar `<agile:ImageView>` nas células (Android) | Glide aplica `Override(ThumbMaxPx, ThumbMaxPx)` automaticamente |
| Servir thumbnails já redimensionados via URL | Elimina decode de imagens em tamanho original na memória |
| Definir `ItemHeight` fixo + imagem com mesmo AR | UIKit não precisa re-medir a célula após o decode |
| Evitar `<Image>` MAUI para URLs em listas longas | Sem controle de decode size ou cancelamento por célula |

No iOS, o `ImageViewHandler` já usa `NSUrlSession` com `CancellationTokenSource` por load e cancela automaticamente em `DisconnectHandler`. A `VrMauiCell` tem `LoadToken` (CTS por célula) que cancela qualquer operação async pendente em `PrepareForReuse`.

---

## HasFixedSize

`PlatformView.Rv.HasFixedSize = true` está sempre ligado.  
O RecyclerView não precisa recalcular seu próprio tamanho quando o número de itens muda — a área da lista é fixa dentro do layout MAUI.
