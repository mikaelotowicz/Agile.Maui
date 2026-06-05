using System.Diagnostics;

namespace Agile.Maui;

/// <summary>
/// Log estático para o PdfViewer.
/// Em DEBUG escreve em Debug.WriteLine e dispara Received para UI.
/// Em Release é compilado fora (Conditional).
/// </summary>
internal static class PdfViewerLog
{
    private static readonly object _gate = new();

    /// <summary>Disparado no thread que chamou Write — use MainThread se for atualizar UI.</summary>
    public static event Action<string>? Received;

    [Conditional("DEBUG")]
    public static void Write(string platform, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{platform}] {message}";
        Debug.WriteLine(line);
        lock (_gate) Received?.Invoke(line);
    }
}
