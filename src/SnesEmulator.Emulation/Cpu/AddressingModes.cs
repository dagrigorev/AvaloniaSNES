using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Utilities;

namespace SnesEmulator.Emulation.Cpu;

/// <summary>
/// Resolves effective addresses for all 65C816 addressing modes.
/// Each method returns a 24-bit effective address. The CPU then reads/writes
/// through the memory bus at that address.
///
/// Reference: 65C816 Programming Manual, Chapter 5 (Addressing Modes)
/// </summary>
public sealed class AddressingModes
{
    private readonly IMemoryBus _bus;
    private readonly CpuState _regs;

    public AddressingModes(IMemoryBus bus, CpuState regs)
    {
        _bus = bus;
        _regs = regs;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reads the next program byte and advances PC.</summary>
    public byte FetchByte()
    {
        byte val = _bus.Read(BitHelper.MakeAddress(_regs.PBR, _regs.PC));
        _regs.PC++;
        return val;
    }

    /// <summary>Reads the next 16-bit immediate and advances PC by 2.</summary>
    public ushort FetchWord()
    {
        byte lo = FetchByte();
        byte hi = FetchByte();
        return BitHelper.MakeWord(lo, hi);
    }

    // ── Addressing Mode Resolvers ─────────────────────────────────────────────

    /// <summary>Immediate: value is the operand itself (returns PC address, then advances).</summary>
    public uint Immediate8()  { uint addr = BitHelper.MakeAddress(_regs.PBR, _regs.PC); _regs.PC += 1; return addr; }
    public uint Immediate16() { uint addr = BitHelper.MakeAddress(_regs.PBR, _regs.PC); _regs.PC += 2; return addr; }

    /// <summary>Absolute: 16-bit address in current data bank. $OP low high → DBR:addr</summary>
    public uint Absolute()
    {
        ushort addr16 = FetchWord();
        return BitHelper.MakeAddress(_regs.DBR, addr16);
    }

    /// <summary>Absolute Long: 24-bit address. $OP low high bank</summary>
    public uint AbsoluteLong()
    {
        byte lo   = FetchByte();
        byte hi   = FetchByte();
        byte bank = FetchByte();
        return BitHelper.MakeAddress(bank, BitHelper.MakeWord(lo, hi));
    }

    /// <summary>Absolute,X: Absolute + X. May cross bank boundary.</summary>
    public uint AbsoluteX()
    {
        ushort base16 = FetchWord();
        uint fullAddr = (uint)(BitHelper.MakeAddress(_regs.DBR, base16) + (_regs.Is8BitIndex ? _regs.X & 0xFF : _regs.X));
        return fullAddr & 0xFFFFFF;
    }

    /// <summary>Absolute,Y: Absolute + Y.</summary>
    public uint AbsoluteY()
    {
        ushort base16 = FetchWord();
        uint fullAddr = (uint)(BitHelper.MakeAddress(_regs.DBR, base16) + (_regs.Is8BitIndex ? _regs.Y & 0xFF : _regs.Y));
        return fullAddr & 0xFFFFFF;
    }

    /// <summary>Absolute Long,X: 24-bit base + X.</summary>
    public uint AbsoluteLongX()
    {
        uint base24 = (uint)(FetchByte() | (FetchByte() << 8) | (FetchByte() << 16));
        return (uint)((base24 + (_regs.Is8BitIndex ? _regs.X & 0xFF : _regs.X)) & 0xFFFFFF);
    }

    /// <summary>Direct Page: 8-bit offset + DP. $OP off → 0:DP+off</summary>
    public uint DirectPage()
    {
        byte dp = FetchByte();
        return (uint)((_regs.DP + dp) & 0xFFFF);
    }

    /// <summary>Direct Page,X: DP + operand + X.</summary>
    public uint DirectPageX()
    {
        byte dp = FetchByte();
        ushort x = _regs.Is8BitIndex ? (byte)_regs.X : _regs.X;
        return (uint)((_regs.DP + dp + x) & 0xFFFF);
    }

    /// <summary>Direct Page,Y: DP + operand + Y.</summary>
    public uint DirectPageY()
    {
        byte dp = FetchByte();
        ushort y = _regs.Is8BitIndex ? (byte)_regs.Y : _regs.Y;
        return (uint)((_regs.DP + dp + y) & 0xFFFF);
    }

    /// <summary>
    /// (Direct Page): 16-bit pointer at DP+op, then dereference within DBR.
    /// </summary>
    public uint DirectPageIndirect()
    {
        byte dp = FetchByte();
        uint ptrAddr = (uint)((_regs.DP + dp) & 0xFFFF);
        ushort ptr = _bus.ReadWord(ptrAddr);
        return BitHelper.MakeAddress(_regs.DBR, ptr);
    }

    /// <summary>(Direct Page,X): DP+op+X = pointer, dereference.</summary>
    public uint DirectPageXIndirect()
    {
        byte dp = FetchByte();
        ushort x = _regs.Is8BitIndex ? (byte)_regs.X : _regs.X;
        uint ptrAddr = (uint)((_regs.DP + dp + x) & 0xFFFF);
        ushort ptr = _bus.ReadWord(ptrAddr);
        return BitHelper.MakeAddress(_regs.DBR, ptr);
    }

    /// <summary>(Direct Page),Y: DP+op = pointer, then ptr + Y.</summary>
    public uint DirectPageIndirectY()
    {
        byte dp = FetchByte();
        uint ptrAddr = (uint)((_regs.DP + dp) & 0xFFFF);
        ushort ptr = _bus.ReadWord(ptrAddr);
        ushort y = _regs.Is8BitIndex ? (byte)_regs.Y : _regs.Y;
        return (BitHelper.MakeAddress(_regs.DBR, ptr) + y) & 0xFFFFFF;
    }

    /// <summary>[Direct Page]: 24-bit pointer stored at DP+op.</summary>
    public uint DirectPageIndirectLong()
    {
        byte dp = FetchByte();
        uint ptrAddr = (uint)((_regs.DP + dp) & 0xFFFF);
        byte lo   = _bus.Read(ptrAddr);
        byte hi   = _bus.Read((ptrAddr + 1) & 0xFFFF);
        byte bank = _bus.Read((ptrAddr + 2) & 0xFFFF);
        return BitHelper.MakeAddress(bank, BitHelper.MakeWord(lo, hi));
    }

    /// <summary>[Direct Page],Y: 24-bit pointer + Y.</summary>
    public uint DirectPageIndirectLongY()
    {
        byte dp = FetchByte();
        uint ptrAddr = (uint)((_regs.DP + dp) & 0xFFFF);
        byte lo   = _bus.Read(ptrAddr);
        byte hi   = _bus.Read((ptrAddr + 1) & 0xFFFF);
        byte bank = _bus.Read((ptrAddr + 2) & 0xFFFF);
        uint base24 = BitHelper.MakeAddress(bank, BitHelper.MakeWord(lo, hi));
        ushort y = _regs.Is8BitIndex ? (byte)_regs.Y : _regs.Y;
        return (base24 + y) & 0xFFFFFF;
    }

    /// <summary>Stack Relative: SP + 8-bit offset.</summary>
    public uint StackRelative()
    {
        byte offset = FetchByte();
        return (uint)((_regs.SP + offset) & 0xFFFF);
    }

    /// <summary>(Stack Relative),Y: pointer at SP+offset, then + Y + DBR.</summary>
    public uint StackRelativeIndirectY()
    {
        byte offset = FetchByte();
        uint ptrAddr = (uint)((_regs.SP + offset) & 0xFFFF);
        ushort ptr = _bus.ReadWord(ptrAddr);
        ushort y = _regs.Is8BitIndex ? (byte)_regs.Y : _regs.Y;
        return (BitHelper.MakeAddress(_regs.DBR, ptr) + y) & 0xFFFFFF;
    }

    /// <summary>Relative: signed 8-bit offset from current PC (for branch instructions).</summary>
    public uint Relative()
    {
        sbyte offset = (sbyte)FetchByte();
        return BitHelper.MakeAddress(_regs.PBR, (ushort)(_regs.PC + offset));
    }

    /// <summary>Relative Long: signed 16-bit offset from current PC (BRL).</summary>
    public uint RelativeLong()
    {
        short offset = (short)FetchWord();
        return BitHelper.MakeAddress(_regs.PBR, (ushort)(_regs.PC + offset));
    }

    /// <summary>Absolute Indirect: read a 16-bit pointer from PBR:addr16.</summary>
    public uint AbsoluteIndirect()
    {
        ushort addr16 = FetchWord();
        ushort ptr = _bus.ReadWord(BitHelper.MakeAddress(0, addr16));
        return BitHelper.MakeAddress(_regs.PBR, ptr);
    }

    /// <summary>Absolute Indirect,X: read pointer from (PBR:addr16+X).</summary>
    public uint AbsoluteIndirectX()
    {
        ushort addr16 = FetchWord();
        ushort x = _regs.Is8BitIndex ? (byte)_regs.X : _regs.X;
        uint ptrAddr = BitHelper.MakeAddress(_regs.PBR, (ushort)(addr16 + x));
        ushort ptr = _bus.ReadWord(ptrAddr);
        return BitHelper.MakeAddress(_regs.PBR, ptr);
    }

    /// <summary>Absolute Indirect Long: read a 24-bit pointer.</summary>
    public uint AbsoluteIndirectLong()
    {
        ushort addr16 = FetchWord();
        uint ptrAddr = BitHelper.MakeAddress(0, addr16);
        byte lo   = _bus.Read(ptrAddr);
        byte hi   = _bus.Read(ptrAddr + 1);
        byte bank = _bus.Read(ptrAddr + 2);
        return BitHelper.MakeAddress(bank, BitHelper.MakeWord(lo, hi));
    }
}
