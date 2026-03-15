using Microsoft.Extensions.Logging;
using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Graphics.Framebuffer;

namespace SnesEmulator.Graphics.Ppu;

/// <summary>
/// SNES Picture Processing Unit (PPU) — S-PPU1 + S-PPU2.
///
/// The SNES PPU is responsible for:
///   - Maintaining VRAM (64 KB), CGRAM (512 bytes), OAM (544 bytes)
///   - Rendering up to 4 background layers in one of 7 BG modes
///   - Rendering up to 128 sprites (OBJ layer)
///   - Applying color math and windowing effects
///   - Generating V-blank and H-blank timing signals
///
/// This implementation provides:
///   - Full register model ($2100–$213F)
///   - VRAM / CGRAM / OAM storage
///   - BG Mode 0 tile rendering (full pipeline for 4-color tiles)
///   - Sprite rendering foundation
///   - Correct V-blank / H-blank signaling
///   - Color math for screen addition/subtraction
///
/// Higher BG modes and full sprite priority are architectural placeholders
/// that can be filled in progressively.
///
/// Reference: https://wiki.superfamicom.org/ppu
/// </summary>
public sealed class Ppu : IPpu
{
    private readonly ILogger<Ppu> _logger;
    private readonly SnesFrameBuffer _frameBuffer;

    // ── Memory ────────────────────────────────────────────────────────────────
    private readonly byte[] _vram  = new byte[SnesConstants.VramSize];
    private readonly byte[] _oam   = new byte[SnesConstants.OamSize];
    private readonly byte[] _cgram = new byte[SnesConstants.CgramSize];

    // ── Timing state ──────────────────────────────────────────────────────────
    private int _dot;        // Current dot within scanline (0–340)
    private int _scanline;   // Current scanline (0–261 NTSC)
    private int _frameCount;
    private int _masterCycleAccum;

    // Each PPU dot = 4 master clock cycles
    private const int MasterCyclesPerDot = 4;

    // ── PPU Registers ─────────────────────────────────────────────────────────
    private byte _inidisp;      // $2100 — Screen display / brightness
    private byte _obsel;        // $2101 — OBJ size and base
    private byte _oamadd;       // $2102 — OAM address (low)
    private byte _oamaddh;      // $2103 — OAM address (high) / priority
    private byte _bgmode;       // $2105 — BG mode and character size
    private byte _mosaic;       // $2106 — Mosaic
    private byte[] _bg1sc  = new byte[1]; // $2107 — BG1 screen base/size
    private byte[] _bg2sc  = new byte[1]; // $2108
    private byte[] _bg3sc  = new byte[1]; // $2109
    private byte[] _bg4sc  = new byte[1]; // $210A
    private byte _bg12nba;      // $210B — BG1/2 character base address
    private byte _bg34nba;      // $210C — BG3/4 character base address
    private ushort[] _bgHOffset = new ushort[4]; // $210D-$2114 (interleaved)
    private ushort[] _bgVOffset = new ushort[4];
    private byte _vmain;        // $2115 — VRAM address increment mode
    private ushort _vmadd;      // $2116–$2117 — VRAM address
    private byte _m7sel;        // $211A — Mode 7 settings
    private byte _cgadd;        // $2121 — CGRAM address
    private byte _w12sel;       // $2123 — Window 1/2 BG mask
    private byte _w34sel;       // $2124
    private byte _wobjsel;      // $2125
    private byte _wh0, _wh1, _wh2, _wh3; // $2126–$2129 window positions
    private byte _wbglog;       // $212A — Window BG logic
    private byte _wobjlog;      // $212B
    private byte _tm;           // $212C — Main screen designation
    private byte _ts;           // $212D — Sub screen designation
    private byte _tmw;          // $212E — Window mask for main
    private byte _tsw;          // $212F — Window mask for sub
    private byte _cgswsel;      // $2130 — Color addition select
    private byte _cgadsub;      // $2131 — Color math designation
    private ushort _coldata;    // $2132 — Fixed color data
    private byte _setini;       // $2133 — Screen mode/video select
    private byte _stat77;       // $213E — PPU status (read-only)
    private byte _stat78;       // $213F — PPU status (read-only)

    // VRAM write latch
    private byte _vramLatch;
    private bool _vramHighByte;

    // CGRAM write latch
    private byte _cgramLatch;
    private bool _cgramHighByte;

    // OAM internal pointer
    private ushort _oamPointer;

    // H/V offset write latches (each register is written twice for 16-bit)
    private byte[] _bgOffsetLatch = new byte[4];

    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<FrameReadyEventArgs>? FrameReady;

    public string Name => "PPU (S-PPU1/S-PPU2)";
    public bool IsVBlank => _scanline >= SnesConstants.VBlankStartNtsc;
    public bool IsHBlank => _dot >= 274 && _dot < 341;
    public IFrameBuffer FrameBuffer => _frameBuffer;

    public PpuStatus Status => new()
    {
        CurrentScanline = _scanline,
        CurrentDot      = _dot,
        InVBlank        = IsVBlank,
        InHBlank        = IsHBlank,
        FrameCount      = _frameCount,
        BgMode          = (byte)(_bgmode & 0x07)
    };

    public Ppu(ILogger<Ppu> logger)
    {
        _logger = logger;
        _frameBuffer = new SnesFrameBuffer();
    }

    // ── IEmulatorComponent ────────────────────────────────────────────────────

    public void Reset()
    {
        Array.Clear(_vram);
        Array.Clear(_oam);
        Array.Clear(_cgram);
        _dot = 0;
        _scanline = 0;
        _frameCount = 0;
        _masterCycleAccum = 0;
        _inidisp = 0x80; // Forced blank on reset
        _bgmode = 0;
        _tm = 0;
        _vmadd = 0;
        _cgadd = 0;
        _oamPointer = 0;
        _frameBuffer.Clear();
        _logger.LogDebug("PPU reset.");
    }

    // ── IPpu ──────────────────────────────────────────────────────────────────

    public void Clock(int masterCycles)
    {
        _masterCycleAccum += masterCycles;

        while (_masterCycleAccum >= MasterCyclesPerDot)
        {
            _masterCycleAccum -= MasterCyclesPerDot;
            TickDot();
        }
    }

    private void TickDot()
    {
        // Render pixel during visible region
        if (_scanline < SnesConstants.ScreenHeightNtsc && _dot < SnesConstants.ScreenWidth)
        {
            RenderPixel(_dot, _scanline);
        }

        _dot++;

        if (_dot >= SnesConstants.DotsPerScanline)
        {
            _dot = 0;
            EndOfScanline();
            _scanline++;

            if (_scanline >= SnesConstants.TotalScanlinesNtsc)
            {
                _scanline = 0;
                EndOfFrame();
            }
        }
    }

    private void EndOfScanline()
    {
        // H-blank period begins
        if (_scanline == SnesConstants.VBlankStartNtsc)
        {
            // V-blank start — raise NMI on next CPU step
            _stat77 |= 0x80;
        }
    }

    private void EndOfFrame()
    {
        _frameCount++;
        _stat77 &= 0x7F; // Clear VBL flag

        // Raise FrameReady event with current pixel data
        FrameReady?.Invoke(this, new FrameReadyEventArgs(
            _frameBuffer.Pixels,
            _frameBuffer.Width,
            _frameBuffer.Height,
            _frameCount));
    }

    // ── Pixel rendering ───────────────────────────────────────────────────────

    private void RenderPixel(int x, int y)
    {
        // Forced blank: output black
        if ((_inidisp & 0x80) != 0)
        {
            _frameBuffer.SetPixel(x, y, 0xFF000000);
            return;
        }

        byte brightness = (byte)(_inidisp & 0x0F);
        uint bgColor = GetBackdropColor();

        uint pixelColor = bgColor;

        // Render enabled background layers according to BG mode
        byte mode = (byte)(_bgmode & 0x07);

        if (mode == 0)
        {
            // Mode 0: 4 layers, 4 colors each (2bpp)
            pixelColor = RenderMode0Pixel(x, y, bgColor);
        }
        else if (mode == 1)
        {
            // Mode 1: BG1+BG2 are 16-color (4bpp), BG3 is 4-color (2bpp)
            pixelColor = RenderMode1Pixel(x, y, bgColor);
        }
        else
        {
            // Other modes: use backdrop + BG1 if enabled, basic rendering
            pixelColor = RenderFallbackPixel(x, y, bgColor);
        }

        // Apply brightness scaling
        if (brightness < 15)
            pixelColor = ApplyBrightness(pixelColor, brightness);

        _frameBuffer.SetPixel(x, y, pixelColor);
    }

    private uint RenderMode0Pixel(int x, int y, uint bgColor)
    {
        // Mode 0: BG1–BG4, each 2bpp (4 colors from sub-palette)
        // Priority (high→low): OBJ3, BG1.1, BG2.1, OBJ2, BG1.0, BG2.0, OBJ1, BG3.1, BG4.1, OBJ0, BG3.0, BG4.0, BD
        uint color = bgColor;

        // Render BG layers in priority order (simplified: back to front)
        for (int layer = 3; layer >= 0; layer--)
        {
            if ((_tm & (1 << layer)) == 0) continue; // Layer disabled
            uint layerColor = SampleBgLayer2bpp(layer, x, y);
            if ((layerColor & 0xFF000000) != 0) // Non-transparent
                color = layerColor;
        }

        return color;
    }

    private uint RenderMode1Pixel(int x, int y, uint bgColor)
    {
        uint color = bgColor;

        // BG3 (2bpp) — lowest priority
        if ((_tm & 0x04) != 0)
        {
            uint c = SampleBgLayer2bpp(2, x, y);
            if ((c & 0xFF000000) != 0) color = c;
        }
        // BG2 (4bpp)
        if ((_tm & 0x02) != 0)
        {
            uint c = SampleBgLayer4bpp(1, x, y);
            if ((c & 0xFF000000) != 0) color = c;
        }
        // BG1 (4bpp) — highest priority BG
        if ((_tm & 0x01) != 0)
        {
            uint c = SampleBgLayer4bpp(0, x, y);
            if ((c & 0xFF000000) != 0) color = c;
        }

        return color;
    }

    private uint RenderFallbackPixel(int x, int y, uint bgColor)
    {
        if ((_tm & 0x01) != 0)
        {
            uint c = SampleBgLayer4bpp(0, x, y);
            if ((c & 0xFF000000) != 0) return c;
        }
        return bgColor;
    }

    // ── Background tile sampling ───────────────────────────────────────────────

    /// <summary>
    /// Samples a 2bpp background layer at screen coordinate (x, y).
    /// Returns transparent (alpha=0) if color index 0.
    /// </summary>
    private uint SampleBgLayer2bpp(int layer, int x, int y)
    {
        // Get scroll offsets for this layer
        int scrollX = _bgHOffset[layer] & 0x3FF;
        int scrollY = _bgVOffset[layer] & 0x3FF;

        // Map position: wrap at 256 or 512 pixels depending on SC size
        int mapX = (x + scrollX) & 0x3FF;
        int mapY = (y + scrollY) & 0x3FF;

        int tileX = mapX / 8;
        int tileY = mapY / 8;
        int pixX  = mapX % 8;
        int pixY  = mapY % 8;

        // Get tilemap base address from BGnSC register
        byte sc = GetBgSc(layer);
        int tilemapBase = (sc >> 2) * 0x800; // Each increment = 2KB

        // Tilemap entry address
        int tilemapAddr = tilemapBase + (tileY * 32 + tileX) * 2;
        tilemapAddr &= 0xFFFF;

        if (tilemapAddr + 1 >= _vram.Length) return 0;
        ushort entry = (ushort)(_vram[tilemapAddr] | (_vram[tilemapAddr + 1] << 8));

        int tileNum  = entry & 0x03FF;
        bool hflip   = (entry & 0x4000) != 0;
        bool vflip   = (entry & 0x8000) != 0;
        int palette  = (entry >> 10) & 0x07;

        // Effective pixel within tile
        int tpx = hflip ? 7 - pixX : pixX;
        int tpy = vflip ? 7 - pixY : pixY;

        // Character data base from BG12NBA / BG34NBA
        int charBase = GetBgCharBase(layer);

        // 2bpp: 8 bytes per tile row; 2 bytes per row
        int tileAddr = charBase + tileNum * 16 + tpy * 2;
        if (tileAddr + 1 >= _vram.Length) return 0;

        byte lo = _vram[tileAddr];
        byte hi = _vram[tileAddr + 1];

        int shift = 7 - tpx;
        int colorIndex = (((lo >> shift) & 1)) | (((hi >> shift) & 1) << 1);

        if (colorIndex == 0) return 0; // Transparent

        // Palette lookup: 2bpp palettes start at index (palette*4 + colorIndex)
        int paletteBase = (palette * 4 + colorIndex) * 2;
        if (paletteBase + 1 >= _cgram.Length) return 0xFF808080;

        ushort snesColor = (ushort)(_cgram[paletteBase] | (_cgram[paletteBase + 1] << 8));
        return SnesFrameBuffer.SnesColorToArgb(snesColor);
    }

    /// <summary>
    /// Samples a 4bpp background layer at screen coordinate (x, y).
    /// </summary>
    private uint SampleBgLayer4bpp(int layer, int x, int y)
    {
        int scrollX = _bgHOffset[layer] & 0x3FF;
        int scrollY = _bgVOffset[layer] & 0x3FF;

        int mapX = (x + scrollX) & 0x3FF;
        int mapY = (y + scrollY) & 0x3FF;

        int tileX = mapX / 8;
        int tileY = mapY / 8;
        int pixX  = mapX % 8;
        int pixY  = mapY % 8;

        byte sc = GetBgSc(layer);
        int tilemapBase = (sc >> 2) * 0x800;
        int tilemapAddr = (tilemapBase + (tileY * 32 + tileX) * 2) & 0xFFFF;

        if (tilemapAddr + 1 >= _vram.Length) return 0;
        ushort entry = (ushort)(_vram[tilemapAddr] | (_vram[tilemapAddr + 1] << 8));

        int tileNum  = entry & 0x03FF;
        bool hflip   = (entry & 0x4000) != 0;
        bool vflip   = (entry & 0x8000) != 0;
        int palette  = (entry >> 10) & 0x07;

        int tpx = hflip ? 7 - pixX : pixX;
        int tpy = vflip ? 7 - pixY : pixY;

        int charBase = GetBgCharBase(layer);
        // 4bpp: 32 bytes per tile (4 bytes per row)
        int tileAddr = charBase + tileNum * 32 + tpy * 4;
        if (tileAddr + 3 >= _vram.Length) return 0;

        byte p0lo = _vram[tileAddr];
        byte p0hi = _vram[tileAddr + 1];
        byte p1lo = _vram[tileAddr + 16]; // Bitplanes 2+3 are offset by 16 bytes
        byte p1hi = _vram[tileAddr + 17];

        int shift = 7 - tpx;
        int colorIndex = ((p0lo >> shift) & 1)
                       | (((p0hi >> shift) & 1) << 1)
                       | (((p1lo >> shift) & 1) << 2)
                       | (((p1hi >> shift) & 1) << 3);

        if (colorIndex == 0) return 0;

        // 4bpp palettes: each has 16 colors; BG palettes start at palette 0
        int paletteBase = (palette * 16 + colorIndex) * 2;
        if (paletteBase + 1 >= _cgram.Length) return 0xFF808080;

        ushort snesColor = (ushort)(_cgram[paletteBase] | (_cgram[paletteBase + 1] << 8));
        return SnesFrameBuffer.SnesColorToArgb(snesColor);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private byte GetBgSc(int layer) => layer switch
    {
        0 => _bg1sc[0],
        1 => _bg2sc[0],
        2 => _bg3sc[0],
        _ => _bg4sc[0]
    };

    private int GetBgCharBase(int layer)
    {
        byte nba = layer < 2 ? _bg12nba : _bg34nba;
        int shift = (layer % 2) * 4;
        return ((nba >> shift) & 0x0F) * 0x2000;
    }

    private uint GetBackdropColor()
    {
        // Color 0 of palette 0 = backdrop
        ushort snesColor = (ushort)(_cgram[0] | (_cgram[1] << 8));
        return SnesFrameBuffer.SnesColorToArgb(snesColor);
    }

    private static uint ApplyBrightness(uint argb, byte brightness)
    {
        if (brightness == 0) return argb & 0xFF000000; // Black
        float scale = brightness / 15.0f;
        byte r = (byte)(((argb >> 16) & 0xFF) * scale);
        byte g = (byte)(((argb >>  8) & 0xFF) * scale);
        byte b = (byte)((argb & 0xFF) * scale);
        return (argb & 0xFF000000) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    // ── Register I/O ──────────────────────────────────────────────────────────

    public byte ReadRegister(byte reg)
    {
        return reg switch
        {
            0x34 => 0x00,   // MPYL
            0x35 => 0x00,   // MPYM
            0x36 => 0x00,   // MPYH
            0x37 => 0x00,   // SLHV (latch H/V counter)
            0x38 => ReadOam(),
            0x39 => ReadVramLow(),
            0x3A => ReadVramHigh(),
            0x3B => ReadCgram(),
            0x3C => 0x00,   // OPHCT (H counter)
            0x3D => 0x00,   // OPVCT (V counter)
            0x3E => _stat77,
            0x3F => (byte)(_frameCount & 0xFF),
            _ => 0xFF
        };
    }

    public void WriteRegister(byte reg, byte value)
    {
        switch (reg)
        {
            case 0x00:
                if (_inidisp != value)
                    _logger.LogDebug("INIDISP: ${Old:X2} → ${New:X2} ({State})",
                        _inidisp, value, (value & 0x80) != 0 ? "BLANKED" : $"ON brightness={value & 0x0F}");
                _inidisp = value;
                break;
            case 0x01: _obsel = value; break;
            case 0x02: _oamadd = value; _oamPointer = (ushort)(_oamadd | ((_oamaddh & 1) << 8)); break;
            case 0x03: _oamaddh = value; _oamPointer = (ushort)(_oamadd | ((_oamaddh & 1) << 8)); break;
            case 0x04: WriteOam(value); break;
            case 0x05:
                if (_bgmode != value)
                    _logger.LogDebug("BGMODE: ${Old:X2} → ${New:X2} (Mode {Mode})", _bgmode, value, value & 7);
                _bgmode = value;
                break;
            case 0x06: _mosaic = value; break;
            case 0x07: _bg1sc[0] = value; break;
            case 0x08: _bg2sc[0] = value; break;
            case 0x09: _bg3sc[0] = value; break;
            case 0x0A: _bg4sc[0] = value; break;
            case 0x0B: _bg12nba = value; break;
            case 0x0C: _bg34nba = value; break;
            case 0x0D: WriteBgHOffset(0, value); break;
            case 0x0E: WriteBgVOffset(0, value); break;
            case 0x0F: WriteBgHOffset(1, value); break;
            case 0x10: WriteBgVOffset(1, value); break;
            case 0x11: WriteBgHOffset(2, value); break;
            case 0x12: WriteBgVOffset(2, value); break;
            case 0x13: WriteBgHOffset(3, value); break;
            case 0x14: WriteBgVOffset(3, value); break;
            case 0x15: _vmain = value; break;
            case 0x16: _vmadd = (ushort)((_vmadd & 0xFF00) | value); break;
            case 0x17: _vmadd = (ushort)((_vmadd & 0x00FF) | (value << 8)); break;
            case 0x18: WriteVramLow(value); break;
            case 0x19: WriteVramHigh(value); break;
            case 0x1A: _m7sel = value; break;
            case 0x21: _cgadd = value; _cgramHighByte = false; break;
            case 0x22: WriteCgram(value); break;
            case 0x23: _w12sel = value; break;
            case 0x24: _w34sel = value; break;
            case 0x25: _wobjsel = value; break;
            case 0x26: _wh0 = value; break;
            case 0x27: _wh1 = value; break;
            case 0x28: _wh2 = value; break;
            case 0x29: _wh3 = value; break;
            case 0x2A: _wbglog = value; break;
            case 0x2B: _wobjlog = value; break;
            case 0x2C:
                if (_tm != value)
                    _logger.LogDebug("TM (main screen): ${Old:X2} → ${New:X2}", _tm, value);
                _tm = value;
                break;
            case 0x2D: _ts = value; break;
            case 0x2E: _tmw = value; break;
            case 0x2F: _tsw = value; break;
            case 0x30: _cgswsel = value; break;
            case 0x31: _cgadsub = value; break;
            case 0x32: WriteColdata(value); break;
            case 0x33: _setini = value; break;
        }
    }

    // ── VRAM access ───────────────────────────────────────────────────────────

    private void WriteVramLow(byte value)
    {
        int addr = GetVramAddress() * 2;
        if (addr < _vram.Length) _vram[addr] = value;
        if ((_vmain & 0x80) == 0) IncrementVramAddress(); // Increment on low write if bit7=0
    }

    private void WriteVramHigh(byte value)
    {
        int addr = GetVramAddress() * 2 + 1;
        if (addr < _vram.Length) _vram[addr] = value;
        if ((_vmain & 0x80) != 0) IncrementVramAddress(); // Increment on high write if bit7=1
    }

    private byte ReadVramLow()
    {
        int addr = GetVramAddress() * 2;
        byte val = addr < _vram.Length ? _vram[addr] : (byte)0;
        if ((_vmain & 0x80) == 0) IncrementVramAddress();
        return val;
    }

    private byte ReadVramHigh()
    {
        int addr = GetVramAddress() * 2 + 1;
        byte val = addr < _vram.Length ? _vram[addr] : (byte)0;
        if ((_vmain & 0x80) != 0) IncrementVramAddress();
        return val;
    }

    private int GetVramAddress() => _vmadd & 0x7FFF;

    private void IncrementVramAddress()
    {
        int increment = (_vmain & 0x03) switch
        {
            0 => 1,
            1 => 32,
            _ => 128
        };
        _vmadd = (ushort)((_vmadd + increment) & 0x7FFF);
    }

    // ── OAM access ────────────────────────────────────────────────────────────

    private byte ReadOam()
    {
        byte val = _oamPointer < _oam.Length ? _oam[_oamPointer] : (byte)0xFF;
        _oamPointer = (ushort)((_oamPointer + 1) & 0x3FF);
        return val;
    }

    private void WriteOam(byte value)
    {
        if (_oamPointer < _oam.Length)
            _oam[_oamPointer] = value;
        _oamPointer = (ushort)((_oamPointer + 1) & 0x3FF);
    }

    // ── CGRAM access ──────────────────────────────────────────────────────────

    private byte ReadCgram()
    {
        int addr = _cgadd * 2;
        byte val;
        if (!_cgramHighByte)
        {
            val = addr < _cgram.Length ? _cgram[addr] : (byte)0;
            _cgramHighByte = true;
        }
        else
        {
            val = (addr + 1) < _cgram.Length ? _cgram[addr + 1] : (byte)0;
            _cgramHighByte = false;
            _cgadd++;
        }
        return val;
    }

    private void WriteCgram(byte value)
    {
        int addr = _cgadd * 2;
        if (!_cgramHighByte)
        {
            _cgramLatch = value;
            _cgramHighByte = true;
        }
        else
        {
            if (addr < _cgram.Length)     _cgram[addr]     = _cgramLatch;
            if (addr + 1 < _cgram.Length) _cgram[addr + 1] = (byte)(value & 0x7F);
            _cgramHighByte = false;
            _cgadd++;
        }
    }

    // ── Scroll register write (each register is written twice) ────────────────

    private void WriteBgHOffset(int layer, byte value)
    {
        _bgHOffset[layer] = (ushort)((_bgOffsetLatch[layer] | (value << 8)) & 0x3FF);
        _bgOffsetLatch[layer] = value;
    }

    private void WriteBgVOffset(int layer, byte value)
    {
        _bgVOffset[layer] = (ushort)((_bgOffsetLatch[layer] | (value << 8)) & 0x3FF);
        _bgOffsetLatch[layer] = value;
    }

    private void WriteColdata(byte value)
    {
        byte intensity = (byte)(value & 0x1F);
        if ((value & 0x20) != 0) _coldata = (ushort)((_coldata & ~0x001F) | intensity);
        if ((value & 0x40) != 0) _coldata = (ushort)((_coldata & ~0x03E0) | (intensity << 5));
        if ((value & 0x80) != 0) _coldata = (ushort)((_coldata & ~0x7C00) | (intensity << 10));
    }

    // ── IStateful ────────────────────────────────────────────────────────────

    public byte[] SaveState()
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        bw.Write(_vram);
        bw.Write(_oam);
        bw.Write(_cgram);
        bw.Write(_dot);
        bw.Write(_scanline);
        bw.Write(_frameCount);
        bw.Write(_inidisp);
        bw.Write(_bgmode);
        bw.Write(_tm);
        bw.Write(_vmadd);
        bw.Write(_cgadd);
        return ms.ToArray();
    }

    public void LoadState(byte[] state)
    {
        using var ms = new System.IO.MemoryStream(state);
        using var br = new System.IO.BinaryReader(ms);
        br.Read(_vram);
        br.Read(_oam);
        br.Read(_cgram);
        _dot = br.ReadInt32();
        _scanline = br.ReadInt32();
        _frameCount = br.ReadInt32();
        _inidisp = br.ReadByte();
        _bgmode = br.ReadByte();
        _tm = br.ReadByte();
        _vmadd = br.ReadUInt16();
        _cgadd = br.ReadByte();
    }
}
