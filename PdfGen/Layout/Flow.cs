namespace Agile.Maui.PdfGen.Layout;

/// <summary>Papel de um item no fluxo vertical, usado na quebra de página.</summary>
public enum FlowItemKind
{
    /// <summary>Bloco atômico comum.</summary>
    Block,
    /// <summary>Cabeçalho de tabela: repetido no topo de cada página que contém linhas da tabela.</summary>
    TableHeader,
    /// <summary>Linha de corpo de tabela.</summary>
    TableRow
}

/// <summary>
/// Unidade indivisível do fluxo vertical de conteúdo. O paginador empacota estes itens em páginas.
/// Height é medido na largura efetiva do item; LeftInset/Width posicionam-no horizontalmente dentro
/// da área de conteúdo (usados por decoradores transparentes como Padding).
/// </summary>
public readonly struct FlowItem
{
    public readonly ILayoutElement Element;
    public readonly float Height;
    public readonly FlowItemKind Kind;
    /// <summary>Identifica a tabela dona (para repetição de cabeçalho). 0 = nenhuma.</summary>
    public readonly int GroupId;
    /// <summary>Deslocamento horizontal a partir da esquerda da área de conteúdo.</summary>
    public readonly float LeftInset;
    /// <summary>Largura do item. &lt;= 0 significa "usar a largura de conteúdo menos o inset".</summary>
    public readonly float Width;

    public FlowItem(ILayoutElement element, float height, FlowItemKind kind = FlowItemKind.Block,
        int groupId = 0, float leftInset = 0f, float width = 0f)
    {
        Element = element;
        Height = height;
        Kind = kind;
        GroupId = groupId;
        LeftInset = leftInset;
        Width = width;
    }

    /// <summary>Cria uma cópia com o inset horizontal deslocado e (se ainda não definida) a largura fixada.</summary>
    public FlowItem ShiftLeft(float dx, float fallbackWidth) =>
        new(Element, Height, Kind, GroupId, LeftInset + dx, Width > 0f ? Width : fallbackWidth);
}

/// <summary>
/// Container que pode fatiar seu conteúdo em itens de fluxo para permitir quebra de página.
/// Elementos que não implementam isto são tratados como um único bloco atômico.
/// </summary>
public interface IFlowContainer
{
    IEnumerable<FlowItem> Flatten(float width);
}
