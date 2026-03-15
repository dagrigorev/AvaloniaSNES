using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Core.Utilities;

namespace SnesEmulator.Emulation.Memory;

/// <summary>
/// The SNES memory bus.
///
/// Key fixes in this version:
///   - NMITIMEN ($4200) is tracked inside the bus as the single source of NMI truth
///   - RDNMI ($4210) correctly returns a latched VBlank flag and clears it on read
///   - HVBJOY ($4212) correctly reflects VBlank/HBlank state
///   - WRIO ($4201) and WRMPYA/B ($4202/$4203) stubs added
/// </summary>
public sealed class MemoryBus : IMemoryBus, IEmulatorComponent
{
    private readonly ILogger<MemoryBus> _logger;
    private readonly WorkRam _wram;
    private readonly IInputManager _inputManager;

    private IPpu? _ppu;
    private IApu? _apu;
    private RomData? _rom;
    private byte[]? _sram;

    private byte _controller1Data;
    private byte _controller2Data;
    private byte _controllerLatch;

    private readonly byte[,] _dmaRegisters = new byte[8, 12];

    // CPU internal registers
    private byte _hvbjoy;     // $4212 — H/V blank + joypad busy flags
    private byte _memsel;     // $420D — ROM access speed
    private byte _nmitimen;   // $4200 — NMI/IRQ/joypad enable
    private bool _vblankLevel;
    private bool _prevVblankLevel;
    private bool _rdnmiLatched;
    private bool _nmiPending;
    private byte _wrio;       // $4201 — Joypad programmable I/O port
    private byte _wrmpya;     // $4202 — Multiplicand
    private byte _wrmpyb;     // $4203 — Multiplier
    private ushort _rdmpy;    // $4216/17 — Multiply result / division remainder
    private ushort _rddiv;    // $4214/15 — Divide result
    private ushort _wrdiva;   // $4204/05 — latched dividend for division

    private byte _lastCpuPbr;
    private ushort _lastCpuPc;
    private int _traceFrame = -1;
    private int _traceScanline = -1;
    private int _ioTraceCount;
    private int _vblankTraceCount;
    private int _nmiReadTraceCount;

    public string Name => "MemoryBus";

    public MemoryBus(WorkRam wram, IInputManager inputManager, ILogger<MemoryBus> logger)
    {
        _wram = wram;
        _inputManager = inputManager;
        _logger = logger;
    }

    public void AttachDevices(IPpu ppu, IApu apu)
    {
        _ppu = ppu;
        _apu = apu;
    }

    public void LoadRom(RomData rom)
    {
        _rom  = rom;
        _sram = rom.Header.SramSizeBytes > 0 ? new byte[rom.Header.SramSizeBytes] : null;
        _logger.LogInformation("ROM mounted on bus: {Mode}, {Size} KB, SRAM: {SramSize} bytes",
            rom.MappingMode, rom.SizeKilobytes, rom.Header.SramSizeBytes);
    }

    public void Reset()
    {
        _wram.Reset();
        _controller1Data = 0;
        _controller2Data = 0;
        _controllerLatch = 0;
        _memsel          = 0;
        _nmitimen        = 0;
        _vblankLevel     = false;
        _prevVblankLevel = false;
        _rdnmiLatched    = false;
        _nmiPending      = false;
        _hvbjoy          = 0;
        _wrdiva          = 0;
        _rddiv           = 0;
        _rdmpy           = 0;
        _lastCpuPbr      = 0;
        _lastCpuPc       = 0;
        _traceFrame      = -1;
        _traceScanline   = -1;
        _ioTraceCount    = 0;
        _vblankTraceCount = 0;
        _nmiReadTraceCount = 0;
        Array.Clear(_dmaRegisters);
    }

    // ── VBlank notification from PPU (called each scanline) ──────────────────

    /// <summary>
    /// Called by EmulationLoop once per scanline to update $4212 (HVBJOY).
    /// The loop owns the timing; the bus just stores the value.
    /// </summary>
    public void SetHvBjoy(bool inVBlank, bool inHBlank)
    {
        _hvbjoy = (byte)
            ((inVBlank ? 0x80 : 0x00) |
             (inHBlank ? 0x40 : 0x00));
    }

    public void SetCpuTraceContext(byte pbr, ushort pc)
    {
        _lastCpuPbr = pbr;
        _lastCpuPc = pc;
    }

    public void SetVBlankState(bool isVBlank, int frameNumber = -1, int scanline = -1)
    {
        _traceFrame = frameNumber;
        _traceScanline = scanline;
        _vblankLevel = isVBlank;

        if (_prevVblankLevel != isVBlank && _vblankTraceCount < 512)
        {
            _logger.LogDebug(
                "VBlank level -> {State} at frame {Frame}, scanline {Scanline}, NMITIMEN=${Nmitimen:X2}, pending={Pending}",
                isVBlank ? 1 : 0, _traceFrame, _traceScanline, _nmitimen, _nmiPending ? 1 : 0);
            _vblankTraceCount++;
        }

        bool enteredVBlank = !_prevVblankLevel && isVBlank;
        if (enteredVBlank)
        {
            _rdnmiLatched = true;
            if ((_nmitimen & 0x80) != 0)
                _nmiPending = true;

            if (_vblankTraceCount < 512)
            {
                _logger.LogDebug(
                    "VBlank edge at frame {Frame}, scanline {Scanline}, RDNMI latched=1, NMI pending={Pending}",
                    _traceFrame, _traceScanline, _nmiPending ? 1 : 0);
                _vblankTraceCount++;
            }
        }

        _prevVblankLevel = isVBlank;
    }

    public bool ConsumeNmi()
    {
        if (!_nmiPending)
            return false;

        _nmiPending = false;

        if (_vblankTraceCount < 512)
        {
            _logger.LogDebug(
                "ConsumeNmi at frame {Frame}, scanline {Scanline}",
                _traceFrame, _traceScanline);
            _vblankTraceCount++;
        }

        return true;
    }

    public void ClearNmiFlag() => _rdnmiLatched = false;

    // ── IMemoryBus ────────────────────────────────────────────────────────────

    public byte Read(uint address)
    {
        byte bank = BitHelper.BankOf(address);
        ushort offset = BitHelper.OffsetOf(address);

        if (bank <= 0x3F || (bank >= 0x80 && bank <= 0xBF))
        {
            return offset switch
            {
                <= 0x1FFF               => _wram.ReadDirect(offset),
                >= 0x2100 and <= 0x213F => ReadPpuRegister(offset),
                >= 0x2140 and <= 0x2143 => ReadApuPort(offset),
                0x2180                  => _wram.ReadWmData(),
                >= 0x4000 and <= 0x41FF => ReadController(offset),
                >= 0x4200 and <= 0x421F => ReadCpuRegister(offset),
                >= 0x4300 and <= 0x43FF => ReadDmaRegister(offset),
                >= 0x8000               => ReadRom(bank, offset),
                _                       => TryReadSram(bank, offset)
            };
        }

        if (bank == 0x7E || bank == 0x7F)
            return _wram.ReadDirect(((bank & 1) << 16) | offset);

        if ((bank >= 0x40 && bank <= 0x7D) || bank >= 0xC0)
            return ReadRom(bank, offset);

        return TryReadSram(bank, offset);
    }

    public void Write(uint address, byte value)
    {
        byte bank = BitHelper.BankOf(address);
        ushort offset = BitHelper.OffsetOf(address);

        if (bank <= 0x3F || (bank >= 0x80 && bank <= 0xBF))
        {
            switch (offset)
            {
                case <= 0x1FFF:               _wram.WriteDirect(offset, value); break;
                case >= 0x2100 and <= 0x213F: WritePpuRegister(offset, value); break;
                case >= 0x2140 and <= 0x2143: WriteApuPort(offset, value); break;
                case 0x2180:                  _wram.WriteWmData(value); break;
                case 0x2181:                  _wram.WriteWmAddressLow(value); break;
                case 0x2182:                  _wram.WriteWmAddressMid(value); break;
                case 0x2183:                  _wram.WriteWmAddressHigh(value); break;
                case >= 0x4000 and <= 0x41FF: WriteController(offset, value); break;
                case >= 0x4200 and <= 0x421F: WriteCpuRegister(offset, value); break;
                case >= 0x4300 and <= 0x43FF: WriteDmaRegister(offset, value); break;
                default:                      TryWriteSram(bank, offset, value); break;
            }
            return;
        }

        if (bank == 0x7E || bank == 0x7F)
        {
            if (_ioTraceCount < 512)
            {
                _logger.LogDebug(
                    "WRAM long write ${Addr:X6} = ${Val:X2} from ${Pbr:X2}:{Pc:X4}",
                    address & 0xFFFFFF, value, _lastCpuPbr, _lastCpuPc);
                _ioTraceCount++;
            }
            _wram.WriteDirect(((bank & 1) << 16) | offset, value);
            return;
        }

        TryWriteSram(bank, offset, value);
    }

    public ushort ReadWord(uint address)
        => BitHelper.MakeWord(Read(address), Read(address + 1));

    public void WriteWord(uint address, ushort value)
    {
        Write(address,     BitHelper.LowByte(value));
        Write(address + 1, BitHelper.HighByte(value));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private byte ReadPpuRegister(ushort offset)
    {
        byte value = _ppu?.ReadRegister((byte)(offset - 0x2100)) ?? OpenBus();

        if (offset == 0x213F && _nmiReadTraceCount < 256)
        {
            _logger.LogDebug(
                "CPU read STAT78 from ${Pbr:X2}:{Pc:X4} -> ${Val:X2} (frame {Frame}, scanline {Scanline})",
                _lastCpuPbr, _lastCpuPc, value, _traceFrame, _traceScanline);
            _nmiReadTraceCount++;
        }

        return value;
    }

    private void WritePpuRegister(ushort offset, byte value)
    {
        if (_ioTraceCount < 512 && (offset == 0x2100 || offset == 0x212C || (offset >= 0x2115 && offset <= 0x2119)))
        {
            _logger.LogDebug(
                "CPU write ${Reg:X4} from ${Pbr:X2}:{Pc:X4} value=${Val:X2} (frame {Frame}, scanline {Scanline})",
                offset, _lastCpuPbr, _lastCpuPc, value, _traceFrame, _traceScanline);
            _ioTraceCount++;
        }

        _ppu?.WriteRegister((byte)(offset - 0x2100), value);
    }

    private byte ReadApuPort(ushort offset)
        => _apu?.ReadPort((byte)(offset - 0x2140)) ?? OpenBus();

    private void WriteApuPort(ushort offset, byte value)
        => _apu?.WritePort((byte)(offset - 0x2140), value);

    private byte ReadController(ushort offset) => offset switch
    {
        0x4016 => (byte)((ReadControllerSerial(1) ? 1 : 0) | 0x1C),
        0x4017 => (byte)((ReadControllerSerial(2) ? 1 : 0) | 0x1C), // bits 2-4 are typically high on reads
        _      => OpenBus()
    };

    private bool ReadControllerSerial(int port)
    {
        var controller = _inputManager.GetController(port);
        if (_controllerLatch != 0)
            controller.Strobe();

        bool bit = controller.ReadSerial();
        if (port == 1)
            _controller1Data = (byte)(bit ? 1 : 0);
        else
            _controller2Data = (byte)(bit ? 1 : 0);

        return bit;
    }

    private void WriteController(ushort offset, byte value)
    {
        if (offset != 0x4016)
            return;

        byte newLatch = (byte)(value & 1);
        if (_controllerLatch == 1 && newLatch == 0)
        {
            _inputManager.GetController(1).Strobe();
            _inputManager.GetController(2).Strobe();
        }

        _controllerLatch = newLatch;
    }

    private byte ReadCpuRegister(ushort offset)
    {
        switch (offset)
        {
            case 0x4210:
            {
                byte val = (byte)((_rdnmiLatched ? 0x80 : 0x00) | 0x02);
                if (_nmiReadTraceCount < 256)
                {
                    _logger.LogDebug(
                        "CPU read RDNMI from ${Pbr:X2}:{Pc:X4} -> ${Val:X2} (frame {Frame}, scanline {Scanline}, vblank={VBlank}, pending={Pending})",
                        _lastCpuPbr, _lastCpuPc, val, _traceFrame, _traceScanline, _vblankLevel ? 1 : 0, _nmiPending ? 1 : 0);
                    _nmiReadTraceCount++;
                }
                _rdnmiLatched = false;
                return val;
            }
            case 0x4211: return 0x00;   // TIMEUP: IRQ flag (not implemented)
            case 0x4212:
            {
                if (_nmiReadTraceCount < 256)
                {
                    _logger.LogDebug(
                        "CPU read HVBJOY from ${Pbr:X2}:{Pc:X4} -> ${Val:X2} (frame {Frame}, scanline {Scanline})",
                        _lastCpuPbr, _lastCpuPc, _hvbjoy, _traceFrame, _traceScanline);
                    _nmiReadTraceCount++;
                }
                return _hvbjoy;
            }
            case 0x4213: return _wrio;
            case 0x4214: return BitHelper.LowByte(_rddiv);
            case 0x4215: return BitHelper.HighByte(_rddiv);
            case 0x4216: return BitHelper.LowByte(_rdmpy);
            case 0x4217: return BitHelper.HighByte(_rdmpy);
            case 0x4218: return BitHelper.LowByte(_inputManager.GetController(1).ButtonState);
            case 0x4219: return BitHelper.HighByte(_inputManager.GetController(1).ButtonState);
            case 0x421A: return BitHelper.LowByte(_inputManager.GetController(2).ButtonState);
            case 0x421B: return BitHelper.HighByte(_inputManager.GetController(2).ButtonState);
            default:     return OpenBus();
        }
    }

    private void WriteCpuRegister(ushort offset, byte value)
    {
        switch (offset)
        {
            case 0x4200: // NMITIMEN — NMI/IRQ/auto-joypad enable
                _nmitimen = value;
                if (_ioTraceCount < 512)
                {
                    _logger.LogDebug(
                        "CPU write NMITIMEN from ${Pbr:X2}:{Pc:X4} value=${Val:X2} (frame {Frame}, scanline {Scanline})",
                        _lastCpuPbr, _lastCpuPc, value, _traceFrame, _traceScanline);
                    _ioTraceCount++;
                }
                break;
            case 0x4201: _wrio   = value; break;
            case 0x4202: _wrmpya = value; break;
            case 0x4203: // WRMPYB — writing triggers multiply
                _wrmpyb = value;
                _rdmpy  = (ushort)(_wrmpya * _wrmpyb);
                break;
            case 0x4204: _wrdiva = (ushort)((_wrdiva & 0xFF00) | value); break;
            case 0x4205: _wrdiva = (ushort)((_wrdiva & 0x00FF) | (value << 8)); break;
            case 0x4206: // WRDIVB — writing triggers divide
                if (value == 0)
                {
                    _rddiv = 0xFFFF;
                    _rdmpy = _wrdiva;
                }
                else
                {
                    ushort dividend = _wrdiva;
                    _rddiv = (ushort)(dividend / value);
                    _rdmpy = (ushort)(dividend % value);
                }
                break;
            case 0x420B:
                if (_ioTraceCount < 512)
                {
                    _logger.LogDebug(
                        "CPU write MDMAEN from ${Pbr:X2}:{Pc:X4} value=${Val:X2} (frame {Frame}, scanline {Scanline})",
                        _lastCpuPbr, _lastCpuPc, value, _traceFrame, _traceScanline);
                    _ioTraceCount++;
                }
                ExecuteDma(value);
                break;
            case 0x420C: /* HDMAEN stub */           break;
            case 0x420D: _memsel = value;            break;
        }
    }

    private byte ReadDmaRegister(ushort offset)
    {
        int ch  = (offset >> 4) & 7;
        int reg = offset & 0x0F;
        return _dmaRegisters[ch, Math.Min(reg, 11)];
    }

    private void WriteDmaRegister(ushort offset, byte value)
    {
        int ch  = (offset >> 4) & 7;
        int reg = offset & 0x0F;
        if (reg < 12) _dmaRegisters[ch, reg] = value;
    }

    private byte ReadRom(byte bank, ushort offset)
    {
        if (_rom is null) return OpenBus();
        int romOffset = _rom.MappingMode == RomMappingMode.LoRom
            ? MapLoRom(bank, offset)
            : MapHiRom(bank, offset);
        return romOffset >= 0 && romOffset < _rom.Data.Length
            ? _rom.Data[romOffset]
            : OpenBus();
    }

    private static int MapLoRom(byte bank, ushort offset)
        => offset < 0x8000 ? -1 : ((bank & 0x7F) * 0x8000) + (offset - 0x8000);

    private static int MapHiRom(byte bank, ushort offset)
        => ((bank & 0x3F) * 0x10000) + offset;

    private byte TryReadSram(byte bank, ushort offset)
    {
        if (_sram is null)
            return OpenBus();

        int off = ResolveSramOffset(bank, offset);
        return off >= 0 && off < _sram.Length ? _sram[off] : OpenBus();
    }

    private void TryWriteSram(byte bank, ushort offset, byte value)
    {
        if (_sram is null) return;
        int off = ResolveSramOffset(bank, offset);
        if (off >= 0 && off < _sram.Length) _sram[off] = value;
    }

    private int ResolveSramOffset(byte bank, ushort offset)
    {
        if (_rom?.MappingMode == RomMappingMode.LoRom && bank >= 0x70 && bank <= 0x7D)
            return ((bank - 0x70) * 0x8000) + (offset & 0x7FFF);
        if (_rom?.MappingMode == RomMappingMode.HiRom && (
                ((bank >= 0x20 && bank <= 0x3F) || (bank >= 0xA0 && bank <= 0xBF))
                && offset is >= 0x6000 and <= 0x7FFF))
            return (((bank & 0x1F)) * 0x2000) + (offset - 0x6000);
        return -1;
    }

    private void ExecuteDma(byte channelBits)
    {
        for (int ch = 0; ch < 8; ch++)
        {
            if ((channelBits & (1 << ch)) == 0) continue;

            byte   control      = _dmaRegisters[ch, 0];
            byte   destReg      = _dmaRegisters[ch, 1];
            uint   srcAddr      = (uint)(_dmaRegisters[ch, 2]
                                | (_dmaRegisters[ch, 3] << 8)
                                | (_dmaRegisters[ch, 4] << 16));
            int    byteCount    = _dmaRegisters[ch, 5] | (_dmaRegisters[ch, 6] << 8);
            if (byteCount == 0) byteCount = 0x10000;

            bool noIncrement = (control & 0x08) != 0;
            bool decrement   = (control & 0x10) != 0;
            int  transferMode = control & 0x07;

            _logger.LogDebug("DMA ch{Ch}: {Count} bytes from {Src:X6} → $21{Reg:X2} mode={Mode}",
                ch, byteCount, srcAddr, destReg, transferMode);

            // Transfer mode determines how destination address increments:
            // 0: write all bytes to destReg            (1-byte)
            // 1: alternate destReg / destReg+1         (2-byte word - most common for VRAM)
            // 2: write all bytes to destReg            (same as 0 effectively)
            // 3: two bytes to destReg, two to destReg+1 (4-byte)
            // 4: four consecutive registers            (rarely used)
            for (int i = 0; i < byteCount; i++)
            {
                byte data = Read(srcAddr);

                byte regOffset = transferMode switch
                {
                    1 => (byte)(i & 1),           // alternate: 0,1,0,1,...
                    3 => (byte)((i >> 1) & 1),    // pairs: 0,0,1,1,0,0,1,1,...
                    4 => (byte)(i & 3),            // sequential: 0,1,2,3,0,1,2,3,...
                    _ => 0                         // modes 0,2: always same register
                };

                Write((uint)(0x2100 | destReg | regOffset), data);

                if (!noIncrement)
                {
                    if (decrement) srcAddr--;
                    else           srcAddr++;
                }
            }
        }
    }

    private static byte OpenBus() => 0xFF;

    public void SaveSram(string path)
    {
        if (_sram is null || _sram.Length == 0) return;
        File.WriteAllBytes(path, _sram);
    }

    public void LoadSram(string path)
    {
        if (_sram is null || !File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);
        Buffer.BlockCopy(data, 0, _sram, 0, Math.Min(data.Length, _sram.Length));
    }
}
