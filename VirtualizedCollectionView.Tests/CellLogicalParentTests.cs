using Microsoft.Maui.Controls;
using Xunit;

namespace Agile.Maui.VirtualizedCollectionTests;

// Regressão: o card de uma célula iOS não tinha pai lógico, e o HideSoftInputOnTapped do MAUI, que
// sobe pelo Parent até a página, não fechava o teclado aberto por um campo do card.
public class CellLogicalParentTests
{
    [Fact]
    public void Na_janela_o_conteudo_ganha_a_lista_como_pai()
    {
        var lista = new VirtualizedCollectionView();
        var card = new Grid();

        CellLogicalParent.Update(lista, card, inWindow: true);

        Assert.Same(lista, card.Parent);
        Assert.Contains(card, ((IElementController)lista).LogicalChildren);
    }

    [Fact]
    public void Fora_da_janela_o_conteudo_sai_da_arvore()
    {
        var lista = new VirtualizedCollectionView();
        var card = new Grid();
        CellLogicalParent.Update(lista, card, inWindow: true);

        CellLogicalParent.Update(lista, card, inWindow: false);

        Assert.Null(card.Parent);
        Assert.DoesNotContain(card, ((IElementController)lista).LogicalChildren);
    }

    // A célula reciclada sem o dono vivo (lista coletada) ainda desliga o conteúdo.
    [Fact]
    public void Liberar_sem_dono_desliga_do_pai_atual()
    {
        var lista = new VirtualizedCollectionView();
        var card = new Grid();
        CellLogicalParent.Update(lista, card, inWindow: true);

        CellLogicalParent.Update(null, card, inWindow: false);

        Assert.Null(card.Parent);
    }

    [Fact]
    public void Voltar_a_janela_duas_vezes_nao_duplica_o_filho()
    {
        var lista = new VirtualizedCollectionView();
        var card = new Grid();

        CellLogicalParent.Update(lista, card, inWindow: true);
        CellLogicalParent.Update(lista, card, inWindow: true);

        Assert.Single(((IElementController)lista).LogicalChildren);
    }

    [Fact]
    public void Conteudo_com_outro_pai_nao_e_tocado()
    {
        var lista = new VirtualizedCollectionView();
        var dono = new ContentView();
        var card = new Label();
        dono.Content = card;

        CellLogicalParent.Update(lista, card, inWindow: true);
        CellLogicalParent.Update(lista, card, inWindow: false);

        Assert.Same(dono, card.Parent);
    }

    [Fact]
    public void O_item_do_card_nao_e_trocado_pelo_contexto_da_lista()
    {
        var item = new object();
        var lista = new VirtualizedCollectionView { BindingContext = new object() };
        var card = new Grid { BindingContext = item };

        CellLogicalParent.Update(lista, card, inWindow: true);
        lista.BindingContext = new object();

        Assert.Same(item, card.BindingContext);
    }

    [Fact]
    public void Sem_dono_na_janela_nada_acontece()
    {
        var card = new Grid();

        CellLogicalParent.Update(null, card, inWindow: true);

        Assert.Null(card.Parent);
    }
}
