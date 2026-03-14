namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Abstracts the SNES memory bus. All CPU reads and writes pass through here.
/// The bus is responsible for routing addresses to the correct device
/// (ROM, WRAM, I/O registers, etc.) according to the SNES memory map.
/// </summary>
public interface IMemoryBus
{
    /// <summary>
    /// Reads a single byte from the given 24-bit address (bank:offset).
    /// </summary>
    /// <param name="address">24-bit address: bits 23-16 = bank, bits 15-0 = offset.</param>
    /// <returns>Byte value at that address.</returns>
    byte Read(uint address);

    /// <summary>
    /// Writes a single byte to the given 24-bit address.
    /// </summary>
    void Write(uint address, byte value);

    /// <summary>
    /// Reads a 16-bit word (little-endian) from the given address.
    /// </summary>
    ushort ReadWord(uint address);

    /// <summary>
    /// Writes a 16-bit word (little-endian) to the given address.
    /// </summary>
    void WriteWord(uint address, ushort value);
}

/// <summary>
/// Represents a device that can be mapped into the SNES address space.
/// Devices register themselves with the memory bus for specific address ranges.
/// </summary>
public interface IMemoryMappedDevice : IEmulatorComponent
{
    /// <summary>
    /// Called when the bus routes a read to this device.
    /// </summary>
    byte Read(uint address);

    /// <summary>
    /// Called when the bus routes a write to this device.
    /// </summary>
    void Write(uint address, byte value);
}
