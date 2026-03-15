using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Core;
using SnesEmulator.Core.Models;
using SnesEmulator.Emulation.Memory;
using Xunit;

namespace SnesEmulator.Emulation.Tests;

/// <summary>
/// Tests for the SNES memory bus — routing, WRAM access, and address decoding.
/// </summary>
public sealed class MemoryBusTests
{
    private MemoryBus CreateBus()
    {
        var wram = new WorkRam();
        wram.Reset();
        var bus = new MemoryBus(wram, new TestInputManager(), NullLogger<MemoryBus>.Instance);
        bus.Reset();
        return bus;
    }

    [Fact]
    public void Write_ReadWram_ReturnsWrittenValue()
    {
        var bus = CreateBus();
        bus.Write(0x7E0010, 0xAB);
        bus.Read(0x7E0010).Should().Be(0xAB);
    }

    [Fact]
    public void Write_WramBank7F_AccessesUpperWram()
    {
        var bus = CreateBus();
        bus.Write(0x7F0020, 0x55);
        bus.Read(0x7F0020).Should().Be(0x55);
    }

    [Fact]
    public void ReadWord_LittleEndian_CorrectOrder()
    {
        var bus = CreateBus();
        bus.Write(0x7E0100, 0x34);
        bus.Write(0x7E0101, 0x12);
        bus.ReadWord(0x7E0100).Should().Be(0x1234);
    }

    [Fact]
    public void WriteWord_LittleEndian_CorrectBytes()
    {
        var bus = CreateBus();
        bus.WriteWord(0x7E0200, 0xABCD);
        bus.Read(0x7E0200).Should().Be(0xCD); // Low byte
        bus.Read(0x7E0201).Should().Be(0xAB); // High byte
    }

    [Fact]
    public void WramMirror_Bank00_Offset0000To1FFF_ReadsWram()
    {
        var bus = CreateBus();
        // Write to full WRAM address
        bus.Write(0x7E0050, 0x42);
        // Read from mirrored location in bank 0
        bus.Read(0x000050).Should().Be(0x42);
    }

    [Fact]
    public void Write_MultipleBytesSequential_AllReadBack()
    {
        var bus = CreateBus();
        for (byte i = 0; i < 16; i++)
            bus.Write((uint)(0x7E1000 + i), i);

        for (byte i = 0; i < 16; i++)
            bus.Read((uint)(0x7E1000 + i)).Should().Be(i);
    }

    [Fact]
    public void WramPort_WriteAndRead_DataAccess()
    {
        // The WRAM port at $2180 provides sequential access
        var wram = new WorkRam();
        wram.Reset();
        wram.WriteWmAddressLow(0x00);
        wram.WriteWmAddressMid(0x10);
        wram.WriteWmAddressHigh(0x00); // Address = $1000

        wram.WriteWmData(0xDE);
        wram.WriteWmData(0xAD);

        wram.WriteWmAddressLow(0x00);
        wram.WriteWmAddressMid(0x10);
        wram.WriteWmAddressHigh(0x00);

        wram.ReadWmData().Should().Be(0xDE);
        wram.ReadWmData().Should().Be(0xAD);
    }

    [Fact]
    public void UnmappedRead_ReturnsOpenBus()
    {
        var bus = CreateBus();
        // Reading unmapped area should return 0xFF (open bus)
        bus.Read(0x400000).Should().Be(0xFF);
    }

    [Fact]
    public void Reset_ClearsPreviousWrites()
    {
        var bus = CreateBus();
        bus.Write(0x7E0000, 0x99);
        bus.Reset();
        bus.Read(0x7E0000).Should().Be(0x00);
    }
    [Fact]
    public void CpuDivideRegisters_PreserveDividendForRemainder()
    {
        var bus = CreateBus();
        bus.Write(0x004204, 0x34);
        bus.Write(0x004205, 0x12); // dividend = 0x1234
        bus.Write(0x004206, 0x10); // divisor = 16

        bus.ReadWord(0x004214).Should().Be(0x0123);
        bus.ReadWord(0x004216).Should().Be(0x0004);
    }

    [Fact]
    public void ControllerPort_ReadsLatchedButtonStateSerially()
    {
        var wram = new WorkRam();
        wram.Reset();
        var input = new TestInputManager();
        var bus = new MemoryBus(wram, input, NullLogger<MemoryBus>.Instance);
        bus.Reset();

        input.SetButtonState(1, SnesButton.B, true);
        bus.Write(0x004016, 0x01);
        bus.Write(0x004016, 0x00);

        (bus.Read(0x004016) & 0x01).Should().Be(1);
    }

}
