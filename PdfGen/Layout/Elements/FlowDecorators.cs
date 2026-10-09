namespace Agile.Maui.PdfGen.Layout.Elements;

internal static class FlowDecorators
{
    /// <summary>
    /// Anexa a decoração a cada fatia do fluxo do filho, sem embrulhar o elemento da fatia: o motor de
    /// layout desenha a decoração uma vez por página sobre as fatias contíguas (ver <see cref="FlowDecoration"/>).
    /// </summary>
    public static IEnumerable<FlowItem> Decorate(IFlowContainer child, float width, ILayoutElement decoration, bool drawOverContent)
    {
        var shared = new FlowDecoration(decoration, drawOverContent);
        foreach (FlowItem item in child.Flatten(width))
            yield return item.Decorate(shared, width);
    }
}
