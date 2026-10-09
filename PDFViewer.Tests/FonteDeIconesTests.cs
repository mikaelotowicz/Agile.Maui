using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>
/// Regressão: a fonte de ícones era só um MauiFont da biblioteca, que chega ao app via ProjectReference
/// mas não viaja no .nupkg — instalado pelo NuGet, o PdfReaderView mostrava os ícones em branco. O
/// pacote precisa levar o .ttf e o .targets (buildTransitive) que declara o MauiFont no app consumidor.
/// </summary>
public class FonteDeIconesTests
{
    static string PdfViewerDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Agile.Maui.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "PDFViewer");
    }

    [Fact]
    public void Targets_buildTransitive_declara_o_MauiFont_registrado_pela_lib()
    {
        var targets = XDocument.Load(Path.Combine(PdfViewerDir(), "buildTransitive", "Agile.Maui.Pdf.targets"));

        var fonts = targets.Descendants().Where(e => e.Name.LocalName == "MauiFont")
            .Select(e => (string?)e.Attribute("Include")).ToList();

        Assert.Contains(fonts, f => f is not null
            && f.Replace('/', '\\').EndsWith(@"Fonts\" + PdfReaderIcons.FontFile, StringComparison.Ordinal));
    }

    [Fact]
    public void Csproj_empacota_a_fonte_e_o_targets_em_buildTransitive()
    {
        var csproj = XDocument.Load(Path.Combine(PdfViewerDir(), "PDFViewer.csproj"));

        var packed = csproj.Descendants().Where(e => e.Name.LocalName == "None"
                && (string?)e.Attribute("Pack") == "true")
            .Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("PackagePath")))
            .ToList();

        Assert.Contains((@"Resources\Fonts\" + PdfReaderIcons.FontFile, @"buildTransitive\Fonts\"), packed);
        Assert.Contains((@"buildTransitive\Agile.Maui.Pdf.targets", @"buildTransitive\"), packed);
    }

    [Fact]
    public void Arquivo_da_fonte_e_TrueType()
    {
        using var stream = File.OpenRead(Path.Combine(PdfViewerDir(), "Resources", "Fonts", PdfReaderIcons.FontFile));
        var header = new byte[4];
        Assert.Equal(4, stream.Read(header, 0, 4));
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, header);   // sfnt version 1.0 (glyf)
    }
}
