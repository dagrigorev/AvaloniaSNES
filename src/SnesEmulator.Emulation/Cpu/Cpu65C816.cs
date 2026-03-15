using Microsoft.Extensions.Logging;
using SnesEmulator.Core;
using SnesEmulator.Core.Exceptions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Core.Utilities;

namespace SnesEmulator.Emulation.Cpu;

/// <summary>
/// 65C816 CPU emulator — the main processor of the SNES.
///
/// Architecture overview:
///   - Native (E=0) mode: full 16-bit 65C816 with 24-bit address space.
///   - Emulation (E=1) mode: backwards-compatible 6502 behavior.
///   - Instruction dispatch via a 256-entry opcode table (delegate array).
///   - Each instruction handler returns master clock cycles consumed.
///
/// Not cycle-accurate per micro-operation, but instruction-level timing
/// is correct based on the 65C816 reference manual cycle counts.
///
/// References:
///   - WDC 65C816 Datasheet and Programming Manual
///   - https://wiki.superfamicom.org/65816-reference
/// </summary>
public sealed class Cpu65C816 : ICpu
{
    private readonly IMemoryBus _bus;
    private readonly ILogger<Cpu65C816> _logger;
    private readonly CpuState _state;
    private readonly AddressingModes _addr;

    // Interrupt pending flags
    private bool _nmiPending;
    private bool _irqPending;
    private int _startupTraceRemaining;
    private int _wramExecutionTraceRemaining;
    private int _staLongXTraceRemaining;
    private int _controlFlowTraceRemaining;
    private bool _wasExecutingFromWram;

    public string Name => "65C816 CPU";
    public CpuRegisters Registers => _state.ToSnapshot();
    public long TotalCycles { get; private set; }

    // Opcode dispatch table: 256 entries, each returning cycle count
    private readonly Func<int>[] _opcodeTable;

    public Cpu65C816(IMemoryBus bus, ILogger<Cpu65C816> logger)
    {
        _bus = bus;
        _logger = logger;
        _state = new CpuState();
        _addr = new AddressingModes(bus, _state);
        _opcodeTable = BuildOpcodeTable();
    }

    /// <inheritdoc />
    public void Reset()
    {
        _state.Reset();
        _nmiPending = false;
        _irqPending = false;
        TotalCycles = 0;
        _startupTraceRemaining = 128;
        _wramExecutionTraceRemaining = 64;
        _staLongXTraceRemaining = 128;
        _controlFlowTraceRemaining = 128;
        _wasExecutingFromWram = false;

        // Load reset vector from $FFFC (emulation mode, PBR=0)
        ushort resetVector = _bus.ReadWord(SnesConstants.EmuResetVector);
        _state.PC = resetVector;
        _state.PBR = 0;

        _logger.LogInformation("CPU reset, PC = ${PC:X4}", resetVector);
    }

    /// <inheritdoc />
    public int Step()
    {
        // ── Handle pending interrupts ─────────────────────────────────────────
        if (_nmiPending)
        {
            _nmiPending = false;
            return ServiceNmi();
        }

        if (_irqPending && !_state.FlagI)
        {
            _irqPending = false;
            return ServiceIrq();
        }

        // ── Fetch opcode ──────────────────────────────────────────────────────
        uint opcodeAddress = _state.FullPC;
        if (_bus is SnesEmulator.Emulation.Memory.MemoryBus traceBus)
            traceBus.SetCpuTraceContext(_state.PBR, _state.PC);

        bool executingFromWram = ((opcodeAddress >> 16) & 0xFF) is 0x7E or 0x7F;
        if (executingFromWram && !_wasExecutingFromWram && _wramExecutionTraceRemaining > 0)
        {
            _logger.LogDebug(
                "CPU execution entered WRAM at ${Addr:X6}: A=${A:X4} X=${X:X4} Y=${Y:X4} SP=${SP:X4} DP=${DP:X4} P=${P:X2} E=${E}",
                opcodeAddress, _state.C, _state.X, _state.Y, _state.SP, _state.DP, _state.P, _state.EmulationMode ? 1 : 0);
            _wramExecutionTraceRemaining--;
        }
        if (executingFromWram && _wramExecutionTraceRemaining > 0)
        {
            _logger.LogDebug(
                "CPU executing from WRAM at ${Addr:X6}: A=${A:X4} X=${X:X4} Y=${Y:X4} SP=${SP:X4} DP=${DP:X4} P=${P:X2} E=${E}",
                opcodeAddress, _state.C, _state.X, _state.Y, _state.SP, _state.DP, _state.P, _state.EmulationMode ? 1 : 0);
            _wramExecutionTraceRemaining--;
        }
        _wasExecutingFromWram = executingFromWram;

        byte opcode = _bus.Read(opcodeAddress);
        _state.PC++;

        if (_startupTraceRemaining > 0)
        {
            byte b1 = _bus.Read((opcodeAddress + 1) & 0xFFFFFF);
            byte b2 = _bus.Read((opcodeAddress + 2) & 0xFFFFFF);
            byte b3 = _bus.Read((opcodeAddress + 3) & 0xFFFFFF);
            _logger.LogDebug(
                "CPU start ${Addr:X6}: ${Op:X2} ${B1:X2} ${B2:X2} ${B3:X2}  A=${A:X4} X=${X:X4} Y=${Y:X4} SP=${SP:X4} DP=${DP:X4} P=${P:X2} E=${E}",
                opcodeAddress, opcode, b1, b2, b3, _state.C, _state.X, _state.Y, _state.SP, _state.DP, _state.P, _state.EmulationMode ? 1 : 0);
            _startupTraceRemaining--;
        }

        int cycles = _opcodeTable[opcode]();
        TotalCycles += cycles;
        return cycles;
    }

    /// <inheritdoc />
    public void TriggerNmi() => _nmiPending = true;

    /// <inheritdoc />
    public void TriggerIrq() => _irqPending = true;

    private void TraceControlFlow(string name, uint source, uint target)
    {
        if (_controlFlowTraceRemaining <= 0)
            return;

        _logger.LogDebug(
            "CPU {Name}: ${Source:X6} -> ${Target:X6}  A=${A:X4} X=${X:X4} Y=${Y:X4} SP=${SP:X4} DP=${DP:X4} P=${P:X2} E=${E}",
            name, source, target, _state.C, _state.X, _state.Y, _state.SP, _state.DP, _state.P, _state.EmulationMode ? 1 : 0);
        _controlFlowTraceRemaining--;
    }

    // ── Interrupt Handlers ────────────────────────────────────────────────────

    private int ServiceNmi()
    {
        uint source = _state.FullPC;
        PushPC();
        PushP();
        _state.FlagI = true;
        _state.FlagD = false;

        ushort vector = _state.EmulationMode
            ? SnesConstants.EmuNmiVector
            : SnesConstants.NativeNmiVector;

        _state.PC = _bus.ReadWord(vector);
        _state.PBR = 0;

        _logger.LogDebug("NMI serviced, jumping to ${PC:X4} from ${Source:X6}", _state.PC, source);
        return 8 * SnesConstants.CpuSlowCycles;
    }

    private int ServiceIrq()
    {
        PushPC();
        PushP();
        _state.FlagI = true;
        _state.FlagD = false;

        ushort vector = _state.EmulationMode
            ? SnesConstants.EmuIrqVector
            : SnesConstants.NativeIrqVector;

        _state.PC = _bus.ReadWord(vector);
        _state.PBR = 0;

        return 8 * SnesConstants.CpuSlowCycles;
    }

    // ── Stack helpers ──────────────────────────────────────────────────────────

    private void Push(byte value)
    {
        _bus.Write(_state.SP, value);
        _state.SP--;
        if (_state.EmulationMode)
            _state.SP = (ushort)((_state.SP & 0x00FF) | 0x0100); // Keep in page 1
    }

    private byte Pop()
    {
        _state.SP++;
        if (_state.EmulationMode)
            _state.SP = (ushort)((_state.SP & 0x00FF) | 0x0100);
        return _bus.Read(_state.SP);
    }

    private void PushWord(ushort value)
    {
        Push(BitHelper.HighByte(value));
        Push(BitHelper.LowByte(value));
    }

    private ushort PopWord()
    {
        byte lo = Pop();
        byte hi = Pop();
        return BitHelper.MakeWord(lo, hi);
    }

    private void PushPC()
    {
        if (!_state.EmulationMode)
            Push(_state.PBR);
        PushWord(_state.PC);
    }

    private void PushP()
    {
        Push(_state.P);
    }

    // ── Memory read helpers ───────────────────────────────────────────────────

    private byte ReadByte(uint address) => _bus.Read(address);

    private ushort ReadWord(uint address) => _bus.ReadWord(address);

    private void WriteByte(uint address, byte value) => _bus.Write(address, value);

    private void WriteWord(uint address, ushort value) => _bus.WriteWord(address, value);

    // ── Disassembler ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string Disassemble(uint address)
    {
        byte op = _bus.Read(address);
        return OpcodeNames.TryGetValue(op, out string? name) ? name : $"??? (${op:X2})";
    }

    // ── Save/Load state ───────────────────────────────────────────────────────

    public byte[] SaveState()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(_state.C);
        bw.Write(_state.X);
        bw.Write(_state.Y);
        bw.Write(_state.SP);
        bw.Write(_state.DP);
        bw.Write(_state.PC);
        bw.Write(_state.PBR);
        bw.Write(_state.DBR);
        bw.Write(_state.P);
        bw.Write(_state.EmulationMode);
        bw.Write(TotalCycles);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var ms = new System.IO.MemoryStream(state);
        using var br = new System.IO.BinaryReader(ms);
        _state.C = br.ReadUInt16();
        _state.X = br.ReadUInt16();
        _state.Y = br.ReadUInt16();
        _state.SP = br.ReadUInt16();
        _state.DP = br.ReadUInt16();
        _state.PC = br.ReadUInt16();
        _state.PBR = br.ReadByte();
        _state.DBR = br.ReadByte();
        _state.P = br.ReadByte();
        _state.EmulationMode = br.ReadBoolean();
        TotalCycles = br.ReadInt64();
    }

    // ── Opcode table construction ─────────────────────────────────────────────

    private Func<int>[] BuildOpcodeTable()
    {
        var table = new Func<int>[256];

        // Initialize all slots with "unimplemented" handler
        for (int i = 0; i < 256; i++)
        {
            byte opcode = (byte)i;
            table[i] = () =>
            {
                _logger.LogWarning(
                    "Unimplemented opcode ${Op:X2} at ${Addr:X6}",
                    opcode, _state.FullPC - 1);
                // Return a safe default cycle count to keep the emulator running
                return 2 * SnesConstants.CpuSlowCycles;
            };
        }

        // ── Load / Store ───────────────────────────────────────────────────────
        table[0xA9] = Op_LDA_Immediate;
        table[0xAD] = Op_LDA_Absolute;
        table[0xBD] = Op_LDA_AbsoluteX;
        table[0xB9] = Op_LDA_AbsoluteY;
        table[0xA5] = Op_LDA_DirectPage;
        table[0xB5] = Op_LDA_DirectPageX;
        table[0xAF] = Op_LDA_AbsoluteLong;
        table[0xBF] = Op_LDA_AbsoluteLongX;
        table[0xA1] = Op_LDA_XIndirect;
        table[0xB1] = Op_LDA_IndirectY;
        table[0xB2] = Op_LDA_Indirect;
        table[0xA3] = Op_LDA_StackRelative;
        table[0xB3] = Op_LDA_StackRelIndirectY;
        table[0xA7] = Op_LDA_DpIndirectLong;
        table[0xB7] = Op_LDA_DpIndirectLongY;

        table[0xA2] = Op_LDX_Immediate;
        table[0xAE] = Op_LDX_Absolute;
        table[0xBE] = Op_LDX_AbsoluteY;
        table[0xA6] = Op_LDX_DirectPage;
        table[0xB6] = Op_LDX_DirectPageY;

        table[0xA0] = Op_LDY_Immediate;
        table[0xAC] = Op_LDY_Absolute;
        table[0xBC] = Op_LDY_AbsoluteX;
        table[0xA4] = Op_LDY_DirectPage;
        table[0xB4] = Op_LDY_DirectPageX;

        table[0x85] = Op_STA_DirectPage;
        table[0x95] = Op_STA_DirectPageX;
        table[0x8D] = Op_STA_Absolute;
        table[0x9D] = Op_STA_AbsoluteX;
        table[0x99] = Op_STA_AbsoluteY;
        table[0x8F] = Op_STA_AbsoluteLong;
        table[0x9F] = Op_STA_AbsoluteLongX;
        table[0x81] = Op_STA_XIndirect;
        table[0x91] = Op_STA_IndirectY;
        table[0x92] = Op_STA_Indirect;
        table[0x87] = Op_STA_DpIndirectLong;
        table[0x97] = Op_STA_DpIndirectLongY;
        table[0x83] = Op_STA_StackRelative;
        table[0x93] = Op_STA_StackRelIndirectY;

        table[0x86] = Op_STX_DirectPage;
        table[0x96] = Op_STX_DirectPageY;
        table[0x8E] = Op_STX_Absolute;

        table[0x84] = Op_STY_DirectPage;
        table[0x94] = Op_STY_DirectPageX;
        table[0x8C] = Op_STY_Absolute;

        table[0x64] = Op_STZ_DirectPage;
        table[0x74] = Op_STZ_DirectPageX;
        table[0x9C] = Op_STZ_Absolute;
        table[0x9E] = Op_STZ_AbsoluteX;

        // ── Transfer ──────────────────────────────────────────────────────────
        table[0xAA] = Op_TAX;
        table[0xA8] = Op_TAY;
        table[0xBA] = Op_TSX;
        table[0x8A] = Op_TXA;
        table[0x9A] = Op_TXS;
        table[0x98] = Op_TYA;
        table[0x9B] = Op_TXY;
        table[0xBB] = Op_TYX;
        table[0x5B] = Op_TCD;
        table[0x7B] = Op_TDC;
        table[0x1B] = Op_TCS;
        table[0x3B] = Op_TSC;
        table[0xEB] = Op_XBA;
        table[0xFB] = Op_XCE;

        // ── Stack ──────────────────────────────────────────────────────────────
        table[0x48] = Op_PHA;
        table[0x68] = Op_PLA;
        table[0xDA] = Op_PHX;
        table[0xFA] = Op_PLX;
        table[0x5A] = Op_PHY;
        table[0x7A] = Op_PLY;
        table[0x08] = Op_PHP;
        table[0x28] = Op_PLP;
        table[0x8B] = Op_PHB;
        table[0xAB] = Op_PLB;
        table[0x0B] = Op_PHD;
        table[0x2B] = Op_PLD;
        table[0x4B] = Op_PHK;
        table[0x62] = Op_PER;
        table[0xF4] = Op_PEA;
        table[0xD4] = Op_PEI;

        // ── Arithmetic ────────────────────────────────────────────────────────
        table[0x69] = Op_ADC_Immediate;
        table[0x6D] = Op_ADC_Absolute;
        table[0x79] = Op_ADC_AbsoluteY;
        table[0x65] = Op_ADC_DirectPage;
        table[0x75] = Op_ADC_DirectPageX;
        table[0x6F] = Op_ADC_AbsoluteLong;
        table[0x7F] = Op_ADC_AbsoluteLongX;
        table[0x61] = Op_ADC_XIndirect;
        table[0x71] = Op_ADC_IndirectY;
        table[0x72] = Op_ADC_Indirect;
        table[0x67] = Op_ADC_DpIndirectLong;
        table[0x77] = Op_ADC_DpIndirectLongY;

        table[0xE9] = Op_SBC_Immediate;
        table[0xED] = Op_SBC_Absolute;
        table[0xF9] = Op_SBC_AbsoluteY;
        table[0xE5] = Op_SBC_DirectPage;
        table[0xF5] = Op_SBC_DirectPageX;
        table[0xEF] = Op_SBC_AbsoluteLong;
        table[0xFF] = Op_SBC_AbsoluteLongX;
        table[0xE1] = Op_SBC_XIndirect;
        table[0xF1] = Op_SBC_IndirectY;
        table[0xF2] = Op_SBC_Indirect;
        table[0xE7] = Op_SBC_DpIndirectLong;
        table[0xF7] = Op_SBC_DpIndirectLongY;
        table[0xF3] = Op_SBC_StackRelIndirectY;
        table[0xFD] = Op_SBC_AbsoluteX;

        // ── Increment / Decrement ─────────────────────────────────────────────
        table[0xE6] = Op_INC_DirectPage;
        table[0xF6] = Op_INC_DirectPageX;
        table[0xEE] = Op_INC_Absolute;
        table[0xFE] = Op_INC_AbsoluteX;
        table[0x1A] = Op_INC_A;
        table[0xC6] = Op_DEC_DirectPage;
        table[0xD6] = Op_DEC_DirectPageX;
        table[0xCE] = Op_DEC_Absolute;
        table[0xDE] = Op_DEC_AbsoluteX;
        table[0x3A] = Op_DEC_A;
        table[0xE8] = Op_INX;
        table[0xC8] = Op_INY;
        table[0xCA] = Op_DEX;
        table[0x88] = Op_DEY;

        // ── Logic ─────────────────────────────────────────────────────────────
        table[0x29] = Op_AND_Immediate;
        table[0x2D] = Op_AND_Absolute;
        table[0x39] = Op_AND_AbsoluteY;
        table[0x25] = Op_AND_DirectPage;
        table[0x35] = Op_AND_DirectPageX;
        table[0x2F] = Op_AND_AbsoluteLong;
        table[0x3F] = Op_AND_AbsoluteLongX;
        table[0x21] = Op_AND_XIndirect;
        table[0x31] = Op_AND_IndirectY;
        table[0x32] = Op_AND_Indirect;
        table[0x27] = Op_AND_DpIndirectLong;
        table[0x37] = Op_AND_DpIndirectLongY;
        table[0x3D] = Op_AND_AbsoluteX;

        table[0x09] = Op_ORA_Immediate;
        table[0x0D] = Op_ORA_Absolute;
        table[0x19] = Op_ORA_AbsoluteY;
        table[0x1D] = Op_ORA_AbsoluteX;
        table[0x05] = Op_ORA_DirectPage;
        table[0x15] = Op_ORA_DirectPageX;
        table[0x0F] = Op_ORA_AbsoluteLong;
        table[0x1F] = Op_ORA_AbsoluteLongX;
        table[0x01] = Op_ORA_XIndirect;
        table[0x11] = Op_ORA_IndirectY;
        table[0x12] = Op_ORA_Indirect;
        table[0x07] = Op_ORA_DpIndirectLong;
        table[0x17] = Op_ORA_DpIndirectLongY;

        table[0x49] = Op_EOR_Immediate;
        table[0x4D] = Op_EOR_Absolute;
        table[0x59] = Op_EOR_AbsoluteY;
        table[0x45] = Op_EOR_DirectPage;
        table[0x55] = Op_EOR_DirectPageX;
        table[0x4F] = Op_EOR_AbsoluteLong;
        table[0x5F] = Op_EOR_AbsoluteLongX;
        table[0x41] = Op_EOR_XIndirect;
        table[0x51] = Op_EOR_IndirectY;
        table[0x52] = Op_EOR_Indirect;
        table[0x47] = Op_EOR_DpIndirectLong;
        table[0x57] = Op_EOR_DpIndirectLongY;

        // ── Shift / Rotate ────────────────────────────────────────────────────
        table[0x0A] = Op_ASL_A;
        table[0x06] = Op_ASL_DirectPage;
        table[0x16] = Op_ASL_DirectPageX;
        table[0x0E] = Op_ASL_Absolute;
        table[0x1E] = Op_ASL_AbsoluteX;

        table[0x4A] = Op_LSR_A;
        table[0x46] = Op_LSR_DirectPage;
        table[0x56] = Op_LSR_DirectPageX;
        table[0x4E] = Op_LSR_Absolute;
        table[0x5E] = Op_LSR_AbsoluteX;
        table[0x5D] = Op_EOR_AbsoluteX;

        table[0x2A] = Op_ROL_A;
        table[0x26] = Op_ROL_DirectPage;
        table[0x36] = Op_ROL_DirectPageX;
        table[0x2E] = Op_ROL_Absolute;
        table[0x3E] = Op_ROL_AbsoluteX;

        table[0x6A] = Op_ROR_A;
        table[0x66] = Op_ROR_DirectPage;
        table[0x76] = Op_ROR_DirectPageX;
        table[0x6E] = Op_ROR_Absolute;
        table[0x7E] = Op_ROR_AbsoluteX;
        table[0x7D] = Op_ADC_AbsoluteX;

        // ── Compare ───────────────────────────────────────────────────────────
        table[0xC9] = Op_CMP_Immediate;
        table[0xCD] = Op_CMP_Absolute;
        table[0xD9] = Op_CMP_AbsoluteY;
        table[0xC5] = Op_CMP_DirectPage;
        table[0xD5] = Op_CMP_DirectPageX;
        table[0xCF] = Op_CMP_AbsoluteLong;
        table[0xDF] = Op_CMP_AbsoluteLongX;
        table[0xC1] = Op_CMP_XIndirect;
        table[0xD1] = Op_CMP_IndirectY;
        table[0xD2] = Op_CMP_Indirect;
        table[0xC7] = Op_CMP_DpIndirectLong;
        table[0xD7] = Op_CMP_DpIndirectLongY;
        table[0xDD] = Op_CMP_AbsoluteX;

        table[0xE0] = Op_CPX_Immediate;
        table[0xEC] = Op_CPX_Absolute;
        table[0xE4] = Op_CPX_DirectPage;

        table[0xC0] = Op_CPY_Immediate;
        table[0xCC] = Op_CPY_Absolute;
        table[0xC4] = Op_CPY_DirectPage;

        // ── Bit test ─────────────────────────────────────────────────────────
        table[0x89] = Op_BIT_Immediate;
        table[0x2C] = Op_BIT_Absolute;
        table[0x3C] = Op_BIT_AbsoluteX;
        table[0x24] = Op_BIT_DirectPage;
        table[0x34] = Op_BIT_DirectPageX;

        // ── Branch ───────────────────────────────────────────────────────────
        table[0x90] = Op_BCC;
        table[0xB0] = Op_BCS;
        table[0xF0] = Op_BEQ;
        table[0xD0] = Op_BNE;
        table[0x30] = Op_BMI;
        table[0x10] = Op_BPL;
        table[0x70] = Op_BVS;
        table[0x50] = Op_BVC;
        table[0x80] = Op_BRA;
        table[0x82] = Op_BRL;

        // ── Jump / Call / Return ──────────────────────────────────────────────
        table[0x4C] = Op_JMP_Absolute;
        table[0x6C] = Op_JMP_AbsoluteIndirect;
        table[0x7C] = Op_JMP_AbsoluteIndirectX;
        table[0x5C] = Op_JML_AbsoluteLong;
        table[0xDC] = Op_JML_AbsoluteIndirectLong;

        table[0x20] = Op_JSR_Absolute;
        table[0xFC] = Op_JSR_AbsoluteIndirectX;
        table[0x22] = Op_JSL_AbsoluteLong;

        table[0x60] = Op_RTS;
        table[0x6B] = Op_RTL;
        table[0x40] = Op_RTI;

        // ── Flags ─────────────────────────────────────────────────────────────
        table[0x18] = () => { _state.FlagC = false; return 2; };  // CLC
        table[0x38] = () => { _state.FlagC = true;  return 2; };  // SEC
        table[0xD8] = () => { _state.FlagD = false; return 2; };  // CLD
        table[0xF8] = () => { _state.FlagD = true;  return 2; };  // SED
        table[0x58] = () => { _state.FlagI = false; return 2; };  // CLI
        table[0x78] = () => { _state.FlagI = true;  return 2; };  // SEI
        table[0xB8] = () => { _state.FlagV = false; return 2; };  // CLV
        table[0xC2] = Op_REP;
        table[0xE2] = Op_SEP;

        // ── Misc ──────────────────────────────────────────────────────────────
        table[0xEA] = () => 2;  // NOP
        table[0x00] = Op_BRK;
        table[0x02] = Op_COP;
        table[0x42] = Op_WDM;   // Unofficial / reserved — treat as NOP
        table[0xCB] = () => { /* WAI: wait for interrupt */ return 3; };
        table[0xDB] = () => { /* STP: stop clock */ return 3; };

        // ── Block Move ────────────────────────────────────────────────────────
        // 65C816 block move opcodes:
        //   $44 = MVP (Move Positive / decrement X,Y)
        //   $54 = MVN (Move Negative / increment X,Y)
        table[0x44] = Op_MVP;
        table[0x54] = Op_MVN;

        return table;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  INSTRUCTION IMPLEMENTATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    // ── LDA ──────────────────────────────────────────────────────────────────

    private int Op_LDA_Immediate()
    {
        if (_state.Is8BitAccumulator)
        {
            _state.A = ReadByte(_addr.Immediate8());
            _state.SetNZ8(_state.A);
            return 2;
        }
        _state.C = ReadWord(_addr.Immediate16());
        _state.SetNZ16(_state.C);
        return 3;
    }

    private int Op_LDA_Absolute()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.Absolute()); _state.SetNZ8(_state.A); return 4; }
        _state.C = ReadWord(_addr.Absolute()); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_LDA_AbsoluteX()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.AbsoluteX()); _state.SetNZ8(_state.A); return 4; }
        _state.C = ReadWord(_addr.AbsoluteX()); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_LDA_AbsoluteY()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.AbsoluteY()); _state.SetNZ8(_state.A); return 4; }
        _state.C = ReadWord(_addr.AbsoluteY()); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_LDA_DirectPage()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPage()); _state.SetNZ8(_state.A); return 3; }
        _state.C = ReadWord(_addr.DirectPage()); _state.SetNZ16(_state.C); return 4;
    }
    private int Op_LDA_DirectPageX()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageX()); _state.SetNZ8(_state.A); return 4; }
        _state.C = ReadWord(_addr.DirectPageX()); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_LDA_AbsoluteLong()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.AbsoluteLong()); _state.SetNZ8(_state.A); return 5; }
        _state.C = ReadWord(_addr.AbsoluteLong()); _state.SetNZ16(_state.C); return 6;
    }
    private int Op_LDA_AbsoluteLongX()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.AbsoluteLongX()); _state.SetNZ8(_state.A); return 5; }
        _state.C = ReadWord(_addr.AbsoluteLongX()); _state.SetNZ16(_state.C); return 6;
    }
    private int Op_LDA_XIndirect()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageXIndirect()); _state.SetNZ8(_state.A); return 6; }
        _state.C = ReadWord(_addr.DirectPageXIndirect()); _state.SetNZ16(_state.C); return 7;
    }
    private int Op_LDA_IndirectY()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageIndirectY()); _state.SetNZ8(_state.A); return 5; }
        _state.C = ReadWord(_addr.DirectPageIndirectY()); _state.SetNZ16(_state.C); return 6;
    }
    private int Op_LDA_Indirect()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageIndirect()); _state.SetNZ8(_state.A); return 5; }
        _state.C = ReadWord(_addr.DirectPageIndirect()); _state.SetNZ16(_state.C); return 6;
    }
    private int Op_LDA_StackRelative()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.StackRelative()); _state.SetNZ8(_state.A); return 4; }
        _state.C = ReadWord(_addr.StackRelative()); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_LDA_StackRelIndirectY()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.StackRelativeIndirectY()); _state.SetNZ8(_state.A); return 7; }
        _state.C = ReadWord(_addr.StackRelativeIndirectY()); _state.SetNZ16(_state.C); return 8;
    }
    private int Op_LDA_DpIndirectLong()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageIndirectLong()); _state.SetNZ8(_state.A); return 6; }
        _state.C = ReadWord(_addr.DirectPageIndirectLong()); _state.SetNZ16(_state.C); return 7;
    }
    private int Op_LDA_DpIndirectLongY()
    {
        if (_state.Is8BitAccumulator) { _state.A = ReadByte(_addr.DirectPageIndirectLongY()); _state.SetNZ8(_state.A); return 6; }
        _state.C = ReadWord(_addr.DirectPageIndirectLongY()); _state.SetNZ16(_state.C); return 7;
    }

    // ── LDX ──────────────────────────────────────────────────────────────────

    private int Op_LDX_Immediate()
    {
        if (_state.Is8BitIndex) { _state.X = ReadByte(_addr.Immediate8()); _state.SetNZ8((byte)_state.X); return 2; }
        _state.X = ReadWord(_addr.Immediate16()); _state.SetNZ16(_state.X); return 3;
    }
    private int Op_LDX_Absolute()
    {
        if (_state.Is8BitIndex) { _state.X = ReadByte(_addr.Absolute()); _state.SetNZ8((byte)_state.X); return 4; }
        _state.X = ReadWord(_addr.Absolute()); _state.SetNZ16(_state.X); return 5;
    }
    private int Op_LDX_AbsoluteY()
    {
        if (_state.Is8BitIndex) { _state.X = ReadByte(_addr.AbsoluteY()); _state.SetNZ8((byte)_state.X); return 4; }
        _state.X = ReadWord(_addr.AbsoluteY()); _state.SetNZ16(_state.X); return 5;
    }
    private int Op_LDX_DirectPage()
    {
        if (_state.Is8BitIndex) { _state.X = ReadByte(_addr.DirectPage()); _state.SetNZ8((byte)_state.X); return 3; }
        _state.X = ReadWord(_addr.DirectPage()); _state.SetNZ16(_state.X); return 4;
    }
    private int Op_LDX_DirectPageY()
    {
        if (_state.Is8BitIndex) { _state.X = ReadByte(_addr.DirectPageY()); _state.SetNZ8((byte)_state.X); return 4; }
        _state.X = ReadWord(_addr.DirectPageY()); _state.SetNZ16(_state.X); return 5;
    }

    // ── LDY ──────────────────────────────────────────────────────────────────

    private int Op_LDY_Immediate()
    {
        if (_state.Is8BitIndex) { _state.Y = ReadByte(_addr.Immediate8()); _state.SetNZ8((byte)_state.Y); return 2; }
        _state.Y = ReadWord(_addr.Immediate16()); _state.SetNZ16(_state.Y); return 3;
    }
    private int Op_LDY_Absolute()
    {
        if (_state.Is8BitIndex) { _state.Y = ReadByte(_addr.Absolute()); _state.SetNZ8((byte)_state.Y); return 4; }
        _state.Y = ReadWord(_addr.Absolute()); _state.SetNZ16(_state.Y); return 5;
    }
    private int Op_LDY_AbsoluteX()
    {
        if (_state.Is8BitIndex) { _state.Y = ReadByte(_addr.AbsoluteX()); _state.SetNZ8((byte)_state.Y); return 4; }
        _state.Y = ReadWord(_addr.AbsoluteX()); _state.SetNZ16(_state.Y); return 5;
    }
    private int Op_LDY_DirectPage()
    {
        if (_state.Is8BitIndex) { _state.Y = ReadByte(_addr.DirectPage()); _state.SetNZ8((byte)_state.Y); return 3; }
        _state.Y = ReadWord(_addr.DirectPage()); _state.SetNZ16(_state.Y); return 4;
    }
    private int Op_LDY_DirectPageX()
    {
        if (_state.Is8BitIndex) { _state.Y = ReadByte(_addr.DirectPageX()); _state.SetNZ8((byte)_state.Y); return 4; }
        _state.Y = ReadWord(_addr.DirectPageX()); _state.SetNZ16(_state.Y); return 5;
    }

    // ── STA ──────────────────────────────────────────────────────────────────

    private void StoreA(uint addr) { if (_state.Is8BitAccumulator) WriteByte(addr, _state.A); else WriteWord(addr, _state.C); }

    private int Op_STA_DirectPage()        { StoreA(_addr.DirectPage());         return _state.Is8BitAccumulator ? 3 : 4; }
    private int Op_STA_DirectPageX()       { StoreA(_addr.DirectPageX());        return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_STA_Absolute()          { StoreA(_addr.Absolute());           return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_STA_AbsoluteX()         { StoreA(_addr.AbsoluteX());          return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_STA_AbsoluteY()         { StoreA(_addr.AbsoluteY());          return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_STA_AbsoluteLong()      { StoreA(_addr.AbsoluteLong());       return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_STA_AbsoluteLongX()
    {
        byte lo = _addr.FetchByte();
        byte hi = _addr.FetchByte();
        byte bank = _addr.FetchByte();
        uint baseAddr = (uint)(lo | (hi << 8) | (bank << 16));
        ushort x = _state.Is8BitIndex ? (byte)_state.X : _state.X;
        uint effectiveAddr = (baseAddr + x) & 0xFFFFFF;

        if (_staLongXTraceRemaining > 0)
        {
            _logger.LogDebug(
                "STA long,X at ${Pc:X6}: base=${Base:X6} X=${X:X4} -> eff=${Eff:X6} A=${A:X4} C=${C:X4} M8={M8}",
                ((_state.PBR << 16) | (ushort)(_state.PC - 4)),
                baseAddr,
                _state.X,
                effectiveAddr,
                _state.A,
                _state.C,
                _state.Is8BitAccumulator ? 1 : 0);
            _staLongXTraceRemaining--;
        }

        StoreA(effectiveAddr);
        return _state.Is8BitAccumulator ? 5 : 6;
    }
    private int Op_STA_XIndirect()         { StoreA(_addr.DirectPageXIndirect()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_STA_IndirectY()         { StoreA(_addr.DirectPageIndirectY()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_STA_Indirect()          { StoreA(_addr.DirectPageIndirect());  return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_STA_DpIndirectLong()    { StoreA(_addr.DirectPageIndirectLong()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_STA_DpIndirectLongY()   { StoreA(_addr.DirectPageIndirectLongY()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_STA_StackRelative()     { StoreA(_addr.StackRelative());      return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_STA_StackRelIndirectY() { StoreA(_addr.StackRelativeIndirectY()); return _state.Is8BitAccumulator ? 7 : 8; }

    // ── STX / STY / STZ ──────────────────────────────────────────────────────

    private void StoreX(uint addr) { if (_state.Is8BitIndex) WriteByte(addr, (byte)_state.X); else WriteWord(addr, _state.X); }
    private void StoreY(uint addr) { if (_state.Is8BitIndex) WriteByte(addr, (byte)_state.Y); else WriteWord(addr, _state.Y); }
    private void StoreZ(uint addr) { if (_state.Is8BitAccumulator) WriteByte(addr, 0); else WriteWord(addr, 0); }

    private int Op_STX_DirectPage()  { StoreX(_addr.DirectPage());  return 3; }
    private int Op_STX_DirectPageY() { StoreX(_addr.DirectPageY()); return 4; }
    private int Op_STX_Absolute()    { StoreX(_addr.Absolute());    return 4; }
    private int Op_STY_DirectPage()  { StoreY(_addr.DirectPage());  return 3; }
    private int Op_STY_DirectPageX() { StoreY(_addr.DirectPageX()); return 4; }
    private int Op_STY_Absolute()    { StoreY(_addr.Absolute());    return 4; }
    private int Op_STZ_DirectPage()  { StoreZ(_addr.DirectPage());  return 3; }
    private int Op_STZ_DirectPageX() { StoreZ(_addr.DirectPageX()); return 4; }
    private int Op_STZ_Absolute()    { StoreZ(_addr.Absolute());    return 4; }
    private int Op_STZ_AbsoluteX()   { StoreZ(_addr.AbsoluteX());   return 5; }

    // ── Transfers ─────────────────────────────────────────────────────────────

    private int Op_TAX()
    {
        _state.X = _state.Is8BitIndex ? _state.A : _state.C;
        if (_state.Is8BitIndex) _state.SetNZ8((byte)_state.X); else _state.SetNZ16(_state.X);
        return 2;
    }

    private int Op_TAY()
    {
        _state.Y = _state.Is8BitIndex ? _state.A : _state.C;
        if (_state.Is8BitIndex) _state.SetNZ8((byte)_state.Y); else _state.SetNZ16(_state.Y);
        return 2;
    }

    private int Op_TXA()
    {
        if (_state.Is8BitAccumulator) { _state.A = (byte)_state.X; _state.SetNZ8(_state.A); }
        else { _state.C = _state.X; _state.SetNZ16(_state.C); }
        return 2;
    }

    private int Op_TYA()
    {
        if (_state.Is8BitAccumulator) { _state.A = (byte)_state.Y; _state.SetNZ8(_state.A); }
        else { _state.C = _state.Y; _state.SetNZ16(_state.C); }
        return 2;
    }

    private int Op_TSX()
    {
        _state.X = _state.Is8BitIndex ? (byte)_state.SP : _state.SP;
        if (_state.Is8BitIndex) _state.SetNZ8((byte)_state.X); else _state.SetNZ16(_state.X);
        return 2;
    }
    private int Op_TXS() { _state.SP = _state.EmulationMode ? (ushort)(0x0100 | (_state.X & 0xFF)) : _state.X; return 2; }
    private int Op_TCD() { _state.DP = _state.C; _state.SetNZ16(_state.DP); return 2; }
    private int Op_TDC() { _state.C = _state.DP; _state.SetNZ16(_state.C); return 2; }
    private int Op_TCS() { _state.SP = _state.C; return 2; }
    private int Op_TSC() { _state.C = _state.SP; _state.SetNZ16(_state.C); return 2; }
    private int Op_XBA() { byte tmp = _state.A; _state.A = _state.B; _state.B = tmp; _state.SetNZ8(_state.A); return 3; }
    private int Op_XCE()
    {
        bool carry = _state.FlagC;
        _state.FlagC = _state.EmulationMode;
        _state.EmulationMode = carry;
        if (_state.EmulationMode) { _state.FlagM = true; _state.FlagX = true; _state.SP = (ushort)(0x0100 | (_state.SP & 0xFF)); }
        return 2;
    }

    // ── Stack ─────────────────────────────────────────────────────────────────

    private int Op_PHA()
    {
        if (_state.Is8BitAccumulator) { Push(_state.A); return 3; }
        PushWord(_state.C); return 4;
    }
    private int Op_PLA()
    {
        if (_state.Is8BitAccumulator) { _state.A = Pop(); _state.SetNZ8(_state.A); return 4; }
        _state.C = PopWord(); _state.SetNZ16(_state.C); return 5;
    }
    private int Op_PHX() { if (_state.Is8BitIndex) { Push((byte)_state.X); return 3; } PushWord(_state.X); return 4; }
    private int Op_PLX() { if (_state.Is8BitIndex) { _state.X = Pop(); _state.SetNZ8((byte)_state.X); return 4; } _state.X = PopWord(); _state.SetNZ16(_state.X); return 5; }
    private int Op_PHY() { if (_state.Is8BitIndex) { Push((byte)_state.Y); return 3; } PushWord(_state.Y); return 4; }
    private int Op_PLY() { if (_state.Is8BitIndex) { _state.Y = Pop(); _state.SetNZ8((byte)_state.Y); return 4; } _state.Y = PopWord(); _state.SetNZ16(_state.Y); return 5; }
    private int Op_PHP() { Push(_state.P); return 3; }
    private int Op_PLP() { _state.P = Pop(); return 4; }
    private int Op_PHB() { Push(_state.DBR); return 3; }
    private int Op_PLB() { _state.DBR = Pop(); _state.SetNZ8(_state.DBR); return 4; }
    private int Op_PHD() { PushWord(_state.DP); return 4; }
    private int Op_PLD() { _state.DP = PopWord(); _state.SetNZ16(_state.DP); return 5; }
    private int Op_PHK() { Push(_state.PBR); return 3; }
    private int Op_PEA() { PushWord(FetchWord()); return 5; }
    private int Op_PEI() { PushWord(ReadWord(_addr.DirectPage())); return 6; }
    private int Op_PER() { ushort offset = FetchWord(); PushWord((ushort)(_state.PC + (short)offset)); return 6; }

    // ── ADC ───────────────────────────────────────────────────────────────────

    private void Adc(ushort operand)
    {
        if (_state.Is8BitAccumulator)
        {
            byte a = _state.A;
            byte op8 = (byte)operand;
            int result = a + op8 + (_state.FlagC ? 1 : 0);
            if (_state.FlagD)
            {
                // BCD mode
                int lo = (a & 0x0F) + (op8 & 0x0F) + (_state.FlagC ? 1 : 0);
                if (lo > 9) lo += 6;
                int hi = (a >> 4) + (op8 >> 4) + (lo > 15 ? 1 : 0);
                if (hi > 9) hi += 6;
                result = (hi << 4) | (lo & 0x0F);
                _state.FlagC = hi > 15;
            }
            else
            {
                _state.FlagV = ((a ^ result) & (op8 ^ result) & 0x80) != 0;
                _state.FlagC = result > 0xFF;
            }
            _state.A = (byte)result;
            _state.SetNZ8(_state.A);
        }
        else
        {
            ushort a = _state.C;
            int result = a + operand + (_state.FlagC ? 1 : 0);
            _state.FlagV = ((a ^ result) & (operand ^ result) & 0x8000) != 0;
            _state.FlagC = result > 0xFFFF;
            _state.C = (ushort)result;
            _state.SetNZ16(_state.C);
        }
    }

    private int Op_ADC_Immediate() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.Immediate8()) : ReadWord(_addr.Immediate16())); return _state.Is8BitAccumulator ? 2 : 3; }
    private int Op_ADC_Absolute()  { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.Absolute())   : ReadWord(_addr.Absolute()));   return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_ADC_AbsoluteY() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteY())  : ReadWord(_addr.AbsoluteY()));  return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_ADC_DirectPage() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPage()) : ReadWord(_addr.DirectPage())); return _state.Is8BitAccumulator ? 3 : 4; }
    private int Op_ADC_DirectPageX() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageX()) : ReadWord(_addr.DirectPageX())); return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_ADC_AbsoluteLong() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteLong()) : ReadWord(_addr.AbsoluteLong())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_ADC_AbsoluteLongX() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteLongX()) : ReadWord(_addr.AbsoluteLongX())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_ADC_XIndirect() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageXIndirect()) : ReadWord(_addr.DirectPageXIndirect())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_ADC_IndirectY() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectY()) : ReadWord(_addr.DirectPageIndirectY())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_ADC_Indirect()  { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirect())  : ReadWord(_addr.DirectPageIndirect()));  return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_ADC_DpIndirectLong() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectLong()) : ReadWord(_addr.DirectPageIndirectLong())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_ADC_DpIndirectLongY() { Adc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectLongY()) : ReadWord(_addr.DirectPageIndirectLongY())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_ADC_AbsoluteX()
    {
        Adc(_state.Is8BitAccumulator
            ? ReadByte(_addr.AbsoluteX())
            : ReadWord(_addr.AbsoluteX()));
        return _state.Is8BitAccumulator ? 4 : 5;
    }

    // ── SBC ───────────────────────────────────────────────────────────────────

    private void Sbc(ushort operand)
    {
        // SBC is ADC with the operand inverted (1's complement)
        Adc((ushort)(~operand));
    }

    private int Op_SBC_Immediate() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.Immediate8()) : ReadWord(_addr.Immediate16())); return _state.Is8BitAccumulator ? 2 : 3; }
    private int Op_SBC_Absolute()  { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.Absolute())   : ReadWord(_addr.Absolute()));   return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_SBC_AbsoluteY() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteY())  : ReadWord(_addr.AbsoluteY()));  return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_SBC_DirectPage() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPage()) : ReadWord(_addr.DirectPage())); return _state.Is8BitAccumulator ? 3 : 4; }
    private int Op_SBC_DirectPageX() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageX()) : ReadWord(_addr.DirectPageX())); return _state.Is8BitAccumulator ? 4 : 5; }
    private int Op_SBC_AbsoluteLong() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteLong()) : ReadWord(_addr.AbsoluteLong())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_SBC_AbsoluteLongX() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.AbsoluteLongX()) : ReadWord(_addr.AbsoluteLongX())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_SBC_XIndirect() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageXIndirect()) : ReadWord(_addr.DirectPageXIndirect())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_SBC_IndirectY() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectY()) : ReadWord(_addr.DirectPageIndirectY())); return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_SBC_Indirect()  { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirect())  : ReadWord(_addr.DirectPageIndirect()));  return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_SBC_DpIndirectLong() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectLong()) : ReadWord(_addr.DirectPageIndirectLong())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_SBC_DpIndirectLongY() { Sbc(_state.Is8BitAccumulator ? ReadByte(_addr.DirectPageIndirectLongY()) : ReadWord(_addr.DirectPageIndirectLongY())); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_SBC_StackRelIndirectY()
    {
        Sbc(_state.Is8BitAccumulator
            ? ReadByte(_addr.StackRelativeIndirectY())
            : ReadWord(_addr.StackRelativeIndirectY()));
        return _state.Is8BitAccumulator ? 7 : 8;
    }
    private int Op_SBC_AbsoluteX()
    {
        Sbc(_state.Is8BitAccumulator
            ? ReadByte(_addr.AbsoluteX())
            : ReadWord(_addr.AbsoluteX()));
        return _state.Is8BitAccumulator ? 4 : 5;
    }

    // ── INC / DEC ─────────────────────────────────────────────────────────────

    private void IncMem(uint addr)
    {
        if (_state.Is8BitAccumulator) { byte v = (byte)(ReadByte(addr) + 1); WriteByte(addr, v); _state.SetNZ8(v); }
        else { ushort v = (ushort)(ReadWord(addr) + 1); WriteWord(addr, v); _state.SetNZ16(v); }
    }
    private void DecMem(uint addr)
    {
        if (_state.Is8BitAccumulator) { byte v = (byte)(ReadByte(addr) - 1); WriteByte(addr, v); _state.SetNZ8(v); }
        else { ushort v = (ushort)(ReadWord(addr) - 1); WriteWord(addr, v); _state.SetNZ16(v); }
    }

    private int Op_INC_DirectPage()  { IncMem(_addr.DirectPage());  return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_INC_DirectPageX() { IncMem(_addr.DirectPageX()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_INC_Absolute()    { IncMem(_addr.Absolute());    return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_INC_AbsoluteX()   { IncMem(_addr.AbsoluteX());   return _state.Is8BitAccumulator ? 7 : 8; }
    private int Op_INC_A()           { if (_state.Is8BitAccumulator) { _state.A++; _state.SetNZ8(_state.A); } else { _state.C++; _state.SetNZ16(_state.C); } return 2; }
    private int Op_DEC_DirectPage()  { DecMem(_addr.DirectPage());  return _state.Is8BitAccumulator ? 5 : 6; }
    private int Op_DEC_DirectPageX() { DecMem(_addr.DirectPageX()); return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_DEC_Absolute()    { DecMem(_addr.Absolute());    return _state.Is8BitAccumulator ? 6 : 7; }
    private int Op_DEC_AbsoluteX()   { DecMem(_addr.AbsoluteX());   return _state.Is8BitAccumulator ? 7 : 8; }
    private int Op_DEC_A()           { if (_state.Is8BitAccumulator) { _state.A--; _state.SetNZ8(_state.A); } else { _state.C--; _state.SetNZ16(_state.C); } return 2; }

    private int Op_INX() { if (_state.Is8BitIndex) { _state.X = (byte)(_state.X + 1); _state.SetNZ8((byte)_state.X); } else { _state.X++; _state.SetNZ16(_state.X); } return 2; }
    private int Op_INY() { if (_state.Is8BitIndex) { _state.Y = (byte)(_state.Y + 1); _state.SetNZ8((byte)_state.Y); } else { _state.Y++; _state.SetNZ16(_state.Y); } return 2; }
    private int Op_DEX() { if (_state.Is8BitIndex) { _state.X = (byte)(_state.X - 1); _state.SetNZ8((byte)_state.X); } else { _state.X--; _state.SetNZ16(_state.X); } return 2; }
    private int Op_DEY() { if (_state.Is8BitIndex) { _state.Y = (byte)(_state.Y - 1); _state.SetNZ8((byte)_state.Y); } else { _state.Y--; _state.SetNZ16(_state.Y); } return 2; }

    // ── AND / ORA / EOR ───────────────────────────────────────────────────────

    private void And(ushort op) { if (_state.Is8BitAccumulator) { _state.A &= (byte)op; _state.SetNZ8(_state.A); } else { _state.C &= op; _state.SetNZ16(_state.C); } }
    private void Ora(ushort op) { if (_state.Is8BitAccumulator) { _state.A |= (byte)op; _state.SetNZ8(_state.A); } else { _state.C |= op; _state.SetNZ16(_state.C); } }
    private void Eor(ushort op) { if (_state.Is8BitAccumulator) { _state.A ^= (byte)op; _state.SetNZ8(_state.A); } else { _state.C ^= op; _state.SetNZ16(_state.C); } }
    private ushort LoadOperand(uint addr) => _state.Is8BitAccumulator ? ReadByte(addr) : ReadWord(addr);

    private int Op_AND_Immediate()  { And(LoadOperand(_state.Is8BitAccumulator ? _addr.Immediate8() : _addr.Immediate16())); return _state.Is8BitAccumulator ? 2:3; }
    private int Op_AND_Absolute()   { And(LoadOperand(_addr.Absolute()));   return _state.Is8BitAccumulator ? 4:5; }
    private int Op_AND_AbsoluteY()  { And(LoadOperand(_addr.AbsoluteY()));  return _state.Is8BitAccumulator ? 4:5; }
    private int Op_AND_DirectPage() { And(LoadOperand(_addr.DirectPage())); return _state.Is8BitAccumulator ? 3:4; }
    private int Op_AND_DirectPageX(){ And(LoadOperand(_addr.DirectPageX()));return _state.Is8BitAccumulator ? 4:5; }
    private int Op_AND_AbsoluteLong() { And(LoadOperand(_addr.AbsoluteLong())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_AND_AbsoluteLongX() { And(LoadOperand(_addr.AbsoluteLongX())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_AND_XIndirect()  { And(LoadOperand(_addr.DirectPageXIndirect())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_AND_IndirectY()  { And(LoadOperand(_addr.DirectPageIndirectY())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_AND_Indirect()   { And(LoadOperand(_addr.DirectPageIndirect()));  return _state.Is8BitAccumulator ? 5:6; }
    private int Op_AND_DpIndirectLong() { And(LoadOperand(_addr.DirectPageIndirectLong())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_AND_DpIndirectLongY() { And(LoadOperand(_addr.DirectPageIndirectLongY())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_AND_AbsoluteX() { And(LoadOperand(_addr.AbsoluteX())); return _state.Is8BitAccumulator ? 4 : 5; }

    private int Op_ORA_Immediate()  { Ora(LoadOperand(_state.Is8BitAccumulator ? _addr.Immediate8() : _addr.Immediate16())); return _state.Is8BitAccumulator ? 2:3; }
    private int Op_ORA_Absolute()   { Ora(LoadOperand(_addr.Absolute()));   return _state.Is8BitAccumulator ? 4:5; }
    private int Op_ORA_AbsoluteX()
    {
        Ora(LoadOperand(_addr.AbsoluteX()));
        return _state.Is8BitAccumulator ? 4 : 5;
    }
    private int Op_ORA_AbsoluteY()  { Ora(LoadOperand(_addr.AbsoluteY()));  return _state.Is8BitAccumulator ? 4:5; }
    private int Op_ORA_DirectPage() { Ora(LoadOperand(_addr.DirectPage())); return _state.Is8BitAccumulator ? 3:4; }
    private int Op_ORA_DirectPageX(){ Ora(LoadOperand(_addr.DirectPageX()));return _state.Is8BitAccumulator ? 4:5; }
    private int Op_ORA_AbsoluteLong() { Ora(LoadOperand(_addr.AbsoluteLong())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_ORA_AbsoluteLongX() { Ora(LoadOperand(_addr.AbsoluteLongX())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_ORA_XIndirect()  { Ora(LoadOperand(_addr.DirectPageXIndirect())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_ORA_IndirectY()  { Ora(LoadOperand(_addr.DirectPageIndirectY())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_ORA_Indirect()   { Ora(LoadOperand(_addr.DirectPageIndirect()));  return _state.Is8BitAccumulator ? 5:6; }
    private int Op_ORA_DpIndirectLong() { Ora(LoadOperand(_addr.DirectPageIndirectLong())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_ORA_DpIndirectLongY() { Ora(LoadOperand(_addr.DirectPageIndirectLongY())); return _state.Is8BitAccumulator ? 6:7; }

    private int Op_EOR_Immediate()  { Eor(LoadOperand(_state.Is8BitAccumulator ? _addr.Immediate8() : _addr.Immediate16())); return _state.Is8BitAccumulator ? 2:3; }
    private int Op_EOR_Absolute()   { Eor(LoadOperand(_addr.Absolute()));   return _state.Is8BitAccumulator ? 4:5; }
    private int Op_EOR_AbsoluteY()  { Eor(LoadOperand(_addr.AbsoluteY()));  return _state.Is8BitAccumulator ? 4:5; }
    private int Op_EOR_DirectPage() { Eor(LoadOperand(_addr.DirectPage())); return _state.Is8BitAccumulator ? 3:4; }
    private int Op_EOR_DirectPageX(){ Eor(LoadOperand(_addr.DirectPageX()));return _state.Is8BitAccumulator ? 4:5; }
    private int Op_EOR_AbsoluteLong() { Eor(LoadOperand(_addr.AbsoluteLong())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_EOR_AbsoluteLongX() { Eor(LoadOperand(_addr.AbsoluteLongX())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_EOR_XIndirect()  { Eor(LoadOperand(_addr.DirectPageXIndirect())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_EOR_IndirectY()  { Eor(LoadOperand(_addr.DirectPageIndirectY())); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_EOR_Indirect()   { Eor(LoadOperand(_addr.DirectPageIndirect()));  return _state.Is8BitAccumulator ? 5:6; }
    private int Op_EOR_DpIndirectLong() { Eor(LoadOperand(_addr.DirectPageIndirectLong())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_EOR_DpIndirectLongY() { Eor(LoadOperand(_addr.DirectPageIndirectLongY())); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_EOR_AbsoluteX() { Eor(LoadOperand(_addr.AbsoluteX())); return _state.Is8BitAccumulator ? 4 : 5; }

    // ── Shift / Rotate ────────────────────────────────────────────────────────

    private int Op_ASL_A()
    {
        if (_state.Is8BitAccumulator) { _state.FlagC = (_state.A & 0x80) != 0; _state.A <<= 1; _state.SetNZ8(_state.A); }
        else { _state.FlagC = (_state.C & 0x8000) != 0; _state.C <<= 1; _state.SetNZ16(_state.C); }
        return 2;
    }
    private int AslMem(uint addr) { if (_state.Is8BitAccumulator) { byte v = ReadByte(addr); _state.FlagC = (v & 0x80) != 0; v <<= 1; WriteByte(addr, v); _state.SetNZ8(v); return 5; } else { ushort v = ReadWord(addr); _state.FlagC = (v & 0x8000) != 0; v <<= 1; WriteWord(addr, v); _state.SetNZ16(v); return 6; } }
    private int Op_ASL_DirectPage()  => AslMem(_addr.DirectPage());
    private int Op_ASL_DirectPageX() => AslMem(_addr.DirectPageX());
    private int Op_ASL_Absolute()    => AslMem(_addr.Absolute());
    private int Op_ASL_AbsoluteX()   => AslMem(_addr.AbsoluteX());

    private int Op_LSR_A()
    {
        if (_state.Is8BitAccumulator) { _state.FlagC = (_state.A & 0x01) != 0; _state.A >>= 1; _state.SetNZ8(_state.A); }
        else { _state.FlagC = (_state.C & 0x0001) != 0; _state.C >>= 1; _state.SetNZ16(_state.C); }
        return 2;
    }
    private int LsrMem(uint addr) { if (_state.Is8BitAccumulator) { byte v = ReadByte(addr); _state.FlagC = (v & 0x01) != 0; v >>= 1; WriteByte(addr, v); _state.SetNZ8(v); return 5; } else { ushort v = ReadWord(addr); _state.FlagC = (v & 0x0001) != 0; v >>= 1; WriteWord(addr, v); _state.SetNZ16(v); return 6; } }
    private int Op_LSR_DirectPage()  => LsrMem(_addr.DirectPage());
    private int Op_LSR_DirectPageX() => LsrMem(_addr.DirectPageX());
    private int Op_LSR_Absolute()    => LsrMem(_addr.Absolute());
    private int Op_LSR_AbsoluteX()   => LsrMem(_addr.AbsoluteX());

    private int Op_ROL_A()
    {
        bool oldC = _state.FlagC;
        if (_state.Is8BitAccumulator) { _state.FlagC = (_state.A & 0x80) != 0; _state.A = (byte)((_state.A << 1) | (oldC ? 1 : 0)); _state.SetNZ8(_state.A); }
        else { _state.FlagC = (_state.C & 0x8000) != 0; _state.C = (ushort)((_state.C << 1) | (oldC ? 1 : 0)); _state.SetNZ16(_state.C); }
        return 2;
    }
    private int RolMem(uint addr) { bool oldC = _state.FlagC; if (_state.Is8BitAccumulator) { byte v = ReadByte(addr); _state.FlagC = (v & 0x80) != 0; v = (byte)((v << 1) | (oldC ? 1 : 0)); WriteByte(addr, v); _state.SetNZ8(v); return 5; } else { ushort v = ReadWord(addr); _state.FlagC = (v & 0x8000) != 0; v = (ushort)((v << 1) | (oldC ? 1 : 0)); WriteWord(addr, v); _state.SetNZ16(v); return 6; } }
    private int Op_ROL_DirectPage()  => RolMem(_addr.DirectPage());
    private int Op_ROL_DirectPageX() => RolMem(_addr.DirectPageX());
    private int Op_ROL_Absolute()    => RolMem(_addr.Absolute());
    private int Op_ROL_AbsoluteX()   => RolMem(_addr.AbsoluteX());

    private int Op_ROR_A()
    {
        bool oldC = _state.FlagC;
        if (_state.Is8BitAccumulator) { _state.FlagC = (_state.A & 0x01) != 0; _state.A = (byte)((_state.A >> 1) | (oldC ? 0x80 : 0)); _state.SetNZ8(_state.A); }
        else { _state.FlagC = (_state.C & 0x0001) != 0; _state.C = (ushort)((_state.C >> 1) | (oldC ? 0x8000 : 0)); _state.SetNZ16(_state.C); }
        return 2;
    }
    private int RorMem(uint addr) { bool oldC = _state.FlagC; if (_state.Is8BitAccumulator) { byte v = ReadByte(addr); _state.FlagC = (v & 0x01) != 0; v = (byte)((v >> 1) | (oldC ? 0x80 : 0)); WriteByte(addr, v); _state.SetNZ8(v); return 5; } else { ushort v = ReadWord(addr); _state.FlagC = (v & 0x0001) != 0; v = (ushort)((v >> 1) | (oldC ? 0x8000 : 0)); WriteWord(addr, v); _state.SetNZ16(v); return 6; } }
    private int Op_ROR_DirectPage()  => RorMem(_addr.DirectPage());
    private int Op_ROR_DirectPageX() => RorMem(_addr.DirectPageX());
    private int Op_ROR_Absolute()    => RorMem(_addr.Absolute());
    private int Op_ROR_AbsoluteX()   => RorMem(_addr.AbsoluteX());

    // ── Compare ───────────────────────────────────────────────────────────────

    private void Cmp(ushort reg, ushort op, bool is8bit)
    {
        if (is8bit) { int r = (byte)reg - (byte)op; _state.FlagC = r >= 0; _state.SetNZ8((byte)r); }
        else { int r = reg - op; _state.FlagC = r >= 0; _state.SetNZ16((ushort)r); }
    }

    private int Op_CMP_Immediate()  { Cmp(_state.C, LoadOperand(_state.Is8BitAccumulator ? _addr.Immediate8() : _addr.Immediate16()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 2:3; }
    private int Op_CMP_Absolute()   { Cmp(_state.C, LoadOperand(_addr.Absolute()),  _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 4:5; }
    private int Op_CMP_AbsoluteY()  { Cmp(_state.C, LoadOperand(_addr.AbsoluteY()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 4:5; }
    private int Op_CMP_DirectPage() { Cmp(_state.C, LoadOperand(_addr.DirectPage()),_state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 3:4; }
    private int Op_CMP_DirectPageX(){ Cmp(_state.C, LoadOperand(_addr.DirectPageX()),_state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 4:5; }
    private int Op_CMP_AbsoluteLong() { Cmp(_state.C, LoadOperand(_addr.AbsoluteLong()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_CMP_AbsoluteLongX() { Cmp(_state.C, LoadOperand(_addr.AbsoluteLongX()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_CMP_XIndirect()  { Cmp(_state.C, LoadOperand(_addr.DirectPageXIndirect()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_CMP_IndirectY()  { Cmp(_state.C, LoadOperand(_addr.DirectPageIndirectY()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_CMP_Indirect()   { Cmp(_state.C, LoadOperand(_addr.DirectPageIndirect()),  _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 5:6; }
    private int Op_CMP_DpIndirectLong() { Cmp(_state.C, LoadOperand(_addr.DirectPageIndirectLong()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_CMP_DpIndirectLongY() { Cmp(_state.C, LoadOperand(_addr.DirectPageIndirectLongY()), _state.Is8BitAccumulator); return _state.Is8BitAccumulator ? 6:7; }
    private int Op_CMP_AbsoluteX()
    {
        Cmp(_state.C, LoadOperand(_addr.AbsoluteX()), _state.Is8BitAccumulator);
        return _state.Is8BitAccumulator ? 4 : 5;
    }

    private int Op_CPX_Immediate()  { Cmp(_state.X, _state.Is8BitIndex ? ReadByte(_addr.Immediate8()) : ReadWord(_addr.Immediate16()), _state.Is8BitIndex); return _state.Is8BitIndex ? 2:3; }
    private int Op_CPX_Absolute()   { Cmp(_state.X, _state.Is8BitIndex ? ReadByte(_addr.Absolute())   : ReadWord(_addr.Absolute()),   _state.Is8BitIndex); return _state.Is8BitIndex ? 4:5; }
    private int Op_CPX_DirectPage() { Cmp(_state.X, _state.Is8BitIndex ? ReadByte(_addr.DirectPage())  : ReadWord(_addr.DirectPage()),  _state.Is8BitIndex); return _state.Is8BitIndex ? 3:4; }

    private int Op_CPY_Immediate()  { Cmp(_state.Y, _state.Is8BitIndex ? ReadByte(_addr.Immediate8()) : ReadWord(_addr.Immediate16()), _state.Is8BitIndex); return _state.Is8BitIndex ? 2:3; }
    private int Op_CPY_Absolute()   { Cmp(_state.Y, _state.Is8BitIndex ? ReadByte(_addr.Absolute())   : ReadWord(_addr.Absolute()),   _state.Is8BitIndex); return _state.Is8BitIndex ? 4:5; }
    private int Op_CPY_DirectPage() { Cmp(_state.Y, _state.Is8BitIndex ? ReadByte(_addr.DirectPage())  : ReadWord(_addr.DirectPage()),  _state.Is8BitIndex); return _state.Is8BitIndex ? 3:4; }

    // ── BIT ───────────────────────────────────────────────────────────────────

    private void BitTest(ushort operand, bool immediate)
    {
        if (_state.Is8BitAccumulator)
        {
            byte op8 = (byte)operand;
            _state.FlagZ = (_state.A & op8) == 0;
            if (!immediate) { _state.FlagN = (op8 & 0x80) != 0; _state.FlagV = (op8 & 0x40) != 0; }
        }
        else
        {
            _state.FlagZ = (_state.C & operand) == 0;
            if (!immediate) { _state.FlagN = (operand & 0x8000) != 0; _state.FlagV = (operand & 0x4000) != 0; }
        }
    }

    private int Op_BIT_Immediate()  { BitTest(LoadOperand(_state.Is8BitAccumulator ? _addr.Immediate8() : _addr.Immediate16()), true);  return _state.Is8BitAccumulator ? 2:3; }
    private int Op_BIT_Absolute()   { BitTest(LoadOperand(_addr.Absolute()),    false); return _state.Is8BitAccumulator ? 4:5; }
    private int Op_BIT_AbsoluteX()  { BitTest(LoadOperand(_addr.AbsoluteX()),   false); return _state.Is8BitAccumulator ? 4:5; }
    private int Op_BIT_DirectPage() { BitTest(LoadOperand(_addr.DirectPage()),  false); return _state.Is8BitAccumulator ? 3:4; }
    private int Op_BIT_DirectPageX(){ BitTest(LoadOperand(_addr.DirectPageX()), false); return _state.Is8BitAccumulator ? 4:5; }

    // ── Branches ──────────────────────────────────────────────────────────────

    private int Branch(bool taken)
    {
        uint target = _addr.Relative(); // Advances PC by 1
        if (taken)
        {
            _state.PC = BitHelper.OffsetOf(target);
            return 3;
        }
        return 2;
    }

    private int Op_BCC() => Branch(!_state.FlagC);
    private int Op_BCS() => Branch(_state.FlagC);
    private int Op_BEQ() => Branch(_state.FlagZ);
    private int Op_BNE() => Branch(!_state.FlagZ);
    private int Op_BMI() => Branch(_state.FlagN);
    private int Op_BPL() => Branch(!_state.FlagN);
    private int Op_BVS() => Branch(_state.FlagV);
    private int Op_BVC() => Branch(!_state.FlagV);
    private int Op_BRA() => Branch(true); // Always branch
    private int Op_BRL() { uint source = _state.FullPC - 1; uint target = _addr.RelativeLong(); TraceControlFlow("BRL", source, target); _state.PC = BitHelper.OffsetOf(target); return 4; }

    // ── Jump / Call ───────────────────────────────────────────────────────────

    private int Op_JMP_Absolute()          { uint source = _state.FullPC - 1; uint target = _addr.Absolute(); TraceControlFlow("JMP", source, (uint)((_state.PBR << 16) | BitHelper.OffsetOf(target))); _state.PC = BitHelper.OffsetOf(target);           return 3; }
    private int Op_JMP_AbsoluteIndirect()  { uint source = _state.FullPC - 1; uint target = _addr.AbsoluteIndirect(); TraceControlFlow("JMP (abs)", source, (uint)((_state.PBR << 16) | BitHelper.OffsetOf(target))); _state.PC = BitHelper.OffsetOf(target);   return 5; }
    private int Op_JMP_AbsoluteIndirectX() { uint source = _state.FullPC - 1; uint target = _addr.AbsoluteIndirectX(); TraceControlFlow("JMP (abs,X)", source, (uint)((_state.PBR << 16) | BitHelper.OffsetOf(target))); _state.PC = BitHelper.OffsetOf(target);  return 6; }
    private int Op_JML_AbsoluteLong()      { uint source = _state.FullPC - 1; uint a = _addr.AbsoluteLong(); TraceControlFlow("JML", source, a); _state.PBR = BitHelper.BankOf(a); _state.PC = BitHelper.OffsetOf(a); return 4; }
    private int Op_JML_AbsoluteIndirectLong() { uint source = _state.FullPC - 1; uint a = _addr.AbsoluteIndirectLong(); TraceControlFlow("JML [abs]", source, a); _state.PBR = BitHelper.BankOf(a); _state.PC = BitHelper.OffsetOf(a); return 6; }

    private int Op_JSR_Absolute()
    {
        uint source = _state.FullPC - 1;
        ushort target = FetchWord();
        TraceControlFlow("JSR", source, (uint)((_state.PBR << 16) | target));
        PushWord((ushort)(_state.PC - 1));
        _state.PC = target;
        return 6;
    }
    private int Op_JSR_AbsoluteIndirectX()
    {
        uint source = _state.FullPC - 1;
        uint target = _addr.AbsoluteIndirectX();
        TraceControlFlow("JSR (abs,X)", source, (uint)((_state.PBR << 16) | BitHelper.OffsetOf(target)));
        PushWord((ushort)(_state.PC - 1));
        _state.PC = BitHelper.OffsetOf(target);
        return 8;
    }
    private int Op_JSL_AbsoluteLong()
    {
        uint source = _state.FullPC - 1;
        // Long operands are encoded little-endian as: low, high, bank.
        // Reading bank first jumps into garbage for real ROM startup code.
        byte lo   = _addr.FetchByte();
        byte hi   = _addr.FetchByte();
        byte bank = _addr.FetchByte();
        ushort off = BitHelper.MakeWord(lo, hi);
        uint target = (uint)(off | (bank << 16));
        TraceControlFlow("JSL", source, target);
        Push(_state.PBR);
        PushWord((ushort)(_state.PC - 1));
        _state.PBR = bank;
        _state.PC  = off;
        return 8;
    }

    private int Op_RTS()
    {
        uint source = _state.FullPC - 1;
        _state.PC = (ushort)(PopWord() + 1);
        TraceControlFlow("RTS", source, (uint)((_state.PBR << 16) | _state.PC));
        return 6;
    }
    private int Op_RTL()
    {
        uint source = _state.FullPC - 1;
        ushort pc  = PopWord();
        byte   bank = Pop();
        _state.PC  = (ushort)(pc + 1);
        _state.PBR = bank;
        TraceControlFlow("RTL", source, _state.FullPC);
        return 6;
    }
    private int Op_RTI()
    {
        uint source = _state.FullPC - 1;
        _state.P = Pop();
        _state.PC = PopWord();
        if (!_state.EmulationMode) _state.PBR = Pop();
        TraceControlFlow("RTI", source, _state.FullPC);
        return _state.EmulationMode ? 6 : 7;
    }

    // ── Flag ops ──────────────────────────────────────────────────────────────

    private int Op_REP() // REset Processor bits
    {
        byte mask = _addr.FetchByte();
        _state.P &= (byte)~mask;
        return 3;
    }
    private int Op_SEP() // SEt Processor bits
    {
        byte mask = _addr.FetchByte();
        _state.P |= mask;
        // In native mode, setting X=1 truncates X and Y to 8 bits
        if (_state.FlagX) { _state.X &= 0x00FF; _state.Y &= 0x00FF; }
        return 3;
    }

    // ── Software interrupts ───────────────────────────────────────────────────

    private int Op_BRK()
    {
        _addr.FetchByte(); // Padding byte (signature)
        PushPC();
        Push((byte)(_state.P | 0x10)); // Push P with B flag set
        _state.FlagI = true;
        _state.FlagD = false;
        ushort vector = _state.EmulationMode ? (ushort)0xFFFE : (ushort)0xFFE6;
        _state.PC = _bus.ReadWord(vector);
        _state.PBR = 0;
        return 8;
    }
    private int Op_COP()
    {
        _addr.FetchByte();
        PushPC();
        PushP();
        _state.FlagI = true;
        _state.FlagD = false;
        ushort vector = _state.EmulationMode ? (ushort)0xFFF4 : (ushort)0xFFE4;
        _state.PC = _bus.ReadWord(vector);
        _state.PBR = 0;
        return 8;
    }
    private int Op_WDM() { _addr.FetchByte(); return 2; } // Reserved/NOP-like

    private int Op_TYX()
    {
        _state.X = _state.Is8BitIndex ? (byte)_state.Y : _state.Y;
        if (_state.Is8BitIndex) _state.SetNZ8((byte)_state.X);
        else _state.SetNZ16(_state.X);
        return 2;
    }

    private int Op_TXY()
    {
        _state.Y = _state.Is8BitIndex ? (byte)_state.X : _state.X;
        if (_state.Is8BitIndex) _state.SetNZ8((byte)_state.Y);
        else _state.SetNZ16(_state.Y);
        return 2;
    }

    // ── Block Move ────────────────────────────────────────────────────────────

    private int Op_MVN()
    {
        byte destBank = _addr.FetchByte();
        byte srcBank  = _addr.FetchByte();
        _state.DBR = destBank;
        if (_state.C != 0xFFFF)
        {
            uint src  = BitHelper.MakeAddress(srcBank,  _state.X);
            uint dest = BitHelper.MakeAddress(destBank, _state.Y);
            WriteByte(dest, ReadByte(src));
            _state.X++;
            _state.Y++;
            _state.C--;
            _state.PC -= 3; // Re-execute MVN until C == 0xFFFF
        }
        return 7;
    }
    private int Op_MVP()
    {
        byte destBank = _addr.FetchByte();
        byte srcBank  = _addr.FetchByte();
        _state.DBR = destBank;
        if (_state.C != 0xFFFF)
        {
            uint src  = BitHelper.MakeAddress(srcBank,  _state.X);
            uint dest = BitHelper.MakeAddress(destBank, _state.Y);
            WriteByte(dest, ReadByte(src));
            _state.X--;
            _state.Y--;
            _state.C--;
            _state.PC -= 3;
        }
        return 7;
    }

    // ── Opcode name table (for disassembly) ──────────────────────────────────

    private ushort FetchWord() => _addr.FetchWord();

    private static readonly Dictionary<byte, string> OpcodeNames = new()
    {
        [0x00] = "BRK", [0x01] = "ORA (dp,X)", [0x02] = "COP #",   [0x03] = "ORA sr,S",
        [0x04] = "TSB dp", [0x05] = "ORA dp",  [0x06] = "ASL dp",  [0x07] = "ORA [dp]",
        [0x08] = "PHP",    [0x09] = "ORA #",    [0x0A] = "ASL A",   [0x0B] = "PHD",
        [0x0C] = "TSB abs",[0x0D] = "ORA abs",  [0x0E] = "ASL abs", [0x0F] = "ORA long",
        [0x10] = "BPL r",  [0x11] = "ORA (dp),Y",[0x12]= "ORA (dp)",[0x13] = "ORA (sr,S),Y",
        [0x14] = "TRB dp", [0x15] = "ORA dp,X", [0x16] = "ASL dp,X",[0x17] = "ORA [dp],Y",
        [0x18] = "CLC",    [0x19] = "ORA abs,Y",[0x1A] = "INC A",   [0x1B] = "TCS",
        [0x1C] = "TRB abs",[0x1D] = "ORA abs,X",[0x1E] = "ASL abs,X",[0x1F]= "ORA long,X",
        [0x20] = "JSR abs",[0x21] = "AND (dp,X)",[0x22]= "JSL long",[0x23] = "AND sr,S",
        [0x24] = "BIT dp", [0x25] = "AND dp",  [0x26] = "ROL dp",  [0x27] = "AND [dp]",
        [0x28] = "PLP",    [0x29] = "AND #",    [0x2A] = "ROL A",   [0x2B] = "PLD",
        [0x2C] = "BIT abs",[0x2D] = "AND abs",  [0x2E] = "ROL abs", [0x2F] = "AND long",
        [0x30] = "BMI r",  [0x31] = "AND (dp),Y",[0x32]= "AND (dp)",[0x33] = "AND (sr,S),Y",
        [0x34] = "BIT dp,X",[0x35]= "AND dp,X", [0x36] = "ROL dp,X",[0x37] = "AND [dp],Y",
        [0x38] = "SEC",    [0x39] = "AND abs,Y",[0x3A] = "DEC A",   [0x3B] = "TSC",
        [0x3C] = "BIT abs,X",[0x3D]="AND abs,X",[0x3E]= "ROL abs,X",[0x3F]= "AND long,X",
        [0x40] = "RTI",    [0x41] = "EOR (dp,X)",[0x42]= "WDM",     [0x43] = "EOR sr,S",
        [0x44] = "MVP",    [0x45] = "EOR dp",  [0x46] = "LSR dp",  [0x47] = "EOR [dp]",
        [0x48] = "PHA",    [0x49] = "EOR #",    [0x4A] = "LSR A",   [0x4B] = "PHK",
        [0x4C] = "JMP abs",[0x4D] = "EOR abs",  [0x4E] = "LSR abs", [0x4F] = "EOR long",
        [0x50] = "BVC r",  [0x51] = "EOR (dp),Y",[0x52]= "EOR (dp)",[0x53] = "EOR (sr,S),Y",
        [0x54] = "MVN",    [0x55] = "EOR dp,X", [0x56] = "LSR dp,X",[0x57] = "EOR [dp],Y",
        [0x58] = "CLI",    [0x59] = "EOR abs,Y",[0x5A] = "PHY",     [0x5B] = "TCD",
        [0x5C] = "JML long",[0x5D]= "EOR abs,X",[0x5E]= "LSR abs,X",[0x5F]= "EOR long,X",
        [0x60] = "RTS",    [0x61] = "ADC (dp,X)",[0x62]= "PER rl",  [0x63] = "ADC sr,S",
        [0x64] = "STZ dp", [0x65] = "ADC dp",  [0x66] = "ROR dp",  [0x67] = "ADC [dp]",
        [0x68] = "PLA",    [0x69] = "ADC #",    [0x6A] = "ROR A",   [0x6B] = "RTL",
        [0x6C] = "JMP (abs)",[0x6D]="ADC abs",  [0x6E] = "ROR abs", [0x6F] = "ADC long",
        [0x70] = "BVS r",  [0x71] = "ADC (dp),Y",[0x72]= "ADC (dp)",[0x73] = "ADC (sr,S),Y",
        [0x74] = "STZ dp,X",[0x75]= "ADC dp,X", [0x76] = "ROR dp,X",[0x77] = "ADC [dp],Y",
        [0x78] = "SEI",    [0x79] = "ADC abs,Y",[0x7A] = "PLY",     [0x7B] = "TDC",
        [0x7C] = "JMP (abs,X)",[0x7D]="ADC abs,X",[0x7E]="ROR abs,X",[0x7F]="ADC long,X",
        [0x80] = "BRA r",  [0x81] = "STA (dp,X)",[0x82]= "BRL rl",  [0x83] = "STA sr,S",
        [0x84] = "STY dp", [0x85] = "STA dp",  [0x86] = "STX dp",  [0x87] = "STA [dp]",
        [0x88] = "DEY",    [0x89] = "BIT #",    [0x8A] = "TXA",     [0x8B] = "PHB",
        [0x8C] = "STY abs",[0x8D] = "STA abs",  [0x8E] = "STX abs", [0x8F] = "STA long",
        [0x90] = "BCC r",  [0x91] = "STA (dp),Y",[0x92]= "STA (dp)",[0x93] = "STA (sr,S),Y",
        [0x94] = "STY dp,X",[0x95]= "STA dp,X", [0x96] = "STX dp,Y",[0x97] = "STA [dp],Y",
        [0x98] = "TYA",    [0x99] = "STA abs,Y",[0x9A] = "TXS",     [0x9B] = "TXY",
        [0x9C] = "STZ abs",[0x9D] = "STA abs,X",[0x9E] = "STZ abs,X",[0x9F]= "STA long,X",
        [0xA0] = "LDY #",  [0xA1] = "LDA (dp,X)",[0xA2]= "LDX #",  [0xA3] = "LDA sr,S",
        [0xA4] = "LDY dp", [0xA5] = "LDA dp",  [0xA6] = "LDX dp",  [0xA7] = "LDA [dp]",
        [0xA8] = "TAY",    [0xA9] = "LDA #",    [0xAA] = "TAX",     [0xAB] = "PLB",
        [0xAC] = "LDY abs",[0xAD] = "LDA abs",  [0xAE] = "LDX abs", [0xAF] = "LDA long",
        [0xB0] = "BCS r",  [0xB1] = "LDA (dp),Y",[0xB2]= "LDA (dp)",[0xB3] = "LDA (sr,S),Y",
        [0xB4] = "LDY dp,X",[0xB5]= "LDA dp,X", [0xB6] = "LDX dp,Y",[0xB7] = "LDA [dp],Y",
        [0xB8] = "CLV",    [0xB9] = "LDA abs,Y",[0xBA] = "TSX",     [0xBB] = "TYX",
        [0xBC] = "LDY abs,X",[0xBD]="LDA abs,X",[0xBE]= "LDX abs,Y",[0xBF]= "LDA long,X",
        [0xC0] = "CPY #",  [0xC1] = "CMP (dp,X)",[0xC2]= "REP #",  [0xC3] = "CMP sr,S",
        [0xC4] = "CPY dp", [0xC5] = "CMP dp",  [0xC6] = "DEC dp",  [0xC7] = "CMP [dp]",
        [0xC8] = "INY",    [0xC9] = "CMP #",    [0xCA] = "DEX",     [0xCB] = "WAI",
        [0xCC] = "CPY abs",[0xCD] = "CMP abs",  [0xCE] = "DEC abs", [0xCF] = "CMP long",
        [0xD0] = "BNE r",  [0xD1] = "CMP (dp),Y",[0xD2]= "CMP (dp)",[0xD3] = "CMP (sr,S),Y",
        [0xD4] = "PEI dp", [0xD5] = "CMP dp,X", [0xD6] = "DEC dp,X",[0xD7] = "CMP [dp],Y",
        [0xD8] = "CLD",    [0xD9] = "CMP abs,Y",[0xDA] = "PHX",     [0xDB] = "STP",
        [0xDC] = "JML [abs]",[0xDD]="CMP abs,X",[0xDE]= "DEC abs,X",[0xDF]= "CMP long,X",
        [0xE0] = "CPX #",  [0xE1] = "SBC (dp,X)",[0xE2]= "SEP #",  [0xE3] = "SBC sr,S",
        [0xE4] = "CPX dp", [0xE5] = "SBC dp",  [0xE6] = "INC dp",  [0xE7] = "SBC [dp]",
        [0xE8] = "INX",    [0xE9] = "SBC #",    [0xEA] = "NOP",     [0xEB] = "XBA",
        [0xEC] = "CPX abs",[0xED] = "SBC abs",  [0xEE] = "INC abs", [0xEF] = "SBC long",
        [0xF0] = "BEQ r",  [0xF1] = "SBC (dp),Y",[0xF2]= "SBC (dp)",[0xF3] = "SBC (sr,S),Y",
        [0xF4] = "PEA abs",[0xF5] = "SBC dp,X", [0xF6] = "INC dp,X",[0xF7] = "SBC [dp],Y",
        [0xF8] = "SED",    [0xF9] = "SBC abs,Y",[0xFA] = "PLX",     [0xFB] = "XCE",
        [0xFC] = "JSR (abs,X)",[0xFD]="SBC abs,X",[0xFE]="INC abs,X",[0xFF]= "SBC long,X"
    };
}
