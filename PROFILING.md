# PROFILING — VirtualizedCollectionView (Android)

Benchmark reference and instructions for measuring performance on Android.

---

## Measured baselines (device: Pixel 6, Android 14)

| Metric | Before | After |
|---|---|---|
| Slowest frame (Davey) | 2170 ms | 758 ms |
| Maximum visible deltaY | 19,328 px | 1,002 px |
| Average heap at idle (50 items) | ~18 MB | ~11 MB |
| Unnecessary rebinds while scrolling | frequent | eliminated (ViewCache) |

**Before**: default `LinearLayoutManager` with linear scroll estimates.  
**After**: `CachingLinearLayoutManager` with progressive caching of actual heights.

---

## Measurement tools

### Android Studio Profiler

1. `Run > Profile 'app'`
2. **CPU** tab → record with **System Trace** while scrolling the list
3. Look for `RecyclerView#onMeasure`, `inflate`, `bind`, and `draw` on the UI threads

### `adb shell dumpsys gfxinfo`

```bash
adb shell dumpsys gfxinfo <package> framestats
```

Shows a frame histogram, Janky frames %, and the Davey frame count (> 700 ms).

### Logcat — cache heuristic

```bash
adb logcat -s VrHandler
```

Prints the cache/pool decision on every reconfiguration:
```
D/VrHandler: RAM=4096MB cols=1 → cache=5 pool=12
```

### Logcat — slow frames (MAUI)

```bash
adb logcat -s Choreographer
```

`Skipped N frames!` lines indicate excessive work on the UI thread.

---

## How to measure deltaY

The `VrScrollListener` forwards `dx/dy` to `VirtualView.RaiseScrolled`. To log it:

```csharp
virtualizedList.Scrolled += (_, e) =>
    System.Diagnostics.Debug.WriteLine($"scrollY={e.VerticalOffset:F0}");
```

A deltaY > 5000 px in a single frame indicates an incorrect scroll estimate — confirm that `CachingLinearLayoutManager` is active (ItemSizingStrategy=Dynamic).

---

## Checklist before reporting a regression

- [ ] Is `ItemSizingStrategy` set to `Dynamic` with `Span=1`? → `CachingLinearLayoutManager` active
- [ ] Does the `VrHandler` log appear in Logcat after scrolling?
- [ ] Does `adb shell dumpsys gfxinfo` show Janky frames > 15%?
- [ ] Does the heap grow linearly as items are added, or does it stabilize after ~50?
- [ ] Does `VrRecyclerListener` cancel the CTSes? (add a log in `OnViewRecycled` to confirm)
