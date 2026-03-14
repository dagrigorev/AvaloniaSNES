using SnesEmulator.Core.Models;

namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Abstraction for the SNES Audio Processing Unit (APU / SPC700).
/// The APU is a self-contained Sony SPC700 CPU with its own 64KB RAM and DSP.
/// Communication with the main CPU is via 4 ports at $2140-$2143.
/// </summary>
public interface IApu : IEmulatorComponent, IStateful
{
    /// <summary>Steps the APU forward, keeping it synchronized with the main CPU.</summary>
    void Clock(int masterCycles);

    /// <summary>Reads from APU communication port (0-3).</summary>
    byte ReadPort(byte port);

    /// <summary>Writes to APU communication port (0-3).</summary>
    void WritePort(byte port, byte value);

    /// <summary>
    /// Fills an audio buffer with rendered samples.
    /// Called by the audio output adapter at the required sample rate.
    /// </summary>
    void FillAudioBuffer(short[] buffer, int offset, int count);
}

/// <summary>
/// Abstraction for a SNES controller (joypad).
/// The SNES supports up to 4 controllers via two controller ports.
/// </summary>
public interface IController : IEmulatorComponent
{
    /// <summary>Current button state as a bitmask (16 bits).</summary>
    ushort ButtonState { get; }

    /// <summary>Reads the serial latch from the controller (for auto-joypad read).</summary>
    bool ReadSerial();

    /// <summary>Strobes (latches) the current button state.</summary>
    void Strobe();

    /// <summary>Updates the controller's button state from external input.</summary>
    void SetButtonState(SnesButton button, bool pressed);
}

/// <summary>
/// Abstraction for the input manager.
/// Maps platform-specific input events (keyboard/gamepad) to SNES controller buttons.
/// </summary>
public interface IInputManager
{
    /// <summary>Gets the controller on the given port (1 or 2).</summary>
    IController GetController(int port);

    /// <summary>Handles a key-down event from the host platform.</summary>
    void HandleKeyDown(string key);

    /// <summary>Handles a key-up event from the host platform.</summary>
    void HandleKeyUp(string key);

    /// <summary>Updates button mappings from current configuration.</summary>
    void UpdateMappings(InputMappingConfig config);
}
