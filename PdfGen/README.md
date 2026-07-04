# Agile.Maui.PdfGen

Biblioteca open source de geracao de PDF para apps .NET e .NET MAUI, com API
fluente inspirada no QuestPDF, implementacao propria e sem SkiaSharp, WebView,
HTML ou dependencias comerciais.

- .NET 10 e .NET 11
- Android, iOS, Mac Catalyst, Windows e hosts .NET comuns
- Motor de layout independente de plataforma, testavel no host
- Escritor PDF 100% gerenciado como backend padrao
- Renderers nativos opcionais em apps MAUI: Android `PdfDocument` e iOS/Mac `CGContextPDF`
- Fontes TrueType embutidas com Unicode e subsetting automatico no backend gerenciado
- Gradientes, alpha em cores solidas, PNG com transparencia, JPEG e exportacao SVG

Este pacote gera PDFs. Para visualizar PDFs em MAUI, use o pacote irmao
`Agile.Maui.Pdf` (`PdfViewer` / `PdfReaderView`).

## Instalacao

```powershell
dotnet add package Agile.Maui.PdfGen
```

## Uso basico

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
            .Text("Pedido")
            .Bold()
            .FontSize(22)
            .FontColor(PdfColor.FromHex("#0D6EFD"));

        page.Content().Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("Cliente: Micael Otowicz");

            col.Item().Table(t =>
            {
                t.Columns(c =>
                {
                    c.ConstantColumn(40);
                    c.RelativeColumn(3);
                    c.RelativeColumn();
                });

                t.Header(h =>
                {
                    h.Cell(Colors.LightGray).Text("#").Bold();
                    h.Cell(Colors.LightGray).Text("Produto").Bold();
                    h.Cell(Colors.LightGray).Text("Total").Bold().AlignRight();
                });

                for (int i = 1; i <= 100; i++)
                {
                    t.Row(r =>
                    {
                        r.Cell().Text(i.ToString());
                        r.Cell().Text($"Produto {i}");
                        r.Cell().Text($"R$ {i * 10},00").AlignRight();
                    });
                }
            });
        });

        page.Footer().AlignCenter().PageNumber("Pagina {0} de {1}");
    });
}).GeneratePdf();
```

`GeneratePdf()` usa o escritor gerenciado e funciona em qualquer host .NET
compativel com os target frameworks do pacote.

## Uso fora do MAUI

O pacote nao depende de `Microsoft.Maui.Controls` para o backend gerenciado.
Voce pode usa-lo em:

- WinForms ou WPF modernos em .NET 10/11
- Blazor Server
- Blazor WebAssembly, gerando `byte[]` para download no navegador
- console apps, workers e APIs

Em Blazor WebAssembly, evite `Save(path)`, porque o browser nao tem acesso direto
ao sistema de arquivos. Gere `byte[]` com `GeneratePdf()` e entregue o download
via JS interop ou endpoint.

## Exemplo premium

O projeto `PdfGen.Sample` gera uma proposta comercial premium de uma pagina,
usando `agile.png` como imagem real no PDF e exportando o mesmo documento para
SVG:

```powershell
dotnet run --project PdfGen.Sample -- output\pdf\premium-proposal.pdf
```

O sample demonstra fonte TrueType embutida, Unicode, PNG com transparencia,
gradientes, alpha, cards, tabela, resumo financeiro e numeracao de paginas. O
arquivo `agile.png` e copiado para a pasta de saida pelo `PdfGen.Sample.csproj`.

## Backends

### `GeneratePdf()`

Backend recomendado para paridade completa. Ele e 100% gerenciado e suporta:

- fontes base-14 e fontes TrueType embutidas;
- Unicode com `ToUnicode` e subsetting automatico;
- JPEG e PNG, incluindo PNG com alpha via `SMask`;
- alpha uniforme em texto, linhas, bordas e fundos solidos;
- gradientes PDF nativos;
- content streams comprimidos com `FlateDecode`;
- WinForms, Blazor, MAUI e outros hosts .NET.

### `GeneratePdfNative()`

Disponivel para apps MAUI que queiram renderizar com APIs nativas de plataforma.
No Android usa `PdfDocument`; no iOS/Mac Catalyst usa `CGContextPDF`; nas demais
plataformas recai no backend gerenciado.

Os renderers nativos sao intencionalmente menores e nao tem paridade total com o
backend gerenciado: fontes embutidas nao sao incorporadas como Type0/subset e
gradientes degradam para a primeira cor quando o backend nativo nao suporta o
recurso. Para Unicode/subsetting garantidos, prefira `GeneratePdf()`.

## Fontes embutidas e Unicode

Por padrao, texto usa as fontes base-14 do PDF (Helvetica, Times e Courier).
Para usar caracteres Unicode amplos ou uma fonte propria, carregue uma fonte
TrueType:

```csharp
var fonte = EmbeddedFont.FromFile(@"C:\Windows\Fonts\arial.ttf");
// ou: EmbeddedFont.Load(bytesDaFonte);

page.Content()
    .Text("Relatorio 2026 - total EUR 1.250,00")
    .Font(fonte)
    .FontSize(14);
```

- A fonte e embutida como Type0/CIDFontType2 (`Identity-H`) no backend gerenciado.
- O CMap `ToUnicode` mantem o texto selecionavel e pesquisavel.
- Apenas os glifos usados sao embutidos, reduzindo o tamanho do PDF.
- Fontes CFF (`.otf` com assinatura `OTTO`) nao sao suportadas; use TrueType
  (`glyf`).

## Texto

O motor inclui wrap automatico, quebras de linha explicitas, alinhamento a
esquerda, centro, direita e justificado. O texto justificado distribui espaco
entre palavras nas linhas que foram quebradas automaticamente; a ultima linha do
paragrafo permanece com alinhamento natural.

## Cores, alpha e gradientes

```csharp
col.Item()
   .Background(new PdfColor(13, 110, 253, 128), cornerRadius: 6f)
   .Padding(12)
   .Text("Fundo azul com alpha");

col.Item()
   .Background(GradientBrush.Linear(Colors.Blue, Colors.White, 90f), cornerRadius: 6f)
   .Padding(12)
   .Text("Gradiente");
```

No backend gerenciado, cores solidas com alpha usam `ExtGState`. Gradientes usam
shading patterns nativos do PDF; alpha uniforme entre todas as paradas e aplicado
ao shape inteiro. Alpha diferente por parada de gradiente ainda nao e suportado.

## Imagens

```csharp
col.Item().Image(PdfImage.FromFile("logo.png"));
col.Item().Image(bytesDaImagem, ImageFit.Contain, HorizontalAlignment.Center);
```

- JPEG e embutido diretamente com `DCTDecode`.
- PNG e decodificado e embutido com `FlateDecode`.
- PNG com transparencia usa `SMask`.
- PNG de 16 bits e PNG entrelacado Adam7 nao sao suportados pelo backend gerenciado.

## Exportacao SVG

O mesmo documento pode ser exportado como SVG:

```csharp
byte[] svg = documento.GenerateSvg();
documento.GenerateSvg(stream);
```

Todas as paginas sao empilhadas verticalmente em um unico SVG.

## Arquitetura

```text
Document -> Layout Tree -> Measure -> Arrange -> Render Tree -> Renderer
```

Cada elemento implementa `ILayoutElement` (`Measure`, `Arrange`, `Render`) e
cada backend implementa `IRenderContext`. O mesmo motor alimenta PDF gerenciado,
PDF nativo e SVG.

## Recursos

**Estrutura**: documento, pagina, tamanho, margem, header, footer e content.

**Containers**: row, column, stack, table/cell, padding, alinhamento, width/height
e spacer.

**Texto**: fontes base-14, fontes TrueType embutidas, Unicode, tamanho, bold,
italic, cor, line height, wrap e alinhamento.

**Graficos**: background, border, cantos arredondados, linhas, alpha solido,
gradientes, PNG e JPEG.

**Paginacao**: quebra automatica em fluxos verticais, header/footer repetidos,
cabecalho de tabela repetido, numero de pagina e wrappers decorativos
(`Background`, `Border` e variantes com gradiente) aplicados por fragmento de
fluxo quando envolvem conteudo paginavel.

Componentes customizados podem implementar `Element` ou `ILayoutElement` e serem
injetados com `.Element(seuElemento)`.

## Licenca

MIT - Copyright 2026 Micael Otowicz
