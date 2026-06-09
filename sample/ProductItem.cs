using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace sample;

public sealed class ProductItem : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public double Price { get; init; }
    public double Rating { get; init; }
    public string ImageUrl { get; init; } = "";
    public string FullImageUrl { get; init; } = "";
    public string Category { get; init; } = "";
    public string RatingText => $"★ {Rating:F1}";
    public string PriceText => $"$ {Price:F2}";

    private bool _isExpandedSpecs;
    public bool IsExpandedSpecs
    {
        get => _isExpandedSpecs;
        set
        {
            if (_isExpandedSpecs == value) return;
            _isExpandedSpecs = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpecsChevron));
        }
    }

    private bool _isExpandedReviews;
    public bool IsExpandedReviews
    {
        get => _isExpandedReviews;
        set
        {
            if (_isExpandedReviews == value) return;
            _isExpandedReviews = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReviewsChevron));
        }
    }

    public string SpecsChevron => IsExpandedSpecs ? "▼" : "▶";
    public string ReviewsChevron => IsExpandedReviews ? "▼" : "▶";

    public string SpecWeight => $"{150 + Id % 850}g";
    public string SpecDimensions => $"{10 + Id % 20}x{8 + Id % 15}x{2 + Id % 8} cm";
    public string SpecMaterial => Materials[Id % Materials.Length];
    public string SpecWarranty => $"{1 + Id % 5} year(s)";
    public string SpecOrigin => Origins[Id % Origins.Length];

    public string Review1Author => Reviewers[(Id + 0) % Reviewers.Length];
    public string Review1Stars => new string('★', 3 + Id % 3) + new string('☆', 5 - (3 + Id % 3));
    public string Review1Text => ReviewComments[(Id + 0) % ReviewComments.Length];
    public string Review2Author => Reviewers[(Id + 3) % Reviewers.Length];
    public string Review2Stars => new string('★', 4 + Id % 2) + new string('☆', 5 - (4 + Id % 2));
    public string Review2Text => ReviewComments[(Id + 4) % ReviewComments.Length];
    public string Review3Author => Reviewers[(Id + 7) % Reviewers.Length];
    public string Review3Stars => new string('★', 3 + (Id * 7) % 3) + new string('☆', 5 - (3 + (Id * 7) % 3));
    public string Review3Text => ReviewComments[(Id + 8) % ReviewComments.Length];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));

    private static readonly string[] Categories =
    [
        "Electronics", "Apparel", "Footwear", "Home", "Sports",
        "Beauty", "Toys", "Books", "Tools", "Grocery",
    ];

    private static readonly string[] Adjectives =
    [
        "Premium", "Ultra", "Pro", "Slim", "Smart", "Max", "Plus",
        "Turbo", "Elite", "Basic", "Eco", "Nano", "Mega", "Mini",
    ];

    private static readonly string[] Nouns =
    [
        "Headphones", "T-Shirt", "Sneakers", "Chair", "Backpack",
        "Watch", "Sunglasses", "Tablet", "Charger", "Mouse",
        "Keyboard", "Monitor", "Camera", "Bottle", "Flashlight",
        "Gloves", "Cap", "Towel", "Pan", "Shorts",
    ];

    private static readonly string[] Descriptions =
    [
        "High-quality product with modern design and long-lasting durability.",
        "Imported product with advanced technology and a premium finish.",
        "Ideal for daily use. Comfortable, light, and resilient.",
        "Ergonomic design that balances style and function.",
        "Top-grade material with quality certification.",
        "Great as a gift, with special packaging included.",
        "Made with sustainable and environmentally conscious materials.",
        "High performance for demanding users. Tested and approved.",
        "Compact and practical enough to carry anywhere.",
        "Limited edition with an exclusive finish.",
    ];

    private static readonly string[] Materials =
    [
        "Brushed aluminum", "ABS plastic", "Synthetic leather", "100% cotton",
        "Stainless steel", "Recycled polymer", "Technical fabric", "Certified wood",
    ];

    private static readonly string[] Origins =
    [
        "Brazil", "China", "Germany", "Italy", "USA", "Japan", "South Korea",
    ];

    private static readonly string[] Reviewers =
    [
        "John S.", "Maria L.", "Carlos R.", "Anna P.", "Peter M.",
        "Luisa F.", "Rafael B.", "Camila T.", "Bruno A.", "Fernanda K.",
    ];

    private static readonly string[] ReviewComments =
    [
        "Excellent product, better than expected. Recommended.",
        "Good value. Fast delivery and exactly as described.",
        "Very good quality. This is already my second purchase.",
        "Functional and good-looking. It fits my needs perfectly.",
        "The finish is average, but it works well.",
        "I have used it for a month and I am satisfied.",
        "Durable material and solid construction. Worth the price.",
        "Arrived quickly and well packaged.",
        "Does what it promises. Good purchase.",
        "Recommended, especially for the price.",
    ];

    public static ProductItem Generate(int id)
    {
        var category = Categories[id % Categories.Length];
        var adjective = Adjectives[id % Adjectives.Length];
        var noun = Nouns[id % Nouns.Length];
        var description = Descriptions[id % Descriptions.Length];
        var price = 29.90 + (id % 100) * 9.50 + (id % 7) * 3.33;
        var rating = 3.0 + (id % 21) * 0.1;

        return new ProductItem
        {
            Id = id,
            Name = $"{adjective} {noun}",
            Category = category,
            Description = description,
            Price = Math.Round(price, 2),
            Rating = Math.Round(Math.Min(rating, 5.0), 1),
            ImageUrl = $"https://picsum.photos/seed/prod{id}/80/80",
            FullImageUrl = $"https://picsum.photos/seed/prod{id}/600/500",
        };
    }

    public static List<ProductItem> GenerateBatch(int startId, int count) =>
        Enumerable.Range(startId, count).Select(Generate).ToList();
}
