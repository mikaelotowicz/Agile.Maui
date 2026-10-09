using System.Reflection;
using System.Text;

namespace Agile.Maui.DeviceTests;

public sealed record DeviceTestResult(string Name, bool Passed, string Message, TimeSpan Elapsed);

/// <summary>
/// Harness próprio (sem pacote de runner): descobre métodos <see cref="DeviceFactAttribute"/>
/// por reflection e os executa sequencialmente na UI thread. Cada teste tem timeout
/// individual. Resultado: linhas parseáveis <c>[DEVICETEST] PASS|FAIL Nome | msg</c> no
/// console/logcat, sumário <c>[DEVICETEST-SUMMARY] total=N pass=N fail=N</c> e, no Windows,
/// o arquivo <c>%TEMP%\agile-devicetests-result.txt</c> com as mesmas linhas.
/// </summary>
public static class DeviceTestRunner
{
    private const string LinePrefix = "[DEVICETEST]";
    private const string SummaryPrefix = "[DEVICETEST-SUMMARY]";

    private static Platforms CurrentPlatform =>
#if ANDROID
        Platforms.Android;
#elif WINDOWS
        Platforms.Windows;
#else
        Platforms.None;
#endif

    public static async Task<IReadOnlyList<DeviceTestResult>> RunAllAsync(TestHost host, Action<string> onStatus)
    {
        var results = new List<DeviceTestResult>();
        var lines = new List<string>();

        var tests = Discover();
        var total = tests.Count;
        var index = 0;

        foreach (var (method, fact) in tests)
        {
            index++;
            var name = method.Name;
            onStatus($"Executando {index}/{total}: {name}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool passed;
            string message;
            try
            {
                var testTask = (Task)method.Invoke(null, new object[] { host })!;
                var timeout = Task.Delay(TimeSpan.FromSeconds(fact.TimeoutSeconds));
                var completed = await Task.WhenAny(testTask, timeout);
                if (completed != testTask)
                    throw new TimeoutException($"timeout do teste após {fact.TimeoutSeconds}s");

                await testTask; // propaga exceção do teste
                passed = true;
                message = $"{sw.ElapsedMilliseconds} ms";
            }
            catch (Exception ex)
            {
                var inner = Unwrap(ex);
                passed = false;
                message = OneLine($"{inner.GetType().Name}: {inner.Message}");
            }
            sw.Stop();

            results.Add(new DeviceTestResult(name, passed, message, sw.Elapsed));
            var line = $"{LinePrefix} {(passed ? "PASS" : "FAIL")} {name} | {message}";
            lines.Add(line);
            Emit(line);

            // Limpa a área de host entre testes (inclusive após timeout/falha).
            try { await host.ResetAsync(); }
            catch (Exception ex) { Emit($"{LinePrefix} WARN {name} | reset: {OneLine(ex.Message)}"); }
        }

        var pass = results.Count(r => r.Passed);
        var fail = results.Count - pass;
        var summary = $"{SummaryPrefix} total={results.Count} pass={pass} fail={fail}";
        lines.Add(summary);
        Emit(summary);

#if WINDOWS
        // Arquivo de resultado para o run-windows.ps1.
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "agile-devicetests-result.txt");
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Emit($"{LinePrefix} WARN resultado-arquivo | {OneLine(ex.Message)}");
        }
#endif

        return results;
    }

    private static List<(MethodInfo Method, DeviceFactAttribute Fact)> Discover()
    {
        var platform = CurrentPlatform;
        return typeof(DeviceTestRunner).Assembly
            .GetTypes()
            .Where(t => t.IsClass && t.IsPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(m => (Method: m, Fact: m.GetCustomAttribute<DeviceFactAttribute>()))
            .Where(x => x.Fact is not null && (x.Fact.Platforms & platform) != 0)
            .Where(x =>
            {
                var p = x.Method.GetParameters();
                return typeof(Task).IsAssignableFrom(x.Method.ReturnType)
                    && p.Length == 1 && p[0].ParameterType == typeof(TestHost);
            })
            .OrderBy(x => x.Method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(x => x.Method.Name, StringComparer.Ordinal)
            .Select(x => (x.Method, x.Fact!))
            .ToList();
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null } tie)
            ex = tie.InnerException!;
        while (ex is AggregateException { InnerExceptions.Count: 1 } agg)
            ex = agg.InnerExceptions[0];
        return ex;
    }

    private static string OneLine(string text) =>
        text.Replace("\r", " ").Replace("\n", " ⏎ ").Trim();

    private static void Emit(string line)
    {
        // Android: Console.WriteLine vai ao logcat (mono-stdout/DOTNET); o Log.Info garante
        // também a tag estável DEVICETEST para o run-android.ps1 filtrar.
        Console.WriteLine(line);
#if ANDROID
        global::Android.Util.Log.Info("DEVICETEST", line);
#endif
    }
}
