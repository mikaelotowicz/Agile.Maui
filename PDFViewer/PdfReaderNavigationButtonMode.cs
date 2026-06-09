namespace Agile.Maui;

/// <summary>Modo do botao de navegacao exibido na toolbar do <see cref="PdfReaderView"/>.</summary>
public enum PdfReaderNavigationButtonMode
{
    /// <summary>Nao exibe botao de navegacao.</summary>
    None,

    /// <summary>Escolhe automaticamente entre voltar e abrir menu, quando possivel.</summary>
    Auto,

    /// <summary>Exibe botao de menu e tenta abrir Shell flyout ou FlyoutPage.</summary>
    Menu,

    /// <summary>Exibe botao de voltar e tenta voltar na pilha de navegacao.</summary>
    Back,
}
