using Microsoft.Maui.Controls;

namespace Agile.Maui;

/// <summary>Liga o conteúdo de uma célula à árvore lógica da lista enquanto a célula está na janela.</summary>
// Sem pai, o MAUI não acha a página a partir de um campo do card: o HideSoftInputOnTapped sobe pelo
// Parent até a ContentPage e exige Window, e desistia. O CollectionView do MAUI liga do mesmo jeito
// (AddLogicalChild depois do BindingContext). Só na janela porque o pai fecha o ciclo célula → lista →
// célula, que no iOS não é coletado: fora dela o ciclo some e a lista fechada não vaza.
internal static class CellLogicalParent
{
    internal static void Update(VirtualizedCollectionView? owner, View? content, bool inWindow)
    {
        if (content is null)
            return;

        if (inWindow && owner is not null)
        {
            // Conteúdo com outro pai não é desta célula.
            if (content.Parent is null)
                owner.AddLogicalChild(content);

            return;
        }

        if (content.Parent is VirtualizedCollectionView current)
            current.RemoveLogicalChild(content);
    }
}
