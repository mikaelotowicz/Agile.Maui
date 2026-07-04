# PdfGen

`Agile.Maui.PdfGen` is the PDF generation package in this repository. It is not
a visual MAUI control and does not require handler registration in `MauiProgram`.
It is a multi-targeted class library with a fluent document API and a managed PDF
writer that can also be used from WinForms, Blazor, console apps, workers, and
APIs.

## Install

```powershell
dotnet add package Agile.Maui.PdfGen
```

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
        page.Content().Text("Ola PDF");
        page.Footer().AlignCenter().PageNumber("Pagina {0} de {1}");
    });
}).GeneratePdf();
```

## Backend Choice

Use `GeneratePdf()` for the full feature set. It uses the managed writer and
supports embedded TrueType fonts, Unicode with `ToUnicode`, PNG transparency,
solid-color alpha, compressed page content streams, gradients, tables,
pagination, and SVG export through `GenerateSvg()`.

Use `GeneratePdfNative()` only when a MAUI app specifically wants the native
Android/iOS/Mac renderer. Native renderers are intentionally smaller and do not
have full parity with the managed backend: embedded fonts are not written as
Type0 subsets and gradients can degrade to the first color.

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
- Images: JPEG and PNG are supported; PNG alpha is preserved with `SMask`.
- Text: wrap, explicit line breaks, left/center/right/justify alignment.
- Pagination: vertical flows, tables, headers, footers, and page numbers are
  handled by the layout engine.
- Decorative wrappers such as `Background`, `Border`, gradient background and
  gradient border can wrap paginated content; the decoration is applied to each
  resulting flow fragment.
