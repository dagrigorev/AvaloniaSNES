using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Emulation.SaveState;

/// <summary>
/// Manages save state serialization and deserialization.
/// A save state is a snapshot of all stateful components at a given point in time.
///
/// File format (binary), version 2:
///   [4 bytes]  Magic: "SNES"
///   [4 bytes]  FormatVersion (int32, big-endian)
///   [4 bytes]  CPU state data length
///   [N bytes]  CPU state data
///   [4 bytes]  PPU state data length
///   [N bytes]  PPU state data
///   [4 bytes]  APU state data length
///   [N bytes]  APU state data
///   [4 bytes]  WRAM state data length
///   [N bytes]  WRAM state data
///
/// Version history:
///   1 — initial format (unused, no migration needed)
///   2 — current; transactional load, length bounds checking
/// </summary>
public sealed class SaveStateManager
{
    private readonly ILogger<SaveStateManager> _logger;
    private static readonly byte[] Magic = "SNES"u8.ToArray();
    private const int FormatVersion = 2;
    private const int MinSupportedVersion = 2;

    public SaveStateManager(ILogger<SaveStateManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Saves the current emulator state to a file.
    /// </summary>
    public void SaveState(string filePath, ICpu cpu, IPpu ppu, IApu apu, IStateful wram)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write header
            writer.Write(Magic);
            writer.Write(FormatVersion);

            // Write each component's state
            WriteSection(writer, cpu.SaveState());
            WriteSection(writer, ppu.SaveState());
            WriteSection(writer, apu.SaveState());
            WriteSection(writer, wram.SaveState());

            _logger.LogInformation("Save state written: {Path}", filePath);
        }
        catch (Exception ex) when (ex is not SaveStateException)
        {
            throw new SaveStateException($"Failed to write save state: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads emulator state from a file. Uses transactional read:
    /// all component data is deserialised from the file first, then
    /// applied to components. If deserialisation fails, no component
    /// state is mutated.
    /// </summary>
    public void LoadState(string filePath, ICpu cpu, IPpu ppu, IApu apu, IStateful wram)
    {
        if (!File.Exists(filePath))
            throw new SaveStateException($"Save state file not found: {filePath}");

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            // Validate magic
            byte[] magic = reader.ReadBytes(4);
            if (!magic.SequenceEqual(Magic))
                throw new SaveStateException("Invalid save state file (bad magic).");

            // Validate version (range check for future/old compatibility)
            int version = reader.ReadInt32();
            if (version > FormatVersion)
                throw new SaveStateException(
                    $"Save state version {version} is from a newer emulator version (max supported: {FormatVersion}). " +
                    $"Please update the emulator.");
            if (version < MinSupportedVersion)
                throw new SaveStateException(
                    $"Save state version {version} is too old (minimum supported: {MinSupportedVersion}).");

            // Transactional read: deserialise all sections before mutating components
            byte[] cpuData   = ReadSection(reader);
            byte[] ppuData   = ReadSection(reader);
            byte[] apuData   = ReadSection(reader);
            byte[] wramData  = ReadSection(reader);

            // Apply — safe after all reads succeeded
            cpu.LoadState(cpuData);
            ppu.LoadState(ppuData);
            apu.LoadState(apuData);
            wram.LoadState(wramData);

            _logger.LogInformation("Save state loaded: {Path}", filePath);
        }
        catch (SaveStateException)
        {
            throw;
        }
        catch (EndOfStreamException)
        {
            throw new SaveStateException("Save state file is truncated or corrupted.");
        }
        catch (Exception ex)
        {
            throw new SaveStateException($"Failed to read save state: {ex.Message}", ex);
        }
    }

    private static void WriteSection(BinaryWriter writer, byte[] data)
    {
        writer.Write(data.Length);
        writer.Write(data);
    }

    private static byte[] ReadSection(BinaryReader reader)
    {
        int length = reader.ReadInt32();

        if (length < 0)
            throw new SaveStateException($"Corrupted save state: negative section length ({length}).");
        if (length > 10 * 1024 * 1024)
            throw new SaveStateException($"Corrupted save state: section length ({length}) exceeds maximum allowed (10 MiB).");

        byte[] data = reader.ReadBytes(length);
        if (data.Length != length)
            throw new SaveStateException($"Corrupted save state: section data truncated (expected {length} bytes, got {data.Length}).");
        return data;
    }
}
