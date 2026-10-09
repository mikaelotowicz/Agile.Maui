using Microsoft.Maui.Controls;
using Xunit;

namespace Agile.Maui.GalleryTests;

public class ImageViewTests
{
    // ── Defaults ─────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_correspondem_ao_contrato_documentado()
    {
        var iv = new ImageView();

        Assert.Null(iv.Source);
        Assert.False(iv.LegacyIsUrl);
        Assert.Null(iv.Placeholder);
        Assert.Equal(5f, iv.MaxZoom);
        Assert.True(iv.EnableFullscreen);
        Assert.Null(iv.FullscreenSource);
        Assert.Equal(ZoomImageAspect.CenterCrop, iv.AspectMode);
        Assert.Equal(720, iv.DecodeMaxPx);
        Assert.False(iv.IsLoading);
        Assert.Null(iv.ImageLoadedCommand);
        Assert.Null(iv.ImageFailedCommand);
    }

    [Fact]
    public void ZoomImageAspect_tem_exatamente_os_dois_modos_do_contrato()
    {
        Assert.Equal(
            new[] { ZoomImageAspect.CenterCrop, ZoomImageAspect.AspectFit },
            Enum.GetValues<ZoomImageAspect>());
    }

    // ── Round-trip das bindable properties ───────────────────────────────

    [Fact]
    public void Propriedades_fazem_round_trip_de_set_e_get()
    {
        var comando = new ComandoFake();
        var iv = new ImageView
        {
            Source           = "https://exemplo.com/a.png",
            Placeholder      = "placeholder",
            FullscreenSource = "https://exemplo.com/a_full.png",
            MaxZoom          = 8f,
            EnableFullscreen = false,
            AspectMode       = ZoomImageAspect.AspectFit,
            DecodeMaxPx      = 1080,
            ImageLoadedCommand = comando,
            ImageFailedCommand = comando,
        };

        Assert.Equal("https://exemplo.com/a.png", iv.Source);
        Assert.Equal("placeholder", iv.Placeholder);
        Assert.Equal("https://exemplo.com/a_full.png", iv.FullscreenSource);
        Assert.Equal(8f, iv.MaxZoom);
        Assert.False(iv.EnableFullscreen);
        Assert.Equal(ZoomImageAspect.AspectFit, iv.AspectMode);
        Assert.Equal(1080, iv.DecodeMaxPx);
        Assert.Same(comando, iv.ImageLoadedCommand);
        Assert.Same(comando, iv.ImageFailedCommand);
    }

    [Fact]
    public void Source_pode_voltar_a_null()
    {
        var iv = new ImageView { Source = "a.png" };
        iv.Source = null;
        Assert.Null(iv.Source);
    }

    [Fact]
    public void IsUrl_legado_e_lido_via_LegacyIsUrl()
    {
        var iv = new ImageView();
        iv.SetValue(ImageView.IsUrlProperty, true);
        Assert.True(iv.LegacyIsUrl);
    }

    [Fact]
    public void Source_dispara_PropertyChanged()
    {
        var iv = new ImageView();
        var nomes = new List<string?>();
        iv.PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        iv.Source = "a.png";

        Assert.Contains(nameof(ImageView.Source), nomes);
    }

    // ── Validação de valores ─────────────────────────────────────────────

    [Fact]
    public void MaxZoom_aceita_o_minimo_1()
    {
        var iv = new ImageView { MaxZoom = 1f };
        Assert.Equal(1f, iv.MaxZoom);
    }

    [Theory]
    [InlineData(0.99f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void MaxZoom_ignora_valores_abaixo_de_1_mantendo_o_anterior(float valor)
    {
        // validateValue=false no MAUI é rejeição silenciosa (log + no-op), não exceção.
        var iv = new ImageView { MaxZoom = 4f };
        iv.MaxZoom = valor;
        Assert.Equal(4f, iv.MaxZoom);
    }

    [Fact]
    public void DecodeMaxPx_aceita_o_minimo_64()
    {
        var iv = new ImageView { DecodeMaxPx = 64 };
        Assert.Equal(64, iv.DecodeMaxPx);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(0)]
    [InlineData(-1)]
    public void DecodeMaxPx_ignora_valores_abaixo_de_64_mantendo_o_anterior(int valor)
    {
        var iv = new ImageView(); // default 720
        iv.DecodeMaxPx = valor;
        Assert.Equal(720, iv.DecodeMaxPx);
    }

    // ── IsLoading (read-only) ────────────────────────────────────────────

    [Fact]
    public void IsLoading_ignora_escrita_publica_por_ser_read_only()
    {
        var iv = new ImageView();

        // SetValue público numa propriedade read-only é ignorado pelo MAUI (log + no-op).
        iv.SetValue(ImageView.IsLoadingProperty, true);

        Assert.False(iv.IsLoading);
    }

    [Fact]
    public void SetIsLoading_interno_altera_o_valor_e_dispara_PropertyChanged()
    {
        var iv = new ImageView();
        var nomes = new List<string?>();
        iv.PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        iv.SetIsLoading(true);

        Assert.True(iv.IsLoading);
        Assert.Contains(nameof(ImageView.IsLoading), nomes);
    }

    [Fact]
    public void SetIsLoading_com_o_mesmo_valor_nao_redispara_PropertyChanged()
    {
        var iv = new ImageView();
        iv.SetIsLoading(true);

        var disparos = 0;
        iv.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImageView.IsLoading)) disparos++;
        };

        iv.SetIsLoading(true);

        Assert.Equal(0, disparos);
    }

    // ── RaiseImageLoaded / RaiseImageFailed ──────────────────────────────

    [Fact]
    public void RaiseImageLoaded_dispara_o_evento_com_sender_correto_e_encerra_IsLoading()
    {
        var iv = new ImageView();
        iv.SetIsLoading(true);

        object? sender = null;
        EventArgs? args = null;
        iv.ImageLoaded += (s, e) => { sender = s; args = e; };

        iv.RaiseImageLoaded();

        Assert.Same(iv, sender);
        Assert.Same(EventArgs.Empty, args);
        Assert.False(iv.IsLoading);
    }

    [Fact]
    public void RaiseImageLoaded_executa_o_comando_quando_CanExecute_permite()
    {
        var comando = new ComandoFake();
        var iv = new ImageView { ImageLoadedCommand = comando };

        iv.RaiseImageLoaded();

        Assert.Equal(1, comando.Execucoes);
        Assert.Null(comando.ParametrosExecute[0]); // o comando é chamado sem parâmetro
        Assert.Single(comando.ParametrosCanExecute);
    }

    [Fact]
    public void RaiseImageLoaded_respeita_CanExecute_false()
    {
        var comando = new ComandoFake { PodeExecutar = false };
        var iv = new ImageView { ImageLoadedCommand = comando };

        iv.RaiseImageLoaded();

        Assert.Equal(0, comando.Execucoes);
        Assert.Single(comando.ParametrosCanExecute);
    }

    [Fact]
    public void RaiseImageLoaded_sem_assinantes_e_sem_comando_nao_lanca()
    {
        var iv = new ImageView();
        iv.RaiseImageLoaded();
    }

    [Fact]
    public void RaiseImageFailed_dispara_o_evento_e_encerra_IsLoading()
    {
        var iv = new ImageView();
        iv.SetIsLoading(true);

        object? sender = null;
        iv.ImageFailed += (s, _) => sender = s;

        iv.RaiseImageFailed();

        Assert.Same(iv, sender);
        Assert.False(iv.IsLoading);
    }

    [Fact]
    public void RaiseImageFailed_executa_o_comando_respeitando_CanExecute()
    {
        var permitido = new ComandoFake();
        var negado    = new ComandoFake { PodeExecutar = false };

        var iv = new ImageView { ImageFailedCommand = permitido };
        iv.RaiseImageFailed();
        Assert.Equal(1, permitido.Execucoes);

        iv.ImageFailedCommand = negado;
        iv.RaiseImageFailed();
        Assert.Equal(0, negado.Execucoes);
    }

    [Fact]
    public void RaiseImageFailed_sem_assinantes_e_sem_comando_nao_lanca()
    {
        var iv = new ImageView();
        iv.RaiseImageFailed();
    }
}
