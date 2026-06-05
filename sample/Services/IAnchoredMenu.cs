namespace sample.Services;

/// <summary>Uma ação exibida no menu ancorado.</summary>
/// <param name="Text">Rótulo exibido no item.</param>
/// <param name="Invoke">Executado quando o item é escolhido.</param>
/// <param name="Icon">Glyph (fonte Material Design Icons) exibido à esquerda. Opcional.</param>
/// <param name="Enabled">Se o item está habilitado para seleção.</param>
public record MenuAction(string Text, Action Invoke, string? Icon = null, bool Enabled = true);

/// <summary>
/// Exibe um menu de opções ancorado a uma <see cref="View"/>, usando o componente nativo de
/// cada plataforma (Windows: <c>MenuFlyout</c>; Android: <c>PopupMenu</c>; iOS/Mac:
/// <c>UIAlertController</c> estilo <i>action sheet</i> com popover ancorado no iPad).
/// </summary>
public interface IAnchoredMenu
{
    /// <summary>Abre o menu junto à <paramref name="anchor"/> com as ações informadas.</summary>
    void Show(View anchor, IReadOnlyList<MenuAction> actions, VisualElement? verticalAnchor = null);
}
