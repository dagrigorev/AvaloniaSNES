using Avalonia;
using SnesEmulator.Desktop;

/// <summary>
/// Application entry point.
/// We use Avalonia's recommended desktop setup with AppBuilder.
/// </summary>
internal sealed class Program
{
    // Avalonia requires the entry point to be synchronous
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
