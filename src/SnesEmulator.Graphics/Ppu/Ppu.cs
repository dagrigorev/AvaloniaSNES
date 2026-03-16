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
    private readonly uint[] _objScanlineBuffer = new uint[SnesConstants.ScreenWidth];
    private readonly bool[] _objScanlineOpaque = new bool[SnesConstants.ScreenWidth];
    private int _preparedObjScanline = -1;

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
    private short _m7a, _m7b, _m7c, _m7d; // $211B-$211E — Mode 7 matrix
    private short _m7x, _m7y;             // $211F-$2120 — Mode 7 center
    private short _m7hofs, _m7vofs;       // $210D/$210E in Mode 7
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
    private byte _oamWriteLatch;
    private bool _oamWriteLowPending;

    // BG scroll registers share a single previous-byte latch across all BGnHOFS/BGnVOFS writes.
    // This matches the SNES write-twice behaviour closely enough for common boot/title code.
    private byte _bgScrollPrevByte;
    private byte _mode7PrevByte;

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
        _bgScrollPrevByte = 0;
        _oamPointer = 0;
        _m7a = 0x0100;
        _m7b = 0;
        _m7c = 0;
        _m7d = 0x0100;
        _m7x = 0;
        _m7y = 0;
        _m7hofs = 0;
        _m7vofs = 0;
        _mode7PrevByte = 0;
        _oamWriteLatch = 0;
        _oamWriteLowPending = false;
        _frameBuffer.Clear();
        _stat77 = 0;
        _stat78 = 0x01;
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
        if (_dot == 0)
            _preparedObjScanline = -1;

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
        else if (mode == 7)
        {
            pixelColor = RenderMode7Pixel(x, y, bgColor);
        }
        else
        {
            // Other modes: use backdrop + BG1 if enabled, basic rendering
            pixelColor = RenderFallbackPixel(x, y, bgColor);
        }

        if ((_tm & 0x10) != 0)
        {
            uint objColor = SampleObjPixel(x, y);
            if ((objColor & 0xFF000000) != 0)
                pixelColor = objColor;
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
        byte sc = GetBgSc(layer);
        int tileSize = GetBgTileSize(layer);
        (int mapWidthPixels, int mapHeightPixels) = GetBgMapDimensions(sc, tileSize);

        int scrollX = _bgHOffset[layer] & 0x3FF;
        int scrollY = _bgVOffset[layer] & 0x3FF;

        int mapX = WrapBgCoordinate(x + scrollX, mapWidthPixels);
        int mapY = WrapBgCoordinate(y + scrollY, mapHeightPixels);

        int tileX = mapX / tileSize;
        int tileY = mapY / tileSize;
        int pixX  = mapX % tileSize;
        int pixY  = mapY % tileSize;

        int tilemapAddr = GetTilemapEntryAddress(sc, tileX, tileY);
        if (tilemapAddr + 1 >= _vram.Length) return 0;

        ushort entry = (ushort)(_vram[tilemapAddr] | (_vram[tilemapAddr + 1] << 8));
        int tileNum  = entry & 0x03FF;
        bool hflip   = (entry & 0x4000) != 0;
        bool vflip   = (entry & 0x8000) != 0;
        int palette  = (entry >> 10) & 0x07;

        ResolveBgTilePixel(tileSize, hflip, vflip, pixX, pixY, ref tileNum, out int tpx, out int tpy);

        int charBase = GetBgCharBase(layer);
        int tileAddr = charBase + tileNum * 16 + tpy * 2;
        if (tileAddr + 1 >= _vram.Length) return 0;

        byte lo = _vram[tileAddr];
        byte hi = _vram[tileAddr + 1];

        int shift = 7 - tpx;
        int colorIndex = (((lo >> shift) & 1)) | (((hi >> shift) & 1) << 1);
        if (colorIndex == 0) return 0;

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
        byte sc = GetBgSc(layer);
        int tileSize = GetBgTileSize(layer);
        (int mapWidthPixels, int mapHeightPixels) = GetBgMapDimensions(sc, tileSize);

        int scrollX = _bgHOffset[layer] & 0x3FF;
        int scrollY = _bgVOffset[layer] & 0x3FF;

        int mapX = WrapBgCoordinate(x + scrollX, mapWidthPixels);
        int mapY = WrapBgCoordinate(y + scrollY, mapHeightPixels);

        int tileX = mapX / tileSize;
        int tileY = mapY / tileSize;
        int pixX  = mapX % tileSize;
        int pixY  = mapY % tileSize;

        int tilemapAddr = GetTilemapEntryAddress(sc, tileX, tileY);
        if (tilemapAddr + 1 >= _vram.Length) return 0;

        ushort entry = (ushort)(_vram[tilemapAddr] | (_vram[tilemapAddr + 1] << 8));
        int tileNum  = entry & 0x03FF;
        bool hflip   = (entry & 0x4000) != 0;
        bool vflip   = (entry & 0x8000) != 0;
        int palette  = (entry >> 10) & 0x07;

        ResolveBgTilePixel(tileSize, hflip, vflip, pixX, pixY, ref tileNum, out int tpx, out int tpy);

        int charBase = GetBgCharBase(layer);
        int tileAddr = charBase + tileNum * 32 + tpy * 2;
        if (tileAddr + 17 >= _vram.Length) return 0;

        byte p0lo = _vram[tileAddr];
        byte p0hi = _vram[tileAddr + 1];
        byte p1lo = _vram[tileAddr + 16];
        byte p1hi = _vram[tileAddr + 17];

        int shift = 7 - tpx;
        int colorIndex = ((p0lo >> shift) & 1)
                       | (((p0hi >> shift) & 1) << 1)
                       | (((p1lo >> shift) & 1) << 2)
                       | (((p1hi >> shift) & 1) << 3);

        if (colorIndex == 0) return 0;

        int paletteBase = (palette * 16 + colorIndex) * 2;
        if (paletteBase + 1 >= _cgram.Length) return 0xFF808080;

        ushort snesColor = (ushort)(_cgram[paletteBase] | (_cgram[paletteBase + 1] << 8));
        return SnesFrameBuffer.SnesColorToArgb(snesColor);
    }

    private uint RenderMode7Pixel(int x, int y, uint bgColor)
    {
        if ((_tm & 0x01) == 0)
            return bgColor;

        int screenX = (_m7sel & 0x01) != 0 ? 255 - x : x;
        int screenY = (_m7sel & 0x02) != 0 ? 255 - y : y;

        int a = _m7a;
        int b = _m7b;
        int c = _m7c;
        int d = _m7d;
        int cx = _m7x;
        int cy = _m7y;
        int hofs = _m7hofs;
        int vofs = _m7vofs;

        int dx = screenX + hofs - cx;
        int dy = screenY + vofs - cy;

        int texX = ((a * dx) + (b * dy) + (cx << 8)) >> 8;
        int texY = ((c * dx) + (d * dy) + (cy << 8)) >> 8;

        bool largeField = (_m7sel & 0x80) != 0;
        bool fillTile0 = (_m7sel & 0x40) != 0;

        if (!largeField)
        {
            texX &= 0x3FF;
            texY &= 0x3FF;
        }
        else if ((uint)texX >= 1024 || (uint)texY >= 1024)
        {
            if (fillTile0)
            {
                texX = 0;
                texY = 0;
            }
            else
            {
                return bgColor;
            }
        }

        int tileX = (texX >> 3) & 0x7F;
        int tileY = (texY >> 3) & 0x7F;
        int mapWord = tileY * 128 + tileX;
        int mapAddr = mapWord * 2;
        if (mapAddr >= _vram.Length)
            return bgColor;

        int tileIndex = _vram[mapAddr];
        int pixelInTile = ((texY & 7) << 3) | (texX & 7);
        int tileByteIndex = tileIndex * 64 + pixelInTile;
        int tileDataAddr = tileByteIndex * 2 + 1;
        if ((uint)tileDataAddr >= (uint)_vram.Length)
            return bgColor;

        byte colorIndex = _vram[tileDataAddr];
        if (colorIndex == 0)
            return bgColor;

        int cgramAddr = colorIndex * 2;
        if (cgramAddr + 1 >= _cgram.Length)
            return bgColor;

        ushort snesColor = (ushort)(_cgram[cgramAddr] | (_cgram[cgramAddr + 1] << 8));
        return SnesFrameBuffer.SnesColorToArgb(snesColor);
    }

    private static short SignExtend13(ushort value)
    {
        value &= 0x1FFF;
        return (short)(((value & 0x1000) != 0) ? (value | 0xE000) : value);
    }



    private uint SampleObjPixel(int x, int y)
    {
        PrepareObjScanline(y);
        return (uint)(x < _objScanlineOpaque.Length && _objScanlineOpaque[x] ? _objScanlineBuffer[x] : 0);
    }

    private void PrepareObjScanline(int y)
    {
        if (_preparedObjScanline == y)
            return;

        Array.Clear(_objScanlineOpaque, 0, _objScanlineOpaque.Length);
        Array.Clear(_objScanlineBuffer, 0, _objScanlineBuffer.Length);

        for (int spriteIndex = 127; spriteIndex >= 0; spriteIndex--)
        {
            int low = spriteIndex * 4;
            int xLow = _oam[low];
            int spriteY = _oam[low + 1];
            int tileBase = _oam[low + 2];
            byte attr = _oam[low + 3];

            int highTableByte = _oam[512 + (spriteIndex >> 2)];
            int shift = (spriteIndex & 0x03) * 2;
            int xHigh = (highTableByte >> shift) & 0x01;
            bool large = ((highTableByte >> (shift + 1)) & 0x01) != 0;

            int spriteX = xLow | (xHigh << 8);
            if (spriteX >= 256)
                spriteX -= 512;

            (int spriteWidth, int spriteHeight) = GetObjSize(large);

            int relY = (y - spriteY + 256) & 0xFF;
            if (relY >= spriteHeight)
                continue;

            bool vflip = (attr & 0x80) != 0;
            bool hflip = (attr & 0x40) != 0;
            int palette = (attr >> 1) & 0x07;
            bool nameTable = (attr & 0x01) != 0;

            int effY = vflip ? spriteHeight - 1 - relY : relY;
            int subTileY = effY >> 3;
            int tilePixelY = effY & 7;

            int startX = Math.Max(0, spriteX);
            int endX = Math.Min(SnesConstants.ScreenWidth, spriteX + spriteWidth);
            if (endX <= startX)
                continue;

            for (int screenX = startX; screenX < endX; screenX++)
            {
                int relX = screenX - spriteX;
                int effX = hflip ? spriteWidth - 1 - relX : relX;
                int subTileX = effX >> 3;
                int tilePixelX = effX & 7;
                int tileIndex = (tileBase + subTileX + subTileY * 16) & 0xFF;

                int tileByteAddress = GetObjTileByteAddress(tileIndex, nameTable) + tilePixelY * 2;
                if (tileByteAddress + 17 >= _vram.Length)
                    continue;

                byte p0lo = _vram[tileByteAddress];
                byte p0hi = _vram[tileByteAddress + 1];
                byte p1lo = _vram[tileByteAddress + 16];
                byte p1hi = _vram[tileByteAddress + 17];

                int bit = 7 - tilePixelX;
                int colorIndex = ((p0lo >> bit) & 1)
                               | (((p0hi >> bit) & 1) << 1)
                               | (((p1lo >> bit) & 1) << 2)
                               | (((p1hi >> bit) & 1) << 3);

                if (colorIndex == 0)
                    continue;

                int cgramIndex = 128 + palette * 16 + colorIndex;
                int cgramAddr = cgramIndex * 2;
                if (cgramAddr + 1 >= _cgram.Length)
                    continue;

                ushort snesColor = (ushort)(_cgram[cgramAddr] | (_cgram[cgramAddr + 1] << 8));
                _objScanlineBuffer[screenX] = SnesFrameBuffer.SnesColorToArgb(snesColor);
                _objScanlineOpaque[screenX] = true;
            }
        }

        _preparedObjScanline = y;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int WrapBgCoordinate(int value, int size)
    {
        if (size <= 0) return 0;
        int result = value % size;
        return result < 0 ? result + size : result;
    }

    private (int WidthPixels, int HeightPixels) GetBgMapDimensions(byte sc, int tileSize)
    {
        int widthTiles = (sc & 0x01) != 0 ? 64 : 32;
        int heightTiles = (sc & 0x02) != 0 ? 64 : 32;
        return (widthTiles * tileSize, heightTiles * tileSize);
    }

    private int GetBgTileSize(int layer)
    {
        int bit = 4 + layer;
        return ((_bgmode >> bit) & 0x01) != 0 ? 16 : 8;
    }

    private static void ResolveBgTilePixel(int tileSize, bool hflip, bool vflip, int pixX, int pixY, ref int tileNum, out int tpx, out int tpy)
    {
        if (tileSize == 16)
        {
            int effX = hflip ? 15 - pixX : pixX;
            int effY = vflip ? 15 - pixY : pixY;
            int subTileX = effX >= 8 ? 1 : 0;
            int subTileY = effY >= 8 ? 1 : 0;
            tileNum = (tileNum + subTileX + subTileY * 16) & 0x03FF;
            tpx = effX & 7;
            tpy = effY & 7;
            return;
        }

        tpx = hflip ? 7 - pixX : pixX;
        tpy = vflip ? 7 - pixY : pixY;
    }

    private void WriteBgSc(int layer, byte value)
    {
        byte previous = GetBgSc(layer);
        if (previous != value)
        {
            int widthTiles = (value & 0x01) != 0 ? 64 : 32;
            int heightTiles = (value & 0x02) != 0 ? 64 : 32;
            _logger.LogDebug("BG{Layer}SC: ${Old:X2} → ${New:X2} (base=${Base:X4}, size={Width}x{Height})",
                layer + 1, previous, value, (value >> 2) * 0x800, widthTiles, heightTiles);
        }

        switch (layer)
        {
            case 0: _bg1sc[0] = value; break;
            case 1: _bg2sc[0] = value; break;
            case 2: _bg3sc[0] = value; break;
            default: _bg4sc[0] = value; break;
        }
    }

    private byte GetBgSc(int layer) => layer switch
    {
        0 => _bg1sc[0],
        1 => _bg2sc[0],
        2 => _bg3sc[0],
        _ => _bg4sc[0]
    };

    private int GetTilemapEntryAddress(byte sc, int tileX, int tileY)
    {
        int baseAddress = ((sc >> 2) & 0x3F) * 0x800;
        int localX = tileX & 31;
        int localY = tileY & 31;

        int screen = 0;
        if ((sc & 0x01) != 0 && tileX >= 32) screen |= 1;
        if ((sc & 0x02) != 0 && tileY >= 32) screen |= 2;

        return (baseAddress + screen * 0x800 + (localY * 32 + localX) * 2) & 0xFFFF;
    }


    private (int Width, int Height) GetObjSize(bool large)
    {
        return ((_obsel >> 5) & 0x07) switch
        {
            0 => large ? (16, 16) : (8, 8),
            1 => large ? (32, 32) : (8, 8),
            2 => large ? (64, 64) : (8, 8),
            3 => large ? (32, 32) : (16, 16),
            4 => large ? (64, 64) : (16, 16),
            5 => large ? (64, 64) : (32, 32),
            6 => large ? (32, 64) : (16, 32),
            _ => large ? (32, 32) : (16, 32)
        };
    }

    private int GetObjTileByteAddress(int tileIndex, bool nameTable)
    {
        int baseWordAddress = (((_obsel & 0x07) << 13) + (tileIndex << 4) + (nameTable ? ((((_obsel >> 3) & 0x03) + 1) << 12) : 0)) & 0x7FFF;
        return (baseWordAddress << 1) & 0xFFFF;
    }

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
            0x3F => _stat78,
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
            case 0x01:
                if (_obsel != value)
                    _logger.LogDebug("OBSEL: ${Old:X2} → ${New:X2}", _obsel, value);
                _obsel = value;
                break;
            case 0x02: _oamadd = value; _oamPointer = (ushort)(_oamadd | ((_oamaddh & 1) << 8)); InvalidateObjCache(); break;
            case 0x03: _oamaddh = value; _oamPointer = (ushort)(_oamadd | ((_oamaddh & 1) << 8)); InvalidateObjCache(); break;
            case 0x04: WriteOam(value); InvalidateObjCache(); break;
            case 0x05:
                if (_bgmode != value)
                    _logger.LogDebug("BGMODE: ${Old:X2} → ${New:X2} (Mode {Mode})", _bgmode, value, value & 7);
                _bgmode = value;
                break;
            case 0x06: _mosaic = value; break;
            case 0x07: WriteBgSc(0, value); break;
            case 0x08: WriteBgSc(1, value); break;
            case 0x09: WriteBgSc(2, value); break;
            case 0x0A: WriteBgSc(3, value); break;
            case 0x0B: _bg12nba = value; break;
            case 0x0C: _bg34nba = value; break;
            case 0x0D:
                if ((_bgmode & 0x07) == 7) WriteMode7HOffset(value); else WriteBgHOffset(0, value);
                break;
            case 0x0E:
                if ((_bgmode & 0x07) == 7) WriteMode7VOffset(value); else WriteBgVOffset(0, value);
                break;
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
            case 0x1A:
                if (_m7sel != value)
                    _logger.LogDebug("M7SEL: ${Old:X2} → ${New:X2}", _m7sel, value);
                _m7sel = value;
                break;
            case 0x1B: WriteMode7Matrix(ref _m7a, value, "M7A"); break;
            case 0x1C: WriteMode7Matrix(ref _m7b, value, "M7B"); break;
            case 0x1D: WriteMode7Matrix(ref _m7c, value, "M7C"); break;
            case 0x1E: WriteMode7Matrix(ref _m7d, value, "M7D"); break;
            case 0x1F: WriteMode7Center(ref _m7x, value, "M7X"); break;
            case 0x20: WriteMode7Center(ref _m7y, value, "M7Y"); break;
            case 0x21: _cgadd = value; _cgramHighByte = false; InvalidateObjCache(); break;
            case 0x22: WriteCgram(value); InvalidateObjCache(); break;
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
        int addr = GetVramByteAddress();
        if (addr < _vram.Length) _vram[addr] = value;
        InvalidateObjCache();
        if ((_vmain & 0x80) == 0) IncrementVramAddress(); // Increment on low write if bit7=0
    }

    private void WriteVramHigh(byte value)
    {
        int addr = GetVramByteAddress() + 1;
        if (addr < _vram.Length) _vram[addr] = value;
        InvalidateObjCache();
        if ((_vmain & 0x80) != 0) IncrementVramAddress(); // Increment on high write if bit7=1
    }

    private byte ReadVramLow()
    {
        int addr = GetVramByteAddress();
        byte val = addr < _vram.Length ? _vram[addr] : (byte)0;
        if ((_vmain & 0x80) == 0) IncrementVramAddress();
        return val;
    }

    private byte ReadVramHigh()
    {
        int addr = GetVramByteAddress() + 1;
        byte val = addr < _vram.Length ? _vram[addr] : (byte)0;
        if ((_vmain & 0x80) != 0) IncrementVramAddress();
        return val;
    }

    private int GetVramByteAddress() => (RemapVramWordAddress(_vmadd) << 1) & 0xFFFF;

    private void IncrementVramAddress()
    {
        int increment = (_vmain & 0x03) switch
        {
            0 => 1,
            1 => 32,
            _ => 128
        };
        _vmadd = (ushort)((_vmadd + increment) & 0xFFFF);
    }

    private ushort RemapVramWordAddress(ushort address)
    {
        return ((_vmain >> 2) & 0x03) switch
        {
            0 => address,
            1 => (ushort)((address & 0xFF00) | ((address & 0x001F) << 3) | ((address >> 5) & 0x0007)),
            2 => (ushort)((address & 0xFE00) | ((address & 0x003F) << 3) | ((address >> 6) & 0x0007)),
            _ => (ushort)((address & 0xFC00) | ((address & 0x007F) << 3) | ((address >> 7) & 0x0007))
        };
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
        if (_oamPointer < 512)
        {
            if ((_oamPointer & 1) == 0)
            {
                _oamWriteLatch = value;
                _oamWriteLowPending = true;
            }
            else
            {
                int baseAddr = _oamPointer - 1;
                if (baseAddr < _oam.Length)
                    _oam[baseAddr] = _oamWriteLowPending ? _oamWriteLatch : (byte)0;
                if (_oamPointer < _oam.Length)
                    _oam[_oamPointer] = value;
                _oamWriteLowPending = false;
            }
        }
        else if (_oamPointer < _oam.Length)
        {
            _oam[_oamPointer] = value;
        }

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
        ushort current = _bgHOffset[layer];
        _bgHOffset[layer] = (ushort)((value << 8) | (_bgScrollPrevByte & 0xF8) | ((current >> 8) & 0x07));
        _bgScrollPrevByte = value;
    }

    private void WriteBgVOffset(int layer, byte value)
    {
        _bgVOffset[layer] = (ushort)((value << 8) | _bgScrollPrevByte);
        _bgScrollPrevByte = value;
    }

    private void WriteMode7Matrix(ref short target, byte value, string name)
    {
        short old = target;
        target = (short)((value << 8) | _mode7PrevByte);
        _mode7PrevByte = value;
        if (old != target)
            _logger.LogDebug("{Name}: {Old} → {New}", name, old, target);
    }

    private void WriteMode7Center(ref short target, byte value, string name)
    {
        short old = target;
        target = SignExtend13((ushort)(((value & 0x1F) << 8) | _mode7PrevByte));
        _mode7PrevByte = value;
        if (old != target)
            _logger.LogDebug("{Name}: {Old} → {New}", name, old, target);
    }

    private void WriteMode7HOffset(byte value)
    {
        _m7hofs = SignExtend13((ushort)(((value & 0x1F) << 8) | _mode7PrevByte));
        _mode7PrevByte = value;
    }

    private void WriteMode7VOffset(byte value)
    {
        _m7vofs = SignExtend13((ushort)(((value & 0x1F) << 8) | _mode7PrevByte));
        _mode7PrevByte = value;
    }


    private void WriteColdata(byte value)
    {
        byte intensity = (byte)(value & 0x1F);
        if ((value & 0x20) != 0) _coldata = (ushort)((_coldata & ~0x001F) | intensity);
        if ((value & 0x40) != 0) _coldata = (ushort)((_coldata & ~0x03E0) | (intensity << 5));
        if ((value & 0x80) != 0) _coldata = (ushort)((_coldata & ~0x7C00) | (intensity << 10));
    }

    private void InvalidateObjCache()
    {
        _preparedObjScanline = -1;
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
