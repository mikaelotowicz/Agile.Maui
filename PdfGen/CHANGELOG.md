# Changelog

Todas as mudancas relevantes deste pacote sao documentadas aqui.
O formato segue Keep a Changelog e o versionamento e semantico.

## [1.1.1] - Em desenvolvimento

### Corrigido
- `AlignJustify()` agora distribui espaco entre palavras nas linhas quebradas
  automaticamente, em vez de se comportar como alinhamento a esquerda.
- A quebra de palavras longas preserva pares substitutos Unicode, evitando partir
  emojis e codepoints fora do BMP.
- Pontuacao WinAnsi fora de Latin-1, como `—`, `–`, aspas tipograficas e `€`,
  agora e codificada corretamente em texto base-14, em vez de virar `?`.
- `PageNumberElement` agora mede texto com `TextStyle.MeasureWidth`, respeitando
  fontes embutidas quando aplicadas ao numero de pagina.
- O escritor PDF gerenciado agora respeita alpha em texto, linhas, bordas e
  fundos solidos usando `/ExtGState`.
- O renderer Android libera os `Bitmap` decodificados ao finalizar a pagina.

### Alterado
- Streams de conteudo de pagina agora sao comprimidos com `FlateDecode`.
- Wrappers decorativos (`Background`, `Border` e variantes com gradiente)
  permitem paginacao do conteudo interno e aplicam a decoracao por fragmento de
  fluxo.
- `PdfGen.Sample` agora gera uma proposta comercial premium de uma pagina, usa
  `agile.png` como imagem real e remove a geracao manual de PNG em runtime.
- Testes de fonte embutida agora procuram fontes TrueType comuns em Windows,
  macOS e Linux, reduzindo a dependencia fixa de `C:\Windows\Fonts\arial.ttf`.
- README atualizado para explicar o uso em WinForms/Blazor/hosts .NET, a
  diferenca entre `GeneratePdf()` e `GeneratePdfNative()` e as limitacoes reais
  dos renderers nativos.

## [1.1.0] - 2026-07-02

### Adicionado
- Fontes TrueType/OTF embutidas (`EmbeddedFont.FromFile` / `EmbeddedFont.Load`)
  com Unicode completo. O texto e gravado como fonte Type0/CIDFontType2
  (`Identity-H`) com CMap `ToUnicode`.
- Subsetting automatico de fonte: apenas os glifos usados sao embutidos,
  incluindo componentes de glifos compostos.
- Gradientes linear e radial (`GradientBrush.Linear` / `GradientBrush.Radial`),
  aplicaveis em `.Background(brush)` e `.Border(thickness, brush)`.
- Imagens PNG no escritor gerenciado: decodificacao propria e embutimento via
  `FlateDecode`, com transparencia mapeada para `SMask`.
- Exportacao para SVG (`PdfDocument.GenerateSvg`): o mesmo documento e motor de
  layout, apenas trocando o backend de renderizacao.

### Corrigido
- Medicao de texto acentuado nas fontes base-14: a faixa WinAnsi 0xA0-0xFF usa
  a largura AFM correspondente da letra base.

### Notas
- Mudancas retrocompativeis: overloads novos e parametro opcional no fim do
  construtor de `TextStyle`; `IRenderContext` ganhou `FillGradient` e
  `StrokeGradient` como default interface methods.
- Limitacoes conhecidas: sem exportacao raster (PNG/JPG/WEBP como saida), sem
  imagem SVG de entrada, fontes CFF (`.otf` com assinatura `OTTO`), PNG de 16
  bits e PNG entrelacado Adam7 nao sao suportados.

## [1.0.0] - 2026

### Adicionado
- Primeira versao: motor de layout independente de plataforma com
  `Measure`/`Arrange`/`Render`, quebra de pagina automatica, header/footer
  repetidos e cabecalho de tabela repetido.
- API fluente inspirada no QuestPDF: documento, pagina, header/footer/content,
  texto, imagem, row, column, stack, table/cell, border, background, padding,
  alinhamento e numero de pagina.
- Backends de renderizacao: escritor PDF 100% gerenciado, Android
  `PdfDocument` e iOS/Mac `CGContextPDF`. Fontes base-14. Sem SkiaSharp,
  WebView ou HTML.
