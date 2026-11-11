# TUNING — Agile.Maui Performance Reference

This document describes every internal parameter that affects rendering speed, memory usage, and scroll smoothness in `VirtualizedCollectionView`, `GalleryView`, and `ImageView`. Values are derived from the actual handler source code, not estimates.

---

## VirtualizedCollectionView — Android

### ItemSizingStrategy

The property controls which `LayoutManager` is created and how cell heights are computed.

| `ItemSizingStrategy` | `ItemHeight` | Android behavior |
|---|---|---|
| `Fixed` | any | `LinearLayoutManager` or `GridLayoutManager`. Cells forced to `ItemHeightRequest`, or to `ItemHeight` when `> 0` (it wins in this mode too). Fastest path. |
| `Dynamic` | `≤ 0` (default `-1`) | `CachingLinearLayoutManager`. Cells use `WrapContent`; heights are measured from content. Only for `Span=1, Orientation=Vertical`. |
| `Dynamic` or `MeasureFirst` | `> 0` | Explicit `ItemHeight` wins: normal fixed-height layout. |
| `MeasureFirst` | `≤ 0` | `LinearLayoutManager`/`GridLayoutManager` (no caching manager). Cells start `WrapContent`; bound holders observe their layout (layout-change listener plus one `View.post`), and the first height `> 0` reported — so a list filled while hidden is measured once shown — is fixed for all holders (`LayoutParams` + relayout; new holders are created with it). Same scroll cost as `Fixed`; assumes uniform heights (taller items clip). |

> **Grid (`Span > 1`) always uses `GridLayoutManager`**, regardless of `ItemSizingStrategy`. In `Dynamic`, grid and horizontal lists still use `WrapContent` cells, but without `CachingLinearLayoutManager`: its cached scroll estimate is single-column, vertical only.

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
| Any other `Span=1` case (`Fixed`, `MeasureFirst`, horizontal, or `ItemHeight > 0`) | `LinearLayoutManager` | `6` |
| `Span > 1` | `GridLayoutManager` | `Span × 3` |

`InitialPrefetchItemCount` tells RecyclerView's `GapWorker` how many items to pre-inflate during idle frames. Higher values reduce visible inflate jank at the cost of more CPU on the first frame.

### CachingLinearLayoutManager

Replaces the default `LinearLayoutManager` in Dynamic mode to eliminate scroll bar jumping caused by incorrect height estimates.

**How it works:**

1. While a position has no cached height, `OnBindViewHolder` posts a `View.post { }` callback that reads the item's real pixel height after layout and stores it in a `SparseIntArray` cache indexed by adapter position; binds of already cached positions skip the post.
2. `GetEstimatedHeight(position)` returns the cached real height if known, then the running average of all measured items, then `ItemHeightRequest` as the last fallback.
3. `ComputeVerticalScrollRange` sums estimated heights for all items — O(n) on first call, then cached until the dataset or any height changes.
4. Incremental operations (`TryIncrementalAdd` / `TryIncrementalRemove` / `TryIncrementalMove`) call `InvalidateFrom(index)`, which drops the range cache and only the cached heights from the changed index onward (their indices shifted); an append at the end drops no heights, so infinite scroll keeps its cache. `TryIncrementalReplace` drops only the replaced range (`InvalidateRange`).
5. Full dataset replacement (`ReplaceAll`) calls `InvalidateCache()` to clear everything; so does a width change (rotation/resize) detected in `OnLayoutCompleted`.

**Invariant:** without this cache, `LinearLayoutManager.computeVerticalScrollOffset` uses a linear extrapolation from the first visible item, producing `deltaY` jumps of 10 000 – 20 000 px when item heights are heterogeneous.

**Bind generation per ViewHolder (Dynamic mode):** `OnBindViewHolder` increments the holder's bind generation (`NextBindGeneration`) on every bind. The `post { }` closure captures it and bails out (`IsCurrentBind`) if the holder has been rebound or recycled before the post fires. `VrRecyclerListener.OnViewRecycled` calls `CancelHeavyBind()`, which bumps the generation immediately when the holder enters the recycle pool, ensuring no stale height is written to a wrong position.

### RecyclerView View Cache and Pool

Sizes are calculated from `ActivityManager.MemoryInfo.TotalMem` each time the handler connects or `Span` changes. The decision is logged to Logcat:

```
D/VrHandler: RAM=4096MB cols=2 → cache=5 pool=12
```

**Base values (single column):**

| Total RAM | `ItemViewCacheSize` | `RecycledViewPool` max (item view type `0`) |
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

### Collection changes and full reload

`INotifyCollectionChanged` events are queued under a lock and drained in one flush posted through the UI `IDispatcher` captured at connect. Incremental operations (`Add`, `Remove`, `Replace`, `Move`) call `NotifyItemInserted` / `NotifyItemRangeInserted` / `NotifyItemRemoved` / `NotifyItemRangeRemoved` / `NotifyItemRangeChanged` / `NotifyItemMoved` directly. The item animator is disabled (`SetItemAnimator(null)`), so these updates are not animated.

A flush falls back to a full reload when it holds more than 30 events, mixed actions, a `Move` among several events, a `Reset`, or indices that do not match the adapter; replacing `ItemsSource` also reloads. The reload is synchronous on the UI thread: when `ItemTemplate`, `Header`, `Footer` and their templates are unchanged, the adapter's `ReplaceAll` swaps the item list and calls `NotifyDataSetChanged()`; otherwise a new adapter is created. There is no `DiffUtil`: a synchronous replacement never races with the incremental notifications.

### VrClippedRecyclerView

A `RecyclerView` subclass that clips its `DispatchDraw` canvas to its own bounds. This prevents item rows from visually leaking outside the RecyclerView area (for example, over a header or search bar) when the first visible item is partially scrolled off-screen.

### VrRecyclerListener

Registered via `RecyclerView.AddRecyclerListener`. On each `OnViewRecycled` call it:

1. Calls `CancelHeavyBind()`: bumps the holder's bind generation (any pending `post { }` height measurement is discarded) and cancels a pending `MeasureFirst` measurement. Header/footer holders stop here.
2. Sets `MauiView.BindingContext = null` — a pooled holder can come back for the same item and the bind skips an identical context, so without this a cleared image would never reload.
3. Walks the item view's descendants (up to 10 levels) and calls `Glide.With(context).Clear(view)` on each native `ImageView`, releasing in-flight Glide requests and freeing the bitmaps.

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

`MeasureFirst` flow: the cell reports its measured height via a callback (`VrDataSource.ReportFirstMeasure` → `OnFirstCellMeasured`); the handler stores it in `_measureFirstHeight` and rebuilds the compositional layout as `Absolute` (deferred via `BeginInvokeOnMainThread` to avoid reentrancy during the layout pass). `RefreshLayout` resets `_measureFirstHeight` so a layout/strategy/height change re-measures; `ReloadItems` does the same for a new source or template.

`ItemHeightRequest` is clamped to a minimum of 44 pt (`Math.Max(44, ItemHeightRequest)`), both as the estimated height and as the `Fixed` absolute height.

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

Events from `ObservableCollection` are queued in `_pendingChanges` and flushed in a single callback posted through the UI `IDispatcher` captured at connect. `IDispatcher.Dispatch` always queues; `MainThread.BeginInvokeOnMainThread` is deliberately not used here because it runs inline when already on the main thread, which would defeat the coalescing. Bursts of rapid changes are drained in one flush: more than 30 events (e.g., 500 × `Items.Add`) become a single `ReloadData`, while smaller uniform bursts get one `UICollectionView.PerformBatchUpdates` call per event (several events in one batch would mix UIKit's pre- and post-update index coordinates).

For pagination, prefer `ObservableRangeCollection<T>.AddRange`: it emits one native batch notification for the whole page instead of one notification per item. For search/filter refreshes, prefer `ReplaceAll`.

**Flush decision:**

| Condition | Action |
|---|---|
| `> 30 pending events` | `ReloadData` (full snapshot) |
| Mixed actions (Add + Remove in same batch) | `ReloadData` |
| Any `Move` action | `ReloadData` |
| ≤ 30 events, uniform action type, no moves, indices valid for the current snapshot | One `PerformBatchUpdates` per event, with native insert/delete/reload animations |

`Reset` action bypasses the queue: it discards the pending events and dispatches `ReloadItems()` (full snapshot).

### ScrollTo

`ScrollToItem` uses `UICollectionViewScrollPosition.Top` for vertical lists and `Left` for horizontal lists. For best results, ensure the collection layout has been applied before calling `ScrollTo` programmatically.

---

## VirtualizedCollectionView — Windows

No custom handler is needed on Windows. `VirtualizedCollectionView` inherits from `ContentView` and sets its `Content` to a MAUI `CollectionView` in its constructor (defined in the Windows-only partial `Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionView.Windows.cs`). The MAUI `ContentViewHandler` renders it normally.

MAUI's `CollectionView` on Windows uses WinUI's `ItemsRepeater` with virtualization enabled by default.

The Windows partial class also adds pointer **drag-to-scroll with inertia**: dragging the list body with the mouse (left button) scrolls it (4 px threshold before the drag starts), hooked to the rendered list's inner `ScrollViewer` (`ListViewBase`, MAUI ≤ 9) or `ScrollView` (`ItemsView`, MAUI 10 / WinAppSDK 1.4+).

| Property | Windows behavior |
|---|---|
| `ItemsSource` | Forwarded directly to `CollectionView.ItemsSource` |
| `ItemTemplate` | Forwarded to `CollectionView.ItemTemplate` |
| `Span` / `Orientation` | Mapped to `GridItemsLayout` (> 1) or `LinearItemsLayout`, in the matching orientation |
| `ItemSpacing` | Mapped to `HorizontalItemSpacing` / `VerticalItemSpacing` / `ItemSpacing` |
| `ItemHeight` | **Ignored.** Windows delegates sizing to the MAUI layout engine. |
| `ItemSizingStrategy` | Mapped to MAUI `CollectionView.ItemSizingStrategy`: `Dynamic` → `MeasureAllItems`; `Fixed` and `MeasureFirst` → `MeasureFirstItem` |
| `ItemHeightRequest` | **Ignored** on Windows. MAUI `CollectionView` has no estimated item height hint. |
| `ItemWidthRequest` | **Ignored** on Windows. |
| `EmptyView` / `EmptyViewTemplate` | Forwarded to `CollectionView` |
| `Header` / `Footer` (and templates), `VerticalScrollBarVisibility` / `HorizontalScrollBarVisibility` | Forwarded to `CollectionView` |
| `RemainingItemsThreshold` | Forwarded; fired by MAUI `CollectionView.RemainingItemsThresholdReached` |

---

## GalleryView — ThumbMaxPx

| Property | Type | Default | Minimum |
|---|---|---|---|
| `ThumbMaxPx` | `int` | `720` | `64` |

**Android:** passed to Glide as `RequestOptions.Override(width, height)` with the pager's measured size in pixels (updated on rotation/resize); `ThumbMaxPx` is used for both dimensions only while the pager has no size yet. Glide never decodes into the original resolution.

**iOS/MacCatalyst:** thumbnails are downloaded with `NSUrlSession` and decoded through `AppleImageCache` at the page's bounds times the screen scale; `ThumbMaxPx` is the maximum pixel size used before the page has been laid out.

**Windows:** thumbnails are loaded via `BitmapImage` with `DecodePixelWidth = ThumbMaxPx` only (also setting `DecodePixelHeight` would discard the aspect ratio).

**When to change it:**

- Gallery with many high-resolution photos and low-end devices → lower to `480` or `360` (on Android/iOS/Mac this only bounds decodes made before layout).
- Gallery with already-small images (< 720 px wide) → leave at default; it does not upscale.
- Use with `ShowIndicator=True` and many images: the indicator dots are small and cheap; the decode size is the only meaningful cost.

---

## ImageView - DecodeMaxPx

| Property | Type | Default | Minimum |
|---|---|---|---|
| `DecodeMaxPx` | `int` | `720` | `64` |

`DecodeMaxPx` controls the bitmap decode limit for a single `ImageView`.
It does not change the visual size of the view; layout still comes from MAUI
properties such as `HeightRequest`, `WidthRequest`, grid constraints, and
`AspectMode`.

**Android:** passed to Glide as `RequestOptions.Override(DecodeMaxPx, DecodeMaxPx)`.
Glide may still serve a cached transformed resource. When testing quality
changes, clear the app/cache or change the image URL/name.

**iOS/MacCatalyst:** local and remote sources are decoded through
`AppleImageCache` using `DecodeMaxPx` or the measured target size when available.

**Windows:** mapped to `BitmapImage.DecodePixelWidth` only (also setting `DecodePixelHeight` would discard the aspect ratio).

**When to change it:**

- Product/list thumbnails: use `128`, `192`, or `256` depending on rendered size.
- Larger detail thumbnails: use `512` or `720`.
- Fullscreen/detail quality: keep the thumbnail small and set `FullscreenSource`
  to a higher-quality source.
- Values below `64` are rejected/clamped and should only be considered while
  temporarily debugging the component internals.

`ImageView.IsLoading` is read-only and is updated by each platform handler. It is
safe to bind to indicators or behaviors, but application code should not try to
set it.

---

## Images inside VirtualizedCollectionView cells

`VirtualizedCollectionView` renders arbitrary MAUI `DataTemplate` content. The list handler has no control over image decoding inside templates. These rules apply regardless of platform:

| Recommendation | Reason |
|---|---|
| Use `<agile:ImageView>` in cell templates | Bounded decode, cancellation/reuse behavior, and platform cache are handled by the component |
| Set `DecodeMaxPx` close to the rendered thumbnail size | Avoids decoding full-resolution images for small cards |
| Use server-side thumbnail URLs | Eliminates decoding full-resolution bitmaps in memory |
| Set a fixed `ItemHeight` matching the image aspect ratio | UICollectionView / RecyclerView skips re-measurement on decode |
| Avoid MAUI `<Image>` for remote URLs in large lists | No bounded decode size; no per-cell cancellation |

**iOS cell image cancellation:** `VrMauiCell.PrepareForReuse` only resets the cell's measurement state. When the reused cell is bound to a new item, the new `BindingContext` changes the `ImageView.Source` and `ImageViewHandler` cancels the previous load's `CancellationTokenSource`, which aborts the `NSUrlSession` download. When the cell's template changes or the cell is disposed, its view gets `BindingContext = null` and `DisconnectHandlers()`, which cancels a pending download too.

---

Note for image-heavy screens: for galleries use `GalleryView.ThumbMaxPx`; for
single images inside list/card templates use `ImageView.DecodeMaxPx`. Both have
a minimum of `64` and should be paired with server-side thumbnails when possible.

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
│   │             Span=1 (vertical) keeps the cached scroll estimate.
│   └─ iOS/Mac:  Dynamic + ItemHeightRequest = <average expected height>
│                 Works with any Span.
│
└─ Windows:
    ItemHeight / ItemHeightRequest are ignored; ItemSizingStrategy maps to MAUI CollectionView sizing.
    MAUI CollectionView handles sizing automatically.
```

```
How do I reduce memory in a large gallery or image-heavy list?
Current rule: lower GalleryView.ThumbMaxPx for galleries, or ImageView.DecodeMaxPx for single images in cards/lists.
│
├─ Lower GalleryView.ThumbMaxPx or ImageView.DecodeMaxPx
├─ Serve pre-resized thumbnails from server (all platforms)
└─ Avoid MAUI <Image> for remote URLs in cell templates
```
