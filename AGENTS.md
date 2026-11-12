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
dotnet build -f net11.0-windows10.0.26100.0
```

Todas as bibliotecas compilam para .NET 10 e .NET 11 (`net10.0*` e `net11.0*`) nos dois canais
de pacote; `AgilePackageChannel` (`Stable`/`Preview`) só muda o sufixo da versão.

## Testes

Cada componente tem um projeto de testes de host (xunit, TFMs neutros `net10.0` e `net11.0`, sem plataforma):
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

Device tests (handlers numa página real, no emulador Android e no Windows) ficam em `DeviceTests/`:
`.\run-android.ps1` / `.\run-windows.ps1`, com `-Dotnet net10|net11` — ver `DeviceTests/README.md`.

## Architecture

### Registro do controle

Consumidores registram no `MauiProgram.cs`:

```csharp
builder.UseAgileGalleryView();
```

`GalleryViewAppBuilderExtensions.cs` usa `#if ANDROID / #if IOS || MACCATALYST / #if WINDOWS` para registrar o handler correto em cada plataforma.

### Handler Pattern

- **`GalleryView/ImageView.cs`** — `View` cross-platform com as bindable properties `Source`, `IsUrl`, `Placeholder`, `MaxZoom`, `EnableFullscreen`, `FullscreenSource`, `AspectMode`, `DecodeMaxPx`, `ImageLoadedCommand`/`ImageFailedCommand` e `IsLoading` (read-only), e eventos `ImageLoaded`/`ImageFailed`. Enum `ZoomImageAspect`: `CenterCrop` e `AspectFit`.
- **`GalleryView/Platforms/Android/ImageView/ImageViewHandler.cs`** — Mapeia para `Android.Widget.ImageView` + Glide (cache disco+memória).
- **`GalleryView/Platforms/iOS/ImageView/ImageViewHandler.cs`** — Mapeia para `UIImageView` + `NSUrlSession` para URLs.
- **`GalleryView/Platforms/Windows/ImageView/ImageViewHandler.cs`** — Mapeia para `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`. Sem fullscreen zoom.
- **`GalleryView/Platforms/MacCatalyst/ImageView/`** — Cópia dos arquivos de iOS (ver seção abaixo).

> `IsLoading` é read-only (o handler o atualiza). `IsUrl` existe apenas por
> compatibilidade; HTTP/HTTPS é detectado automaticamente.

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

A classe `ZoomTouchHandler` (`internal sealed`, no mesmo arquivo; não pode ser `file` porque implementa interfaces Java — o gerador de JCW falha com XAJCW7024) encapsula toda a lógica. Ela implementa `View.IOnTouchListener` e `ScaleGestureDetector.IOnScaleGestureListener`. A matrix é uma array `float[9]` onde os índices relevantes são: `[0]`=ScaleX, `[2]`=TransX, `[4]`=ScaleY, `[5]`=TransY.

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
- O conteúdo do template da `VrMauiCell` é filho lógico da lista **só enquanto a célula está na janela** (`CellLogicalParent`, chamado no fim do `Bind`, no `MovedToWindow` e no `LiberarView`). Sem pai, o MAUI não acha a página a partir de um campo do card (`HideSoftInputOnTapped` não fechava o teclado); com pai permanente, o ciclo célula → lista → célula não é coletado e a lista vaza. Header/Footer passados como View ficam fora.

### VirtualizedCollectionView — Android (teardown)

**Ordem crítica no `DisconnectHandler`:** chamar `Rv.SetAdapter(null)` / `Rv.SetLayoutManager(null)` **ANTES** de `_adapter?.Dispose()`. `SetAdapter(null)` recicla as views e ainda invoca callbacks no adapter/listener; fazer o `Dispose` antes mata o peer gerenciado e o runtime tenta reativá-lo a partir do handle nativo → `NotSupportedException ("Unable to activate instance ... from native handle")`. O `RecyclerListener` também é removido (`RemoveRecyclerListener`) antes do dispose.

**`VrRecyclerListener` precisa do construtor de ativação** `(IntPtr handle, JniHandleOwnership transfer)` e de `Context?` anulável: se o peer gerenciado for coletado enquanto o Java ainda referencia o listener, o runtime o reativa por esse construtor — sem ele, `NotSupportedException`. `OnViewRecycled` faz guarda de nulo antes de `Glide.With(_context).Clear(...)`.

### Patterns importantes

- Propriedades são `BindableProperty` para XAML binding.
- Helpers internos sem base Java podem ser `file sealed class`; os que estendem tipos Java ou implementam interfaces Java (Android) precisam ser `internal` — `file` quebra o gerador de JCW (XAJCW7024).
- iOS handler remove o gesture recognizer ANTES de cancelar o CTS no disconnect.
- Android: `LoadImage()` chama `Glide.With(imageView).Clear(imageView)` antes do early return de fonte vazia (a foto anterior não fica na view).

## Dependencies

| Package | Platform | Purpose |
|---|---|---|
| `Xamarin.Android.Glide` (namespace `Bumptech.Glide`) | Android | Gallery: carregamento de imagens com cache (4.16.0.14 no net10, 5.0.7.1 no net11) |
| `Xamarin.AndroidX.Fragment` | Android | Gallery: DialogFragment do fullscreen (1.8.8.1 no net10, 1.8.9.3 no net11) |
| `PDFiumCore` | Android, Windows | Pdf: camada de texto/busca no Android e render no Windows (153.0.7999) |
| `bblanchon.PDFium.Android` | Android | Pdf: binário nativo do PDFium (153.0.7999) |
| `Microsoft.Maui.Controls` | All | MAUI framework (10.0.90 no net10, 11.0.0 no net11) |
