using SnesEmulator.Core.Models;

namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Abstraction for the SNES Picture Processing Unit (PPU).
/// Renders backgrounds and sprites, manages VRAM/CGRAM/OAM,
/// and generates V-blank and H-blank timing signals.
/// </summary>
public interface IPpu : IEmulatorComponent, IStateful
{
    /// <summary>Current PPU status information for diagnostics.</summary>
    PpuStatus Status { get; }

    /// <summary>
    /// Steps the PPU by one master clock cycle.
    /// Internally tracks H/V position and renders scanlines.
    /// </summary>
    void Clock(int masterCycles);

    /// <summary>
    /// Returns true if the PPU is currently in the V-blank period.
    /// Used to trigger NMI on the CPU.
    /// </summary>
    bool IsVBlank { get; }

    /// <summary>
    /// Returns true if the PPU is currently in the H-blank period.
    /// </summary>
    bool IsHBlank { get; }

    /// <summary>
    /// The completed framebuffer ready for display.
    /// 256x224 pixels (or 256x239 in overscan mode), ARGB32.
    /// </summary>
    IFrameBuffer FrameBuffer { get; }

    /// <summary>
    /// Event raised when a complete frame has been rendered and is ready for display.
    /// </summary>
    event EventHandler<FrameReadyEventArgs>? FrameReady;

    /// <summary>Reads a PPU register (addresses $2100-$213F).</summary>
    byte ReadRegister(byte register);

    /// <summary>Writes a PPU register (addresses $2100-$213F).</summary>
    void WriteRegister(byte register, byte value);
}
