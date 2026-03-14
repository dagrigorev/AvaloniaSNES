using Microsoft.Extensions.Logging;
using SnesEmulator.Core;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;

namespace SnesEmulator.Hardware.Rom;

/// <summary>
/// Loads and parses SNES ROM files (.smc / .sfc).
///
/// Detection strategy:
///   1. Strip 512-byte copier header if file size mod 1024 == 512.
///   2. Try reading a header at the HiROM offset ($FFC0); score it.
///   3. Try reading a header at the LoROM offset ($7FC0); score it.
///   4. Pick the mapping mode with the higher confidence score.
/// </summary>
public sealed class RomLoader : IRomLoader
{
    private readonly ILogger<RomLoader> _logger;

    public RomLoader(ILogger<RomLoader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public RomData LoadRom(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new RomLoadException("File path cannot be empty.", filePath);

        if (!File.Exists(filePath))
            throw new RomLoadException($"ROM file not found: {filePath}", filePath);

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is not (".smc" or ".sfc" or ".fig" or ".bin"))
            _logger.LogWarning("Unusual ROM extension '{Ext}'. Attempting to load anyway.", extension);

        _logger.LogInformation("Loading ROM: {Path}", filePath);

        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(filePath);
        }
        catch (IOException ex)
        {
            throw new RomLoadException($"Failed to read ROM file: {ex.Message}", filePath, ex);
        }

        if (rawBytes.Length < 0x8000)
            throw new RomLoadException(
                $"ROM file too small ({rawBytes.Length} bytes). Minimum valid SNES ROM is 32 KB.", filePath);

        // Strip optional 512-byte copier header
        bool hadCopierHeader = rawBytes.Length % 1024 == SnesConstants.CopierHeaderSize;
        byte[] romData = hadCopierHeader
            ? rawBytes[SnesConstants.CopierHeaderSize..]
            : rawBytes;

        if (hadCopierHeader)
            _logger.LogDebug("Stripped 512-byte copier header.");

        // Determine mapping mode and parse header
        (RomMappingMode mappingMode, RomHeader header) = DetectMappingMode(romData, filePath);

        _logger.LogInformation(
            "ROM loaded: '{Title}' | {Mode} | {Size} KB | Region: {Region} | Checksum: {Cs}",
            header.Title, mappingMode, romData.Length / 1024,
            header.Region, header.IsChecksumValid ? "OK" : "INVALID");

        return new RomData(romData, header, mappingMode, hadCopierHeader);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private (RomMappingMode mode, RomHeader header) DetectMappingMode(byte[] data, string filePath)
    {
        // Score both possible header locations and pick the more credible one.
        int loScore = 0;
        int hiScore = 0;
        RomHeader? loHeader = null;
        RomHeader? hiHeader = null;

        if (data.Length > SnesConstants.LoRomHeaderOffset + 0x30)
        {
            loHeader = ParseHeader(data, SnesConstants.LoRomHeaderOffset);
            loScore = ScoreHeader(loHeader, data, RomMappingMode.LoRom);
        }

        if (data.Length > SnesConstants.HiRomHeaderOffset + 0x30)
        {
            hiHeader = ParseHeader(data, SnesConstants.HiRomHeaderOffset);
            hiScore = ScoreHeader(hiHeader, data, RomMappingMode.HiRom);
        }

        _logger.LogDebug("Header scores — LoROM: {Lo}, HiROM: {Hi}", loScore, hiScore);

        if (loScore <= 0 && hiScore <= 0)
            throw new RomLoadException("Could not identify a valid SNES ROM header.", filePath);

        if (hiScore > loScore && hiHeader is not null)
            return (RomMappingMode.HiRom, hiHeader);

        if (loHeader is not null)
            return (RomMappingMode.LoRom, loHeader);

        throw new RomLoadException("Failed to determine ROM mapping mode.", filePath);
    }

    /// <summary>
    /// Reads and parses the 32-byte ROM internal header at the given offset.
    /// </summary>
    private static RomHeader ParseHeader(byte[] data, int offset)
    {
        // Title: bytes $00–$14 (21 chars, ASCII, space-padded)
        string title = System.Text.Encoding.ASCII
            .GetString(data, offset, 21)
            .TrimEnd('\0', ' ');

        return new RomHeader
        {
            Title               = title,
            MapMode             = data[offset + 0x15],
            RomType             = data[offset + 0x16],
            RomSizeCode         = data[offset + 0x17],
            SramSizeCode        = data[offset + 0x18],
            CountryCode         = data[offset + 0x19],
            DeveloperId         = data[offset + 0x1A],
            Version             = data[offset + 0x1B],
            ChecksumComplement  = (ushort)(data[offset + 0x1C] | (data[offset + 0x1D] << 8)),
            Checksum            = (ushort)(data[offset + 0x1E] | (data[offset + 0x1F] << 8))
        };
    }

    /// <summary>
    /// Assigns a confidence score to a parsed header.
    /// Higher = more likely correct header for the given mapping mode.
    /// </summary>
    private static int ScoreHeader(RomHeader header, byte[] data, RomMappingMode mode)
    {
        int score = 0;

        // Checksum complement + checksum must XOR to $FFFF
        if ((header.Checksum ^ header.ChecksumComplement) == 0xFFFF)
            score += 4;

        // Map mode byte should match expected bits for LoROM/HiROM
        byte expectedMapBit = mode == RomMappingMode.LoRom ? (byte)0x20 : (byte)0x21;
        if ((header.MapMode & 0x2F) == expectedMapBit)
            score += 2;

        // ROM size code should be reasonable (8–13 = 256 KB to 8 MB)
        if (header.RomSizeCode is >= 8 and <= 13)
            score += 1;

        // ROM size code should approximately match actual file size
        int expectedSize = 1024 << header.RomSizeCode;
        if (Math.Abs(expectedSize - data.Length) < expectedSize / 2)
            score += 1;

        // Title should contain only printable ASCII
        bool titlePrintable = header.Title.All(c => c is >= ' ' and <= '~');
        if (titlePrintable && header.Title.Length > 0)
            score += 1;

        // Country code should be a known value
        if (header.CountryCode <= 0x0C)
            score += 1;

        return score;
    }
}
