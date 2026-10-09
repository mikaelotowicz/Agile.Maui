using System.Reflection;
using System.Runtime.CompilerServices;

namespace Agile.Maui.PdfTests;

/// <summary>
/// No TFM neutro (net10.0) o <c>MainThread</c> do MAUI lança
/// <c>NotImplementedInReferenceAssemblyException</c> — o que inviabilizaria testar os métodos
/// <c>Raise*</c> do <see cref="Agile.Maui.PdfViewer"/> (todos despacham via <c>OnMainThread</c>).
/// O MAUI expõe um seam INTERNO de teste (<c>MainThread.SetCustomImplementation</c>); instalamos
/// via reflection uma implementação que roteia para o <see cref="TestDispatcher"/> da thread
/// corrente: com <see cref="TestDispatcher.EnqueueMode"/> desligado (padrão) tudo roda inline
/// (determinístico); ligado, "não é main thread" e as actions ficam pendentes até
/// <see cref="TestDispatcher.ProcessQueue"/> — permitindo testar o caminho de despacho.
/// </summary>
internal static class TestMainThreadInstaller
{
    [ModuleInitializer]
    internal static void Install()
    {
        var mt  = typeof(Microsoft.Maui.ApplicationModel.MainThread);
        var set = mt.GetMethod("SetCustomImplementation", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "Seam interno MainThread.SetCustomImplementation não encontrado — a versão do MAUI mudou; " +
                "atualize TestMainThread.cs (os testes de eventos dependem dele no host).");

        Func<bool>     isMainThread = static () => !TestDispatcherProvider.ForCurrentThread().EnqueueMode;
        Action<Action> beginInvoke  = static a  => TestDispatcherProvider.ForCurrentThread().Dispatch(a);
        set.Invoke(null, new object[] { isMainThread, beginInvoke });
    }
}
