namespace Agile.Maui;

/// <summary>Modo de distribuicao visual dos chips no <see cref="ChipGroup"/>.</summary>
public enum ChipGroupLayoutMode
{
    /// <summary>Organiza os chips em linhas e quebra automaticamente quando falta espaco.</summary>
    Wrap,

    /// <summary>Organiza os chips em uma unica linha com rolagem horizontal.</summary>
    Horizontal,

    /// <summary>Organiza os chips em uma lista vertical.</summary>
    Vertical,
}
