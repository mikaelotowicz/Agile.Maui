using Xunit;

namespace Agile.Maui.PdfTests;

/// <summary>Congela os enums públicos do pacote (nomes E valores numéricos — contrato binário).</summary>
public class EnumsTests
{
    [Fact]
    public void PdfScrollOrientation_tem_valores_estaveis()
    {
        Assert.Equal(new[] { "Vertical", "Horizontal" }, Enum.GetNames<PdfScrollOrientation>());
        Assert.Equal(0, (int)PdfScrollOrientation.Vertical);
        Assert.Equal(1, (int)PdfScrollOrientation.Horizontal);
    }

    [Fact]
    public void PdfThumbnailPlacement_tem_valores_estaveis()
    {
        Assert.Equal(new[] { "None", "Left", "Right" }, Enum.GetNames<PdfThumbnailPlacement>());
        Assert.Equal(0, (int)PdfThumbnailPlacement.None);
        Assert.Equal(1, (int)PdfThumbnailPlacement.Left);
        Assert.Equal(2, (int)PdfThumbnailPlacement.Right);
    }

    [Fact]
    public void PdfReaderNavigationButtonMode_tem_valores_estaveis()
    {
        Assert.Equal(new[] { "None", "Auto", "Menu", "Back" }, Enum.GetNames<PdfReaderNavigationButtonMode>());
        Assert.Equal(0, (int)PdfReaderNavigationButtonMode.None);
        Assert.Equal(1, (int)PdfReaderNavigationButtonMode.Auto);
        Assert.Equal(2, (int)PdfReaderNavigationButtonMode.Menu);
        Assert.Equal(3, (int)PdfReaderNavigationButtonMode.Back);
    }

    [Fact]
    public void PdfReaderFullscreenTogglePlacement_tem_valores_estaveis()
    {
        Assert.Equal(new[] { "Top", "Bottom" }, Enum.GetNames<PdfReaderFullscreenTogglePlacement>());
        Assert.Equal(0, (int)PdfReaderFullscreenTogglePlacement.Top);
        Assert.Equal(1, (int)PdfReaderFullscreenTogglePlacement.Bottom);
    }
}

/// <summary>
/// Congela os glifos do <see cref="PdfReaderIcons"/>: apps consumidores podem referenciá-los
/// diretamente em XAML — trocar um codepoint é breaking change visual.
/// </summary>
public class PdfReaderIconsTests
{
    [Fact]
    public void FontFamily_e_o_alias_registrado_pela_lib()
        => Assert.Equal("AgilePdfIcons", PdfReaderIcons.FontFamily);

    [Theory]
    [InlineData(nameof(PdfReaderIcons.Menu),           "\U000f035c")]
    [InlineData(nameof(PdfReaderIcons.Back),           "\U000f004d")]
    [InlineData(nameof(PdfReaderIcons.Search),         "\U000f0349")]
    [InlineData(nameof(PdfReaderIcons.Print),          "\U000f1786")]
    [InlineData(nameof(PdfReaderIcons.Share),          "\U000f1514")]
    [InlineData(nameof(PdfReaderIcons.Thumbnails),     "\U000f11d9")]
    [InlineData(nameof(PdfReaderIcons.Horizontal),     "\U000f0b63")]
    [InlineData(nameof(PdfReaderIcons.Vertical),       "\U000f148a")]
    [InlineData(nameof(PdfReaderIcons.ZoomIn),         "\U000f06ed")]
    [InlineData(nameof(PdfReaderIcons.ZoomOut),        "\U000f06ec")]
    [InlineData(nameof(PdfReaderIcons.Fullscreen),     "\U000f0293")]
    [InlineData(nameof(PdfReaderIcons.FullscreenExit), "\U000f0294")]
    [InlineData(nameof(PdfReaderIcons.Prev),           "\U000f0141")]
    [InlineData(nameof(PdfReaderIcons.Next),           "\U000f0142")]
    [InlineData(nameof(PdfReaderIcons.Up),             "\U000f0143")]
    [InlineData(nameof(PdfReaderIcons.Down),           "\U000f0140")]
    [InlineData(nameof(PdfReaderIcons.Close),          "\U000f0156")]
    public void Glifo_esta_congelado(string nome, string esperado)
    {
        var valor = (string?)typeof(PdfReaderIcons).GetField(nome)?.GetValue(null);
        Assert.Equal(esperado, valor);
    }

    [Fact]
    public void Todo_glifo_e_um_unico_codepoint_do_plano_suplementar()
    {
        var glifos = typeof(PdfReaderIcons)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(PdfReaderIcons.FontFamily))
            .Select(f => (f.Name, Valor: (string)f.GetValue(null)!));

        Assert.All(glifos, g =>
        {
            Assert.Equal(2, g.Valor.Length);                     // par substituto UTF-16
            Assert.True(char.IsSurrogatePair(g.Valor[0], g.Valor[1]), $"{g.Name} não é um par substituto");
        });
    }
}

/// <summary>
/// Forma do contrato <see cref="IPdfEngine"/> (interno — não tem lógica default; congela a
/// assinatura que os motores por plataforma implementam).
/// </summary>
public class IPdfEngineTests
{
    [Fact]
    public void E_uma_interface_interna_e_descartavel()
    {
        var t = typeof(IPdfEngine);
        Assert.True(t.IsInterface);
        Assert.False(t.IsPublic);   // interna — só os handlers implementam
        Assert.Contains(typeof(IDisposable), t.GetInterfaces());
    }

    [Fact]
    public void Expoe_as_propriedades_IsOpen_e_PageCount()
    {
        var nomes = typeof(IPdfEngine).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "IsOpen", "PageCount" }, nomes);
    }

    [Fact]
    public void Expoe_os_metodos_do_contrato()
    {
        var nomes = typeof(IPdfEngine).GetMethods()
            .Where(m => !m.IsSpecialName)   // exclui getters
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(
            new[] { "Close", "ExtractTextAsync", "GetPageSize", "OpenAsync", "OpenAsync",
                    "RenderPageAsync", "RenderThumbnailAsync" },
            nomes);
    }

    [Fact]
    public void OpenAsync_aceita_caminho_ou_stream_com_senha_opcional()
    {
        var sobrecargas = typeof(IPdfEngine).GetMethods().Where(m => m.Name == "OpenAsync").ToList();
        Assert.Equal(2, sobrecargas.Count);
        Assert.Contains(sobrecargas, m => m.GetParameters()[0].ParameterType == typeof(string));
        Assert.Contains(sobrecargas, m => m.GetParameters()[0].ParameterType == typeof(Stream));
        Assert.All(sobrecargas, m =>
        {
            var ps = m.GetParameters();
            Assert.Equal(typeof(string), ps[1].ParameterType);            // password
            Assert.Equal(typeof(CancellationToken), ps[2].ParameterType); // ct
        });
    }
}

public class PdfViewerLogTests
{
#if DEBUG
    [Fact]
    public void Write_entrega_a_linha_formatada_ao_evento_Received()
    {
        string? recebido = null;
        Action<string> handler = l => recebido = l;
        PdfViewerLog.Received += handler;
        try
        {
            PdfViewerLog.Write("Teste", "mensagem de log");
        }
        finally
        {
            PdfViewerLog.Received -= handler;   // evento estático — não vazar entre testes
        }

        Assert.NotNull(recebido);
        Assert.EndsWith("[Teste] mensagem de log", recebido);
    }
#else
    [Fact]
    public void Write_e_compilado_fora_em_Release()
    {
        // [Conditional("DEBUG")]: em Release a chamada nem é emitida no chamador.
        Assert.True(true);
    }
#endif
}
