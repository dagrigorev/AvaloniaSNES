using SnesEmulator.Core.Models;

namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Top-level facade for the SNES emulator.
/// Provides a clean API for loading ROMs, controlling emulation,
/// and accessing hardware state. Follows the Facade pattern to hide
/// the complexity of the underlying subsystems from the UI layer.
/// </summary>
public interface IEmulator
{
    /// <summary>Current emulation state (Idle, Running, Paused, etc.).</summary>
    EmulatorState State { get; }

    /// <summary>Currently loaded ROM information, or null if no ROM is loaded.</summary>
    RomInfo? LoadedRom { get; }

    /// <summary>The PPU subsystem (for frame rendering).</summary>
    IPpu Ppu { get; }

    /// <summary>The CPU subsystem (for diagnostics).</summary>
    ICpu Cpu { get; }

    /// <summary>The input manager.</summary>
    IInputManager Input { get; }

    /// <summary>Event raised when emulator state changes.</summary>
    event EventHandler<EmulatorStateChangedEventArgs>? StateChanged;

    /// <summary>Event raised on emulation error.</summary>
    event EventHandler<EmulatorErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Loads a ROM from the given file path (.smc or .sfc).
    /// Throws <see cref="Exceptions.RomLoadException"/> on failure.
    /// </summary>
    void LoadRom(string filePath);

    /// <summary>Starts or resumes emulation.</summary>
    void Run();

    /// <summary>Pauses emulation, preserving all state.</summary>
    void Pause();

    /// <summary>Hard-resets the emulator (power cycle).</summary>
    void Reset();

    /// <summary>
    /// Executes exactly one CPU instruction and pauses.
    /// Used for step-by-step debugging.
    /// </summary>
    void Step();

    /// <summary>Saves the current emulator state to a file.</summary>
    void SaveState(string filePath);

    /// <summary>Loads emulator state from a file.</summary>
    void LoadState(string filePath);
}
