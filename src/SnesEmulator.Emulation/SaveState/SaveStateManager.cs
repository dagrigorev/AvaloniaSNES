using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Emulation.SaveState;

/// <summary>
/// Manages save state serialization and deserialization.
/// A save state is a snapshot of all stateful components at a given point in time.
///
/// File format (binary):
///   [4 bytes]  Magic: "SNES"
///   [4 bytes]  Version: 1
///   [4 bytes]  CPU state length
///   [N bytes]  CPU state data
///   [4 bytes]  PPU state length
///   [N bytes]  PPU state data
///   [4 bytes]  APU state length
///   [N bytes]  APU state data
///   [4 bytes]  WRAM state length
///   [N bytes]  WRAM state data
/// </summary>
public sealed class SaveStateManager
{
    private readonly ILogger<SaveStateManager> _logger;
    private static readonly byte[] Magic = "SNES"u8.ToArray();
    private const int FormatVersion = 1;

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
    /// Loads emulator state from a file.
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

            // Validate version
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new SaveStateException($"Unsupported save state version {version} (expected {FormatVersion}).");

            cpu.LoadState(ReadSection(reader));
            ppu.LoadState(ReadSection(reader));
            apu.LoadState(ReadSection(reader));
            wram.LoadState(ReadSection(reader));

            _logger.LogInformation("Save state loaded: {Path}", filePath);
        }
        catch (SaveStateException)
        {
            throw;
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
        return reader.ReadBytes(length);
    }
}
