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
    /// <summary>Decorações que envolvem o item, da mais externa para a mais interna (null = nenhuma).</summary>
    public readonly IReadOnlyList<FlowDecorationFrame>? Decorations;

    public FlowItem(ILayoutElement element, float height, FlowItemKind kind = FlowItemKind.Block,
        int groupId = 0, float leftInset = 0f, float width = 0f)
        : this(element, height, kind, groupId, leftInset, width, null)
    {
    }

    FlowItem(ILayoutElement element, float height, FlowItemKind kind, int groupId, float leftInset, float width,
        IReadOnlyList<FlowDecorationFrame>? decorations)
    {
        Element = element;
        Height = height;
        Kind = kind;
        GroupId = groupId;
        LeftInset = leftInset;
        Width = width;
        Decorations = decorations;
    }

    /// <summary>Cria uma cópia com o inset horizontal deslocado e (se ainda não definida) a largura fixada.</summary>
    public FlowItem ShiftLeft(float dx, float fallbackWidth)
    {
        IReadOnlyList<FlowDecorationFrame>? decorations = Decorations;
        if (decorations is not null && dx != 0f)
        {
            var shifted = new FlowDecorationFrame[decorations.Count];
            for (int i = 0; i < shifted.Length; i++)
            {
                FlowDecorationFrame f = decorations[i];
                shifted[i] = new FlowDecorationFrame(f.Decoration, f.LeftInset + dx, f.Width);
            }
            decorations = shifted;
        }

        return new(Element, Height, Kind, GroupId, LeftInset + dx, Width > 0f ? Width : fallbackWidth, decorations);
    }

    /// <summary>Cria uma cópia com a largura informada, preservando as decorações.</summary>
    public FlowItem WithWidth(float width) =>
        new(Element, Height, Kind, GroupId, LeftInset, width, Decorations);

    /// <summary>
    /// Envolve o item com uma decoração que ocupa [0, outerWidth] no referencial do decorador.
    /// A largura do item é fixada para não depender mais da largura externa.
    /// </summary>
    public FlowItem Decorate(FlowDecoration decoration, float outerWidth)
    {
        int count = Decorations?.Count ?? 0;
        var frames = new FlowDecorationFrame[count + 1];
        frames[0] = new FlowDecorationFrame(decoration, 0f, outerWidth);
        for (int i = 0; i < count; i++)
            frames[i + 1] = Decorations![i];

        float width = Width > 0f ? Width : MathF.Max(0f, outerWidth - LeftInset);
        return new(Element, Height, Kind, GroupId, LeftInset, width, frames);
    }
}

/// <summary>
/// Decoração (fundo ou borda) de um container paginável. Em vez de ser repetida em cada fatia do
/// fluxo, o motor de layout a desenha uma única vez por página, sobre a união das fatias contíguas
/// que a carregam: um bloco arredondado continua sendo um único retângulo arredondado, e um bloco
/// partido entre páginas fecha e reabre os cantos na quebra.
/// </summary>
public sealed class FlowDecoration
{
    public FlowDecoration(ILayoutElement element, bool drawOverContent)
    {
        Element = element;
        DrawOverContent = drawOverContent;
    }

    /// <summary>Elemento (sem filho) que pinta a decoração nos bounds do trecho.</summary>
    public ILayoutElement Element { get; }

    /// <summary>true para bordas (pintadas depois do conteúdo); false para fundos (pintados antes).</summary>
    public bool DrawOverContent { get; }
}

/// <summary>Decoração aplicada a um item, com a posição horizontal do decorador na área de conteúdo.</summary>
public readonly struct FlowDecorationFrame
{
    public readonly FlowDecoration Decoration;
    /// <summary>Deslocamento horizontal do decorador a partir da esquerda da área de conteúdo.</summary>
    public readonly float LeftInset;
    /// <summary>Largura do decorador.</summary>
    public readonly float Width;

    public FlowDecorationFrame(FlowDecoration decoration, float leftInset, float width)
    {
        Decoration = decoration;
        LeftInset = leftInset;
        Width = width;
    }
}

/// <summary>
/// Container que pode fatiar seu conteúdo em itens de fluxo para permitir quebra de página.
/// Elementos que não implementam isto são tratados como um único bloco atômico.
/// </summary>
public interface IFlowContainer
{
    IEnumerable<FlowItem> Flatten(float width);
}
