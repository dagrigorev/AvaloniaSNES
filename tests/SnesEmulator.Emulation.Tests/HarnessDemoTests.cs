using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Emulation.Cpu;
using SnesEmulator.Emulation.Tests.Helpers;
using Xunit;

namespace SnesEmulator.Emulation.Tests;

public sealed class HarnessDemoTests
{
    [Fact]
    public void ResetVector_StartsExecutionAtSpecifiedAddress()
    {
        var program = new byte[] { 0xEA };
        var harness = new EmulationHarness(program, 0x8000);

        harness.Registers.PC.Should().Be(0x8000);
        harness.Registers.PBR.Should().Be(0);
        harness.Registers.EmulationMode.Should().BeTrue();

        harness.StepOnce();
        harness.Registers.PC.Should().Be(0x8001);
    }

    [Fact]
    public void LDA_ThenSTA_WritesAccumulatorToMemory()
    {
        var program = new byte[]
        {
            0xA9, 0x42,       // LDA #$42
            0x8D, 0x00, 0x02, // STA $0200
        };
        var harness = new EmulationHarness(program, 0x8000);

        harness.Step(2);

        harness.ReadByte(0x0200).Should().Be(0x42);
        harness.Registers.A.Should().Be(0x42);
    }

    [Fact]
    public void BEQ_BranchesWhenZeroFlagSet()
    {
        var program = new byte[]
        {
            0xA9, 0x00, // LDA #$00 (sets Z)
            0xF0, 0x02, // BEQ +2 (branch over NOP)
            0xEA,       // NOP at $8004 (skipped)
        };
        var harness = new EmulationHarness(program, 0x8000);

        harness.StepOnce();
        harness.Registers.FlagZ.Should().BeTrue();

        harness.StepOnce();
        harness.Registers.PC.Should().Be(0x8006);
    }

    [Fact]
    public void JSR_ThenRTS_ReturnsToCaller()
    {
        var mem = new TestMemory();
        mem[0x8000] = 0x20;
        mem[0x8001] = 0x04;
        mem[0x8002] = 0x80;
        mem[0x8003] = 0xEA;
        mem[0x8004] = 0x60;
        mem[0xFFFC] = 0x00;
        mem[0xFFFD] = 0x80;

        var bus = new FlatMemoryBus(mem);
        var cpu = new Cpu65C816(bus, NullLogger<Cpu65C816>.Instance);
        cpu.Reset();

        cpu.Step();
        cpu.Registers.PC.Should().Be(0x8004);

        cpu.Step();
        cpu.Registers.PC.Should().Be(0x8003);
    }

    [Fact]
    public void JSR_ThenRTS_ReturnsToCaller_FromHarness()
    {
        var program = new byte[]
        {
            0x20, 0x04, 0x80, // JSR $8004
            0xEA,             // NOP at $8003 (return here)
            0x60,             // RTS at $8004
        };
        var harness = new EmulationHarness(program, 0x8000);

        harness.StepOnce();
        harness.Registers.PC.Should().Be(0x8004);

        harness.StepOnce();
        harness.Registers.PC.Should().Be(0x8003);
    }
}
