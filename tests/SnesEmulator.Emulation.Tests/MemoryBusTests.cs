using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;
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

    [Fact]
    public void Rdnmi_ReadClearsLatch_AndNmiPendsOnlyOnVblankEdge()
    {
        var bus = CreateBus();

        bus.Write(0x004200, 0x80); // enable NMI
        bus.SetVBlankState(false, 0, 224);
        bus.SetVBlankState(true, 0, 225);

        bus.ConsumeNmi().Should().BeTrue();
        bus.ConsumeNmi().Should().BeFalse();

        bus.Read(0x004210).Should().Be(0x82);
        bus.Read(0x004210).Should().Be(0x02);

        bus.SetVBlankState(true, 0, 226);
        bus.ConsumeNmi().Should().BeFalse();
    }


    [Fact]
    public void EnablingNmiInsideActiveVblank_PendsAtMostOnceUntilNextEdge()
    {
        var bus = CreateBus();

        bus.Write(0x004200, 0x00);
        bus.SetVBlankState(false, 0, 224);
        bus.SetVBlankState(true, 0, 225);

        bus.ConsumeNmi().Should().BeFalse();

        bus.Write(0x004200, 0x80);
        bus.ConsumeNmi().Should().BeTrue();
        bus.ConsumeNmi().Should().BeFalse();

        bus.Write(0x004200, 0x00);
        bus.Write(0x004200, 0x80);
        bus.ConsumeNmi().Should().BeFalse();

        bus.SetVBlankState(false, 0, 260);
        bus.SetVBlankState(true, 1, 225);
        bus.ConsumeNmi().Should().BeTrue();
    }

    [Fact]
    public void RdnmiReadDuringActiveVblank_DoesNotCreateAnotherPendingNmi()
    {
        var bus = CreateBus();

        bus.Write(0x004200, 0x80);
        bus.SetVBlankState(false, 0, 224);
        bus.SetVBlankState(true, 0, 225);

        bus.ConsumeNmi().Should().BeTrue();
        bus.Read(0x004210).Should().Be(0x82);
        bus.Read(0x004210).Should().Be(0x02);
        bus.ConsumeNmi().Should().BeFalse();
    }

    [Fact]
    public void Hvbjoy_ReflectsCurrentVblankAndHblankBits()
    {
        var bus = CreateBus();

        bus.SetHvBjoy(inVBlank: false, inHBlank: false);
        bus.Read(0x004212).Should().Be(0x00);

        bus.SetHvBjoy(inVBlank: false, inHBlank: true);
        bus.Read(0x004212).Should().Be(0x40);

        bus.SetHvBjoy(inVBlank: true, inHBlank: false);
        bus.Read(0x004212).Should().Be(0x80);

        bus.SetHvBjoy(inVBlank: true, inHBlank: true);
        bus.Read(0x004212).Should().Be(0xC0);
    }

    [Fact]
    public void Dma_Mode1_AlternatesBetweenVramLowAndHigh_AndAdvancesSourceRegisters()
    {
        var wram = new WorkRam();
        wram.Reset();
        var ppu = new RecordingPpu();
        var bus = new MemoryBus(wram, new TestInputManager(), NullLogger<MemoryBus>.Instance);
        bus.AttachDevices(ppu, new StubApu());
        bus.Reset();

        bus.Write(0x7E1000, 0x11);
        bus.Write(0x7E1001, 0x22);
        bus.Write(0x7E1002, 0x33);
        bus.Write(0x7E1003, 0x44);

        bus.Write(0x004300, 0x01); // mode 1, CPU->PPU, increment source
        bus.Write(0x004301, 0x18); // $2118/$2119
        bus.Write(0x004302, 0x00);
        bus.Write(0x004303, 0x10);
        bus.Write(0x004304, 0x7E);
        bus.Write(0x004305, 0x04);
        bus.Write(0x004306, 0x00);

        bus.Write(0x00420B, 0x01);

        ppu.Writes.Should().ContainInOrder(
            (0x18, (byte)0x11),
            (0x19, (byte)0x22),
            (0x18, (byte)0x33),
            (0x19, (byte)0x44));

        bus.Read(0x004302).Should().Be(0x04);
        bus.Read(0x004303).Should().Be(0x10);
        bus.Read(0x004304).Should().Be(0x7E);
        bus.Read(0x004305).Should().Be(0x00);
        bus.Read(0x004306).Should().Be(0x00);
    }

    [Fact]
    public void Dma_ToWramPort_UsesUpdatedSourceOnSecondTrigger()
    {
        var wram = new WorkRam();
        wram.Reset();
        var bus = new MemoryBus(wram, new TestInputManager(), NullLogger<MemoryBus>.Instance);
        bus.AttachDevices(new RecordingPpu(), new StubApu());
        bus.Reset();

        bus.Write(0x7E2000, 0xA1);
        bus.Write(0x7E2001, 0xA2);
        bus.Write(0x7E2002, 0xA3);
        bus.Write(0x7E2003, 0xA4);

        bus.Write(0x004300, 0x00); // mode 0
        bus.Write(0x004301, 0x80); // $2180 WMDATA port
        bus.Write(0x004302, 0x00);
        bus.Write(0x004303, 0x20);
        bus.Write(0x004304, 0x7E);
        bus.Write(0x004305, 0x02);
        bus.Write(0x004306, 0x00);
        bus.Write(0x002181, 0x00);
        bus.Write(0x002182, 0x30);
        bus.Write(0x002183, 0x00);

        bus.Write(0x00420B, 0x01);

        bus.Write(0x004305, 0x02);
        bus.Write(0x004306, 0x00);
        bus.Write(0x00420B, 0x01);

        bus.Read(0x7E3000).Should().Be(0xA1);
        bus.Read(0x7E3001).Should().Be(0xA2);
        bus.Read(0x7E3002).Should().Be(0xA3);
        bus.Read(0x7E3003).Should().Be(0xA4);
    }

}

internal sealed class RecordingPpu : IPpu
{
    public List<(byte reg, byte value)> Writes { get; } = new();
    public PpuStatus Status => new();
    public bool IsVBlank => false;
    public bool IsHBlank => false;
    public IFrameBuffer FrameBuffer => throw new NotSupportedException();
    public event EventHandler<FrameReadyEventArgs>? FrameReady;
    public string Name => "RecordingPPU";
    public void Reset() { }
    public void Clock(int masterCycles) { }
    public byte ReadRegister(byte register) => 0;
    public void WriteRegister(byte register, byte value) => Writes.Add((register, value));
    public byte[] SaveState() => Array.Empty<byte>();
    public void LoadState(byte[] state) { }
}

internal sealed class StubApu : IApu
{
    public string Name => "StubAPU";
    public void Reset() { }
    public void Clock(int masterCycles) { }
    public byte ReadPort(byte port) => 0;
    public void WritePort(byte port, byte value) { }
    public void FillAudioBuffer(short[] buffer, int offset, int count) { }
    public byte[] SaveState() => Array.Empty<byte>();
    public void LoadState(byte[] state) { }
}
