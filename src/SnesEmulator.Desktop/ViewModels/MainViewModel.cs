using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Desktop.Controls;

namespace SnesEmulator.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Follows MVVM: exposes commands and observable properties consumed by MainWindow.axaml.
/// All emulator interaction goes through the IEmulator facade.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IEmulator _emulator;
    private readonly ILogger<MainViewModel> _logger;

    // ── Child ViewModels ──────────────────────────────────────────────────────
    public CpuStateViewModel CpuState { get; }
    public LogViewModel      Logs     { get; }

    // ── Observable Properties ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string _statusText = "Ready — Load a ROM to begin";
    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private string _romTitle = "No ROM loaded";
    public string RomTitle
    {
        get => _romTitle;
        private set => this.RaiseAndSetIfChanged(ref _romTitle, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRunning, value);
            NotifyCommandsCanExecuteChanged();
        }
    }

    private bool _isRomLoaded;
    public bool IsRomLoaded
    {
        get => _isRomLoaded;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRomLoaded, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRomNotLoaded)));
            NotifyCommandsCanExecuteChanged();
        }
    }

    public bool IsRomNotLoaded => !IsRomLoaded;

    private string _fpsDisplay = "0 FPS";
    public string FpsDisplay
    {
        get => _fpsDisplay;
        private set => this.RaiseAndSetIfChanged(ref _fpsDisplay, value);
    }

    private string _ppuStatus = "PPU: ---";
    public string PpuStatus
    {
        get => _ppuStatus;
        private set => this.RaiseAndSetIfChanged(ref _ppuStatus, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand OpenRomCommand  { get; }
    public ICommand RunCommand      { get; }
    public ICommand PauseCommand    { get; }
    public ICommand ResetCommand    { get; }
    public ICommand StepCommand     { get; }
    public ICommand SaveStateCommand { get; }
    public ICommand LoadStateCommand { get; }

    // ── FPS tracking ──────────────────────────────────────────────────────────
    private volatile int _frameCount;
    private DateTime _lastFpsUpdate = DateTime.UtcNow;

    // ── Public interface for ROM file open ────────────────────────────────────
    /// <summary>
    /// Called by the View when the user has selected a ROM file.
    /// The View handles the file dialog; the ViewModel processes the result.
    /// </summary>
    public Action? RequestOpenFileDialog { get; set; }

    public MainViewModel(
        IEmulator emulator,
        CpuStateViewModel cpuState,
        LogViewModel logs,
        ILogger<MainViewModel> logger)
    {
        _emulator = emulator;
        CpuState  = cpuState;
        Logs      = logs;
        _logger   = logger;

        // Wire commands
        OpenRomCommand   = new RelayCommand(OnOpenRom);
        RunCommand       = new RelayCommand(OnRun,   () => IsRomLoaded);
        PauseCommand     = new RelayCommand(OnPause, () => IsRunning);
        ResetCommand     = new RelayCommand(OnReset, () => IsRomLoaded);
        StepCommand = new RelayCommand(OnStep, () => IsRomLoaded && !IsRunning);
        SaveStateCommand = new RelayCommand(OnSaveState, () => IsRomLoaded);
        LoadStateCommand = new RelayCommand(OnLoadState, () => IsRomLoaded);

        // Subscribe to emulator events
        _emulator.StateChanged    += OnEmulatorStateChanged;
        _emulator.ErrorOccurred   += OnEmulatorError;
        _emulator.Ppu.FrameReady  += OnFrameReady;

        // Start the UI refresh timer (updates status bar, CPU registers, etc.)
        DispatcherTimer.Run(() =>
        {
            UpdateDiagnostics();
            return true; // Keep running
        }, TimeSpan.FromMilliseconds(500));
    }

    // ── Command handlers ──────────────────────────────────────────────────────

    private void OnOpenRom()
    {
        // Delegate file dialog to the View
        RequestOpenFileDialog?.Invoke();
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        ((RelayCommand)RunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ResetCommand).NotifyCanExecuteChanged();
        ((RelayCommand)StepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)SaveStateCommand).NotifyCanExecuteChanged();
        ((RelayCommand)LoadStateCommand).NotifyCanExecuteChanged();
    }

    /// <summary>Called by the View after the user selects a ROM file.</summary>
    public void LoadRomFile(string filePath)
    {
        try
        {
            _emulator.LoadRom(filePath);
            RomTitle  = $"⬤  {_emulator.LoadedRom?.Title ?? "Unknown"}";
            StatusText = $"Loaded: {Path.GetFileName(filePath)} ({_emulator.LoadedRom?.MappingMode})";
            IsRomLoaded = true;
            ErrorMessage = string.Empty;
            HasError = false;
            _logger.LogInformation("ROM loaded: {File}", filePath);
        }
        catch (RomLoadException ex)
        {
            ShowError($"ROM load failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error loading ROM: {ex.Message}");
        }
    }

    private void OnRun()
    {
        try
        {
            _emulator.Run();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnPause()
    {
        _emulator.Pause();
    }

    private void OnReset()
    {
        try
        {
            _emulator.Reset();
            StatusText = "Reset — Ready";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnStep()
    {
        try
        {
            _emulator.Step();
            CpuState.Update(_emulator.Cpu.Registers);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnSaveState()
    {
        try
        {
            string path = GetSaveStatePath();
            _emulator.SaveState(path);
            StatusText = $"State saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ShowError($"Save state failed: {ex.Message}");
        }
    }

    private void OnLoadState()
    {
        try
        {
            string path = GetSaveStatePath();
            if (!File.Exists(path)) { ShowError("No save state found."); return; }
            _emulator.LoadState(path);
            StatusText = "State loaded.";
        }
        catch (Exception ex)
        {
            ShowError($"Load state failed: {ex.Message}");
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnEmulatorStateChanged(object? sender, EmulatorStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = e.NewState == EmulatorState.Running;
            StatusText = e.NewState switch
            {
                EmulatorState.Running  => $"Running: {_emulator.LoadedRom?.Title}",
                EmulatorState.Paused   => "Paused",
                EmulatorState.Stepping => "Stepping...",
                EmulatorState.Idle     => "Ready",
                EmulatorState.Error    => "Error",
                _ => StatusText
            };

            if (e.NewState is not EmulatorState.Error)
            {
                ErrorMessage = string.Empty;
                HasError = false;
            }
        });
    }

    private void OnEmulatorError(object? sender, EmulatorErrorEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ShowError(e.Message));
    }

    private void OnFrameReady(object? sender, FrameReadyEventArgs e)
    {
        // Track FPS
        System.Threading.Interlocked.Increment(ref _frameCount);
    }

    private void UpdateDiagnostics()
    {
        // FPS calculation
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsUpdate).TotalSeconds;
        if (elapsed >= 0.4)
        {
            var count = System.Threading.Interlocked.Exchange(ref _frameCount, 0);
            FpsDisplay = $"{count / elapsed:F1} FPS";
            _frameCount = 0;
            _lastFpsUpdate = now;
        }

        // PPU status
        if (_emulator.State != EmulatorState.Idle)
        {
            var ppu = _emulator.Ppu.Status;
            PpuStatus = $"SL:{ppu.CurrentScanline:D3} DOT:{ppu.CurrentDot:D3} " +
                       $"{(ppu.InVBlank ? "[VBL]" : "     ")} Frame:{ppu.FrameCount}";
        }

        // CPU registers (when paused or stepping)
        if (_emulator.State is EmulatorState.Paused or EmulatorState.Stepping)
        {
            CpuState.Update(_emulator.Cpu.Registers);
        }
    }

    /// <summary>
    /// Called by the View to wire the GameScreen control to the PPU FrameReady event.
    /// Kept here so the ViewModel controls access to the emulator internals.
    /// </summary>
    public void AttachGameScreen(Controls.GameScreen screen)
    {
        screen.SubscribeToPpu(_emulator.Ppu);
    }

    // ── Input routing (called from code-behind) ───────────────────────────────

    /// <summary>Routes a host key event to the emulator's input manager.</summary>
    public void HandleKey(string key, bool pressed)
    {
        if (pressed)
            _emulator.Input.HandleKeyDown(key);
        else
            _emulator.Input.HandleKeyUp(key);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        StatusText = $"Error: {message}";
        _logger.LogError("UI Error: {Message}", message);
    }

    private string GetSaveStatePath()
    {
        string title = _emulator.LoadedRom?.Title ?? "state";
        // Remove invalid filename characters
        string safe = string.Concat(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnesEmulator", "saves", $"{safe}.state");
    }
}
