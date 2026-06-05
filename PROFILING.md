# PROFILING — VirtualizedCollectionView (Android)

Referência de benchmarks e instruções para medir performance no Android.

---

## Baselines medidos (dispositivo: Pixel 6, Android 14)

| Métrica | Antes | Depois |
|---|---|---|
| Frame mais lento (Davey) | 2170 ms | 758 ms |
| deltaY máximo visível | 19.328 px | 1.002 px |
| Heap médio em idle (50 itens) | ~18 MB | ~11 MB |
| Rebinds desnecessários ao rolar | frequentes | eliminados (ViewCache) |

**Antes**: `LinearLayoutManager` padrão com estimativas lineares de scroll.  
**Depois**: `CachingLinearLayoutManager` com cache progressivo de alturas reais.

---

## Ferramentas de medição

### Android Studio Profiler

1. `Run > Profile 'app'`
2. Aba **CPU** → gravar com **System Trace** enquanto rola a lista
3. Procurar por `RecyclerView#onMeasure`, `inflate`, `bind` e `draw` nas threads de UI

### `adb shell dumpsys gfxinfo`

```bash
adb shell dumpsys gfxinfo <package> framestats
```

Mostra histograma de frames, Janky frames % e contagem de Davey frames (> 700 ms).

### Logcat — heurístico de cache

```bash
adb logcat -s VrHandler
```

Imprime a decisão de cache/pool a cada reconfiguração:
```
D/VrHandler: RAM=4096MB cols=1 → cache=5 pool=12
```

### Logcat — frames lentos (MAUI)

```bash
adb logcat -s Choreographer
```

Linhas `Skipped N frames!` indicam trabalho excessivo na thread de UI.

---

## Como medir deltaY

O `VrScrollListener` repassa `dx/dy` para `VirtualView.RaiseScrolled`. Para logar:

```csharp
virtualizedList.Scrolled += (_, e) =>
    System.Diagnostics.Debug.WriteLine($"scrollY={e.VerticalOffset:F0}");
```

Um deltaY > 5000 px em um único frame indica estimativa de scroll incorreta — confirmar que `CachingLinearLayoutManager` está ativo (ItemSizingStrategy=Dynamic).

---

## Checklist antes de reportar regressão

- [ ] `ItemSizingStrategy` é `Dynamic` e `Span=1`? → `CachingLinearLayoutManager` ativo
- [ ] Log `VrHandler` aparece no Logcat após scrollar?
- [ ] `adb shell dumpsys gfxinfo` mostra Janky frames > 15%?
- [ ] Heap cresce linearmente ao adicionar itens ou estabiliza depois de ~50?
- [ ] `VrRecyclerListener` cancela CTSes? (adicionar log em `OnViewRecycled` para confirmar)
