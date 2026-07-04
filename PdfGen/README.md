# Agile.Maui.PdfGen

Biblioteca **open source** de **geração de PDF** para .NET MAUI, com API fluente inspirada no QuestPDF — porém com implementação própria, focada em desempenho e **sem** SkiaSharp, WebView, HTML ou dependências comerciais.

- ✅ .NET 10 e .NET 11
- ✅ Android, iOS, Mac Catalyst, Windows
- ✅ Motor de layout **independente de plataforma** (testável no host)
- ✅ Renderização por **APIs nativas**: Android `PdfDocument`, iOS/Mac `CGContextPDF` (o mesmo que o `UIGraphicsPDFRenderer` encapsula)
- ✅ **Escritor PDF 100% gerenciado** (C# puro) como backend padrão — funciona em qualquer plataforma, inclusive fora do MAUI
- ✅ **Fontes TrueType/OTF embutidas** com **Unicode completo** e **subsetting** automático
- ✅ **Gradientes** (linear/radial), **PNG** com transparência, **JPEG**
- ✅ **Exportação para SVG** — o mesmo documento, outro backend

> Este pacote é o **gerador** de PDF. Para **visualizar** PDFs use o pacote irmão `Agile.Maui.Pdf` (PdfViewer).

## Instalação

```
dotnet add package Agile.Maui.PdfGen
```

## Uso

```csharp
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Primitives;
using Agile.Maui.PdfGen.Text;

byte[] pdf = PdfDocument.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(new TextStyle(fontSize: 11));

        page.Header()
            .Text("Pedido").Bold().FontSize(22).FontColor(PdfColor.FromHex("#0D6EFD"));

        page.Content().Column(col =>
        {
            col.Spacing(10);

            col.Item().Text("Cliente: Micael Otowicz");

            col.Item().Table(t =>
            {
                t.Columns(c => { c.ConstantColumn(40); c.RelativeColumn(3); c.RelativeColumn(); });
                t.Header(h =>
                {
                    h.Cell(Colors.LightGray).Text("#").Bold();
                    h.Cell(Colors.LightGray).Text("Produto").Bold();
                    h.Cell(Colors.LightGray).Text("Total").Bold().AlignRight();
                });
                for (int i = 1; i <= 100; i++)
                    t.Row(r =>
                    {
                        r.Cell().Text(i.ToString());
                        r.Cell().Text($"Produto {i}");
                        r.Cell().Text($"R$ {i * 10},00").AlignRight();
                    });
            });
        });

        page.Footer().AlignCenter().PageNumber("Página {0} de {1}");
    });
}).GeneratePdf();               // escritor gerenciado (qualquer plataforma)

// Em app MAUI, para usar o renderer nativo da plataforma:
// byte[] pdf = documento.GeneratePdfNative();
```

`GeneratePdf()` funciona em qualquer lugar. `GeneratePdfNative()` usa o renderer nativo
(Android/iOS/Mac) e recai no gerenciado nas demais plataformas.

## Fontes embutidas e Unicode

Por padrão o texto usa as fontes **base-14** do PDF (Helvetica/Times/Courier), cobrindo o
alfabeto latino (incluindo acentuação pt-BR). Para usar uma **fonte própria** e/ou caracteres
**Unicode** (símbolos, €, moedas, outros alfabetos), embuta uma fonte TrueType/OTF:

```csharp
var fonte = EmbeddedFont.FromFile(@"C:\Windows\Fonts\arial.ttf");
// ou: EmbeddedFont.Load(bytesDaFonte);

page.Content().Text("Relatório 2026 — total € 1.250,00").Font(fonte).FontSize(14);
```

- A fonte é embutida como **Type0/CIDFontType2 (Identity-H)** com CMap **ToUnicode** (o texto
  continua selecionável e pesquisável no leitor).
- **Subsetting automático**: apenas os glifos usados são embutidos, mantendo o PDF pequeno
  (ex.: Arial cai de ~1 MB para poucos KB).
- Suporta contornos **TrueType (glyf)**. Fontes CFF (`.otf` do tipo "OTTO") não são suportadas.

## Gradientes

```csharp
using Agile.Maui.PdfGen.Primitives;

// Fundo com gradiente linear (ângulo em graus: 0 = →, 90 = ↓)
col.Item()
   .Background(GradientBrush.Linear(PdfColor.FromHex("#0D6EFD"), Colors.White, 90f), cornerRadius: 6f)
   .Padding(12)
   .Text("Cabeçalho").FontColor(Colors.White);

// Borda com gradiente radial e múltiplas paradas
col.Item()
   .Border(2f, GradientBrush.Radial(
       new GradientStop(0f, Colors.Red),
       new GradientStop(1f, Colors.Black)))
   .Padding(8)
   .Text("Destaque");
```

No escritor gerenciado vira um *shading pattern* nativo do PDF. Nos renderers nativos que não
suportam gradiente, degrada automaticamente para a cor da primeira parada.

## Imagens

```csharp
col.Item().Image(PdfImage.FromFile("logo.png"));      // PNG (com transparência) ou JPEG
col.Item().Image(bytesDaImagem, ImageFit.Contain, HorizontalAlignment.Center);
```

- **JPEG**: embutido diretamente (DCTDecode).
- **PNG**: decodificado e embutido com FlateDecode; a transparência vira uma máscara `SMask`.
  (Não suportado: PNG de 16 bits e entrelaçamento Adam7.)

## Exportação SVG

O mesmo documento pode ser exportado como SVG apenas trocando o método de geração — todas as
páginas são empilhadas verticalmente em um único SVG:

```csharp
byte[] svg = documento.GenerateSvg();
// documento.GenerateSvg(stream);
```

## Arquitetura

```
Document  →  Layout Tree  →  Measure  →  Layout (Arrange)  →  Render Tree  →  Renderer (PDF/SVG)
```

Todo o layout é calculado **antes** da renderização. Cada elemento implementa
`ILayoutElement` (`Measure` / `Arrange` / `Render`) e cada backend implementa um
`IRenderContext` (`DrawText`, `DrawImage`, `DrawLine`, `DrawRectangle`, `FillRectangle`,
`FillGradient`, `StrokeGradient`, `SaveState`, `RestoreState`, `ClipRectangle`). O mesmo motor
alimenta tanto os backends de PDF (gerenciado e nativos) quanto o de SVG.

## Recursos

**Estrutura** — Documento · Página · Orientação · Tamanho · Margens · Header · Footer · Content
**Contêineres** — Row · Column · Stack · Table · Cell · Padding · Alinhamento · Width/Height · Spacer
**Texto** — FontFamily (base-14) · fontes TrueType/OTF embutidas · Unicode · FontSize · Bold · Italic ·
cor · alinhamento · line height · wrap automático
**Gráficos** — Background (cor/gradiente) · Border (cor/gradiente, cantos arredondados) · linhas ·
gradientes linear/radial · imagens PNG/JPEG
**Paginação** — quebra de página automática · cabeçalho e rodapé repetidos · cabeçalho de tabela
repetido · número de página
**Saída** — PDF (gerenciado, multiplataforma) · PDF nativo (Android/iOS/Mac) · SVG

Componentes customizados: implemente `Element` (ou `ILayoutElement`) e use `.Element(seuElemento)`.

## Licença

MIT — © 2026 Micael Otowicz
