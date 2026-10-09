# PdfGen

`Agile.Maui.PdfGen` is the PDF generation package in this repository. It is not
a visual MAUI control and does not require handler registration in `MauiProgram`.
It is a multi-targeted class library with a fluent document API and a managed PDF
writer that can also be used from WinForms, Blazor, console apps, workers, and
APIs.

The package ships its own README and `CHANGELOG.md` (in Portuguese), kept in
`PdfGen/`.

## Install

```powershell
dotnet add package Agile.Maui.PdfGen --version 1.2.0
```

For .NET 11 preview projects:

```powershell
dotnet add package Agile.Maui.PdfGen --version 1.2.0-preview.1
```

`1.2.0` is the first version published on nuget.org; it includes everything
listed under `1.1.0` in the changelog.

## Basic Usage

```csharp
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;

byte[] pdf = PdfDocument.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.Header().Text("Pedido").Bold().FontSize(22);
        page.Content().Text("Olá PDF");
        page.Footer().AlignCenter().PageNumber("Página {0} de {1}");
    });
}).GeneratePdf();
```

A `PdfDocument` instance is not thread-safe: do not render the same document in
parallel. Sequential renders of the same instance are supported.

## Use in MAUI apps

In a MAUI app with `ImplicitUsings` (the template default), the MAUI global
usings bring in types whose names match public PdfGen types. Using one of these
names together with the PdfGen `using` directives raises CS0104 (ambiguous
reference):

| PdfGen type | Collides with |
|---|---|
| `Agile.Maui.PdfGen.Api.IContainer` | `Microsoft.Maui.IContainer` |
| `Agile.Maui.PdfGen.Text.EmbeddedFont` | `Microsoft.Maui.EmbeddedFont` |
| `Agile.Maui.PdfGen.Primitives.Colors` | `Microsoft.Maui.Graphics.Colors` |
| `Agile.Maui.PdfGen.Primitives.GradientBrush` | `Microsoft.Maui.Controls.GradientBrush` |
| `Agile.Maui.PdfGen.Primitives.GradientStop` | `Microsoft.Maui.Controls.GradientStop` |
| `Agile.Maui.PdfGen.Primitives.HorizontalAlignment` | `Microsoft.Maui.Graphics.HorizontalAlignment` |
| `Agile.Maui.PdfGen.Primitives.VerticalAlignment` | `Microsoft.Maui.Graphics.VerticalAlignment` |
| `Agile.Maui.PdfGen.Primitives.FontWeight` | `Microsoft.Maui.FontWeight` |
| `Agile.Maui.PdfGen.Rendering.ImageFormat` | `Microsoft.Maui.Graphics.ImageFormat` |
| `Agile.Maui.PdfGen.Layout.Element` | `Microsoft.Maui.Controls.Element` |

Keep the PDF generation code in its own file and declare aliases at the top of
it for the types you use. An alias takes precedence over the global usings:

```csharp
using IContainer = Agile.Maui.PdfGen.Api.IContainer;
using Colors = Agile.Maui.PdfGen.Primitives.Colors;
using GradientBrush = Agile.Maui.PdfGen.Primitives.GradientBrush;
using GradientStop = Agile.Maui.PdfGen.Primitives.GradientStop;
using HorizontalAlignment = Agile.Maui.PdfGen.Primitives.HorizontalAlignment;
using VerticalAlignment = Agile.Maui.PdfGen.Primitives.VerticalAlignment;
using FontWeight = Agile.Maui.PdfGen.Primitives.FontWeight;
using EmbeddedFont = Agile.Maui.PdfGen.Text.EmbeddedFont;
// Only when using Rendering/Layout directly:
// using ImageFormat = Agile.Maui.PdfGen.Rendering.ImageFormat;
// using Element = Agile.Maui.PdfGen.Layout.Element;
```

- Only names actually used in the file need an alias.
- Do not add these aliases to pages or code-behind that use the MAUI types with
  the same names (`Colors`, `Element`, and so on): the alias applies to the whole
  file.
- Alternatively, keep PDF generation in a class library without the MAUI global
  usings (for example, a plain `net10.0` library without `UseMaui`) and
  reference it from the app, or fully qualify the type at the call site.

## Backend Choice

Use `GeneratePdf()` for the full feature set. It uses the managed writer and
supports embedded TrueType fonts, Unicode with `ToUnicode`, PNG transparency,
JPEG in RGB, grayscale and CMYK, solid-color alpha, compressed page content
streams, gradients, tables, pagination, and SVG export through `GenerateSvg()`.

Use `GeneratePdfNative()` only when a MAUI app specifically wants the native
Android/iOS/Mac renderer. Native renderers are intentionally smaller and do not
have full parity with the managed backend:

- embedded fonts are used for drawing (`Typeface` on Android, `CGFont`/`CTFont`
  on iOS/Mac), but how the font is written to the PDF (subset, `ToUnicode`) is
  up to the platform API; if the font cannot be loaded, text falls back to the
  system font;
- gradients degrade to the first stop color.

## Blazor and WinForms

WinForms and Blazor Server can call `GeneratePdf()` directly. In Blazor
WebAssembly, generate the `byte[]` and trigger a browser download; do not use
`Save(path)` because the browser cannot write directly to an arbitrary local
path.

## Sample

`PdfGen.Sample` generates a one-page premium commercial proposal and writes both
PDF and SVG output. It uses the repository `agile.png` asset as a real embedded
image instead of generating a logo at runtime.

```powershell
dotnet run --project PdfGen.Sample -- output\pdf\premium-proposal.pdf
```

The sample covers embedded TrueType fonts, Unicode text, PNG transparency,
gradients, alpha, cards, tables, a financial summary, and page numbering.

## Notes

- Fonts: use `EmbeddedFont.FromFile` or `EmbeddedFont.Load` with TrueType fonts.
  CFF fonts (`OTTO`) are not supported; truncated or corrupted fonts throw
  `InvalidDataException`.
- Images: JPEG (RGB, grayscale, CMYK) and PNG are supported; PNG alpha is
  preserved with `SMask`; malformed PNG throws `InvalidDataException`.
- Text: wrap, explicit line breaks, left/center/right/justify alignment. Text
  with an embedded font is vertically centered on its line.
- Page numbers follow the container alignment (`Footer().AlignRight().PageNumber(...)`)
  as well as the text alignment (`.PageNumber(...).AlignRight()`).
- Pagination: vertical flows, tables, headers, footers, and page numbers are
  handled by the layout engine.
- Decorative wrappers such as `Background`, `Border`, gradient background and
  gradient border can wrap paginated content; the decoration is drawn once per
  page over the contiguous section (a rounded block stays a single rounded
  rectangle, and a block split across pages closes and reopens its corners at
  the break).
