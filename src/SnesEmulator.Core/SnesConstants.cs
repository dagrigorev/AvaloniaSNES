namespace SnesEmulator.Core;

/// <summary>
/// SNES hardware timing and size constants.
/// All values are based on NTSC timing; PAL has slightly different values.
///
/// Reference: https://wiki.superfamicom.org/timing
/// </summary>
public static class SnesConstants
{
    // ── Master Clock ─────────────────────────────────────────────────────────
    /// <summary>NTSC master clock frequency in Hz (21.477272 MHz).</summary>
    public const double NtscMasterClockHz = 21_477_272.0;

    /// <summary>PAL master clock frequency in Hz (21.281370 MHz).</summary>
    public const double PalMasterClockHz = 21_281_370.0;

    // ── CPU ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Number of master clock cycles per CPU "slow" cycle (2.68 MHz region).
    /// Most ROM and I/O accesses use 8 master clocks per cycle.
    /// </summary>
    public const int CpuSlowCycles = 8;

    /// <summary>Number of master clocks per CPU "fast" cycle (3.58 MHz region, ROM banks 80-FF).</summary>
    public const int CpuFastCycles = 6;

    /// <summary>Number of master clocks per CPU "extra slow" cycle (I/O registers).</summary>
    public const int CpuExtraSlowCycles = 12;

    // ── PPU / Display ────────────────────────────────────────────────────────
    /// <summary>Visible pixels per scanline.</summary>
    public const int ScreenWidth = 256;

    /// <summary>Visible scanlines per frame (NTSC).</summary>
    public const int ScreenHeightNtsc = 224;

    /// <summary>Visible scanlines per frame (PAL).</summary>
    public const int ScreenHeightPal = 239;

    /// <summary>Total scanlines per frame including V-blank (NTSC).</summary>
    public const int TotalScanlinesNtsc = 262;

    /// <summary>Total scanlines per frame including V-blank (PAL).</summary>
    public const int TotalScanlinesPal = 312;

    /// <summary>Total dots (master clock ticks) per scanline.</summary>
    public const int DotsPerScanline = 341;

    /// <summary>Scanline at which V-blank starts (NTSC).</summary>
    public const int VBlankStartNtsc = 225;

    /// <summary>NTSC target frame rate.</summary>
    public const double NtscFrameRate = NtscMasterClockHz / (DotsPerScanline * 4 * TotalScanlinesNtsc);

    // ── Memory Map ───────────────────────────────────────────────────────────
    /// <summary>Size of WRAM (Work RAM): 128 KB.</summary>
    public const int WramSize = 0x20000; // 128 KB

    /// <summary>Size of VRAM: 64 KB.</summary>
    public const int VramSize = 0x10000; // 64 KB

    /// <summary>Size of OAM (Object Attribute Memory): 544 bytes.</summary>
    public const int OamSize = 0x220;    // 544 bytes

    /// <summary>Size of CGRAM (Color Graphics RAM / palette): 512 bytes.</summary>
    public const int CgramSize = 0x200;  // 512 bytes = 256 colors × 2 bytes

    // ── ROM Header Offsets ────────────────────────────────────────────────────
    /// <summary>LoROM header offset within the ROM data.</summary>
    public const int LoRomHeaderOffset = 0x7FC0;

    /// <summary>HiROM header offset within the ROM data.</summary>
    public const int HiRomHeaderOffset = 0xFFC0;

    /// <summary>Size of the optional copier header added by some backup devices.</summary>
    public const int CopierHeaderSize = 512;

    // ── Controller ───────────────────────────────────────────────────────────
    /// <summary>Number of serial bits in one joypad read (16 buttons + 16 padding).</summary>
    public const int JoypadSerialBits = 16;

    // ── Interrupt Vectors (LoROM native mode) ─────────────────────────────────
    public const ushort NativeNmiVector   = 0xFFEA;
    public const ushort NativeResetVector = 0xFFFC;
    public const ushort NativeIrqVector   = 0xFFEE;
    public const ushort EmuNmiVector      = 0xFFFA;
    public const ushort EmuResetVector    = 0xFFFC;
    public const ushort EmuIrqVector      = 0xFFFE;
}
