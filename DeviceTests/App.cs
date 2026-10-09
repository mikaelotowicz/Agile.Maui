namespace Agile.Maui.DeviceTests;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new TestRunnerPage());
}
