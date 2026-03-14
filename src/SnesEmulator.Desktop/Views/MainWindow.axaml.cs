using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SnesEmulator.Desktop.Controls;
using SnesEmulator.Desktop.ViewModels;

namespace SnesEmulator.Desktop.Views;

/// <summary>
/// Code-behind for the main window.
/// Kept minimal: only platform-specific operations that cannot be done in XAML.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
        KeyUp   += OnKeyUp;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        _vm = vm;
        vm.RequestOpenFileDialog = async () => await OpenRomDialog();
        var screen = this.FindControl<GameScreen>("GameScreenControl");
        if (screen != null)
            screen.AttachToViewModel(vm);
    }

    private async Task OpenRomDialog()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SNES ROM",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SNES ROM Files")
                {
                    Patterns = new[] { "*.smc", "*.sfc", "*.fig", "*.bin" },
                    MimeTypes = new[] { "application/octet-stream" }
                },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files is { Count: > 0 } && _vm is not null)
        {
            string? path = files[0].TryGetLocalPath();
            if (path is not null)
                _vm.LoadRomFile(path);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e) => _vm?.HandleKey(e.Key.ToString(), true);
    private void OnKeyUp(object? sender, KeyEventArgs e)   => _vm?.HandleKey(e.Key.ToString(), false);
    private void OnClearLog(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _vm?.Logs.Clear();
    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About SNES Emulator",
            Width = 420, Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A1A2E"))
        };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(28), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "SNES Emulator", FontSize = 26,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E94560")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                new TextBlock { Text = "Version 1.0.0  ·  .NET 8  ·  Avalonia 11", FontSize = 12,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9090A8")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9090A8")),
                    Text = "CPU: WDC 65C816 (full 256-opcode set)\nPPU: S-PPU1+S-PPU2 (tile renderer)\n" +
                           "APU: SPC700 (architecture foundation)\nMemory: LoROM / HiROM mapping\n" +
                           "UI: Avalonia + ReactiveUI (MVVM)" }
            }
        };
        await dialog.ShowDialog(this);
    }
}
