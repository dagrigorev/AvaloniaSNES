using SnesEmulator.Core.Models;
using SnesEmulator.Core.Utilities;

namespace SnesEmulator.Emulation.Cpu;

/// <summary>
/// Mutable working state for all 65C816 CPU registers.
/// This is the runtime CPU state; CpuRegisters (in Core) is the immutable snapshot.
///
/// The 65C816 register model:
///   A  — 16-bit accumulator (AH:AL, where B is the hidden high byte)
///   X  — 16-bit index register (XH:XL)
///   Y  — 16-bit index register (YH:YL)
///   SP — 16-bit stack pointer
///   DP — 16-bit direct page register
///   PC — 16-bit program counter (within PBR bank)
///   PBR — 8-bit program bank
///   DBR — 8-bit data bank
///   P  — 8-bit status flags
///   E  — emulation mode bit (not part of P on 65C816, set via XCE)
/// </summary>
public sealed class CpuState
{
    // ── Accumulator ──────────────────────────────────────────────────────────
    /// <summary>Full 16-bit accumulator (C register). Low byte = A, high byte = B.</summary>
    public ushort C;

    /// <summary>Low byte of accumulator (A). Valid in both 8-bit and 16-bit modes.</summary>
    public byte A
    {
        get => (byte)(C & 0xFF);
        set => C = (ushort)((C & 0xFF00) | value);
    }

    /// <summary>High byte of accumulator (B). Accessible in 8-bit mode as hidden register.</summary>
    public byte B
    {
        get => (byte)((C >> 8) & 0xFF);
        set => C = (ushort)((C & 0x00FF) | (value << 8));
    }

    // ── Index Registers ───────────────────────────────────────────────────────
    public ushort X;
    public ushort Y;

    // ── Stack, Direct Page, Banks ────────────────────────────────────────────
    public ushort SP = 0x01FF; // Stack pointer (initialized to end of page 1)
    public ushort DP;          // Direct page register
    public byte PBR;           // Program bank register
    public byte DBR;           // Data bank register

    // ── Program Counter ───────────────────────────────────────────────────────
    public ushort PC;

    // ── Status Register (P) ──────────────────────────────────────────────────
    public byte P = 0x34; // Default: M=1, X=1, I=1 (8-bit mode, interrupts disabled)

    // ── Emulation Mode ────────────────────────────────────────────────────────
    /// <summary>
    /// Emulation mode flag (E). When true, CPU mimics 6502 behavior.
    /// Set/cleared via XCE instruction (eXchange Carry with Emulation bit).
    /// </summary>
    public bool EmulationMode = true;

    // ── Status flag properties ────────────────────────────────────────────────
    public bool FlagN { get => (P & 0x80) != 0; set => P = BitHelper.SetBitTo(P, 7, value); }
    public bool FlagV { get => (P & 0x40) != 0; set => P = BitHelper.SetBitTo(P, 6, value); }
    public bool FlagM { get => (P & 0x20) != 0; set => P = BitHelper.SetBitTo(P, 5, value); }
    public bool FlagX { get => (P & 0x10) != 0; set => P = BitHelper.SetBitTo(P, 4, value); }
    public bool FlagD { get => (P & 0x08) != 0; set => P = BitHelper.SetBitTo(P, 3, value); }
    public bool FlagI { get => (P & 0x04) != 0; set => P = BitHelper.SetBitTo(P, 2, value); }
    public bool FlagZ { get => (P & 0x02) != 0; set => P = BitHelper.SetBitTo(P, 1, value); }
    public bool FlagC { get => (P & 0x01) != 0; set => P = BitHelper.SetBitTo(P, 0, value); }

    /// <summary>True when accumulator is 8-bit (M flag set, or emulation mode).</summary>
    public bool Is8BitAccumulator => FlagM || EmulationMode;

    /// <summary>True when index registers are 8-bit (X flag set, or emulation mode).</summary>
    public bool Is8BitIndex => FlagX || EmulationMode;

    /// <summary>Full 24-bit program counter: PBR:PC.</summary>
    public uint FullPC => ((uint)PBR << 16) | PC;

    /// <summary>Resets CPU state to power-on values.</summary>
    public void Reset()
    {
        C = 0;
        X = 0;
        Y = 0;
        SP = 0x01FF;
        DP = 0;
        PBR = 0;
        DBR = 0;
        PC = 0;
        P = 0x34;
        EmulationMode = true;
    }

    /// <summary>Creates an immutable snapshot of current state (for diagnostics/save state).</summary>
    public CpuRegisters ToSnapshot() => new()
    {
        A = C,   // Full 16-bit C register
        X = X,
        Y = Y,
        SP = SP,
        DP = DP,
        PC = PC,
        PBR = PBR,
        DBR = DBR,
        P = P,
        EmulationMode = EmulationMode
    };

    /// <summary>Restores state from a snapshot.</summary>
    public void FromSnapshot(CpuRegisters snapshot)
    {
        C = snapshot.A;
        X = snapshot.X;
        Y = snapshot.Y;
        SP = snapshot.SP;
        DP = snapshot.DP;
        PC = snapshot.PC;
        PBR = snapshot.PBR;
        DBR = snapshot.DBR;
        P = snapshot.P;
        EmulationMode = snapshot.EmulationMode;
    }

    /// <summary>Sets N and Z flags based on an 8-bit result.</summary>
    public void SetNZ8(byte value)
    {
        FlagN = (value & 0x80) != 0;
        FlagZ = value == 0;
    }

    /// <summary>Sets N and Z flags based on a 16-bit result.</summary>
    public void SetNZ16(ushort value)
    {
        FlagN = (value & 0x8000) != 0;
        FlagZ = value == 0;
    }
}
