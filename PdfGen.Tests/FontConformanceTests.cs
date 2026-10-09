using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Agile.Maui.PdfGen.Api;
using Agile.Maui.PdfGen.Text;
using Xunit;

namespace Agile.Maui.PdfGen.Tests;

/// <summary>
/// Conformidade estrutural do subset TrueType embutido (FontFile2): checksums sfnt,
/// checkSumAdjustment, loca/hmtx/maxp consistentes, glifos compostos completos, /W fiel ao
/// hmtx original, tag de subset e saída determinística. Pula graciosamente sem fonte de sistema.
/// </summary>
public class FontConformanceTests
{
    static string? FindTestFontPath()
    {
        string[] candidates =
        {
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    // PDF com fonte embutida + acentos (glifos compostos) gerado duas vezes a partir da mesma
    // definição, para os testes de estrutura e de determinismo.
    static readonly Lazy<(byte[]? pdf, byte[]? pdf2, byte[]? originalFont)> Data = new(() =>
    {
        string? path = FindTestFontPath();
        if (path is null)
            return (null, null, null);

        byte[] original = File.ReadAllBytes(path);
        EmbeddedFont font = EmbeddedFont.Load(original);

        byte[] Build() => PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Column(col =>
            {
                col.Item().Text("Ágil Ação çãé ÀÈÌÒÙ não").Font(font).FontSize(12f);
                col.Item().Text("ABC abc 0123 – teste").Font(font);
            }));
        }).GeneratePdf();

        return (Build(), Build(), original);
    });

    // ---- estrutura do subset ----

    [Fact]
    public void FontFile2_length1_e_o_tamanho_descomprimido()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        var (subset, length1) = PdfMiniParser.FontFile2(pdf);
        Assert.Equal(subset.Length, length1);
    }

    [Fact]
    public void Checksums_das_tabelas_conferem_com_o_diretorio_sfnt()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        var (subset, _) = PdfMiniParser.FontFile2(pdf);
        var dir = PdfMiniParser.SfntDirectory(subset);
        foreach (string required in new[] { "glyf", "head", "hhea", "hmtx", "loca", "maxp" })
            Assert.Contains(required, dir.Keys);

        foreach (var (tag, (off, len, checksum)) in dir)
        {
            byte[] copy = new byte[len];
            System.Array.Copy(subset, off, copy, 0, len);
            if (tag == "head")
                copy[8] = copy[9] = copy[10] = copy[11] = 0;   // checkSumAdjustment zerado no cálculo
            Assert.True(PdfMiniParser.SfntChecksum(copy, 0, len) == checksum,
                $"checksum da tabela {tag} não confere");
        }
    }

    [Fact]
    public void CheckSumAdjustment_fecha_a_soma_do_arquivo_em_B1B0AFBA()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        var (subset, _) = PdfMiniParser.FontFile2(pdf);
        Assert.Equal(0xB1B0AFBAu, PdfMiniParser.SfntChecksum(subset, 0, subset.Length));
    }

    [Fact]
    public void Subset_loca_longo_hmtx_e_maxp_consistentes()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        var (subset, _) = PdfMiniParser.FontFile2(pdf);
        var dir = PdfMiniParser.SfntDirectory(subset);

        Assert.Equal(1, PdfMiniParser.U16(subset, dir["head"].off + 50));   // indexToLocFormat longo
        int numGlyphs = PdfMiniParser.U16(subset, dir["maxp"].off + 4);
        Assert.Equal((numGlyphs + 1) * 4, dir["loca"].len);
        Assert.Equal(numGlyphs, PdfMiniParser.U16(subset, dir["hhea"].off + 34));
        Assert.Equal(numGlyphs * 4, dir["hmtx"].len);

        // Último offset da loca fecha com o tamanho do glyf.
        uint last = PdfMiniParser.U32(subset, dir["loca"].off + numGlyphs * 4);
        Assert.Equal((uint)dir["glyf"].len, last);
    }

    [Fact]
    public void Glifos_compostos_mantem_todos_os_componentes_no_subset()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        var (subset, _) = PdfMiniParser.FontFile2(pdf);
        var dir = PdfMiniParser.SfntDirectory(subset);
        int numGlyphs = PdfMiniParser.U16(subset, dir["maxp"].off + 4);
        var loca = dir["loca"];
        var glyf = dir["glyf"];

        bool foundComposite = false;
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            uint s0 = PdfMiniParser.U32(subset, loca.off + gid * 4);
            uint e0 = PdfMiniParser.U32(subset, loca.off + gid * 4 + 4);
            if (e0 <= s0)
                continue;
            short contours = (short)PdfMiniParser.U16(subset, glyf.off + (int)s0);
            if (contours >= 0)
                continue;

            foundComposite = true;
            int p = glyf.off + (int)s0 + 10;
            bool more = true;
            while (more)
            {
                int flags = PdfMiniParser.U16(subset, p);
                int componentGid = PdfMiniParser.U16(subset, p + 2);
                p += 4;

                Assert.True(componentGid < numGlyphs, $"componente {componentGid} fora do subset");
                uint cs = PdfMiniParser.U32(subset, loca.off + componentGid * 4);
                uint ce = PdfMiniParser.U32(subset, loca.off + componentGid * 4 + 4);
                Assert.True(ce > cs, $"componente {componentGid} do composto {gid} ficou sem contorno");

                p += (flags & 0x0001) != 0 ? 4 : 2;
                if ((flags & 0x0008) != 0) p += 2;
                else if ((flags & 0x0040) != 0) p += 4;
                else if ((flags & 0x0080) != 0) p += 8;
                more = (flags & 0x0020) != 0;
            }
        }

        Assert.True(foundComposite, "o texto acentuado deveria produzir ao menos um glifo composto");
    }

    [Fact]
    public void W_do_cidfont_confere_com_o_hmtx_original_escalado()
    {
        if (Data.Value.pdf is not byte[] pdf || Data.Value.originalFont is not byte[] original)
            return;

        var odir = PdfMiniParser.SfntDirectory(original);
        int unitsPerEm = PdfMiniParser.U16(original, odir["head"].off + 18);
        int numHMetrics = PdfMiniParser.U16(original, odir["hhea"].off + 34);
        int hmtxOff = odir["hmtx"].off;

        string raw = PdfMiniParser.AsLatin1(pdf);
        // O conteúdo do /W tem colchetes aninhados (gid [w] gid [w] ...): captura até o "] >>" do dict.
        Match w = Regex.Match(raw, @"/W \[(.*?)\] >>");
        Assert.True(w.Success, "/W não encontrado");

        var entries = Regex.Matches(w.Groups[1].Value, @"(\d+) \[(\d+)\]");
        Assert.NotEmpty(entries);
        foreach (Match m in entries)
        {
            int gid = int.Parse(m.Groups[1].Value);
            int width = int.Parse(m.Groups[2].Value);
            int advance = gid < numHMetrics
                ? PdfMiniParser.U16(original, hmtxOff + gid * 4)
                : PdfMiniParser.U16(original, hmtxOff + (numHMetrics - 1) * 4);
            Assert.Equal((int)MathF.Round(advance * 1000f / unitsPerEm), width);
        }
    }

    // ---- nome do subset e determinismo ----

    [Fact]
    public void Tag_de_subset_igual_no_type0_cidfont_e_fontdescriptor()
    {
        if (Data.Value.pdf is not byte[] pdf)
            return;

        string raw = PdfMiniParser.AsLatin1(pdf);
        var baseFonts = Regex.Matches(raw, @"/BaseFont /([A-Z]{6})\+(\S+)");
        var fontNames = Regex.Matches(raw, @"/FontName /([A-Z]{6})\+(\S+)");

        Assert.Equal(2, baseFonts.Count);   // Type0 + CIDFontType2
        Assert.Single(fontNames);           // FontDescriptor
        string tag = baseFonts[0].Groups[1].Value;
        Assert.All(baseFonts, m => Assert.Equal(tag, m.Groups[1].Value));
        Assert.Equal(tag, fontNames[0].Groups[1].Value);
    }

    [Fact]
    public void Mesma_definicao_gera_bytes_identicos()
    {
        if (Data.Value.pdf is not byte[] pdf || Data.Value.pdf2 is not byte[] pdf2)
            return;

        Assert.True(pdf.SequenceEqual(pdf2), "duas gerações da mesma definição divergiram");
    }

    // ---- cmap ----

    [Fact]
    public void Cmap_formato4_mapeia_bmp()
    {
        string? path = FindTestFontPath();
        if (path is null)
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        Assert.NotEqual(0, font.GlyphId('A'));
        Assert.NotEqual(0, font.GlyphId('ç'));
        Assert.NotEqual(0, font.GlyphId('€'));
        Assert.NotEqual(font.GlyphId('A'), font.GlyphId('B'));
        Assert.Equal(0, font.GlyphId(0xE0000));   // fora da fonte → .notdef
    }

    [Fact]
    public void Cmap_formato12_mapeia_plano_suplementar()
    {
        string path = @"C:\Windows\Fonts\seguiemj.ttf";   // cmap (3,10) formato 12
        if (!File.Exists(path))
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        Assert.NotEqual(0, font.GlyphId(0x1F600));   // 😀
        Assert.True(font.MeasureWidth("😀", 12f) > 0f);
    }

    [Fact]
    public void ToUnicode_mapeia_par_substituto_em_utf16be()
    {
        string path = @"C:\Windows\Fonts\seguiemj.ttf";
        if (!File.Exists(path))
            return;

        EmbeddedFont font = EmbeddedFont.FromFile(path);
        byte[] pdf = PdfDocument.Create(doc =>
        {
            doc.Page(page => page.Content().Text("😀").Font(font));
        }).GeneratePdf();

        // U+1F600 em UTF-16BE = D83D DE00 no bfchar do CMap ToUnicode (não comprimido).
        string raw = PdfMiniParser.AsLatin1(pdf);
        Assert.Contains("beginbfchar", raw);
        Assert.Contains("D83DDE00", raw);
    }
}
