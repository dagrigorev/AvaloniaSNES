using Microsoft.Extensions.Logging;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Core.Utilities;

namespace SnesEmulator.Emulation.Memory;

/// <summary>
/// The SNES memory bus: routes all 24-bit CPU address space reads/writes
/// to the appropriate device or register.
///
/// The SNES uses a complex bank/offset address scheme. This bus handles:
///   - LoROM and HiROM mapping
///   - WRAM mirroring
///   - PPU and APU I/O register routing ($21xx)
///   - DMA registers ($43xx)
///   - SRAM (battery save) where present
///
/// Reference: https://wiki.superfamicom.org/memory-mapping
/// </summary>
public sealed class MemoryBus : IMemoryBus, IEmulatorComponent
{
    private readonly ILogger<MemoryBus> _logger;
    private readonly WorkRam _wram;

    // Devices plugged into the bus (set after construction)
    private IPpu? _ppu;
    private IApu? _apu;
    private RomData? _rom;
    private byte[]? _sram;

    // Auto-joypad / controller state
    private byte _controller1Data;
    private byte _controller2Data;
    private byte _controllerLatch;

    // DMA registers – 8 channels × 12 bytes each
    private readonly byte[,] _dmaRegisters = new byte[8, 12];

    // HVBJOY and STAT78 status registers
    private byte _hvbjoy;   // $4212
    private byte _memsel;   // $420D — ROM access speed

    public string Name => "MemoryBus";

    public MemoryBus(WorkRam wram, ILogger<MemoryBus> logger)
    {
        _wram = wram;
        _logger = logger;
    }

    /// <summary>Attaches hardware devices to the bus.</summary>
    public void AttachDevices(IPpu ppu, IApu apu)
    {
        _ppu = ppu;
        _apu = apu;
    }

    /// <summary>Loads ROM data into the bus (maps ROM address space).</summary>
    public void LoadRom(RomData rom)
    {
        _rom = rom;
        _sram = rom.Header.SramSizeBytes > 0
            ? new byte[rom.Header.SramSizeBytes]
            : null;

        _logger.LogInformation(
            "ROM mounted on bus: {Mode}, {Size} KB, SRAM: {SramSize} bytes",
            rom.MappingMode, rom.SizeKilobytes, rom.Header.SramSizeBytes);
    }

    /// <inheritdoc />
    public void Reset()
    {
        _wram.Reset();
        _controller1Data = 0;
        _controller2Data = 0;
        _controllerLatch = 0;
        _memsel = 0;
        Array.Clear(_dmaRegisters);
    }

    // ── IMemoryBus ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public byte Read(uint address)
    {
        byte bank   = BitHelper.BankOf(address);
        ushort offset = BitHelper.OffsetOf(address);

        // ── System Area: banks $00–$3F and $80–$BF ────────────────────────────
        if ((bank <= 0x3F) || (bank >= 0x80 && bank <= 0xBF))
        {
            return offset switch
            {
                // WRAM mirror: $0000–$1FFF
                <= 0x1FFF => _wram.ReadDirect(offset),

                // PPU registers: $2100–$213F
                >= 0x2100 and <= 0x213F => ReadPpuRegister(offset),

                // APU ports: $2140–$2143
                >= 0x2140 and <= 0x2143 => ReadApuPort(offset),

                // WRAM data port: $2180
                0x2180 => _wram.ReadWmData(),

                // Controller & misc: $4000–$41FF
                >= 0x4000 and <= 0x41FF => ReadController(offset),

                // CPU internal registers: $4200–$420F
                >= 0x4200 and <= 0x420F => ReadCpuRegister(offset),

                // DMA registers: $4300–$43FF
                >= 0x4300 and <= 0x43FF => ReadDmaRegister(offset),

                // ROM area: $8000–$FFFF in LoROM
                >= 0x8000 => ReadRom(bank, offset),

                _ => OpenBus()
            };
        }

        // ── WRAM banks: $7E and $7F ───────────────────────────────────────────
        if (bank == 0x7E || bank == 0x7F)
        {
            int wramOffset = ((bank & 0x01) << 16) | offset;
            return _wram.ReadDirect(wramOffset);
        }

        // ── HiROM banks: $40–$7D and $C0–$FF ─────────────────────────────────
        if ((bank >= 0x40 && bank <= 0x7D) || bank >= 0xC0)
        {
            return ReadRom(bank, offset);
        }

        return OpenBus();
    }

    /// <inheritdoc />
    public void Write(uint address, byte value)
    {
        byte bank   = BitHelper.BankOf(address);
        ushort offset = BitHelper.OffsetOf(address);

        if ((bank <= 0x3F) || (bank >= 0x80 && bank <= 0xBF))
        {
            switch (offset)
            {
                case <= 0x1FFF:
                    _wram.WriteDirect(offset, value);
                    break;
                case >= 0x2100 and <= 0x213F:
                    WritePpuRegister(offset, value);
                    break;
                case >= 0x2140 and <= 0x2143:
                    WriteApuPort(offset, value);
                    break;
                case 0x2180:
                    _wram.WriteWmData(value);
                    break;
                case 0x2181:
                    _wram.WriteWmAddressLow(value);
                    break;
                case 0x2182:
                    _wram.WriteWmAddressMid(value);
                    break;
                case 0x2183:
                    _wram.WriteWmAddressHigh(value);
                    break;
                case >= 0x4000 and <= 0x41FF:
                    WriteController(offset, value);
                    break;
                case >= 0x4200 and <= 0x420F:
                    WriteCpuRegister(offset, value);
                    break;
                case >= 0x4300 and <= 0x43FF:
                    WriteDmaRegister(offset, value);
                    break;
                // Writes to ROM space are ignored (or SRAM)
                default:
                    TryWriteSram(bank, offset, value);
                    break;
            }
            return;
        }

        if (bank == 0x7E || bank == 0x7F)
        {
            int wramOffset = ((bank & 0x01) << 16) | offset;
            _wram.WriteDirect(wramOffset, value);
            return;
        }

        // HiROM banks — SRAM or ignore
        TryWriteSram(bank, offset, value);
    }

    /// <inheritdoc />
    public ushort ReadWord(uint address)
    {
        byte lo = Read(address);
        byte hi = Read(address + 1);
        return BitHelper.MakeWord(lo, hi);
    }

    /// <inheritdoc />
    public void WriteWord(uint address, ushort value)
    {
        Write(address, BitHelper.LowByte(value));
        Write(address + 1, BitHelper.HighByte(value));
    }

    // ── Internal routing helpers ─────────────────────────────────────────────

    private byte ReadPpuRegister(ushort offset)
    {
        if (_ppu is null) return OpenBus();
        return _ppu.ReadRegister((byte)(offset - 0x2100));
    }

    private void WritePpuRegister(ushort offset, byte value)
    {
        _ppu?.WriteRegister((byte)(offset - 0x2100), value);
    }

    private byte ReadApuPort(ushort offset)
    {
        if (_apu is null) return OpenBus();
        return _apu.ReadPort((byte)(offset - 0x2140));
    }

    private void WriteApuPort(ushort offset, byte value)
    {
        _apu?.WritePort((byte)(offset - 0x2140), value);
    }

    private byte ReadController(ushort offset) => offset switch
    {
        0x4016 => (byte)(_controller1Data & 1),
        0x4017 => (byte)(_controller2Data & 1),
        _ => OpenBus()
    };

    private void WriteController(ushort offset, byte value)
    {
        if (offset == 0x4016)
        {
            _controllerLatch = (byte)(value & 1);
            // When latch goes low→high→low, the controllers latch their state
        }
    }

    private byte ReadCpuRegister(ushort offset) => offset switch
    {
        0x4210 => 0x02, // RDNMI: NMI flag (simplified)
        0x4211 => 0x00, // TIMEUP: IRQ flag
        0x4212 => _hvbjoy,
        0x4213 => 0x00, // RDIO
        0x4214 => 0x00, // RDDIVL
        0x4215 => 0x00, // RDDIVH
        0x4216 => 0x00, // RDMPYL
        0x4217 => 0x00, // RDMPYH
        0x4218 => _controller1Data,
        0x4219 => 0x00, // JOY1H
        0x421A => _controller2Data,
        0x421B => 0x00, // JOY2H
        _ => OpenBus()
    };

    private void WriteCpuRegister(ushort offset, byte value)
    {
        switch (offset)
        {
            case 0x420B: ExecuteDma(value); break;
            case 0x420C: /* HDMAEN */ break;
            case 0x420D: _memsel = value; break;
        }
    }

    private byte ReadDmaRegister(ushort offset)
    {
        int channel = (offset >> 4) & 7;
        int reg = offset & 0x0F;
        return _dmaRegisters[channel, Math.Min(reg, 11)];
    }

    private void WriteDmaRegister(ushort offset, byte value)
    {
        int channel = (offset >> 4) & 7;
        int reg = offset & 0x0F;
        if (reg < 12)
            _dmaRegisters[channel, reg] = value;
    }

    private byte ReadRom(byte bank, ushort offset)
    {
        if (_rom is null) return OpenBus();

        int romOffset = _rom.MappingMode == RomMappingMode.LoRom
            ? MapLoRom(bank, offset)
            : MapHiRom(bank, offset);

        if (romOffset < 0 || romOffset >= _rom.Data.Length)
            return OpenBus();

        return _rom.Data[romOffset];
    }

    /// <summary>
    /// Maps a LoROM bank/offset pair to a ROM data offset.
    /// LoROM: each bank has 32 KB of ROM at offset $8000–$FFFF.
    /// ROM address = (bank & 0x7F) * 0x8000 + (offset - 0x8000)
    /// </summary>
    private static int MapLoRom(byte bank, ushort offset)
    {
        if (offset < 0x8000) return -1; // Not ROM in this range
        return ((bank & 0x7F) * 0x8000) + (offset - 0x8000);
    }

    /// <summary>
    /// Maps a HiROM bank/offset pair to a ROM data offset.
    /// HiROM: each bank maps directly — offset 0x0000–0xFFFF = ROM[bank*0x10000 + offset]
    /// </summary>
    private static int MapHiRom(byte bank, ushort offset)
    {
        return ((bank & 0x3F) * 0x10000) + offset;
    }

    private void TryWriteSram(byte bank, ushort offset, byte value)
    {
        if (_sram is null) return;
        int sramOffset = ResolveSramOffset(bank, offset);
        if (sramOffset >= 0 && sramOffset < _sram.Length)
            _sram[sramOffset] = value;
    }

    private int ResolveSramOffset(byte bank, ushort offset)
    {
        // LoROM SRAM: banks $70–$7D, offset $0000–$7FFF
        if (_rom?.MappingMode == RomMappingMode.LoRom && bank >= 0x70 && bank <= 0x7D)
            return ((bank - 0x70) * 0x8000) + (offset & 0x7FFF);

        // HiROM SRAM: banks $20–$3F, offset $6000–$7FFF
        if (_rom?.MappingMode == RomMappingMode.HiRom && bank >= 0x20 && bank <= 0x3F
            && offset is >= 0x6000 and <= 0x7FFF)
            return ((bank - 0x20) * 0x2000) + (offset - 0x6000);

        return -1;
    }

    private void ExecuteDma(byte channelBits)
    {
        // Simplified DMA: iterate enabled channels and bulk-copy memory
        for (int ch = 0; ch < 8; ch++)
        {
            if ((channelBits & (1 << ch)) == 0) continue;

            byte control = _dmaRegisters[ch, 0];
            byte destReg = _dmaRegisters[ch, 1];
            uint srcAddr = (uint)(_dmaRegisters[ch, 2]
                          | (_dmaRegisters[ch, 3] << 8)
                          | (_dmaRegisters[ch, 4] << 16));
            int byteCount = _dmaRegisters[ch, 5] | (_dmaRegisters[ch, 6] << 8);
            if (byteCount == 0) byteCount = 0x10000;

            bool increment = (control & 0x08) == 0;
            bool decrement = (control & 0x10) != 0;

            _logger.LogDebug(
                "DMA ch{Ch}: {Count} bytes from {Src:X6} → $21{Reg:X2}",
                ch, byteCount, srcAddr, destReg);

            for (int i = 0; i < byteCount; i++)
            {
                byte data = Read(srcAddr);
                Write((uint)(0x2100 | destReg), data);

                if (increment)       srcAddr++;
                else if (decrement)  srcAddr--;
            }
        }
    }

    private static byte OpenBus() => 0xFF;

    /// <summary>Updates HVBJOY status bits (called by PPU).</summary>
    public void SetHvBjoy(bool inVBlank, bool inHBlank)
    {
        _hvbjoy = (byte)((inVBlank ? 0x80 : 0)
                       | (inHBlank ? 0x40 : 0));
    }

    /// <summary>Saves SRAM to a file (battery save).</summary>
    public void SaveSram(string path)
    {
        if (_sram is null || _sram.Length == 0) return;
        File.WriteAllBytes(path, _sram);
    }

    /// <summary>Loads SRAM from a file.</summary>
    public void LoadSram(string path)
    {
        if (_sram is null) return;
        if (!File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);
        Buffer.BlockCopy(data, 0, _sram, 0, Math.Min(data.Length, _sram.Length));
    }
}
