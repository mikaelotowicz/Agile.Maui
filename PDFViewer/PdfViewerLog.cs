using System.Diagnostics;

namespace Agile.Maui;

/// <summary>
/// Log estático para o PdfViewer.
/// Em DEBUG escreve em Debug.WriteLine e dispara Received para UI.
/// Em Release é compilado fora (Conditional).
/// </summary>
internal static class PdfViewerLog
{
    /// <summary>Disparado no thread que chamou Write — use MainThread se for atualizar UI.</summary>
    public static event Action<string>? Received;

    [Conditional("DEBUG")]
    public static void Write(string platform, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{platform}] {message}";
        Debug.WriteLine(line);
        // Sem gate: delegate multicast é imutável e a leitura é atômica; invocar handlers
        // segurando um lock serializava (e podia travar) threads de render que logam juntas.
        Received?.Invoke(line);
    }
}
