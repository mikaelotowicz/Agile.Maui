namespace Agile.Maui;

public sealed class ChipSelectionChangedEventArgs : EventArgs
{
    public ChipSelectionChangedEventArgs(object? selectedItem, IReadOnlyList<object?> selectedItems)
    {
        SelectedItem = selectedItem;
        SelectedItems = selectedItems;
    }

    public object? SelectedItem { get; }
    public IReadOnlyList<object?> SelectedItems { get; }
}
