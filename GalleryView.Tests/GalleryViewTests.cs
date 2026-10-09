using System.Collections.ObjectModel;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Agile.Maui.GalleryTests;

public class GalleryViewTests
{
    // ── Defaults ─────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_correspondem_ao_contrato_documentado()
    {
        var g = new GalleryView();

        Assert.Null(g.Images);
        Assert.False(g.LegacyIsUrl);
        Assert.Null(g.Placeholder);
        Assert.Equal(0, g.SelectedIndex);
        Assert.Equal(ZoomImageAspect.CenterCrop, g.AspectMode);
        Assert.Equal(5f, g.MaxZoom);
        Assert.False(g.ShowIndicator);
        Assert.Equal(Colors.White, g.IndicatorColor);
        Assert.Equal(new Color(1f, 1f, 1f, 0.5f), g.IndicatorInactiveColor);
        Assert.Equal(720, g.ThumbMaxPx);
        Assert.Equal(ImageAlignment.Center, g.VerticalImageAlignment);
        Assert.Null(g.SelectionChangedCommand);
        Assert.Null(g.ImageLoadedCommand);
        Assert.Null(g.ImageFailedCommand);
    }

    [Fact]
    public void ImageAlignment_tem_exatamente_os_tres_valores_do_contrato()
    {
        Assert.Equal(
            new[] { ImageAlignment.Center, ImageAlignment.Start, ImageAlignment.End },
            Enum.GetValues<ImageAlignment>());
    }

    // ── Round-trip das bindable properties ───────────────────────────────

    [Fact]
    public void Propriedades_fazem_round_trip_de_set_e_get()
    {
        var comando = new ComandoFake();
        var imagens = new List<string> { "a.png", "https://exemplo.com/b.png" };

        var g = new GalleryView
        {
            Images                 = imagens,
            Placeholder            = "placeholder",
            SelectedIndex          = 1,
            AspectMode             = ZoomImageAspect.AspectFit,
            MaxZoom                = 3f,
            ShowIndicator          = true,
            IndicatorColor         = Colors.Red,
            IndicatorInactiveColor = Colors.Gray,
            ThumbMaxPx             = 1024,
            VerticalImageAlignment = ImageAlignment.End,
            SelectionChangedCommand = comando,
            ImageLoadedCommand      = comando,
            ImageFailedCommand      = comando,
        };

        Assert.Same(imagens, g.Images);
        Assert.Equal("placeholder", g.Placeholder);
        Assert.Equal(1, g.SelectedIndex);
        Assert.Equal(ZoomImageAspect.AspectFit, g.AspectMode);
        Assert.Equal(3f, g.MaxZoom);
        Assert.True(g.ShowIndicator);
        Assert.Equal(Colors.Red, g.IndicatorColor);
        Assert.Equal(Colors.Gray, g.IndicatorInactiveColor);
        Assert.Equal(1024, g.ThumbMaxPx);
        Assert.Equal(ImageAlignment.End, g.VerticalImageAlignment);
        Assert.Same(comando, g.SelectionChangedCommand);
        Assert.Same(comando, g.ImageLoadedCommand);
        Assert.Same(comando, g.ImageFailedCommand);
    }

    [Fact]
    public void Images_aceita_ObservableCollection_e_pode_voltar_a_null()
    {
        var colecao = new ObservableCollection<string> { "a.png" };
        var g = new GalleryView { Images = colecao };

        Assert.Same(colecao, g.Images);

        g.Images = null;
        Assert.Null(g.Images);
    }

    [Fact]
    public void IsUrl_legado_e_lido_via_LegacyIsUrl()
    {
        var g = new GalleryView();
        g.SetValue(GalleryView.IsUrlProperty, true);
        Assert.True(g.LegacyIsUrl);
    }

    [Fact]
    public void SelectedIndex_dispara_PropertyChanged()
    {
        var g = new GalleryView();
        var nomes = new List<string?>();
        g.PropertyChanged += (_, e) => nomes.Add(e.PropertyName);

        g.SelectedIndex = 2;

        Assert.Contains(nameof(GalleryView.SelectedIndex), nomes);
    }

    // ── Validação de valores ─────────────────────────────────────────────

    [Fact]
    public void SelectedIndex_ignora_valores_negativos_mantendo_o_anterior()
    {
        // validateValue=false no MAUI é rejeição silenciosa (log + no-op), não exceção.
        var g = new GalleryView { SelectedIndex = 2 };
        g.SelectedIndex = -1;
        Assert.Equal(2, g.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_nao_e_clampado_pelo_controle()
    {
        // Contrato atual: o controle aceita qualquer índice >= 0; o clamp ao tamanho de Images
        // é responsabilidade dos handlers de plataforma.
        var g = new GalleryView { Images = new List<string> { "a.png" } };
        g.SelectedIndex = 42;
        Assert.Equal(42, g.SelectedIndex);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0f)]
    public void MaxZoom_ignora_valores_abaixo_de_1_mantendo_o_anterior(float valor)
    {
        var g = new GalleryView(); // default 5f
        g.MaxZoom = valor;
        Assert.Equal(5f, g.MaxZoom);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(0)]
    public void ThumbMaxPx_ignora_valores_abaixo_de_64_mantendo_o_anterior(int valor)
    {
        var g = new GalleryView(); // default 720
        g.ThumbMaxPx = valor;
        Assert.Equal(720, g.ThumbMaxPx);
    }

    // ── RaiseSelectionChanged ────────────────────────────────────────────

    [Fact]
    public void RaiseSelectionChanged_dispara_o_evento_com_o_indice()
    {
        var g = new GalleryView();
        object? sender = null;
        GalleryIndexChangedEventArgs? args = null;
        g.SelectionChanged += (s, e) => { sender = s; args = e; };

        g.RaiseSelectionChanged(3);

        Assert.Same(g, sender);
        Assert.NotNull(args);
        Assert.Equal(3, args.Index);
    }

    [Fact]
    public void RaiseSelectionChanged_passa_o_indice_para_CanExecute_e_Execute_do_comando()
    {
        var comando = new ComandoFake();
        var g = new GalleryView { SelectionChangedCommand = comando };

        g.RaiseSelectionChanged(7);

        Assert.Equal(new object?[] { 7 }, comando.ParametrosCanExecute);
        Assert.Equal(new object?[] { 7 }, comando.ParametrosExecute);
    }

    [Fact]
    public void RaiseSelectionChanged_respeita_CanExecute_false()
    {
        var comando = new ComandoFake { PodeExecutar = false };
        var g = new GalleryView { SelectionChangedCommand = comando };

        g.RaiseSelectionChanged(1);

        Assert.Equal(0, comando.Execucoes);
    }

    [Fact]
    public void RaiseSelectionChanged_sem_assinantes_e_sem_comando_nao_lanca()
    {
        var g = new GalleryView();
        g.RaiseSelectionChanged(0);
    }

    // ── RaiseImageLoaded / RaiseImageFailed ──────────────────────────────

    [Fact]
    public void RaiseImageLoaded_dispara_o_evento_e_o_comando_sem_parametro()
    {
        var comando = new ComandoFake();
        var g = new GalleryView { ImageLoadedCommand = comando };
        var disparos = 0;
        g.ImageLoaded += (_, _) => disparos++;

        g.RaiseImageLoaded();

        Assert.Equal(1, disparos);
        Assert.Equal(1, comando.Execucoes);
        Assert.Null(comando.ParametrosExecute[0]);
    }

    [Fact]
    public void RaiseImageFailed_dispara_o_evento_e_respeita_CanExecute()
    {
        var comando = new ComandoFake { PodeExecutar = false };
        var g = new GalleryView { ImageFailedCommand = comando };
        var disparos = 0;
        g.ImageFailed += (_, _) => disparos++;

        g.RaiseImageFailed();

        Assert.Equal(1, disparos);
        Assert.Equal(0, comando.Execucoes);
    }

    [Fact]
    public void RaiseImageLoaded_e_RaiseImageFailed_sem_assinantes_nao_lancam()
    {
        var g = new GalleryView();
        g.RaiseImageLoaded();
        g.RaiseImageFailed();
    }

    // ── GalleryIndexChangedEventArgs ─────────────────────────────────────

    [Fact]
    public void GalleryIndexChangedEventArgs_expoe_o_indice_recebido()
    {
        var args = new GalleryIndexChangedEventArgs(5);
        Assert.Equal(5, args.Index);
    }
}
