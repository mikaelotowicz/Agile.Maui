using System.Windows.Input;

namespace Agile.Maui.GalleryTests;

/// <summary>
/// ICommand de teste: registra os parâmetros recebidos em CanExecute/Execute e permite
/// configurar o resultado de CanExecute.
/// </summary>
internal sealed class ComandoFake : ICommand
{
    public bool PodeExecutar { get; set; } = true;

    public List<object?> ParametrosCanExecute { get; } = [];
    public List<object?> ParametrosExecute { get; } = [];

    public int Execucoes => ParametrosExecute.Count;

    // Não usado nos testes; implementação vazia evita o warning de evento não utilizado.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter)
    {
        ParametrosCanExecute.Add(parameter);
        return PodeExecutar;
    }

    public void Execute(object? parameter) => ParametrosExecute.Add(parameter);
}
