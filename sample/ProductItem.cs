// sample/ProductItem.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace sample;

public sealed class ProductItem : INotifyPropertyChanged
{
    public int    Id          { get; init; }
    public string Name        { get; init; } = "";
    public string Description { get; init; } = "";
    public double Price       { get; init; }
    public double Rating      { get; init; }
    public string ImageUrl         { get; init; } = "";
    public string FullImageUrl     { get; init; } = "";
    public string Category    { get; init; } = "";
    public string RatingText  => $"★ {Rating:F1}";
    public string PriceText   => $"R$ {Price:F2}";

    // ── Expanders: estado vive no item (ViewModel) para sobreviver ao
    // recycling de ViewHolders no RecyclerView/CollectionView.

    private bool _isExpandedSpecs;
    public bool IsExpandedSpecs
    {
        get => _isExpandedSpecs;
        set { if (_isExpandedSpecs != value) { _isExpandedSpecs = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpecsChevron)); } }
    }

    private bool _isExpandedReviews;
    public bool IsExpandedReviews
    {
        get => _isExpandedReviews;
        set { if (_isExpandedReviews != value) { _isExpandedReviews = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReviewsChevron)); } }
    }

    public string SpecsChevron   => IsExpandedSpecs   ? "▼" : "▶";
    public string ReviewsChevron => IsExpandedReviews ? "▼" : "▶";

    // Conteúdo dos expanders — gerado de forma determinística a partir do Id
    public string SpecWeight     => $"{(150 + Id % 850)}g";
    public string SpecDimensions => $"{10 + Id % 20}×{8 + Id % 15}×{2 + Id % 8} cm";
    public string SpecMaterial   => Materials[Id % Materials.Length];
    public string SpecWarranty   => $"{1 + Id % 5} ano(s)";
    public string SpecOrigin     => Origins[Id % Origins.Length];

    public string Review1Author => Reviewers[(Id + 0) % Reviewers.Length];
    public string Review1Stars  => new string('★', 3 + Id % 3) + new string('☆', 5 - (3 + Id % 3));
    public string Review1Text   => ReviewComments[(Id + 0) % ReviewComments.Length];
    public string Review2Author => Reviewers[(Id + 3) % Reviewers.Length];
    public string Review2Stars  => new string('★', 4 + Id % 2) + new string('☆', 5 - (4 + Id % 2));
    public string Review2Text   => ReviewComments[(Id + 4) % ReviewComments.Length];
    public string Review3Author => Reviewers[(Id + 7) % Reviewers.Length];
    public string Review3Stars  => new string('★', 3 + (Id * 7) % 3) + new string('☆', 5 - (3 + (Id * 7) % 3));
    public string Review3Text   => ReviewComments[(Id + 8) % ReviewComments.Length];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));

    private static readonly string[] Categories =
    [
        "Eletrônicos", "Roupas", "Calçados", "Casa", "Esportes",
        "Beleza", "Brinquedos", "Livros", "Ferramentas", "Alimentos",
    ];

    private static readonly string[] Adjectives =
    [
        "Premium", "Ultra", "Pro", "Slim", "Smart", "Max", "Plus",
        "Turbo", "Elite", "Basic", "Eco", "Nano", "Mega", "Mini",
    ];

    private static readonly string[] Nouns =
    [
        "Fone de Ouvido", "Camiseta", "Tênis", "Cadeira", "Mochila",
        "Relógio", "Óculos", "Tablet", "Carregador", "Mouse",
        "Teclado", "Monitor", "Câmera", "Garrafa", "Lanterna",
        "Luva", "Boné", "Toalha", "Panela", "Bermuda",
    ];

    private static readonly string[] Descriptions =
    [
        "Alta qualidade, design moderno e durabilidade garantida por 2 anos.",
        "Produto importado com tecnologia avançada e acabamento premium.",
        "Ideal para uso diário. Confortável, leve e resistente.",
        "Design ergonômico que combina estilo e funcionalidade.",
        "Material de primeira linha com certificação de qualidade.",
        "Perfeito para presentear. Embalagem especial inclusa.",
        "Fabricado com materiais sustentáveis e ecologicamente corretos.",
        "Alta performance para os mais exigentes. Testado e aprovado.",
        "Compacto e prático para levar a qualquer lugar.",
        "Edição limitada com acabamento exclusivo.",
    ];

    private static readonly string[] Materials =
    [
        "Alumínio escovado", "Plástico ABS", "Couro sintético", "Algodão 100%",
        "Aço inox", "Polímero reciclado", "Tecido técnico", "Madeira certificada",
    ];

    private static readonly string[] Origins =
    [
        "Brasil", "China", "Alemanha", "Itália", "EUA", "Japão", "Coreia do Sul",
    ];

    private static readonly string[] Reviewers =
    [
        "João S.", "Maria L.", "Carlos R.", "Ana P.", "Pedro M.",
        "Luiza F.", "Rafael B.", "Camila T.", "Bruno A.", "Fernanda K.",
    ];

    private static readonly string[] ReviewComments =
    [
        "Produto excelente, superou as expectativas. Recomendo!",
        "Bom custo-benefício. Entrega rápida e produto conforme descrição.",
        "Qualidade muito boa. Já é o segundo que compro.",
        "Funcional e bonito. Atendeu perfeitamente ao que precisava.",
        "Achei o acabamento mediano, mas funciona bem.",
        "Estou usando há um mês e estou satisfeito.",
        "Material resistente e bem trabalhado. Vale o preço.",
        "Chegou rápido e bem embalado. Produto top.",
        "Cumpre o que promete. Boa compra.",
        "Recomendo, principalmente pelo preço.",
    ];

    public static ProductItem Generate(int id)
    {
        var cat   = Categories[id % Categories.Length];
        var adj   = Adjectives[id % Adjectives.Length];
        var noun  = Nouns[id % Nouns.Length];
        var desc  = Descriptions[id % Descriptions.Length];
        var price = 29.90 + (id % 100) * 9.50 + (id % 7) * 3.33;
        var rating = 3.0 + (id % 21) * 0.1;

        return new ProductItem
        {
            Id          = id,
            Name        = $"{adj} {noun}",
            Category    = cat,
            Description = desc,
            Price       = Math.Round(price, 2),
            Rating      = Math.Round(Math.Min(rating, 5.0), 1),
            ImageUrl     = $"https://picsum.photos/seed/prod{id}/80/80",
            FullImageUrl = $"https://picsum.photos/seed/prod{id}/600/500",
        };
    }

    public static List<ProductItem> GenerateBatch(int startId, int count) =>
        Enumerable.Range(startId, count).Select(Generate).ToList();
}
