namespace SnesEmulator.Core.Models;

/// <summary>
/// SNES ROM memory mapping modes.
/// Determines how the ROM is laid out in the 24-bit address space.
/// </summary>
public enum RomMappingMode
{
    /// <summary>LoROM: ROM data mapped at banks $00-$7D and $80-$FF, offset $8000-$FFFF.</summary>
    LoRom,

    /// <summary>HiROM: ROM data mapped at banks $40-$7D and $C0-$FF, offset $0000-$FFFF.</summary>
    HiRom,

    /// <summary>ExHiROM: Extended HiROM for ROMs larger than 4 MB.</summary>
    ExHiRom,

    /// <summary>Special or unrecognized mapping mode.</summary>
    Unknown
}

/// <summary>
/// SNES ROM type flags from the ROM header.
/// </summary>
[Flags]
public enum RomChipType : byte
{
    RomOnly = 0x00,
    RomRam = 0x01,
    RomRamBattery = 0x02,
    RomCoprocessor = 0x03,
    SA1 = 0x34,
    SuperFX = 0x13
}

/// <summary>
/// Contains the raw bytes and metadata parsed from a loaded SNES ROM file.
/// This is the value object produced by IRomLoader.
/// </summary>
public sealed class RomData
{
    /// <summary>The raw ROM data bytes (after stripping any copier header).</summary>
    public byte[] Data { get; }

    /// <summary>Parsed ROM header information.</summary>
    public RomHeader Header { get; }

    /// <summary>Detected memory mapping mode.</summary>
    public RomMappingMode MappingMode { get; }

    /// <summary>True if the ROM had a 512-byte copier header that was stripped.</summary>
    public bool HadCopierHeader { get; }

    public RomData(byte[] data, RomHeader header, RomMappingMode mappingMode, bool hadCopierHeader)
    {
        Data = data;
        Header = header;
        MappingMode = mappingMode;
        HadCopierHeader = hadCopierHeader;
    }

    /// <summary>Size of the ROM in bytes.</summary>
    public int SizeBytes => Data.Length;

    /// <summary>Size of the ROM in kilobytes.</summary>
    public int SizeKilobytes => Data.Length / 1024;
}

/// <summary>
/// Parsed SNES ROM internal header.
/// The header is located at different offsets depending on mapping mode:
///   LoROM: $007FC0  HiROM: $00FFC0
/// </summary>
public sealed class RomHeader
{
    /// <summary>Game title (21 ASCII characters, space-padded).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Map mode byte from header.</summary>
    public byte MapMode { get; init; }

    /// <summary>ROM type (chip configuration).</summary>
    public byte RomType { get; init; }

    /// <summary>ROM size in power-of-2 kilobytes (e.g., 8 = 256 KB).</summary>
    public byte RomSizeCode { get; init; }

    /// <summary>SRAM size in power-of-2 kilobytes.</summary>
    public byte SramSizeCode { get; init; }

    /// <summary>Country/region code.</summary>
    public byte CountryCode { get; init; }

    /// <summary>Developer ID.</summary>
    public byte DeveloperId { get; init; }

    /// <summary>ROM version number.</summary>
    public byte Version { get; init; }

    /// <summary>Checksum complement (should be complement of Checksum).</summary>
    public ushort ChecksumComplement { get; init; }

    /// <summary>Checksum of the ROM.</summary>
    public ushort Checksum { get; init; }

    /// <summary>True if the checksum is valid (complement XOR checksum == 0xFFFF).</summary>
    public bool IsChecksumValid => (ushort)(Checksum ^ ChecksumComplement) == 0xFFFF;

    /// <summary>Inferred ROM size in bytes from the size code.</summary>
    public int RomSizeBytes => (1 << RomSizeCode) * 1024;

    /// <summary>Inferred SRAM size in bytes (0 if no SRAM).</summary>
    public int SramSizeBytes => SramSizeCode > 0 ? (1 << SramSizeCode) * 1024 : 0;

    /// <summary>Detected region based on country code.</summary>
    public string Region => CountryCode switch
    {
        0x00 => "Japan",
        0x01 => "North America",
        0x02 => "Europe",
        0x03 => "Sweden",
        0x04 => "Finland",
        0x05 => "Denmark",
        0x06 => "France",
        0x07 => "Netherlands",
        0x08 => "Spain",
        0x09 => "Germany",
        0x0A => "Italy",
        0x0B => "China/Hong Kong",
        _ => "Unknown"
    };

    /// <summary>True if this is a PAL region ROM (affects timing).</summary>
    public bool IsPal => CountryCode >= 0x02 && CountryCode <= 0x0C;
}

/// <summary>
/// Summary information about a loaded ROM, exposed via the emulator facade.
/// </summary>
public sealed record RomInfo(
    string Title,
    string FilePath,
    RomMappingMode MappingMode,
    int SizeKilobytes,
    bool IsPal,
    bool IsChecksumValid,
    string Region
);
