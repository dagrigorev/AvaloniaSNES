using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Emulation.Memory;
using SnesEmulator.Emulation.SaveState;
using SnesEmulator.Emulation.Timing;
using SnesEmulator.Input.Controllers;

namespace SnesEmulator.Infrastructure.DependencyInjection;

/// <summary>
/// Top-level Facade implementing <see cref="IEmulator"/>.
/// Orchestrates all subsystems behind a clean, state-guarded public API.
/// UI layer only ever talks to this class through the IEmulator interface.
/// </summary>
public sealed class SnesEmulatorFacade : IEmulator, IDisposable
{
    private readonly ILogger<SnesEmulatorFacade> _logger;
    private readonly IRomLoader       _romLoader;
    private readonly MemoryBus        _bus;
    private readonly WorkRam          _wram;
    private readonly EmulationLoop    _loop;
    private readonly SaveStateManager _saveState;
    private readonly InputManager     _inputManager;

    public ICpu          Cpu   { get; }
    public IPpu          Ppu   { get; }
    public IApu          Apu   { get; }
    public IInputManager Input => _inputManager;

    private EmulatorState _state     = EmulatorState.Idle;
    private RomInfo?      _loadedRom;

    public EmulatorState State     => _state;
    public RomInfo?      LoadedRom => _loadedRom;

    public event EventHandler<EmulatorStateChangedEventArgs>? StateChanged;
    public event EventHandler<EmulatorErrorEventArgs>?        ErrorOccurred;

    private CancellationTokenSource? _runCts;
    private Task?                    _runTask;

    public SnesEmulatorFacade(
        ILogger<SnesEmulatorFacade> logger,
        IRomLoader       romLoader,
        MemoryBus        bus,
        WorkRam          wram,
        ICpu             cpu,
        IPpu             ppu,
        IApu             apu,
        EmulationLoop    loop,
        SaveStateManager saveState,
        InputManager     inputManager)
    {
        _logger       = logger;
        _romLoader    = romLoader;
        _bus          = bus;
        _wram         = wram;
        Cpu           = cpu;
        Ppu           = ppu;
        Apu           = apu;
        _loop         = loop;
        _saveState    = saveState;
        _inputManager = inputManager;
    }

    public void LoadRom(string filePath)
    {
        EnsureNotRunning();
        _logger.LogInformation("Loading ROM: {Path}", filePath);

        RomData romData = _romLoader.LoadRom(filePath);
        _bus.LoadRom(romData);
        _bus.AttachDevices(Ppu, Apu);
        HardReset();

        _loadedRom = new RomInfo(
            romData.Header.Title, filePath, romData.MappingMode,
            romData.SizeKilobytes, romData.Header.IsPal,
            romData.Header.IsChecksumValid, romData.Header.Region);

        _logger.LogInformation(
            "ROM ready: '{Title}' [{Mode}] {Size} KB | {Region} | Checksum: {Cs}",
            _loadedRom.Title, _loadedRom.MappingMode, _loadedRom.SizeKilobytes,
            _loadedRom.Region, _loadedRom.IsChecksumValid ? "OK" : "BAD");

        TransitionTo(EmulatorState.Paused);
    }

    public void Run()
    {
        if (_state == EmulatorState.Idle)
            throw new EmulatorStateException("Load a ROM before starting emulation.");
        if (_state == EmulatorState.Running) return;

        TransitionTo(EmulatorState.Running);
        StartRunLoop();
    }

    public void Pause()
    {
        if (_state != EmulatorState.Running) return;
        StopRunLoop();
        TransitionTo(EmulatorState.Paused);
    }

    public void Reset()
    {
        bool wasRunning = _state == EmulatorState.Running;
        if (wasRunning) StopRunLoop();
        HardReset();
        TransitionTo(_loadedRom is not null ? EmulatorState.Paused : EmulatorState.Idle);
        if (wasRunning) Run();
    }

    public void Step()
    {
        if (_state == EmulatorState.Idle)
            throw new EmulatorStateException("Load a ROM before stepping.");
        if (_state == EmulatorState.Running)
            throw new EmulatorStateException("Pause emulation before stepping.");

        TransitionTo(EmulatorState.Stepping);
        try   { _loop.StepOne(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during step");
            ErrorOccurred?.Invoke(this, new EmulatorErrorEventArgs(ex.Message, ex));
            TransitionTo(EmulatorState.Error);
            return;
        }
        TransitionTo(EmulatorState.Paused);
    }

    public void SaveState(string filePath)
    {
        if (_state == EmulatorState.Idle)
            throw new EmulatorStateException("Nothing to save — no ROM loaded.");

        bool wasRunning = _state == EmulatorState.Running;
        if (wasRunning) Pause();
        try
        {
            EnsureDir(filePath);
            _saveState.SaveState(filePath, Cpu, Ppu, Apu, _wram);
        }
        finally { if (wasRunning) Run(); }
    }

    public void LoadState(string filePath)
    {
        if (_state == EmulatorState.Idle)
            throw new EmulatorStateException("Load a ROM before restoring state.");

        bool wasRunning = _state == EmulatorState.Running;
        if (wasRunning) Pause();
        try   { _saveState.LoadState(filePath, Cpu, Ppu, Apu, _wram); }
        finally { if (wasRunning) Run(); }
    }

    private void StartRunLoop()
    {
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;

        _runTask = Task.Run(async () =>
        {
            var target = TimeSpan.FromSeconds(1.0 / 60.0);
            while (!token.IsCancellationRequested)
            {
                var t0 = DateTime.UtcNow;
                try   { _loop.RunFrame(); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Emulation error");
                    ErrorOccurred?.Invoke(this, new EmulatorErrorEventArgs(ex.Message, ex));
                    TransitionTo(EmulatorState.Error);
                    return;
                }
                var rem = target - (DateTime.UtcNow - t0);
                if (rem > TimeSpan.Zero)
                {
                    try { await Task.Delay(rem, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, token);
    }

    private void StopRunLoop()
    {
        _runCts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _runCts?.Dispose();
        _runCts  = null;
        _runTask = null;
    }

    private void HardReset()
    {
        Cpu.Reset(); Ppu.Reset(); Apu.Reset();
        _bus.Reset(); _inputManager.Reset();
    }

    private void TransitionTo(EmulatorState next)
    {
        if (_state == next) return;
        var prev = _state;
        _state = next;
        StateChanged?.Invoke(this, new EmulatorStateChangedEventArgs(prev, next));
    }

    private void EnsureNotRunning()
    {
        if (_state == EmulatorState.Running)
            throw new EmulatorStateException("Stop the emulator before loading a new ROM.");
    }

    private static void EnsureDir(string path)
    {
        string? d = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d))
            Directory.CreateDirectory(d);
    }

    public void Dispose() { StopRunLoop(); _runCts?.Dispose(); }
}
