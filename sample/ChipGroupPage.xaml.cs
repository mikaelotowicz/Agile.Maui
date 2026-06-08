using System.Collections.ObjectModel;
using Agile.Maui;

namespace sample;

public partial class ChipGroupPage : ContentPage
{
    public ChipGroupPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public ObservableCollection<ChipItem> Days { get; } =
    [
        new() { Text = "Monday", Value = "Monday" },
        new() { Text = "Tuesday", Value = "Tuesday" },
        new() { Text = "Wednesday", Value = "Wednesday" },
        new() { Text = "Thursday", Value = "Thursday", IsSelected = true },
        new() { Text = "Friday", Value = "Friday" },
        new() { Text = "Saturday", Value = "Saturday" },
        new() { Text = "Sunday", Value = "Sunday" },
    ];

    public ObservableCollection<ChipItem> Interests { get; } =
    [
        new() { Text = "Photography", Value = "Photography" },
        new() { Text = "Video", Value = "Video", IsSelected = true },
        new() { Text = "Music", Value = "Music" },
        new() { Text = "Coding & Apps", Value = "Coding & Apps", IsSelected = true },
        new() { Text = "Art & Design", Value = "Art & Design" },
        new() { Text = "Business", Value = "Business" },
    ];

    public ObservableCollection<ChipItem> Categories { get; } =
    [
        new() { Text = "All", Value = "All", IsSelected = true },
        new() { Text = "Popular", Value = "Popular" },
        new() { Text = "Recent", Value = "Recent" },
        new() { Text = "Design", Value = "Design" },
        new() { Text = "Development", Value = "Development" },
        new() { Text = "Marketing", Value = "Marketing" },
        new() { Text = "Finance", Value = "Finance" },
        new() { Text = "Operations", Value = "Operations" },
        new() { Text = "Support", Value = "Support" },
    ];

    public ObservableCollection<ChipItem> Statuses { get; } =
    [
        new() { Text = "Open", Value = "Open", IsSelected = true },
        new() { Text = "In progress", Value = "InProgress" },
        new() { Text = "Waiting", Value = "Waiting" },
        new() { Text = "Closed", Value = "Closed" },
    ];
}
