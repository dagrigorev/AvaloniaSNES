using SnesEmulator.Core.Models;

namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Represents a pixel framebuffer produced by the PPU.
/// The renderer reads from this to display each frame.
/// 256x224 pixels (NTSC standard height), stored as ARGB32.
/// </summary>
public interface IFrameBuffer
{
    /// <summary>Width of the framebuffer in pixels.</summary>
    int Width { get; }

    /// <summary>Height of the framebuffer in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// Raw pixel data in ARGB32 format.
    /// Layout: [y * Width + x] for pixel (x, y).
    /// </summary>
    uint[] Pixels { get; }

    /// <summary>Sets a single pixel at (x, y) to the given ARGB color.</summary>
    void SetPixel(int x, int y, uint argb);

    /// <summary>Clears the entire framebuffer to a given color (default black).</summary>
    void Clear(uint color = 0xFF000000);
}

/// <summary>
/// Responsible for loading and parsing SNES ROM files.
/// Handles LoROM, HiROM, and ExHiROM mapping modes,
/// and validates the ROM header checksum.
/// </summary>
public interface IRomLoader
{
    /// <summary>
    /// Loads a ROM from the specified path.
    /// Supports .smc and .sfc file extensions.
    /// </summary>
    /// <param name="filePath">Full path to the ROM file.</param>
    /// <returns>Loaded and parsed ROM data.</returns>
    /// <exception cref="Exceptions.RomLoadException">Thrown if the file is invalid.</exception>
    RomData LoadRom(string filePath);
}
