using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Agile.Maui;

/// <summary>Item pronto para uso com <see cref="ChipGroup"/>.</summary>
public sealed class ChipItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private object? _value;
    private bool _isSelected;
    private bool _isEnabled = true;

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
