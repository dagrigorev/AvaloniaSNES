using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SnesEmulator.Core.Models;
using SnesEmulator.Desktop.ViewModels;

namespace SnesEmulator.Desktop.Controls;

/// <summary>
/// Renders the SNES PPU framebuffer into the Avalonia window.
/// Pixel format: emulator produces ARGB32, Avalonia Bgra8888 wants B,G,R,A in memory.
/// </summary>
public sealed class GameScreen : Control
{
    private const int SnesWidth  = 256;
    private const int SnesHeight = 224;

    private WriteableBitmap? _bitmap;
    private readonly object  _bitmapLock = new();
    private volatile bool    _hasFrame;

    public void AttachToViewModel(MainViewModel vm) => vm.AttachGameScreen(this);

    public void SubscribeToPpu(Core.Interfaces.IPpu ppu)
        => ppu.FrameReady += OnFrameReady;

    private void OnFrameReady(object? sender, FrameReadyEventArgs e)
    {
        UpdateBitmap(e.Pixels, e.Width, e.Height);
        _hasFrame = true;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void UpdateBitmap(IReadOnlyList<uint> pixels, int width, int height)
    {
        lock (_bitmapLock)
        {
            if (_bitmap is null ||
                _bitmap.PixelSize.Width  != width ||
                _bitmap.PixelSize.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormats.Bgra8888);
            }

            using var fb = _bitmap.Lock();
            unsafe
            {
                uint* dst = (uint*)fb.Address;
                for (int i = 0; i < pixels.Count; i++)
                {
                    // Emulator pixel: 0xAARRGGBB
                    // Avalonia Bgra8888 in memory layout: B G R A (little-endian uint = 0xAARRGGBB)
                    // So actually no swap needed — Bgra8888 stores as uint32 ARGB on little-endian
                    uint argb = pixels[i];
                    uint a = (argb >> 24) & 0xFF;
                    uint r = (argb >> 16) & 0xFF;
                    uint g = (argb >>  8) & 0xFF;
                    uint b =  argb        & 0xFF;
                    // Bgra8888: byte order in memory = B, G, R, A
                    // As uint32 little-endian: A<<24 | R<<16 | G<<8 | B
                    dst[i] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        if (!_hasFrame) return;

        WriteableBitmap? bmp;
        lock (_bitmapLock) bmp = _bitmap;
        if (bmp is null) return;

        double scaleX = Bounds.Width  / SnesWidth;
        double scaleY = Bounds.Height / SnesHeight;
        double scale  = Math.Min(scaleX, scaleY);

        double w  = SnesWidth  * scale;
        double h  = SnesHeight * scale;
        double ox = (Bounds.Width  - w) / 2;
        double oy = (Bounds.Height - h) / 2;

        context.DrawImage(bmp,
            new Rect(0, 0, SnesWidth, SnesHeight),
            new Rect(ox, oy, w, h));
    }
}
