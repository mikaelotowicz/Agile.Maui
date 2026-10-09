namespace Agile.Maui.DeviceTests;

/// <summary>Plataformas em que um <see cref="DeviceFactAttribute"/> roda.</summary>
[Flags]
public enum Platforms
{
    None = 0,
    Android = 1,
    Windows = 2,
    All = Android | Windows,
}

/// <summary>
/// Marca um método como device test. Convenção:
/// <c>public static async Task NomeDoTeste(TestHost host)</c> numa classe pública.
/// A descoberta é por reflection (ver <see cref="DeviceTestRunner"/>); a execução é
/// sequencial na UI thread, com timeout individual para não travar a suíte.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DeviceFactAttribute : Attribute
{
    public DeviceFactAttribute(Platforms platforms = Platforms.All) => Platforms = platforms;

    public Platforms Platforms { get; }

    /// <summary>Timeout individual do teste, em segundos. Default: 60.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
