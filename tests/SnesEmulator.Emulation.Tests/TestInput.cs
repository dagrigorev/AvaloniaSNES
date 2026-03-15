using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;

namespace SnesEmulator.Emulation.Tests;

internal sealed class TestController : IController
{
    private ushort _buttons;
    private ushort _shiftReg;
    private int _bitPos;

    public string Name => "Test Controller";
    public ushort ButtonState => _buttons;

    public void Reset()
    {
        _buttons = 0;
        _shiftReg = 0;
        _bitPos = 0;
    }

    public bool ReadSerial()
    {
        if (_bitPos >= 16)
            return true;

        bool bit = (_shiftReg & 0x8000) != 0;
        _shiftReg <<= 1;
        _bitPos++;
        return bit;
    }

    public void Strobe()
    {
        _shiftReg = _buttons;
        _bitPos = 0;
    }

    public void SetButtonState(SnesButton button, bool pressed)
    {
        if (pressed)
            _buttons |= (ushort)button;
        else
            _buttons &= (ushort)~button;
    }
}

internal sealed class TestInputManager : IInputManager
{
    private readonly TestController _controller1 = new();
    private readonly TestController _controller2 = new();

    public IController GetController(int port) => port == 1 ? _controller1 : _controller2;
    public void HandleKeyDown(string key) { }
    public void HandleKeyUp(string key) { }
    public void UpdateMappings(InputMappingConfig config) { }

    public void SetButtonState(int port, SnesButton button, bool pressed)
    {
        var controller = port == 1 ? _controller1 : _controller2;
        controller.SetButtonState(button, pressed);
    }
}
