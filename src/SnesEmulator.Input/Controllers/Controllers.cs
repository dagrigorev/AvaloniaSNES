using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;

namespace SnesEmulator.Input.Controllers;

/// <summary>
/// Emulates a standard SNES gamepad controller.
/// The SNES joypad uses a 16-bit serial shift register:
///   Bits 15–0: B, Y, Sel, Start, Up, Down, Left, Right, A, X, L, R, 0, 0, 0, 0
/// The controller is read by strobing $4016 then reading $4016/$4017 16 times.
/// </summary>
public sealed class SnesController : IController
{
    private ushort _buttons;     // Current latched button state
    private ushort _shiftReg;    // Serial shift register for bit-by-bit reading
    private int    _bitPos;      // Current serial read position (0–15)

    public string Name => "SNES Controller";

    public ushort ButtonState => _buttons;

    public void Reset()
    {
        _buttons  = 0;
        _shiftReg = 0;
        _bitPos   = 0;
    }

    /// <summary>
    /// Strobes the controller, latching the current button state into the shift register.
    /// Called when $4016 bit 0 goes high→low.
    /// </summary>
    public void Strobe()
    {
        _shiftReg = _buttons;
        _bitPos   = 0;
    }

    /// <summary>
    /// Reads one bit from the shift register (MSB first: B button first).
    /// Returns true (button pressed) or false (released / no more data).
    /// </summary>
    public bool ReadSerial()
    {
        if (_bitPos >= 16) return true; // Returns 1 after all bits are read
        bool bit = (_shiftReg & 0x8000) != 0;
        _shiftReg <<= 1;
        _bitPos++;
        return bit;
    }

    /// <summary>Updates one button's pressed state.</summary>
    public void SetButtonState(SnesButton button, bool pressed)
    {
        if (pressed)
            _buttons |= (ushort)button;
        else
            _buttons &= (ushort)~button;
    }
}

/// <summary>
/// Manages input for both controller ports.
/// Maps host keyboard/gamepad events to SNES controller buttons
/// using a configurable key mapping (Strategy pattern for mappings).
/// </summary>
public sealed class InputManager : IInputManager
{
    private readonly ILogger<InputManager> _logger;
    private readonly SnesController _controller1 = new();
    private readonly SnesController _controller2 = new();

    private InputMappingConfig _config = new();

    public InputManager(ILogger<InputManager> logger)
    {
        _logger = logger;
    }

    public IController GetController(int port) => port == 1 ? _controller1 : _controller2;

    public void HandleKeyDown(string key)
    {
        if (_config.Player1.TryGetValue(key, out SnesButton btn1))
        {
            _controller1.SetButtonState(btn1, true);
            return;
        }
        if (_config.Player2.TryGetValue(key, out SnesButton btn2))
        {
            _controller2.SetButtonState(btn2, true);
        }
    }

    public void HandleKeyUp(string key)
    {
        if (_config.Player1.TryGetValue(key, out SnesButton btn1))
        {
            _controller1.SetButtonState(btn1, false);
            return;
        }
        if (_config.Player2.TryGetValue(key, out SnesButton btn2))
        {
            _controller2.SetButtonState(btn2, false);
        }
    }

    public void UpdateMappings(InputMappingConfig config)
    {
        _config = config;
        _logger.LogInformation("Input mappings updated.");
    }

    /// <summary>Resets both controllers.</summary>
    public void Reset()
    {
        _controller1.Reset();
        _controller2.Reset();
    }
}
