namespace SnesEmulator.Core.Utilities;

/// <summary>
/// Low-level bit manipulation helpers used throughout the emulator.
/// These are inlined hot-path utilities for manipulating CPU flags and register values.
/// </summary>
public static class BitHelper
{
    /// <summary>Returns true if the specified bit is set.</summary>
    public static bool IsBitSet(byte value, int bit) => (value & (1 << bit)) != 0;

    /// <summary>Returns true if the specified bit is set in a ushort.</summary>
    public static bool IsBitSet(ushort value, int bit) => (value & (1 << bit)) != 0;

    /// <summary>Sets a specific bit in a byte and returns the result.</summary>
    public static byte SetBit(byte value, int bit) => (byte)(value | (1 << bit));

    /// <summary>Clears a specific bit in a byte and returns the result.</summary>
    public static byte ClearBit(byte value, int bit) => (byte)(value & ~(1 << bit));

    /// <summary>Sets or clears a bit based on a boolean condition.</summary>
    public static byte SetBitTo(byte value, int bit, bool set) =>
        set ? SetBit(value, bit) : ClearBit(value, bit);

    /// <summary>Extracts the low byte of a 16-bit value.</summary>
    public static byte LowByte(ushort value) => (byte)(value & 0xFF);

    /// <summary>Extracts the high byte of a 16-bit value.</summary>
    public static byte HighByte(ushort value) => (byte)((value >> 8) & 0xFF);

    /// <summary>Combines two bytes into a 16-bit word (little-endian).</summary>
    public static ushort MakeWord(byte low, byte high) => (ushort)((high << 8) | low);

    /// <summary>Combines a bank byte and a 16-bit address into a 24-bit address.</summary>
    public static uint MakeAddress(byte bank, ushort offset) => ((uint)bank << 16) | offset;

    /// <summary>Extracts the bank byte from a 24-bit address.</summary>
    public static byte BankOf(uint address) => (byte)((address >> 16) & 0xFF);

    /// <summary>Extracts the 16-bit offset from a 24-bit address.</summary>
    public static ushort OffsetOf(uint address) => (ushort)(address & 0xFFFF);

    /// <summary>Sign-extends an 8-bit value to 16 bits.</summary>
    public static short SignExtend8(byte value) => (sbyte)value;

    /// <summary>Sign-extends an 8-bit value to 32 bits.</summary>
    public static int SignExtend8To32(byte value) => (sbyte)value;

    /// <summary>Wraps a 16-bit addition within the same bank (no bank crossing for some instructions).</summary>
    public static ushort WrapAround16(int value) => (ushort)(value & 0xFFFF);
}
