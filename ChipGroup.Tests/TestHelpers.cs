using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Agile.Maui.ChipGroupTests;

/// <summary>Helpers para inspecionar a árvore visual gerada pelo ChipGroup e simular toques.</summary>
internal static class ChipTestHelpers
{
    /// <summary>FlexLayout interno, independente do LayoutMode (no Horizontal fica dentro do ScrollView).</summary>
    public static FlexLayout GetLayout(ChipGroup group) => group.Content switch
    {
        FlexLayout layout => layout,
        ScrollView scroll => (FlexLayout)scroll.Content!,
        _ => throw new InvalidOperationException($"Content inesperado: {group.Content?.GetType().Name ?? "null"}"),
    };

    /// <summary>Borders dos chips, na ordem do ItemsSource.</summary>
    public static IReadOnlyList<Border> GetChips(ChipGroup group)
        => GetLayout(group).Children.Cast<Border>().ToList();

    /// <summary>Label de texto do chip (ignora o Label interno do indicador de seleção).</summary>
    public static Label GetLabel(Border chip)
        => ((HorizontalStackLayout)chip.Content!).Children.OfType<Label>().Single();

    /// <summary>Indicador circular (presente só em Multiple + ShowCheckmark), ou null.</summary>
    public static Border? GetIndicator(Border chip)
        => ((HorizontalStackLayout)chip.Content!).Children.OfType<Border>().FirstOrDefault();

    /// <summary>Simula o toque do usuário no chip (executa o Command do TapGestureRecognizer).</summary>
    public static void Tap(Border chip)
        => chip.GestureRecognizers.OfType<TapGestureRecognizer>().Single().Command!.Execute(null);

    /// <summary>Liga o EnqueueMode do dispatcher da thread e garante a limpeza (drena + desliga) no Dispose.</summary>
    public static EnqueueScope Enqueue() => new(TestDispatcherProvider.ForCurrentThread());
}

internal sealed class EnqueueScope : IDisposable
{
    public TestDispatcher Dispatcher { get; }

    public EnqueueScope(TestDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
        Dispatcher.EnqueueMode = true;
    }

    public void Dispose()
    {
        Dispatcher.EnqueueMode = false;
        Dispatcher.ProcessQueue(); // não deixa callbacks pendentes vazarem para o próximo teste da thread
    }
}

/// <summary>
/// Handler mínimo para exercitar OnHandlerChanged (attach/detach) sem plataforma nativa.
/// </summary>
internal sealed class StubViewHandler : IViewHandler
{
    private IView? _virtualView;

    public bool HasContainer { get; set; }
    public object? ContainerView => null;
    public object? PlatformView => null;
    public IMauiContext? MauiContext => null;

    public IView? VirtualView => _virtualView;
    IElement? IElementHandler.VirtualView => (IElement?)_virtualView;

    public void SetMauiContext(IMauiContext mauiContext) { }
    public void SetVirtualView(IElement view) => _virtualView = (IView)view;
    public void UpdateValue(string property) { }
    public void Invoke(string command, object? args = null) { }
    public void DisconnectHandler() => _virtualView = null;
    public void PlatformArrange(Rect frame) { }
    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
}

/// <summary>POCO para testes de DisplayMemberPath/ValueMemberPath.</summary>
internal sealed class Categoria
{
    public string Nome { get; set; } = string.Empty;
    public int Id { get; set; }
    public override string ToString() => $"Categoria:{Nome}";
}
