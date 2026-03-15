using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Emulation.Cpu;
using SnesEmulator.Emulation.Memory;
using Xunit;

namespace SnesEmulator.Emulation.Tests;

/// <summary>
/// Unit tests for the 65C816 CPU instruction set.
/// Each test sets up a minimal memory image, executes one instruction,
/// and asserts the resulting register state.
/// </summary>
public sealed class CpuInstructionTests
{
    /// <summary>Creates a test environment with a flat 64KB RAM bus.</summary>
    private (Cpu65C816 cpu, TestMemory mem) CreateCpu()
    {
        var mem = new TestMemory();
        var bus = new FlatMemoryBus(mem);
        var cpu = new Cpu65C816(bus, NullLogger<Cpu65C816>.Instance);
        return (cpu, mem);
    }

    // Helper: set CPU to native mode (E=0) with 8-bit accumulator
    private static void SetNativeMode8Bit(CpuState state)
    {
        state.EmulationMode = false;
        state.FlagM = true;  // 8-bit accumulator
        state.FlagX = true;  // 8-bit index
    }

    // ── LDA tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void LDA_Immediate_8Bit_LoadsValueIntoA()
    {
        var (cpu, mem) = CreateCpu();
        // Set up: emulation mode, LDA #$42
        mem[0x8000] = 0xA9; // LDA immediate
        mem[0x8001] = 0x42;
        mem[0xFFFC] = 0x00; // Reset vector low
        mem[0xFFFD] = 0x80; // Reset vector high ($8000)

        cpu.Reset();
        cpu.Step();

        cpu.Registers.A.Should().Be(0x42);
        cpu.Registers.FlagZ.Should().BeFalse();
        cpu.Registers.FlagN.Should().BeFalse();
    }

    [Fact]
    public void LDA_Immediate_Zero_SetsZeroFlag()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xA9;
        mem[0x8001] = 0x00;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step();

        cpu.Registers.FlagZ.Should().BeTrue();
        cpu.Registers.A.Should().Be(0x00);
    }

    [Fact]
    public void LDA_Immediate_Negative_SetsNegativeFlag()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xA9;
        mem[0x8001] = 0x80; // Bit 7 set = negative in 8-bit mode
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step();

        cpu.Registers.FlagN.Should().BeTrue();
    }

    // ── STA tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void STA_Absolute_WritesAccumulatorToMemory()
    {
        var (cpu, mem) = CreateCpu();
        // LDA #$55
        mem[0x8000] = 0xA9;
        mem[0x8001] = 0x55;
        // STA $0200
        mem[0x8002] = 0x8D;
        mem[0x8003] = 0x00;
        mem[0x8004] = 0x02;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // LDA
        cpu.Step(); // STA

        mem[0x0200].Should().Be(0x55);
    }

    // ── INX / DEX tests ───────────────────────────────────────────────────────

    [Fact]
    public void INX_IncrementsX()
    {
        var (cpu, mem) = CreateCpu();
        // LDX #$05
        mem[0x8000] = 0xA2;
        mem[0x8001] = 0x05;
        // INX
        mem[0x8002] = 0xE8;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // LDX
        cpu.Step(); // INX

        cpu.Registers.X.Should().Be(6);
    }

    [Fact]
    public void DEX_DecrementsX()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xA2;
        mem[0x8001] = 0x05;
        mem[0x8002] = 0xCA; // DEX
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // LDX
        cpu.Step(); // DEX

        cpu.Registers.X.Should().Be(4);
    }

    // ── Branch tests ──────────────────────────────────────────────────────────

    [Fact]
    public void BEQ_WhenZeroSet_TakesBranch()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xA9; // LDA #$00 (sets Z)
        mem[0x8001] = 0x00;
        mem[0x8002] = 0xF0; // BEQ +4
        mem[0x8003] = 0x04;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // LDA sets Z
        cpu.Step(); // BEQ should branch

        // PC should be at $8002 + 2 (after BEQ) + 4 (offset) = $8008
        cpu.Registers.PC.Should().Be(0x8008);
    }

    [Fact]
    public void BNE_WhenZeroClear_TakesBranch()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xA9; // LDA #$01 (clears Z)
        mem[0x8001] = 0x01;
        mem[0x8002] = 0xD0; // BNE +2
        mem[0x8003] = 0x02;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // LDA
        cpu.Step(); // BNE

        cpu.Registers.PC.Should().Be(0x8006);
    }

    // ── JSR / RTS tests ───────────────────────────────────────────────────────

    [Fact]
    public void JSR_PushesReturnAddress_AndJumps()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0x20; // JSR $8010
        mem[0x8001] = 0x10;
        mem[0x8002] = 0x80;
        mem[0x8010] = 0x60; // RTS
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        ushort spBefore = cpu.Registers.SP;
        cpu.Step(); // JSR

        cpu.Registers.PC.Should().Be(0x8010);
        cpu.Registers.SP.Should().Be((ushort)(spBefore - 2)); // Return addr pushed
    }

    [Fact]
    public void JSR_ThenRTS_ReturnsToCallerPlusOne()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0x20; // JSR $8010
        mem[0x8001] = 0x10;
        mem[0x8002] = 0x80;
        mem[0x8003] = 0xEA; // NOP (return address + 1)
        mem[0x8010] = 0x60; // RTS
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // JSR
        cpu.Step(); // RTS

        cpu.Registers.PC.Should().Be(0x8003);
    }

    // ── CLC / SEC / ADC tests ─────────────────────────────────────────────────

    [Fact]
    public void ADC_WithCarryClear_AddsCorrectly()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0x18; // CLC
        mem[0x8001] = 0xA9; // LDA #$10
        mem[0x8002] = 0x10;
        mem[0x8003] = 0x69; // ADC #$20
        mem[0x8004] = 0x20;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // CLC
        cpu.Step(); // LDA
        cpu.Step(); // ADC

        cpu.Registers.A.Should().Be(0x30);
        cpu.Registers.FlagC.Should().BeFalse();
    }

    [Fact]
    public void ADC_WithOverflow_SetsCarryFlag()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0x18; // CLC
        mem[0x8001] = 0xA9; // LDA #$FF
        mem[0x8002] = 0xFF;
        mem[0x8003] = 0x69; // ADC #$01
        mem[0x8004] = 0x01;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step(); // CLC
        cpu.Step(); // LDA
        cpu.Step(); // ADC

        cpu.Registers.A.Should().Be(0x00);
        cpu.Registers.FlagC.Should().BeTrue();
        cpu.Registers.FlagZ.Should().BeTrue();
    }

    [Fact]
    public void TSB_DirectPage_SetsMemoryBits_AndSetsZeroFromOverlap()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x0010] = 0x0F;
        mem[0x8000] = 0xA9; // LDA #$F0
        mem[0x8001] = 0xF0;
        mem[0x8002] = 0x04; // TSB $10
        mem[0x8003] = 0x10;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step();
        cpu.Step();

        mem[0x0010].Should().Be(0xFF);
        cpu.Registers.FlagZ.Should().BeTrue();
    }

    [Fact]
    public void TRB_Absolute_ClearsMemoryBits_AndLeavesOtherBits()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x1234] = 0b1111_0011;
        mem[0x8000] = 0xA9; // LDA #$0F
        mem[0x8001] = 0x0F;
        mem[0x8002] = 0x1C; // TRB $1234
        mem[0x8003] = 0x34;
        mem[0x8004] = 0x12;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        cpu.Step();
        cpu.Step();

        mem[0x1234].Should().Be(0b1111_0000);
        cpu.Registers.FlagZ.Should().BeFalse();
    }

    //[Fact]
    //public void StackRelative_Cmp_UsesStackBasedAddress()
    //{
    //    var (cpu, mem) = CreateCpu();
    //    cpu.Reset();
    //    cpu.Registers.EmulationMode = false;
    //    cpu.Registers.FlagM = true;
    //    cpu.Registers.FlagX = true;
    //    cpu.Registers.SP = 0x01F0;

    //    mem[0x01F5] = 0x44;
    //    mem[0x8000] = 0xA9; // LDA #$44
    //    mem[0x8001] = 0x44;
    //    mem[0x8002] = 0xC3; // CMP 5,S
    //    mem[0x8003] = 0x05;
    //    mem[0xFFFC] = 0x00;
    //    mem[0xFFFD] = 0x80;
    //    cpu.Registers.PC = 0x8000;

    //    cpu.Step();
    //    cpu.Step();

    //    cpu.Registers.FlagZ.Should().BeTrue();
    //    cpu.Registers.FlagC.Should().BeTrue();
    //}

    // ── NOP cycles ────────────────────────────────────────────────────────────

    [Fact]
    public void NOP_Returns2Cycles()
    {
        var (cpu, mem) = CreateCpu();
        mem[0x8000] = 0xEA; // NOP
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        cpu.Reset();
        // NOP should execute in 2 CPU cycles
        // (our Step returns instruction cycles, not master cycles)
        cpu.Step().Should().Be(2);
    }
}

// ── Test infrastructure ────────────────────────────────────────────────────────

/// <summary>Simple flat memory array for CPU testing.</summary>
public sealed class TestMemory
{
    private readonly byte[] _data = new byte[0x10000];

    public byte this[int addr]
    {
        get => _data[addr & 0xFFFF];
        set => _data[addr & 0xFFFF] = value;
    }

    public byte Read(uint addr) => _data[addr & 0xFFFF];
    public void Write(uint addr, byte val) => _data[addr & 0xFFFF] = val;
}

/// <summary>Flat memory bus adapter for CPU tests (no SNES banking).</summary>
public sealed class FlatMemoryBus : Core.Interfaces.IMemoryBus
{
    private readonly TestMemory _mem;
    public FlatMemoryBus(TestMemory mem) => _mem = mem;

    public byte   Read(uint address)              => _mem.Read(address);
    public void   Write(uint address, byte value) => _mem.Write(address, value);
    public ushort ReadWord(uint address)  => (ushort)(_mem.Read(address) | (_mem.Read(address + 1) << 8));
    public void   WriteWord(uint address, ushort value) { _mem.Write(address, (byte)(value & 0xFF)); _mem.Write(address + 1, (byte)(value >> 8)); }
}
