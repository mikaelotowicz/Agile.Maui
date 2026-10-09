using Microsoft.Maui.Controls;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// Header/Footer passados como View não entram na árvore lógica do controle — a herança
// de BindingContext é feita manualmente (OnBindingContextChanged/OnHeaderFooterChanged).
public class VirtualizedCollectionViewHeaderFooterTests
{
    [Fact]
    public void Header_setado_antes_do_contexto_herda_o_BindingContext()
    {
        var header = new Label();
        var view = new VirtualizedCollectionView { Header = header };
        var contexto = new object();

        view.BindingContext = contexto;

        Assert.Same(contexto, header.BindingContext);
    }

    [Fact]
    public void Header_setado_depois_do_contexto_herda_imediatamente()
    {
        var view = new VirtualizedCollectionView();
        var contexto = new object();
        view.BindingContext = contexto;
        var header = new Label();

        view.Header = header;

        Assert.Same(contexto, header.BindingContext);
    }

    [Fact]
    public void Header_com_contexto_proprio_nao_e_sobrescrito()
    {
        var proprio = new object();
        var header = new Label { BindingContext = proprio };
        var view = new VirtualizedCollectionView { Header = header };

        view.BindingContext = new object();

        Assert.Same(proprio, header.BindingContext);
    }

    [Fact]
    public void Heranca_nao_acompanha_trocas_posteriores_do_contexto()
    {
        // Contrato atual: a herança só acontece enquanto o contexto do Header é nulo.
        // Depois da primeira herança ele fica "congelado" — teste documenta o comportamento.
        var header = new Label();
        var view = new VirtualizedCollectionView { Header = header };
        var primeiro = new object();
        view.BindingContext = primeiro;

        view.BindingContext = new object();

        Assert.Same(primeiro, header.BindingContext);
    }

    [Fact]
    public void Footer_herda_o_BindingContext_como_o_header()
    {
        var footer = new Label();
        var view = new VirtualizedCollectionView { Footer = footer };
        var contexto = new object();

        view.BindingContext = contexto;

        Assert.Same(contexto, footer.BindingContext);
    }

    [Fact]
    public void Footer_setado_depois_do_contexto_herda_imediatamente()
    {
        var view = new VirtualizedCollectionView();
        var contexto = new object();
        view.BindingContext = contexto;
        var footer = new Label();

        view.Footer = footer;

        Assert.Same(contexto, footer.BindingContext);
    }

    [Fact]
    public void Header_e_footer_que_nao_sao_BindableObject_nao_lancam()
    {
        var view = new VirtualizedCollectionView
        {
            Header = "cabeçalho em string",
            Footer = 42,
        };

        view.BindingContext = new object();

        Assert.Equal("cabeçalho em string", view.Header);
        Assert.Equal(42, view.Footer);
    }

    [Fact]
    public void Header_nulo_durante_troca_de_contexto_nao_lanca()
    {
        var view = new VirtualizedCollectionView { Header = null, Footer = null };

        view.BindingContext = new object();

        Assert.Null(view.Header);
    }
}
