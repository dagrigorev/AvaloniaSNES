using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Models;
using SnesEmulator.Hardware.Rom;
using Xunit;

namespace SnesEmulator.Hardware.Tests;

/// <summary>
/// Unit tests for the ROM loader.
/// We use a minimal synthetic ROM to avoid any real game files.
/// </summary>
public sealed class RomLoaderTests
{
    private readonly RomLoader _loader = new(NullLogger<RomLoader>.Instance);

    // ── Helper: build a minimal valid LoROM ───────────────────────────────────

    private static byte[] BuildLoRom(string title = "TEST GAME", bool validChecksum = true)
    {
        // Minimum LoROM = 32 KB
        var data = new byte[0x8000];

        // Write header at $7FC0
        int offset = 0x7FC0;
        byte[] titleBytes = System.Text.Encoding.ASCII.GetBytes(title.PadRight(21));
        Array.Copy(titleBytes, 0, data, offset, 21);

        data[offset + 0x15] = 0x20; // MapMode: LoROM
        data[offset + 0x16] = 0x00; // RomType: ROM only
        data[offset + 0x17] = 0x08; // RomSize: 256 KB code
        data[offset + 0x18] = 0x00; // SramSize: 0
        data[offset + 0x19] = 0x01; // Country: North America

        if (validChecksum)
        {
            // Set checksum complement + checksum to valid pair
            data[offset + 0x1C] = 0x00; // ChecksumComplement low
            data[offset + 0x1D] = 0xFF; // ChecksumComplement high
            data[offset + 0x1E] = 0xFF; // Checksum low
            data[offset + 0x1F] = 0x00; // Checksum high  (0xFF00 ^ 0x00FF = 0xFFFF ✓)
        }

        return data;
    }

    private static string WriteToTempFile(byte[] data, string ext = ".smc")
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_rom_{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, data);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadRom_ValidLoRom_ParsesTitleCorrectly()
    {
        string path = WriteToTempFile(BuildLoRom("TEST GAME"));
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.Header.Title.Should().Be("TEST GAME");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadRom_ValidLoRom_DetectsMappingMode()
    {
        string path = WriteToTempFile(BuildLoRom());
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.MappingMode.Should().Be(RomMappingMode.LoRom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadRom_SizeMod1024Is512_StripsHeader()
    {
        // 512-byte copier header prepended
        byte[] romData   = BuildLoRom();
        byte[] withHeader = new byte[romData.Length + 512];
        Array.Copy(romData, 0, withHeader, 512, romData.Length);

        string path = WriteToTempFile(withHeader);
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.HadCopierHeader.Should().BeTrue();
            rom.Data.Length.Should().Be(romData.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadRom_FileNotFound_ThrowsRomLoadException()
    {
        Action act = () => _loader.LoadRom("/nonexistent/path/game.smc");
        act.Should().Throw<RomLoadException>()
           .WithMessage("*not found*");
    }

    [Fact]
    public void LoadRom_TooSmallFile_ThrowsRomLoadException()
    {
        string path = WriteToTempFile(new byte[100]);
        try
        {
            Action act = () => _loader.LoadRom(path);
            act.Should().Throw<RomLoadException>()
               .WithMessage("*too small*");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadRom_EmptyPath_ThrowsRomLoadException()
    {
        Action act = () => _loader.LoadRom(string.Empty);
        act.Should().Throw<RomLoadException>();
    }

    [Fact]
    public void LoadRom_ValidChecksum_ReportsChecksumValid()
    {
        string path = WriteToTempFile(BuildLoRom(validChecksum: true));
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.Header.IsChecksumValid.Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RomHeader_NtscCountry_IsNotPal()
    {
        string path = WriteToTempFile(BuildLoRom());
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.Header.IsPal.Should().BeFalse();
            rom.Header.Region.Should().Be("North America");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RomData_SizeKilobytes_MatchesDataLength()
    {
        byte[] data = BuildLoRom();
        string path = WriteToTempFile(data);
        try
        {
            RomData rom = _loader.LoadRom(path);
            rom.SizeKilobytes.Should().Be(data.Length / 1024);
        }
        finally { File.Delete(path); }
    }
}
