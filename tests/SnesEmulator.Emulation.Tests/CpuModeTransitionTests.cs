using FluentAssertions;
using SnesEmulator.Emulation.Tests.Helpers;
using Xunit;

namespace SnesEmulator.Emulation.Tests;

public sealed class CpuModeTransitionTests
{
    // ── XCE mode switching ──────────────────────────────────────────────────

    [Fact]
    public void CLC_XCE_EntersNativeMode()
    {
        var h = new EmulationHarness([0x18, 0xFB], 0x8000);

        h.StepOnce();
        h.StepOnce();

        h.Registers.EmulationMode.Should().BeFalse();
        h.Registers.FlagC.Should().BeTrue("carry receives old emulation bit (1)");
    }

    [Fact]
    public void SEC_XCE_StaysInEmulationMode()
    {
        var h = new EmulationHarness([0x38, 0xFB], 0x8000);

        h.StepOnce();
        h.StepOnce();

        h.Registers.EmulationMode.Should().BeTrue();
        h.Registers.FlagC.Should().BeTrue("carry receives old emulation bit (1)");
    }

    [Fact]
    public void XCE_PreservesOtherFlags()
    {
        var h = new EmulationHarness([
            0x38,       // SEC            → C=1
            0xFB,       // XCE            → E=1 (unchanged), C=1
            0xC2, 0x01, // REP #$01       → clear C (bit 0), E stays 1
            0x18,       // CLC            → C=0
            0xFB        // XCE            → E=0 (enter native), C=1
        ], 0x8000);

        h.Step(5);

        h.Registers.EmulationMode.Should().BeFalse();
        h.Registers.FlagC.Should().BeTrue("carry gets old emulation bit (1)");
    }

    // ── REP and SEP flag manipulation ────────────────────────────────────────

    [Fact]
    public void REP_ClearsMFlag_InNativeMode()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x20, // REP #$20 → clear bit 5 (M)
        ], 0x8000);

        h.Step(3);

        h.Registers.EmulationMode.Should().BeFalse();
        h.Registers.FlagM.Should().BeFalse("REP #$20 clears M flag");
    }

    [Fact]
    public void REP_ClearsXFlag_InNativeMode()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x10, // REP #$10 → clear bit 4 (X)
        ], 0x8000);

        h.Step(3);

        h.Registers.FlagX.Should().BeFalse("REP #$10 clears X flag");
    }

    [Fact]
    public void REP_ClearsBothMX_WithCombinedMask()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x30, // REP #$30 → clear bits 4 and 5 (M and X)
        ], 0x8000);

        h.Step(3);

        h.Registers.FlagM.Should().BeFalse();
        h.Registers.FlagX.Should().BeFalse();
    }

    [Fact]
    public void SEP_SetsBothFlags_AndTruncatesX()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x10, // REP #$10 → clear X (16-bit index)
            0xA2, 0xCD, 0xAB, // LDX #$ABCD → X=0xABCD
            0xE2, 0x10, // SEP #$10 → set X flag, truncates X to 0x00CD
        ], 0x8000);

        h.Step(5);

        h.Registers.FlagX.Should().BeTrue("SEP #$10 sets X flag");
        h.Registers.X.Should().Be(0x00CD, "SEP truncates X to low byte when setting X flag");
    }

    [Fact]
    public void SEP_SetsMFlag()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x20, // REP #$20 → clear M (16-bit A)
            0xE2, 0x20, // SEP #$20 → set M (8-bit A)
        ], 0x8000);

        h.Step(4);

        h.Registers.FlagM.Should().BeTrue("SEP #$20 sets M flag");
    }

    // ── Accumulator width ────────────────────────────────────────────────────

    [Fact]
    public void LDA_16Bit_LoadsFullWord()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE → E=0
            0xC2, 0x20, // REP #$20 → clear M (16-bit A)
            0xA9, 0xCD, 0xAB, // LDA #$ABCD
        ], 0x8000);

        h.Step(4);

        h.Registers.A.Should().Be(0xABCD);
        h.Registers.FlagN.Should().BeTrue("bit 15 set");
        h.Registers.FlagZ.Should().BeFalse();
    }

    [Fact]
    public void LDA_8BitInNative_PreservesHighByte()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x20, // REP #$20 → 16-bit A
            0xA9, 0x34, 0x12, // LDA #$1234 → C=0x1234
            0xE2, 0x20, // SEP #$20 → 8-bit A
            0xA9, 0x42, // LDA #$42 → A=0x42, B=0x12 preserved
        ], 0x8000);

        h.Step(6);

        h.Registers.A.Should().Be(0x1242, "B (0x12) preserved, A set to 0x42");
    }

    [Fact]
    public void LDA_16Bit_SetsNZFlagsCorrectly()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x20, // REP #$20 → 16-bit A
            0xA9, 0x00, 0x80, // LDA #$8000 → N=1, Z=0
        ], 0x8000);

        h.Step(4);

        h.Registers.FlagN.Should().BeTrue("bit 15 set");
        h.Registers.FlagZ.Should().BeFalse();
    }

    [Fact]
    public void LDA_16Bit_Zero_SetsZeroFlag()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x20, // REP #$20 → 16-bit A
            0xA9, 0x00, 0x00, // LDA #$0000
        ], 0x8000);

        h.Step(4);

        h.Registers.FlagZ.Should().BeTrue();
        h.Registers.FlagN.Should().BeFalse();
    }

    // ── Index width ──────────────────────────────────────────────────────────

    [Fact]
    public void LDX_16Bit_LoadsFullX()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x10, // REP #$10 → 16-bit index
            0xA2, 0x34, 0x12, // LDX #$1234
        ], 0x8000);

        h.Step(4);

        h.Registers.X.Should().Be(0x1234);
        h.Registers.FlagN.Should().BeFalse("bit 15 clear");
        h.Registers.FlagZ.Should().BeFalse();
    }

    [Fact]
    public void LDX_8BitInNative_ClearsHighByte()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x10, // REP #$10 → 16-bit index
            0xA2, 0x34, 0x12, // LDX #$1234 → X=0x1234
            0xE2, 0x10, // SEP #$10 → 8-bit index (truncates X to 0x0034)
            0xA2, 0x42, // LDX #$42 → X=0x0042
        ], 0x8000);

        h.Step(6);

        h.Registers.X.Should().Be(0x0042, "8-bit LDX clears high byte");
    }

    [Fact]
    public void LDY_16Bit_LoadsFullY()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x10, // REP #$10 → 16-bit index
            0xA0, 0x34, 0x12, // LDY #$1234
        ], 0x8000);

        h.Step(4);

        h.Registers.Y.Should().Be(0x1234);
    }

    // ── Stack behavior ───────────────────────────────────────────────────────

    [Fact]
    public void PHA_PLA_8Bit_RoundTrip()
    {
        var h = new EmulationHarness([
            0xA9, 0x42, // LDA #$42
            0x48,       // PHA
            0xA9, 0x00, // LDA #$00
            0x68,       // PLA
        ], 0x8000);

        ushort spBefore = h.Registers.SP;
        h.Step(4);

        h.Registers.A.Should().Be(0x42);
        h.Registers.SP.Should().Be(spBefore, "SP returns to original after push+pop");
        h.Registers.FlagZ.Should().BeFalse();
    }

    [Fact]
    public void PHA_16Bit_PushesAndPopsWord()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x20, // REP #$20 → 16-bit A
            0xA9, 0xCD, 0xAB, // LDA #$ABCD
            0x48,       // PHA (16-bit → 2 bytes)
            0xA9, 0x00, 0x00, // LDA #$0000
            0x68,       // PLA (16-bit → 2 bytes)
        ], 0x8000);

        ushort spBefore = h.Registers.SP;
        h.Step(7);

        h.Registers.A.Should().Be(0xABCD);
        h.Registers.SP.Should().Be(spBefore, "SP returns to original after 16-bit push+pop");
    }

    [Fact]
    public void PHX_PLX_8Bit_RoundTrip()
    {
        var h = new EmulationHarness([
            0xA2, 0x55, // LDX #$55
            0xDA,       // PHX
            0xA2, 0x00, // LDX #$00
            0xFA,       // PLX
        ], 0x8000);

        h.Step(4);

        h.Registers.X.Should().Be(0x0055);
    }

    [Fact]
    public void PHX_16Bit_PushesAndPopsWord()
    {
        var h = new EmulationHarness([
            0x18,       // CLC
            0xFB,       // XCE
            0xC2, 0x10, // REP #$10 → 16-bit index
            0xA2, 0xCD, 0xAB, // LDX #$ABCD
            0xDA,       // PHX (16-bit → 2 bytes)
            0xA2, 0x00, 0x00, // LDX #$0000
            0xFA,       // PLX (16-bit → 2 bytes)
        ], 0x8000);

        h.Step(7);

        h.Registers.X.Should().Be(0xABCD);
    }

    [Fact]
    public void EmulationStack_WrapsInPage1()
    {
        var h = new EmulationHarness([
            0xA9, 0x42, // LDA #$42
            0x48,       // PHA
            0x68,       // PLA
        ], 0x8000);

        h.Step(3);

        h.Registers.SP.Should().Be(0x01FF, "emulation mode SP wraps in page 1");
        h.Registers.A.Should().Be(0x42);
    }
}
