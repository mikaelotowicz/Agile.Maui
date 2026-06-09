using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace sample;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public ObservableCollection<string> UrlImages { get; } =
    [
        "https://picsum.photos/seed/gallery1/800/500",
        "https://picsum.photos/seed/gallery2/800/600",
        "https://picsum.photos/seed/gallery3/600/800",
        "https://picsum.photos/seed/gallery4/800/500",
        "https://picsum.photos/seed/gallery5/900/600",
    ];

    public ObservableCollection<string> LocalImages { get; } =
    [
        "gallery_01",
        "gallery_02",
        "gallery_03",
        "gallery_04",
        "gallery_05",
    ];

    private int _urlSelectedIndex;
    public int UrlSelectedIndex
    {
        get => _urlSelectedIndex;
        set => Set(ref _urlSelectedIndex, value);
    }

    private int _localSelectedIndex;
    public int LocalSelectedIndex
    {
        get => _localSelectedIndex;
        set => Set(ref _localSelectedIndex, value);
    }

    private string _logText = "-";
    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    private readonly List<string> _log = [];

    private void AppendLog(string entry)
    {
        _log.Insert(0, entry);
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
        LogText = string.Join("\n", _log);
    }

    public ICommand ImageLoadedCommand { get; }
    public ICommand ImageFailedCommand { get; }
    public ICommand GalleryImageLoadedCommand { get; }
    public ICommand GalleryImageFailedCommand { get; }
    public ICommand UrlSelectionChangedCommand { get; }
    public ICommand LocalSelectionChangedCommand { get; }

    public MainViewModel()
    {
        ImageLoadedCommand = new Command(() => AppendLog("[OK] ImageView loaded"));
        ImageFailedCommand = new Command(() => AppendLog("[ERROR] ImageView failed"));
        GalleryImageLoadedCommand = new Command(() => AppendLog("[OK] Gallery image loaded"));
        GalleryImageFailedCommand = new Command(() => AppendLog("[ERROR] Gallery image failed"));

        UrlSelectionChangedCommand = new Command(param =>
        {
            if (param is int index) AppendLog($"[NAV] URL index {index}");
        });

        LocalSelectionChangedCommand = new Command(param =>
        {
            if (param is int index) AppendLog($"[NAV] Local index {index}");
        });
    }
}
