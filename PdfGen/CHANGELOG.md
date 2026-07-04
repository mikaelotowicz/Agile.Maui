# Changelog

Todas as mudanças relevantes deste pacote são documentadas aqui.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/)
e o versionamento é [Semântico](https://semver.org/lang/pt-BR/).

## [1.1.0] — 2026-07-02

### Adicionado
- **Fontes TrueType/OTF embutidas** (`EmbeddedFont.FromFile` / `EmbeddedFont.Load`) com
  **Unicode completo**. O texto é gravado como fonte **Type0/CIDFontType2 (Identity-H)** com
  CMap **ToUnicode** (permanece selecionável e pesquisável). API: `.Text(...).Font(fonte)`.
- **Subsetting automático de fonte**: apenas os glifos usados são embutidos, incluindo
  componentes de glifos compostos (ex.: Arial "Olá mundo" cai de ~1 MB para ~15 KB no PDF).
- **Gradientes** linear e radial (`GradientBrush.Linear` / `GradientBrush.Radial`), aplicáveis
  em `.Background(brush)` e `.Border(thickness, brush)`. No escritor gerenciado viram *shading
  patterns* nativos; nos renderers nativos degradam para a cor da primeira parada.
- **Imagens PNG no escritor gerenciado**: decodificação própria e embutimento via FlateDecode,
  com transparência mapeada para `SMask`. (JPEG continua via DCTDecode.)
- **Exportação para SVG** (`PdfDocument.GenerateSvg`): o mesmo documento e motor de layout,
  apenas trocando o backend de renderização; páginas empilhadas em um único SVG.

### Corrigido
- **Medição de texto acentuado (base-14)**: a faixa WinAnsi 0xA0–0xFF agora usa a largura AFM
  correta (letra acentuada = mesmo avanço da letra base), corrigindo o alinhamento à direita e a
  quebra de linha de textos em pt-BR que antes recaíam numa largura padrão aproximada.

### Notas
- Mudanças **retrocompatíveis** (apenas adições de API): overloads novos e parâmetro opcional
  no fim do construtor de `TextStyle`; `IRenderContext` ganhou `FillGradient`/`StrokeGradient`
  como *default interface methods*, então implementações existentes seguem compilando.
- **Limitações conhecidas**: sem exportação raster (PNG/JPG/WEBP como saída) — depende de
  rasterizador; sem imagem SVG de *entrada*; fontes CFF (`.otf` "OTTO"), PNG de 16 bits e PNG
  entrelaçado (Adam7) não são suportados.

## [1.0.0] — 2026

### Adicionado
- Primeira versão: motor de layout independente de plataforma (Measure/Arrange/Render) com
  quebra de página automática, cabeçalho/rodapé repetidos e cabeçalho de tabela repetido.
- API fluente inspirada no QuestPDF: Documento, Página, Header/Footer/Content, Text, Image
  (JPEG; PNG nos renderers nativos), Row, Column, Stack, Table/Cell, Border, Background, Padding,
  alinhamento, número de página.
- Backends de renderização: escritor PDF 100% gerenciado (padrão, multiplataforma), Android
  `PdfDocument` e iOS/Mac `CGContextPDF`. Fontes base-14. Sem SkiaSharp, WebView ou HTML.
