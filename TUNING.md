# TUNING — Agile.Maui Performance Reference

This document describes every internal parameter that affects rendering speed, memory usage, and scroll smoothness in `VirtualizedCollectionView`, `GalleryView`, and `ImageView`. Values are derived from the actual handler source code, not estimates.

---

## VirtualizedCollectionView — Android

### ItemSizingStrategy

The property controls which `LayoutManager` is created and how cell heights are computed.

| `ItemSizingStrategy` | `ItemHeight` | Android behavior |
|---|---|---|
| `Fixed` | any | `LinearLayoutManager` or `GridLayoutManager`. Cells forced to `ItemHeightRequest` (Android ignores `ItemHeight` in this mode). Fastest path. |
| `Dynamic` | `≤ 0` (default `-1`) | `CachingLinearLayoutManager`. Cells use `WrapContent`; heights are measured from content. Only for `Span=1, Orientation=Vertical`. |
| `Dynamic` | `> 0` | Explicit `ItemHeight` wins: normal fixed-height layout. |
| `MeasureFirst` | any | `LinearLayoutManager`/`GridLayoutManager` (no caching manager). Cells start `WrapContent`; the first bound item is measured (`View.post`), its height is fixed for all via `SetFixedHeight` + relayout. Same scroll cost as `Fixed`; assumes uniform heights (taller items clip). |

> **Grid (`Span > 1`) always uses `GridLayoutManager` with fixed sizing**, regardless of `ItemSizingStrategy`. Dynamic self-sizing is not supported in multi-column mode on Android.

```xml
<!-- Fixed: all cells exactly 120 dp tall, zero per-item measurement -->
<agile:VirtualizedCollectionView
    ItemSizingStrategy="Fixed"
    ItemHeightRequest="120" />

<!-- Dynamic: cells wrap content, CachingLinearLayoutManager active -->
<agile:VirtualizedCollectionView
    ItemSizingStrategy="Dynamic"
    ItemHeightRequest="200" />

<!-- MeasureFirst: first item measured, its height applied to all (uniform items) -->
<agile:VirtualizedCollectionView
    ItemSizingStrategy="MeasureFirst"
    ItemHeightRequest="200" />
```

### Layout Managers

Three managers are used, selected automatically:

| Condition | Manager | `InitialPrefetchItemCount` |
|---|---|---|
| `Dynamic`, `Span=1`, vertical, `ItemHeight <= 0` | `CachingLinearLayoutManager` | `4` |
| `Fixed` or `MeasureFirst`, `Span=1` | `LinearLayoutManager` | `6` |
| `Span > 1` | `GridLayoutManager` | `Span × 3` |

`InitialPrefetchItemCount` tells RecyclerView's `GapWorker` how many items to pre-inflate during idle frames. Higher values reduce visible inflate jank at the cost of more CPU on the first frame.

### CachingLinearLayoutManager

Replaces the default `LinearLayoutManager` in Dynamic mode to eliminate scroll bar jumping caused by incorrect height estimates.

**How it works:**

1. Each `OnBindViewHolder` posts a `View.post { }` callback that reads the item's real pixel height after layout and stores it in a `SparseIntArray` cache indexed by adapter position.
2. `GetEstimatedHeight(position)` returns the cached real height if known, then the running average of all measured items, then `ItemHeightRequest` as the last fallback.
3. `ComputeVerticalScrollRange` sums estimated heights for all items — O(n) on first call, then cached until the dataset or any height changes.
4. Incremental operations (`IncrementalAdd` / `IncrementalRemove`) call `InvalidateScrollRange()` to clear only the total range cache while preserving per-item heights.
5. Full dataset replacement (`UpdateItemsAsync`) calls `InvalidateCache()` to clear everything.

**Invariant:** without this cache, `LinearLayoutManager.computeVerticalScrollOffset` uses a linear extrapolation from the first visible item, producing `deltaY` jumps of 10 000 – 20 000 px when item heights are heterogeneous.

**CTS per ViewHolder (Dynamic mode):** `OnBindViewHolder` creates a new `CancellationTokenSource` per bind. The `post { }` closure captures the token and bails out if the holder has been recycled before the post fires. `VrRecyclerListener.OnViewRecycled` cancels the token immediately when the holder enters the recycle pool, ensuring no stale height is written to a wrong position.

### RecyclerView View Cache and Pool

Sizes are calculated from `ActivityManager.MemoryInfo.TotalMem` each time the handler connects or `Span` changes. The decision is logged to Logcat:

```
D/VrHandler: RAM=4096MB cols=2 → cache=5 pool=12
```

**Base values (single column):**

| Total RAM | `ItemViewCacheSize` | `RecycledViewPool` max (per viewType) |
|---|---|---|
| ≥ 6 GB | 8 | 20 |
| ≥ 3 GB | 5 | 12 |
| ≥ 1.5 GB | 3 | 8 |
| < 1.5 GB | 2 | 5 |

**Grid scaling (`Span > 1`):** both values are multiplied by `Span / 2`.

Example: 3-column grid on a 4 GB device → `cache = 5 × 3/2 = 7`, `pool = 12 × 3/2 = 18`.

**What each controls:**

- **`ItemViewCacheSize`** (`SetItemViewCacheSize`): off-screen views stay *bound* (no rebind needed) until this limit is exceeded. Increasing it reduces rebinds when scrolling back up, at the cost of keeping more MAUI Views alive in memory.
- **`RecycledViewPool`** (`SetMaxRecycledViews`): unbound views waiting for reuse. Increasing the pool reduces `OnCreateViewHolder` inflation; decreasing it shrinks the heap but causes more allocations during fast scroll.

### Async DiffUtil

When `ItemsSource` is replaced entirely (or `NotifyCollectionChanged.Reset` fires), `DiffUtil.CalculateDiff` runs on a `Task.Run` thread pool thread. The result is dispatched back to the UI thread via `result.DispatchUpdatesTo(adapter)`, animating individual insertions and removals.

A `CancellationTokenSource` (`_diffCts`) ensures that if a new diff is requested before the previous one finishes, the previous task is cancelled and the result discarded — preventing stale animations.

Incremental operations (`Add`, `Remove`, `Replace`, `Move`) bypass DiffUtil entirely and call `NotifyItemInserted` / `NotifyItemRemoved` / etc. directly.

### ClippedRecyclerView

A `RecyclerView` subclass that clips its `DispatchDraw` canvas to its own bounds. This prevents item rows from visually leaking outside the RecyclerView area (for example, over a header or search bar) when the first visible item is partially scrolled off-screen.

### VrRecyclerListener

Registered via `RecyclerView.AddRecyclerListener`. On each `OnViewRecycled` call it:

1. Cancels and disposes the `BindCts` (stops any pending `post { }` height measurement).
2. Calls `Glide.With(context).Clear(itemView)` to release any in-flight Glide request and free the bitmap from the view.
3. Sets `MauiView.BindingContext = null` to disconnect the data binding before the holder re-enters the pool.

### HasFixedSize

`PlatformView.Rv.HasFixedSize = true` is always set in `ApplyLayoutManager`. This tells RecyclerView it does not need to re-measure itself when the adapter data changes — the list area is a fixed size inside the MAUI layout.

---

## VirtualizedCollectionView — iOS / macOS Catalyst

### ItemSizingStrategy and CompositionalLayout

Every layout change rebuilds a `UICollectionViewCompositionalLayout`. The dimension used for item height is chosen as follows:

| Condition | Dimension | Cell behavior |
|---|---|---|
| `ItemHeight > 0` | `CreateAbsolute(ItemHeight)` | Fixed height, overrides `ItemSizingStrategy` |
| `ItemHeight ≤ 0` and `ItemSizingStrategy = Fixed` | `CreateAbsolute(ItemHeightRequest)` | Fixed height from the request hint |
| `ItemHeight ≤ 0` and `ItemSizingStrategy = Dynamic` | `CreateEstimated(ItemHeightRequest)` | Self-sizing via `PreferredLayoutAttributesFitting` |
| `ItemHeight ≤ 0` and `ItemSizingStrategy = MeasureFirst`, before measuring | `CreateEstimated(ItemHeightRequest)` | Self-sizing; the first measured cell reports its height back to the handler |
| `ItemHeight ≤ 0` and `ItemSizingStrategy = MeasureFirst`, after measuring | `CreateAbsolute(measuredHeight)` | Layout rebuilt with the first cell's height for all items |

`MeasureFirst` flow: the cell reports its measured height via a callback (`VrDataSource.ReportFirstMeasure` → `OnFirstCellMeasured`); the handler stores it in `_measureFirstHeight` and rebuilds the compositional layout as `Absolute` (deferred via `BeginInvokeOnMainThread` to avoid reentrancy during the layout pass). `RefreshLayout` resets `_measureFirstHeight` so a layout/strategy/height change re-measures.

The estimated height is clamped to a minimum of 44 pt (`Math.Max(44, ItemHeightRequest)`).

> **Important:** `ItemHeight > 0` always wins over `ItemSizingStrategy` on iOS, just as on Android. Set `ItemHeight = -1` (the default) to let `ItemSizingStrategy` control sizing.

### Self-Sizing in Dynamic Mode

When `CreateEstimated` is active, UIKit calls `PreferredLayoutAttributesFitting` on each `VrMauiCell` to get the actual cell height. The implementation uses MAUI's own measure pass — not Auto Layout — because MAUI views do not expose `IntrinsicContentSize`:

```csharp
var measured = ((IView)_mauiView).Measure(width, double.PositiveInfinity);
var height   = Math.Max(1, measured.Height);
```

Using `SystemLayoutSizeFittingSize` instead would return `height = 0`, making cells invisible.

**`_layoutStabilized` guard:** the first call to `PreferredLayoutAttributesFitting` sets `_layoutStabilized = true`. Only after this point does the cell react to `MeasureInvalidated` (for example, when an expander opens). Before stabilization, `MeasureInvalidated` is silently ignored to prevent an infinite loop: `BindingContext set → MeasureInvalidated → InvalidateLayout → new cell → BindingContext set → …`

**`ItemHeightRequest` as the estimated height:** UIKit uses this value to allocate the initial cell pool. Set it as close to the real average height as possible.

> If `ItemHeightRequest` is too small (e.g., `44`), UIKit estimates that many more cells fit on screen than actually do, and creates an oversized reuse pool. Example: 44 pt estimate on a 900 pt screen → 20 estimated visible cells × pool factor 2 = 40 MAUI Views created upfront. At ~12 MB each, that's ~480 MB before any scroll.

### PrefetchingEnabled = false

`platformView.PrefetchingEnabled = false` is set unconditionally in `ConnectHandler`. iOS prefetching pre-creates cells outside the visible area, which multiplies the number of live MAUI Views in memory by the prefetch lookahead distance. Disabling it limits the active pool to the cells actually visible on screen.

### INotifyCollectionChanged — Coalescing and Batch Updates

Events from `ObservableCollection` are queued in `_pendingChanges` and flushed in a single `MainThread.BeginInvokeOnMainThread` callback, coalescing bursts of rapid changes (e.g., 500 × `Items.Add`) into one `UICollectionView.PerformBatchUpdates` call.

**Flush decision:**

| Condition | Action |
|---|---|
| `> 30 pending events` | `ReloadData` (full snapshot) |
| Mixed actions (Add + Remove in same batch) | `ReloadData` |
| Any `Move` action | `ReloadData` |
| ≤ 30 events, uniform action type, no moves | `PerformBatchUpdates` with native insert/delete/reload animations |

`Reset` action bypasses the queue entirely and calls `ReloadItems()` directly.

### ScrollTo

`ScrollToItem` uses `UICollectionViewScrollPosition.Top`. For best results, ensure the collection layout has been applied before calling `ScrollTo` programmatically.

---

## VirtualizedCollectionView — Windows

No custom handler is needed on Windows. `VirtualizedCollectionView` inherits from `ContentView` and sets its `Content` to a MAUI `CollectionView` in its constructor (guarded by `#if WINDOWS`). The MAUI `ContentViewHandler` renders it normally.

MAUI's `CollectionView` on Windows uses WinUI's `ItemsRepeater` with virtualization enabled by default.

| Property | Windows behavior |
|---|---|
| `ItemsSource` | Forwarded directly to `CollectionView.ItemsSource` |
| `ItemTemplate` | Forwarded to `CollectionView.ItemTemplate` |
| `Span` | Mapped to `GridItemsLayout` (> 1) or `LinearItemsLayout` |
| `ItemSpacing` | Mapped to `HorizontalItemSpacing` / `VerticalItemSpacing` / `ItemSpacing` |
| `ItemHeight` | **Ignored.** Windows delegates sizing to the MAUI layout engine. |
| `ItemSizingStrategy` | Mapped to MAUI `CollectionView.ItemSizingStrategy`: `Dynamic` → `MeasureAllItems`; `Fixed` and `MeasureFirst` → `MeasureFirstItem` |
| `ItemHeightRequest` | **Ignored** on Windows. MAUI `CollectionView` has no estimated item height hint. |
| `EmptyView` / `EmptyViewTemplate` | Forwarded to `CollectionView` |
| `RemainingItemsThreshold` | Forwarded; fired by MAUI `CollectionView.RemainingItemsThresholdReached` |

---

## GalleryView — ThumbMaxPx

| Property | Type | Default | Minimum |
|---|---|---|---|
| `ThumbMaxPx` | `int` | `720` | `64` |

**Android:** passed to Glide as `RequestOptions.Override(ThumbMaxPx, ThumbMaxPx)`. Glide decodes the bitmap at this pixel dimension, never into the original resolution. Lowering `ThumbMaxPx` reduces peak memory per thumbnail at the cost of visual quality for very large images.

**iOS:** thumbnails are downloaded full-size via `NSUrlSession`. `ThumbMaxPx` does not limit the decode on iOS — use server-side resizing to bound memory usage.

**Windows:** thumbnails are loaded via `BitmapImage`. `ThumbMaxPx` has no effect on Windows.

**When to change it:**

- Gallery with many high-resolution photos and low-end devices → lower to `480` or `360`.
- Gallery with already-small images (< 720 px wide) → leave at default; it does not upscale.
- Use with `ShowIndicator=True` and many images: the indicator dots are small and cheap; the decode size is the only meaningful cost.

---

## Images inside VirtualizedCollectionView cells

`VirtualizedCollectionView` renders arbitrary MAUI `DataTemplate` content. The list handler has no control over image decoding inside templates. These rules apply regardless of platform:

| Recommendation | Reason |
|---|---|
| Use `<agile:ImageView>` in cell templates (Android) | Glide applies `RequestOptions.Override(ThumbMaxPx, ThumbMaxPx)` automatically |
| Use server-side thumbnail URLs | Eliminates decoding full-resolution bitmaps in memory |
| Set a fixed `ItemHeight` matching the image aspect ratio | UICollectionView / RecyclerView skips re-measurement on decode |
| Avoid MAUI `<Image>` for remote URLs in large lists | No bounded decode size; no per-cell cancellation |

**iOS cell image cancellation:** `VrMauiCell.PrepareForReuse` sets `_mauiView.BindingContext = null`, which triggers `DisconnectHandler` on any MAUI handler inside the template — including `ImageViewHandler`, which cancels its `CancellationTokenSource` and aborts the `NSUrlSession` download.

---

## Quick decision guide

```
Which ItemSizingStrategy should I use?
│
├─ All items same height, and you know it?
│   └─ Fixed + ItemHeightRequest = <that height>
│       Best performance on all platforms.
│
├─ All items same height, but you'd rather not hardcode it?
│   └─ MeasureFirst + ItemHeightRequest = <estimate>
│       First item is measured and its height applied to all.
│       Same scroll cost as Fixed. Taller items would clip.
│
├─ Items have variable height (text wrapping, expandable sections)?
│   ├─ Android:  Dynamic + ItemHeightRequest = <average expected height>
│   │             Span must be 1.
│   └─ iOS/Mac:  Dynamic + ItemHeightRequest = <average expected height>
│                 Works with any Span.
│
└─ Windows:
    ItemHeight / ItemHeightRequest are ignored; ItemSizingStrategy maps to MAUI CollectionView sizing.
    MAUI CollectionView handles sizing automatically.
```

```
How do I reduce memory in a large gallery?
│
├─ Lower ThumbMaxPx (Android only — Glide decode bound)
├─ Serve pre-resized thumbnails from server (all platforms)
└─ Avoid MAUI <Image> for remote URLs in cell templates
```
