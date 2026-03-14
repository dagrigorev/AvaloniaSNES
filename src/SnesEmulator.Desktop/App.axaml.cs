using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SnesEmulator.Desktop.ViewModels;
using SnesEmulator.Desktop.Views;
using SnesEmulator.Infrastructure.DependencyInjection;
using SnesEmulator.Infrastructure.Logging;

namespace SnesEmulator.Desktop;

/// <summary>
/// Avalonia application root. Wires up the DI container and launches the main window.
/// </summary>
public sealed class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build the DI container
        var services = new ServiceCollection();
        services.AddSnesEmulator();

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<CpuStateViewModel>();
        services.AddSingleton<LogViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // Wire the DiagnosticLogSink into the logging pipeline
        var logSink = _serviceProvider.GetRequiredService<DiagnosticLogSink>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        loggerFactory.AddProvider(logSink);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    //protected override void OnExit(ExitEventArgs e)
    //{
    //    _serviceProvider?.Dispose();
    //    base.OnExit(e);
    //}
}
