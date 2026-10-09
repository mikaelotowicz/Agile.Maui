using Xunit;

namespace Agile.Maui.GalleryTests;

public class ImageSourceResolverTests
{
    // ── IsRemote ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://exemplo.com/foto.png")]
    [InlineData("https://exemplo.com/foto.png")]
    [InlineData("HTTP://EXEMPLO.COM/FOTO.PNG")]
    [InlineData("HtTpS://exemplo.com/Foto.Png")]
    [InlineData("https://exemplo.com/pasta/foto sem extensao")]
    public void IsRemote_detecta_http_e_https_sem_diferenciar_caixa(string source)
    {
        Assert.True(ImageSourceResolver.IsRemote(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dog")]
    [InlineData("dog.png")]
    [InlineData("pasta/dog.png")]
    [InlineData("ftp://servidor/foto.png")]
    [InlineData("file:///C:/fotos/a.png")]
    [InlineData("content://media/external/images/1")]
    [InlineData("ms-appx:///Resources/a.png")]
    [InlineData(@"C:\fotos\a.png")]
    public void IsRemote_nao_considera_remoto_fontes_locais_nulas_ou_de_outros_esquemas(string? source)
    {
        Assert.False(ImageSourceResolver.IsRemote(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dog.png")]
    [InlineData("qualquer coisa")]
    public void IsRemote_com_legacyIsUrl_forca_remoto_por_compatibilidade(string? source)
    {
        Assert.True(ImageSourceResolver.IsRemote(source, legacyIsUrl: true));
    }

    // ── TryGetAbsoluteLocalUri ───────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\imagens\foto.png")]
    [InlineData("file:///C:/imagens/foto.png")]
    [InlineData("content://media/external/images/1")]
    [InlineData("ms-appx:///Resources/Images/foto.png")]
    public void TryGetAbsoluteLocalUri_aceita_uri_absoluta_nao_http(string source)
    {
        Assert.True(ImageSourceResolver.TryGetAbsoluteLocalUri(source, out var uri));
        Assert.True(uri.IsAbsoluteUri);
        Assert.NotEqual(Uri.UriSchemeHttp, uri.Scheme);
        Assert.NotEqual(Uri.UriSchemeHttps, uri.Scheme);
    }

    [Fact]
    public void TryGetAbsoluteLocalUri_aplica_trim_no_source()
    {
        Assert.True(ImageSourceResolver.TryGetAbsoluteLocalUri("  content://media/1  ", out var uri));
        Assert.Equal("content", uri.Scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dog.png")]
    [InlineData("imagens/foto.png")]
    [InlineData("http://exemplo.com/foto.png")]
    [InlineData("https://exemplo.com/foto.png")]
    public void TryGetAbsoluteLocalUri_rejeita_relativos_vazios_e_http(string? source)
    {
        Assert.False(ImageSourceResolver.TryGetAbsoluteLocalUri(source, out _));
    }

    // ── TryGetLocalFilePath ──────────────────────────────────────────────

    [Fact]
    public void TryGetLocalFilePath_aceita_caminho_absoluto_do_host()
    {
        // Caminho rooted real do host (com espaço no nome, caso comum em fotos).
        var caminho = Path.Combine(Path.GetTempPath(), "foto do teste.png");

        Assert.True(ImageSourceResolver.TryGetLocalFilePath(caminho, out var path));
        Assert.Equal(caminho, path);
    }

    [Fact]
    public void TryGetLocalFilePath_aceita_caminho_windows_com_drive()
    {
        Assert.True(ImageSourceResolver.TryGetLocalFilePath(@"C:\Temp\foto.png", out var path));
        Assert.Equal(@"C:\Temp\foto.png", path);
    }

    [Fact]
    public void TryGetLocalFilePath_converte_uri_file_para_caminho_local()
    {
        Assert.True(ImageSourceResolver.TryGetLocalFilePath("file:///C:/Temp/foto.png", out var path));
        Assert.Equal(@"C:\Temp\foto.png", path);
    }

    [Fact]
    public void TryGetLocalFilePath_aceita_caminho_unc()
    {
        Assert.True(ImageSourceResolver.TryGetLocalFilePath(@"\\servidor\fotos\a.png", out var path));
        Assert.Equal(@"\\servidor\fotos\a.png", path);
    }

    [Fact]
    public void TryGetLocalFilePath_aplica_trim_no_source()
    {
        Assert.True(ImageSourceResolver.TryGetLocalFilePath("  C:\\Temp\\a.png  ", out var path));
        Assert.Equal(@"C:\Temp\a.png", path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("foto.png")]
    [InlineData("imagens/foto.png")]
    [InlineData("http://exemplo.com/foto.png")]
    [InlineData("https://exemplo.com/foto.png")]
    [InlineData("content://media/external/images/1")]
    [InlineData("ms-appx:///Resources/a.png")]
    public void TryGetLocalFilePath_rejeita_relativos_vazios_e_esquemas_nao_file(string? source)
    {
        Assert.False(ImageSourceResolver.TryGetLocalFilePath(source, out var path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void TryGetLocalFilePath_rejeita_pseudo_uri_invalida_com_separador_de_esquema()
    {
        // Não parseia como URI absoluta (espaço no esquema), mas contém "://": nunca é caminho local.
        Assert.False(ImageSourceResolver.TryGetLocalFilePath("ht tp://exemplo/a.png", out _));
    }

    // ── ResourceName ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("dog.png", "dog")]
    [InlineData("dog", "dog")]
    [InlineData("Resources/Images/dog.png", "dog")]
    [InlineData(@"Resources\Images\dog.png", "dog")]
    [InlineData(@"C:\fotos\dog.jpeg", "dog")]
    [InlineData("file:///C:/pasta/icone.webp", "icone")]
    [InlineData("  dog.png  ", "dog")]
    public void ResourceName_extrai_o_nome_sem_pasta_e_sem_extensao(string source, string esperado)
    {
        Assert.Equal(esperado, ImageSourceResolver.ResourceName(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://exemplo.com/dog.png")] // URI absoluta não-file não vira resource
    [InlineData("content://media/external/1")]
    [InlineData(".png")] // só extensão: nome vazio
    public void ResourceName_devolve_null_quando_nao_ha_nome_de_resource(string? source)
    {
        Assert.Null(ImageSourceResolver.ResourceName(source));
    }

    // ── MauiResourcePath ─────────────────────────────────────────────────

    [Theory]
    [InlineData("dog", "dog.png")] // invariante do projeto: sem extensão → fallback .png
    [InlineData(" dog ", "dog.png")]
    [InlineData(@"Resources\Images\dog", "Resources/Images/dog.png")]
    [InlineData("/dog", "dog.png")]
    [InlineData("pasta.v2/dog", "pasta.v2/dog.png")] // ponto na pasta não conta como extensão
    public void MauiResourcePath_adiciona_fallback_png_quando_nao_ha_extensao(string source, string esperado)
    {
        Assert.Equal(esperado, ImageSourceResolver.MauiResourcePath(source));
    }

    [Theory]
    [InlineData("dog.jpg", "dog.jpg")]
    [InlineData("dog.PNG", "dog.PNG")]
    [InlineData(@"Resources\Images\dog.webp", "Resources/Images/dog.webp")]
    [InlineData("/icons/dog.svg", "icons/dog.svg")]
    public void MauiResourcePath_preserva_extensao_existente_e_normaliza_separadores(string source, string esperado)
    {
        Assert.Equal(esperado, ImageSourceResolver.MauiResourcePath(source));
    }
}
