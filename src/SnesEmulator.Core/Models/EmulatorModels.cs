namespace SnesEmulator.Core.Models;

/// <summary>
/// Represents the current lifecycle state of the emulator.
/// Follows a well-defined state machine:
///   Idle → Running ↔ Paused → Idle
///   Any state → Error
/// </summary>
public enum EmulatorState
{
    /// <summary>No ROM loaded, not running.</summary>
    Idle,

    /// <summary>ROM loaded, emulation actively running.</summary>
    Running,

    /// <summary>ROM loaded, emulation suspended (can resume).</summary>
    Paused,

    /// <summary>Executing a single instruction (step debug mode).</summary>
    Stepping,

    /// <summary>An unrecoverable error occurred.</summary>
    Error
}

/// <summary>Event args for emulator state transitions.</summary>
public sealed class EmulatorStateChangedEventArgs : EventArgs
{
    public EmulatorState OldState { get; }
    public EmulatorState NewState { get; }

    public EmulatorStateChangedEventArgs(EmulatorState oldState, EmulatorState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>Event args for emulator errors.</summary>
public sealed class EmulatorErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }

    public EmulatorErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}

/// <summary>Event args raised when a new frame is ready for display.</summary>
public sealed class FrameReadyEventArgs : EventArgs
{
    public IReadOnlyList<uint> Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int FrameNumber { get; }

    public FrameReadyEventArgs(uint[] pixels, int width, int height, int frameNumber)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        FrameNumber = frameNumber;
    }
}

/// <summary>PPU status snapshot for diagnostics.</summary>
public sealed record PpuStatus
{
    public int CurrentScanline { get; init; }
    public int CurrentDot { get; init; }
    public bool InVBlank { get; init; }
    public bool InHBlank { get; init; }
    public int FrameCount { get; init; }
    public byte BgMode { get; init; }

    public override string ToString() =>
        $"Scanline:{CurrentScanline:D3} Dot:{CurrentDot:D3} " +
        $"VBL:{(InVBlank ? "Y" : "N")} HBL:{(InHBlank ? "Y" : "N")} " +
        $"Frame:{FrameCount} Mode:{BgMode}";
}
