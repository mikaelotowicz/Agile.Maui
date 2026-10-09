using System.Windows.Input;

namespace Agile.Maui.VirtualizedCollectionTests;

/// <summary>ICommand de teste: registra as consultas a CanExecute e as execuções.</summary>
internal sealed class ComandoDeTeste : ICommand
{
    public bool PodeExecutar { get; set; } = true;
    public List<object?> Execucoes { get; } = [];
    public List<object?> ConsultasCanExecute { get; } = [];

    // Explícito e vazio para não gerar CS0067 (evento nunca disparado).
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter)
    {
        ConsultasCanExecute.Add(parameter);
        return PodeExecutar;
    }

    public void Execute(object? parameter) => Execucoes.Add(parameter);
}
