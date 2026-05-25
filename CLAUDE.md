# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Agile.Maui** é uma biblioteca de componentes .NET MAUI (v1.0.0) que fornece um controle `ImageView` customizado com zoom e fullscreen, targeting Android, iOS, macOS Catalyst e Windows via single-project multi-targeting.

## Build Commands

```powershell
dotnet build
dotnet build -f net10.0-android
dotnet build -f net10.0-ios
dotnet build -f net10.0-maccatalyst
dotnet build -f net10.0-windows10.0.19041.0
```

Não há testes automatizados neste projeto.

## Architecture

### Registro do controle

Consumidores registram no `MauiProgram.cs`:

```csharp
builder.UseZoomImageView();
```

`MauiAppBuilderExtensions.cs` (namespace `Controls`) usa `#if ANDROID / #if IOS || MACCATALYST / #if WINDOWS` para registrar o handler correto em cada plataforma.

### Handler Pattern

- **`Controls/ImageView.cs`** — `View` cross-platform com 6 bindable properties (`Source`, `IsUrl`, `Placeholder`, `MaxZoom`, `EnableFullscreen`, `AspectMode`) e eventos `ImageLoaded`/`ImageFailed`. Enum `ZoomImageAspect`: `CenterCrop` e `AspectFit`.
- **`Platforms/Android/ImageViewHandler.cs`** — Mapeia para `Android.Widget.ImageView` + Glide (cache disco+memória).
- **`Platforms/iOS/ImageViewHandler.cs`** — Mapeia para `UIImageView` + `NSUrlSession` para URLs.
- **`Platforms/Windows/ImageViewHandler.cs`** — Mapeia para `Microsoft.UI.Xaml.Controls.Image` + `BitmapImage`. Sem fullscreen zoom.
- **`Platforms/MacCatalyst/`** — Compilado a partir dos arquivos de `Platforms/iOS/` via ItemGroup no csproj.

### MacCatalyst compartilha iOS

O `Controls.csproj` inclui `Platforms/iOS/**/*.cs` na compilação `net10.0-maccatalyst`. O `MauiAppBuilderExtensions` usa `#if IOS || MACCATALYST` para registrar o mesmo handler.

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
