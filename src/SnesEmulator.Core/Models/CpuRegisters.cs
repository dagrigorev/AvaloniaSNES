namespace SnesEmulator.Core.Models;

/// <summary>
/// Immutable snapshot of the 65C816 CPU register state.
/// Used for diagnostics, debugger display, and save states.
///
/// The 65C816 is a 16-bit extension of the 6502 with a 24-bit address space.
/// In native mode (E=0) it exposes 16-bit A, X, Y and a full stack.
/// In emulation mode (E=1) it behaves like a 6502.
/// </summary>
public sealed record CpuRegisters
{
    // ── Accumulator ─────────────────────────────────────────────────────────
    /// <summary>
    /// Accumulator. 16-bit in native mode with M=0, 8-bit with M=1.
    /// In 8-bit mode, the high byte is stored in the hidden B register.
    /// </summary>
    public ushort A { get; init; }

    // ── Index Registers ──────────────────────────────────────────────────────
    /// <summary>X index register. 16-bit with X=0, 8-bit with X=1.</summary>
    public ushort X { get; init; }

    /// <summary>Y index register. 16-bit with X=0, 8-bit with X=1.</summary>
    public ushort Y { get; init; }

    // ── Stack & Direct Page ─────────────────────────────────────────────────
    /// <summary>Stack pointer. 16-bit in native mode, $01xx in emulation mode.</summary>
    public ushort SP { get; init; }

    /// <summary>Direct Page register. Base address for direct page addressing.</summary>
    public ushort DP { get; init; }

    // ── Program Counter ──────────────────────────────────────────────────────
    /// <summary>Program Counter (16-bit offset within current bank).</summary>
    public ushort PC { get; init; }

    /// <summary>Program Bank Register. The bank byte for instruction fetches.</summary>
    public byte PBR { get; init; }

    /// <summary>Data Bank Register. The default bank for data reads/writes.</summary>
    public byte DBR { get; init; }

    // ── Status Register ──────────────────────────────────────────────────────
    /// <summary>
    /// Processor Status Register (P).
    /// Bits: N V M X D I Z C (native mode)
    ///       N V 1 B D I Z C (emulation mode)
    /// </summary>
    public byte P { get; init; }

    /// <summary>Emulation mode flag. When true, CPU behaves like 6502.</summary>
    public bool EmulationMode { get; init; }

    // ── Status flag accessors ────────────────────────────────────────────────
    /// <summary>Negative flag (bit 7).</summary>
    public bool FlagN => (P & 0x80) != 0;

    /// <summary>Overflow flag (bit 6).</summary>
    public bool FlagV => (P & 0x40) != 0;

    /// <summary>Memory/Accumulator width flag (bit 5). 1 = 8-bit, 0 = 16-bit.</summary>
    public bool FlagM => (P & 0x20) != 0;

    /// <summary>Index register width flag (bit 4). 1 = 8-bit, 0 = 16-bit.</summary>
    public bool FlagX => (P & 0x10) != 0;

    /// <summary>Decimal mode flag (bit 3). Affects ADC/SBC on 65C816.</summary>
    public bool FlagD => (P & 0x08) != 0;

    /// <summary>IRQ disable flag (bit 2). When set, hardware IRQ is ignored.</summary>
    public bool FlagI => (P & 0x04) != 0;

    /// <summary>Zero flag (bit 1).</summary>
    public bool FlagZ => (P & 0x02) != 0;

    /// <summary>Carry flag (bit 0).</summary>
    public bool FlagC => (P & 0x01) != 0;

    /// <summary>Full 24-bit program counter (PBR:PC).</summary>
    public uint FullPC => ((uint)PBR << 16) | PC;

    /// <summary>Returns a formatted string suitable for debugger display.</summary>
    public override string ToString() =>
        $"A:{A:X4} X:{X:X4} Y:{Y:X4} SP:{SP:X4} DP:{DP:X4} " +
        $"PC:{PBR:X2}:{PC:X4} DBR:{DBR:X2} " +
        $"P:[{(FlagN ? "N" : "n")}{(FlagV ? "V" : "v")}{(FlagM ? "M" : "m")}" +
        $"{(FlagX ? "X" : "x")}{(FlagD ? "D" : "d")}{(FlagI ? "I" : "i")}" +
        $"{(FlagZ ? "Z" : "z")}{(FlagC ? "C" : "c")}] E:{(EmulationMode ? "1" : "0")}";
}
