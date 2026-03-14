using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Graphics.Framebuffer;

/// <summary>
/// ARGB32 framebuffer for the SNES PPU output.
/// 256 × 224 pixels (NTSC), each pixel stored as 0xAARRGGBB.
///
/// The framebuffer is double-buffered conceptually: the PPU writes
/// to the back buffer during rendering, then swaps when the frame
/// is complete. The renderer reads from the front buffer.
/// </summary>
public sealed class SnesFrameBuffer : IFrameBuffer
{
    private readonly uint[] _pixels;

    public int Width  { get; }
    public int Height { get; }
    public uint[] Pixels => _pixels;

    public SnesFrameBuffer(int width = SnesConstants.ScreenWidth,
                           int height = SnesConstants.ScreenHeightNtsc)
    {
        Width  = width;
        Height = height;
        _pixels = new uint[width * height];
        Clear();
    }

    /// <inheritdoc />
    public void SetPixel(int x, int y, uint argb)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            _pixels[y * Width + x] = argb;
    }

    /// <inheritdoc />
    public void Clear(uint color = 0xFF000000) => Array.Fill(_pixels, color);

    /// <summary>
    /// Converts a 15-bit SNES color (BGR555) to 32-bit ARGB.
    /// SNES format: 0BBBBBGGGGGRRRRR
    /// </summary>
    public static uint SnesColorToArgb(ushort snesColor)
    {
        byte r = (byte)((snesColor & 0x001F) << 3);
        byte g = (byte)(((snesColor >> 5) & 0x1F) << 3);
        byte b = (byte)(((snesColor >> 10) & 0x1F) << 3);
        // Expand 5-bit to 8-bit by replicating high bits into low
        r |= (byte)(r >> 5);
        g |= (byte)(g >> 5);
        b |= (byte)(b >> 5);
        return (uint)(0xFF000000 | (r << 16) | (g << 8) | b);
    }
}
