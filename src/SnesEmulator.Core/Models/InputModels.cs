namespace SnesEmulator.Core.Models;

/// <summary>
/// SNES controller buttons as a flags enum.
/// The 16 bits match the serial read order of the joypad:
/// B, Y, Select, Start, Up, Down, Left, Right, A, X, L, R, (4 reserved)
/// </summary>
[Flags]
public enum SnesButton : ushort
{
    None   = 0x0000,
    B      = 0x8000,
    Y      = 0x4000,
    Select = 0x2000,
    Start  = 0x1000,
    Up     = 0x0800,
    Down   = 0x0400,
    Left   = 0x0200,
    Right  = 0x0100,
    A      = 0x0080,
    X      = 0x0040,
    L      = 0x0020,
    R      = 0x0010
}

/// <summary>
/// Keyboard-to-controller button mapping configuration.
/// Maps string key names (as returned by UI frameworks) to SNES buttons.
/// </summary>
public sealed class InputMappingConfig
{
    /// <summary>Mappings for player 1 controller.</summary>
    public Dictionary<string, SnesButton> Player1 { get; set; } = new(DefaultPlayer1Mapping);

    /// <summary>Mappings for player 2 controller.</summary>
    public Dictionary<string, SnesButton> Player2 { get; set; } = new(DefaultPlayer2Mapping);

    /// <summary>Default keyboard mapping for player 1.</summary>
    public static readonly Dictionary<string, SnesButton> DefaultPlayer1Mapping = new()
    {
        ["Z"]         = SnesButton.B,
        ["A"]         = SnesButton.Y,
        ["X"]         = SnesButton.A,
        ["S"]         = SnesButton.X,
        ["Q"]         = SnesButton.L,
        ["W"]         = SnesButton.R,
        ["Return"]    = SnesButton.Start,
        ["Back"]      = SnesButton.Select,
        ["Up"]        = SnesButton.Up,
        ["Down"]      = SnesButton.Down,
        ["Left"]      = SnesButton.Left,
        ["Right"]     = SnesButton.Right
    };

    /// <summary>Default keyboard mapping for player 2 (numpad).</summary>
    public static readonly Dictionary<string, SnesButton> DefaultPlayer2Mapping = new()
    {
        ["NumPad0"]   = SnesButton.B,
        ["NumPad1"]   = SnesButton.Y,
        ["NumPad2"]   = SnesButton.Down,
        ["NumPad3"]   = SnesButton.A,
        ["NumPad4"]   = SnesButton.Left,
        ["NumPad6"]   = SnesButton.Right,
        ["NumPad7"]   = SnesButton.L,
        ["NumPad8"]   = SnesButton.Up,
        ["NumPad9"]   = SnesButton.R,
        ["NumPadEnter"] = SnesButton.Start
    };
}
