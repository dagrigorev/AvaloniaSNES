namespace SnesEmulator.Core.Exceptions;

/// <summary>
/// Thrown when a ROM file cannot be loaded or parsed.
/// </summary>
public sealed class RomLoadException : Exception
{
    public string? FilePath { get; }

    public RomLoadException(string message, string? filePath = null)
        : base(message)
    {
        FilePath = filePath;
    }

    public RomLoadException(string message, string? filePath, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
    }
}

/// <summary>
/// Thrown when the CPU encounters an unimplemented or illegal opcode.
/// </summary>
public sealed class InvalidOpcodeException : Exception
{
    public byte Opcode { get; }
    public uint Address { get; }

    public InvalidOpcodeException(byte opcode, uint address)
        : base($"Unimplemented opcode 0x{opcode:X2} at address {address:X6}")
    {
        Opcode = opcode;
        Address = address;
    }
}

/// <summary>
/// Thrown when the emulator is asked to perform an operation
/// that is invalid for its current state (e.g., Step when no ROM is loaded).
/// </summary>
public sealed class EmulatorStateException : Exception
{
    public EmulatorStateException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a save state file is corrupt or incompatible.
/// </summary>
public sealed class SaveStateException : Exception
{
    public SaveStateException(string message) : base(message) { }
    public SaveStateException(string message, Exception inner) : base(message, inner) { }
}
