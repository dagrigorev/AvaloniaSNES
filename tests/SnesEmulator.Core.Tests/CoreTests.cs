using FluentAssertions;
using SnesEmulator.Core.Models;
using SnesEmulator.Core.Utilities;
using SnesEmulator.Graphics.Framebuffer;
using Xunit;

namespace SnesEmulator.Core.Tests;

/// <summary>Tests for BitHelper utility functions.</summary>
public sealed class BitHelperTests
{
    [Theory]
    [InlineData(0b10101010, 1, true)]
    [InlineData(0b10101010, 0, false)]
    [InlineData(0xFF, 7, true)]
    [InlineData(0x00, 7, false)]
    public void IsBitSet_Byte_ReturnsCorrectResult(byte value, int bit, bool expected)
        => BitHelper.IsBitSet(value, bit).Should().Be(expected);

    [Fact]
    public void SetBit_SetsBitCorrectly()
        => BitHelper.SetBit(0b00000000, 3).Should().Be(0b00001000);

    [Fact]
    public void ClearBit_ClearsBitCorrectly()
        => BitHelper.ClearBit(0b11111111, 3).Should().Be(0b11110111);

    [Fact]
    public void SetBitTo_True_SetsBit()
        => BitHelper.SetBitTo(0x00, 5, true).Should().Be(0x20);

    [Fact]
    public void SetBitTo_False_ClearsBit()
        => BitHelper.SetBitTo(0xFF, 5, false).Should().Be(0xDF);

    [Fact]
    public void LowByte_ExtractsCorrectly()
        => BitHelper.LowByte(0xABCD).Should().Be(0xCD);

    [Fact]
    public void HighByte_ExtractsCorrectly()
        => BitHelper.HighByte(0xABCD).Should().Be(0xAB);

    [Fact]
    public void MakeWord_CombinesCorrectly()
        => BitHelper.MakeWord(0xCD, 0xAB).Should().Be(0xABCD);

    [Fact]
    public void MakeAddress_CombinesCorrectly()
        => BitHelper.MakeAddress(0x7E, 0x1234).Should().Be(0x7E1234u);

    [Fact]
    public void BankOf_ExtractsBank()
        => BitHelper.BankOf(0x7E1234).Should().Be(0x7E);

    [Fact]
    public void OffsetOf_ExtractsOffset()
        => BitHelper.OffsetOf(0x7E1234).Should().Be(0x1234);

    [Fact]
    public void SignExtend8_Positive_ReturnsPositive()
        => BitHelper.SignExtend8(0x40).Should().Be(64);

    [Fact]
    public void SignExtend8_Negative_ReturnsNegative()
        => BitHelper.SignExtend8(0x80).Should().Be(-128);

    [Fact]
    public void MakeWord_ThenLowHighByte_RoundTrip()
    {
        ushort word = BitHelper.MakeWord(0x34, 0x12);
        BitHelper.LowByte(word).Should().Be(0x34);
        BitHelper.HighByte(word).Should().Be(0x12);
    }
}

/// <summary>Tests for the CpuRegisters snapshot model.</summary>
public sealed class CpuRegistersTests
{
    [Fact]
    public void FlagN_WhenBit7Set_ReturnsTrue()
    {
        var regs = new CpuRegisters { P = 0x80 };
        regs.FlagN.Should().BeTrue();
    }

    [Fact]
    public void FlagZ_WhenBit1Set_ReturnsTrue()
    {
        var regs = new CpuRegisters { P = 0x02 };
        regs.FlagZ.Should().BeTrue();
    }

    [Fact]
    public void FullPC_CombinesPBRAndPC()
    {
        var regs = new CpuRegisters { PBR = 0x7E, PC = 0x1234 };
        regs.FullPC.Should().Be(0x7E1234u);
    }

    [Fact]
    public void ToString_ContainsAllRegisters()
    {
        var regs = new CpuRegisters
        {
            A = 0x1234, X = 0x0001, Y = 0x0002,
            SP = 0x01FF, DP = 0x0000, PC = 0x8000,
            PBR = 0x00, DBR = 0x00, P = 0x34
        };
        string s = regs.ToString();
        s.Should().Contain("A:");
        s.Should().Contain("X:");
        s.Should().Contain("Y:");
        s.Should().Contain("PC:");
    }

    [Fact]
    public void AllFlags_DefaultP_MatchExpected()
    {
        // Default P = 0x34 = 0011_0100 → M=1, X=1, I=1
        var regs = new CpuRegisters { P = 0x34 };
        regs.FlagM.Should().BeTrue();
        regs.FlagX.Should().BeTrue();
        regs.FlagI.Should().BeTrue();
        regs.FlagN.Should().BeFalse();
        regs.FlagV.Should().BeFalse();
        regs.FlagD.Should().BeFalse();
        regs.FlagZ.Should().BeFalse();
        regs.FlagC.Should().BeFalse();
    }
}

/// <summary>Tests for the framebuffer and color conversion.</summary>
public sealed class FrameBufferTests
{
    [Fact]
    public void SnesColorToArgb_Black_ReturnsOpaqueBlack()
    {
        uint argb = SnesFrameBuffer.SnesColorToArgb(0x0000);
        argb.Should().Be(0xFF000000u);
    }

    [Fact]
    public void SnesColorToArgb_White_ReturnsOpaqueWhite()
    {
        // SNES white = 0x7FFF (all 5 bits set for R, G, B)
        uint argb = SnesFrameBuffer.SnesColorToArgb(0x7FFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >>  8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        r.Should().BeGreaterThan(240);
        g.Should().BeGreaterThan(240);
        b.Should().BeGreaterThan(240);
    }

    [Fact]
    public void SnesColorToArgb_Red_HasHighRedChannel()
    {
        // SNES red: R=31, G=0, B=0 → bits 4:0 = 11111
        uint argb = SnesFrameBuffer.SnesColorToArgb(0x001F);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >>  8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        r.Should().BeGreaterThan(240);
        g.Should().Be(0);
        b.Should().Be(0);
    }

    [Fact]
    public void FrameBuffer_SetPixel_SetsCorrectIndex()
    {
        var fb = new SnesFrameBuffer(256, 224);
        fb.SetPixel(10, 5, 0xFFAABBCC);
        fb.Pixels[5 * 256 + 10].Should().Be(0xFFAABBCC);
    }

    [Fact]
    public void FrameBuffer_Clear_SetsAllBlack()
    {
        var fb = new SnesFrameBuffer(256, 224);
        fb.SetPixel(0, 0, 0xFFFFFFFF);
        fb.Clear();
        fb.Pixels[0].Should().Be(0xFF000000u);
    }

    [Fact]
    public void FrameBuffer_OutOfBounds_DoesNotThrow()
    {
        var fb = new SnesFrameBuffer(256, 224);
        Action act = () =>
        {
            fb.SetPixel(-1, 0, 0xFF000000);
            fb.SetPixel(256, 0, 0xFF000000);
            fb.SetPixel(0, 224, 0xFF000000);
        };
        act.Should().NotThrow();
    }
}

/// <summary>Tests for ROM model value objects.</summary>
public sealed class RomModelTests
{
    [Fact]
    public void RomHeader_IsChecksumValid_CorrectXor()
    {
        var header = new RomHeader
        {
            Checksum = 0xFF00,
            ChecksumComplement = 0x00FF
        };
        header.IsChecksumValid.Should().BeTrue();
    }

    [Fact]
    public void RomHeader_IsChecksumValid_InvalidXor()
    {
        var header = new RomHeader
        {
            Checksum = 0x1234,
            ChecksumComplement = 0xABCD
        };
        header.IsChecksumValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x00, "Japan")]
    [InlineData(0x01, "North America")]
    [InlineData(0x02, "Europe")]
    public void RomHeader_Region_MapsCorrectly(byte countryCode, string expectedRegion)
    {
        var header = new RomHeader { CountryCode = countryCode };
        header.Region.Should().Be(expectedRegion);
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x01, false)]
    [InlineData(0x02, true)]
    [InlineData(0x07, true)]
    public void RomHeader_IsPal_CorrectForCountry(byte code, bool expected)
    {
        var header = new RomHeader { CountryCode = code };
        header.IsPal.Should().Be(expected);
    }

    [Fact]
    public void RomHeader_RomSizeBytes_PowerOf2()
    {
        var header = new RomHeader { RomSizeCode = 8 }; // 256 KB
        header.RomSizeBytes.Should().Be(256 * 1024);
    }

    [Fact]
    public void RomHeader_SramSizeBytes_ZeroWhenNoSram()
    {
        var header = new RomHeader { SramSizeCode = 0 };
        header.SramSizeBytes.Should().Be(0);
    }
}

/// <summary>Tests for SnesButton input flags.</summary>
public sealed class InputModelTests
{
    [Fact]
    public void SnesButton_CombineMultipleButtons()
    {
        var state = SnesButton.A | SnesButton.B | SnesButton.Start;
        (state & SnesButton.A).Should().Be(SnesButton.A);
        (state & SnesButton.B).Should().Be(SnesButton.B);
        (state & SnesButton.Start).Should().Be(SnesButton.Start);
        (state & SnesButton.Select).Should().Be(SnesButton.None);
    }

    [Fact]
    public void InputMappingConfig_DefaultPlayer1_ContainsAllButtons()
    {
        var config = new InputMappingConfig();
        config.Player1.Should().ContainKey("Z"); // B button
        config.Player1.Should().ContainKey("Return"); // Start
        config.Player1.Should().ContainKey("Up");
        config.Player1.Should().ContainKey("Down");
    }
}
