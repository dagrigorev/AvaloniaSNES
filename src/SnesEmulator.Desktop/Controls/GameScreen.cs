using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SnesEmulator.Core.Models;
using SnesEmulator.Desktop.ViewModels;

namespace SnesEmulator.Desktop.Controls;

/// <summary>
/// Custom Avalonia control that renders the SNES PPU framebuffer.
///
/// The PPU raises FrameReady with ARGB32 pixel data on the emulation thread.
/// This control converts that to a WriteableBitmap and paints it scaled to
/// fit the control, maintaining the SNES native 8:7 (approx 4:3) aspect ratio.
/// </summary>
public sealed class GameScreen : Control
{
    private const int SnesWidth  = 256;
    private const int SnesHeight = 224;

    private WriteableBitmap? _bitmap;
    private readonly object  _bitmapLock = new();
    private volatile bool    _hasFrame;

    // ── Attach to ViewModel's Emulator ────────────────────────────────────────

    /// <summary>
    /// Wires the GameScreen to the emulator's PPU FrameReady event.
    /// The ViewModel exposes the emulator's PPU directly.
    /// </summary>
    public void AttachToViewModel(MainViewModel vm)
    {
        vm.AttachGameScreen(this);
    }

    /// <summary>Called by MainViewModel to subscribe to PPU frames.</summary>
    public void SubscribeToPpu(Core.Interfaces.IPpu ppu)
    {
        ppu.FrameReady += OnFrameReady;
    }

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
                uint* ptr = (uint*)fb.Address;
                for (int i = 0; i < pixels.Count; i++)
                {
                    // Convert ARGB (emulator) → BGRA (Avalonia Bgra8888)
                    uint argb = pixels[i];
                    uint a = (argb >> 24) & 0xFF;
                    uint r = (argb >> 16) & 0xFF;
                    uint g = (argb >>  8) & 0xFF;
                    uint b = argb         & 0xFF;
                    ptr[i] = (a << 24) | (r << 16) | (g << 8) | b;
                    // Note: Avalonia's Bgra8888 is actually stored as B,G,R,A in memory
                    // but the PixelFormat name refers to logical channel order.
                    // The above maps ARGB → BGRA correctly for display.
                }
            }
        }
    }

    // ── Avalonia rendering ────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);

        if (!_hasFrame) return;

        WriteableBitmap? bitmap;
        lock (_bitmapLock) bitmap = _bitmap;
        if (bitmap is null) return;

        // Scale to fill available space, maintaining 8:7 pixel aspect ratio
        double scaleX = Bounds.Width  / SnesWidth;
        double scaleY = Bounds.Height / SnesHeight;
        double scale  = Math.Min(scaleX, scaleY);

        double drawW = SnesWidth  * scale;
        double drawH = SnesHeight * scale;
        double ox    = (Bounds.Width  - drawW) / 2;
        double oy    = (Bounds.Height - drawH) / 2;

        var destRect = new Rect(ox, oy, drawW, drawH);
        var srcRect  = new Rect(0, 0, SnesWidth, SnesHeight);

        context.DrawImage(bitmap, srcRect, destRect);
    }
}
