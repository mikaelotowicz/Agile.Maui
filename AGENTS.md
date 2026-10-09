# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

**Agile.Maui** é uma biblioteca **modular** de componentes .NET MAUI: cada componente vive em seu próprio projeto/pacote com versão própria definida no respectivo `.csproj` (todos compartilham o namespace `Agile.Maui`), targeting Android, iOS, macOS Catalyst e Windows. Componentes: `ImageView`/`GalleryView` (`Agile.Maui.Gallery`), `PdfViewer`/`PdfReaderView` (`Agile.Maui.Pdf`), `VirtualizedCollectionView` (`Agile.Maui.VirtualizedCollection`), `ChipGroup` (`Agile.Maui.ChipGroup`), `SignaturePad` (`Agile.Maui.SignaturePad`) e o gerador de PDF `Agile.Maui.PdfGen`.

## Build Commands

```powershell
dotnet build
dotnet build -f net10.0-android
dotnet build -f net10.0-ios
dotnet build -f net11.0-maccatalyst
dotnet build -f net10.0-windows10.0.19041.0
```

## Testes

Cada componente tem um projeto de testes de host (xunit, TFM neutro `net10.0`, sem plataforma):
`ChipGroup.Tests`, `GalleryView.Tests`, `PDFViewer.Tests`, `PdfGen.Tests`, `SignaturePad.Tests`
e `VirtualizedCollectionView.Tests`.

```powershell
dotnet test ChipGroup.Tests    # idem para os demais <Projeto>.Tests
```

Convenções:
- Namespaces `Agile.Maui.XTests` (ex.: `Agile.Maui.ChipGroupTests`) — evita colisão com o tipo do controle.
- `TestDispatcher` instalado via `[ModuleInitializer]`: executa inline por padrão; `EnqueueMode` +
  `ProcessQueue()` testam coalescing e threading de forma determinística (sem sleeps).
- As libs têm `InternalsVisibleTo` para o respectivo projeto de testes.
- `PdfGen.Tests` usa o `PdfMiniParser` para validar a estrutura dos PDFs gerados.
- **Todo fix de bug deve ganhar um teste de regressão no projeto de testes do componente.**

## Architecture

### Registro do controle

Consumidores registram no `MauiProgram.cs`:

```csharp
builder.UseAgileGalleryView();
```

`GalleryViewAppBuilderExtensions.cs` usa `#if ANDROID / #if IOS || MACCATALYST / #if WINDOWS` para registrar o handler correto em cada plataforma.

### Handler Pattern

- **`GalleryView/ImageView.cs`** — `View` cross-platform com 6 bindable properties (`Source`, `IsUrl`, `Placeholder`, `MaxZoom`, `EnableFullscreen`, `AspectMode`) e eventos `ImageLoaded`/`ImageFailed`. Enum `ZoomImageAspect`: `CenterCrop` e `AspectFit`.
- **`GalleryView/Platforms/Android/ImageView/ImageViewHandler.cs`** — Mapeia para `Android.Widget.ImageView` + Glide (cache disco+memória).
- **`GalleryView/Platforms/iOS/ImageView/ImageViewHandler.cs`** — Mapeia para `UIImageView` + `NSUrlSession` para URLs.
- **`GalleryView/Platforms/Windows/ImageView/ImageViewHandler.cs`** — Mapeia para `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`. Sem fullscreen zoom.
- **`GalleryView/Platforms/MacCatalyst/ImageView/`** — Cópia dos arquivos de iOS (ver seção abaixo).

> `ImageView` possui `DecodeMaxPx` e `IsLoading` read-only. `IsUrl` existe
> apenas por compatibilidade; HTTP/HTTPS e detectado automaticamente.

### MacCatalyst duplica iOS

O SDK do MAUI **não** compila `Platforms/iOS/` para maccatalyst: cada componente mantém uma **cópia** dos arquivos em `Platforms/MacCatalyst/` (mesmo namespace `Agile.Maui.Platforms.iOS`). **Todo fix feito em `Platforms/iOS/` deve ser replicado em `Platforms/MacCatalyst/`.** Os `*AppBuilderExtensions` usam `#if IOS || MACCATALYST` para registrar o mesmo handler.

### Zoom fullscreen Android — Matrix nativo

O `FullscreenZoomDialogFragment` implementa zoom completo **sem dependências externas**, usando apenas APIs nativas do Android:

| Funcionalidade | Implementação |
|---|---|
| Pinch-to-zoom | `ScaleGestureDetector` → `Matrix.PostScale()` |
| Pan quando ampliado | `MotionEvent.ActionMove` → `Matrix.PostTranslate()` |
| Double-tap zoom | `GestureDetector.SimpleOnGestureListener.OnDoubleTap` |
| Single-tap dismiss | `GestureDetector.OnSingleTapConfirmed` |
| Limites de zoom | `Math.Clamp` + `ConstrainMatrix()` |
| Animação suave | `ValueAnimator.OfFloat` com lerp de matrix |

A class `ZoomTouchHandler` (file-scoped) encapsula toda a lógica. Ela implementa `View.IOnTouchListener` e `ScaleGestureDetector.IOnScaleGestureListener`. A matrix é uma array `float[9]` onde os índices relevantes são: `[0]`=ScaleX, `[2]`=TransX, `[4]`=ScaleY, `[5]`=TransY.

`InitMatrix()` é chamado via `Post()` após o Glide carregar a imagem, garantindo que a view já foi layoutada antes de calcular a escala fit-center.

### Zoom fullscreen iOS/MacCatalyst

`FullscreenZoomViewController` usa `UIScrollView` com `IUIScrollViewDelegate` para zoom nativo (pinch built-in do iOS), `UITapGestureRecognizer` para double-tap e single-tap.

### Eventos

```csharp
imageView.ImageLoaded += (s, e) => { };
imageView.ImageFailed += (s, e) => { };
```

Internamente: `VirtualView?.RaiseImageLoaded()` / `VirtualView?.RaiseImageFailed()`.

### VirtualizedCollectionView — iOS/MacCatalyst

**Handler:** `ViewHandler<VirtualizedCollectionView, UICollectionView>`

| Componente | Responsabilidade |
|---|---|
| `UICollectionViewCompositionalLayout` | Sizing por coluna com `CreateAbsolute` (fixo) ou `CreateEstimated(ItemHeightRequest)` (self-sizing) |
| `VrMauiCell : UICollectionViewCell` | Cria/reutiliza MAUI View lazy; frame-based layout via `AutoresizingMask` |
| `VrDataSource` | Snapshot dos items; batch updates via `PerformBatchUpdates` |
| `VrCollectionDelegate` | Scroll events + threshold checking |

**Regras críticas para self-sizing (iOS):**
- `PreferredLayoutAttributesFitting` usa **`((IView)_mauiView).Measure(width, ∞)`** — NÃO `SystemLayoutSizeFittingSize`. MAUI views não expõem `IntrinsicContentSize` para Auto Layout; usar Auto Layout retorna `height=0` e torna células invisíveis.
- `LayoutSubviews` chama **`((IView)_mauiView).Arrange(bounds)`** explicitamente para garantir que o sistema de layout do MAUI posicione os filhos com o frame final.
- `_nativeView` usa **frame-based layout** (`AutoresizingMask.FlexibleWidth | FlexibleHeight`) — NÃO Auto Layout constraints — para evitar conflito entre NSLayoutConstraint e MAUI Arrange.

**Regras de memória (iOS):**
- `PrefetchingEnabled = false` — desabilita criação antecipada de células fora da tela.
- `CreateEstimated` deve usar `ItemHeightRequest` (default 350pt), não um valor pequeno. Com 44pt, o UIKit cria ~20 células visíveis estimadas × pool 2× = 40 MAUI views × ~12 MB = 480 MB.
- MacCatalyst tem cópia idêntica do handler iOS em `Platforms/MacCatalyst/` (mesmo namespace `Agile.Maui.Platforms.iOS`).

### VirtualizedCollectionView — Android (teardown)

**Ordem crítica no `DisconnectHandler`:** chamar `Rv.SetAdapter(null)` / `Rv.SetLayoutManager(null)` **ANTES** de `_adapter?.Dispose()`. `SetAdapter(null)` recicla as views e ainda invoca callbacks no adapter/listener; fazer o `Dispose` antes mata o peer gerenciado e o runtime tenta reativá-lo a partir do handle nativo → `NotSupportedException ("Unable to activate instance ... from native handle")`. O `RecyclerListener` também é removido (`RemoveRecyclerListener`) antes do dispose.

**`VrRecyclerListener` precisa do construtor de ativação** `(IntPtr handle, JniHandleOwnership transfer)` e de `Context?` anulável: se o peer gerenciado for coletado enquanto o Java ainda referencia o listener, o runtime o reativa por esse construtor — sem ele, `NotSupportedException`. `OnViewRecycled` faz guarda de nulo antes de `Glide.With(_context).Clear(...)`.

### Patterns importantes

- Propriedades são `BindableProperty` para XAML binding.
- Helpers internos usam `file sealed class` (file-scoped).
- iOS handler remove o gesture recognizer ANTES de cancelar o CTS no disconnect.
- Android: `Glide.With(PlatformView).Clear()` chamado antes de cada early return em `LoadImage()`.

## Dependencies

| Package | Platform | Purpose |
|---|---|---|
| `Bumptech.Glide` | Android | Carregamento de imagens com cache |
| `AndroidX.Fragment.App` | Android | DialogFragment |
| `Microsoft.Maui.Controls` | All | MAUI framework |
