using Microsoft.Extensions.Logging;
using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Emulation.Memory;

namespace SnesEmulator.Emulation.Timing;

/// <summary>
/// Scanline-accurate emulation loop.
///
/// SNES frame timing (NTSC):
///   Scanlines 0–224:   Active display  → $4212 bit7=0, game logic runs
///   Scanline  225:     VBlank start    → $4212 bit7=1, NMI fires (if enabled)
///   Scanlines 226–261: VBlank          → PPU register uploads
///   Scanline  0 next:  New frame start → $4212 bit7=0, NMI flag cleared
///
/// SMW boot sequence relies on:
///   1. $4212 bit7=0 during active display so VBlank-wait loops exit
///   2. NMI flag in $4210 only set for the CURRENT frame's VBlank
///   3. NMI vector fired once at scanline 225 when NMITIMEN bit7=1
/// </summary>
public sealed class EmulationLoop
{
    private readonly ICpu      _cpu;
    private readonly IPpu      _ppu;
    private readonly IApu      _apu;
    private readonly MemoryBus _bus;
    private readonly ILogger<EmulationLoop> _logger;

    private const int MasterCyclesPerScanline = 1364;  // 341 dots × 4
    private const int ScanlinesPerFrame       = 262;
    private const int VBlankStartScanline     = 225;
    private const int CpuCyclesMultiplier     = SnesConstants.CpuSlowCycles;

    public bool NmiEnabled { get; set; } = false;

    public EmulationLoop(ICpu cpu, IPpu ppu, IApu apu, MemoryBus bus,
                         ILogger<EmulationLoop> logger)
    {
        _cpu    = cpu;
        _ppu    = ppu;
        _apu    = apu;
        _bus    = bus;
        _logger = logger;
    }

    public void Reset()
    {
        NmiEnabled = false;
        // Start with screen in active display state (not VBlank)
        _bus.SetHvBjoy(inVBlank: false, inHBlank: false);
        _bus.ClearNmiFlag();
    }

    public long RunFrame()
    {
        long totalMasterCycles = 0;

        // Clear NMI flag at start of each new frame (scanline 0)
        // This ensures the flag only reflects THIS frame's VBlank
        _bus.ClearNmiFlag();

        for (int sl = 0; sl < ScanlinesPerFrame; sl++)
        {
            bool inVBlank = sl >= VBlankStartScanline;

            // Update $4212 at scanline start: HBlank=false, VBlank per scanline
            _bus.SetHvBjoy(inVBlank, inHBlank: false);

            // Fire NMI exactly once: at the first scanline of VBlank
            if (sl == VBlankStartScanline && NmiEnabled)
                _cpu.TriggerNmi();

            // Set NMI flag in $4210 when VBlank starts (readable even without NMI interrupt)
            if (sl == VBlankStartScanline)
                _bus.SetNmiFlag();

            // Run CPU instructions for this scanline
            int scanlineCycles = 0;
            while (scanlineCycles < MasterCyclesPerScanline)
            {
                int cpuCycles    = _cpu.Step();
                int masterCycles = cpuCycles * CpuCyclesMultiplier;
                _ppu.Clock(masterCycles);
                _apu.Clock(masterCycles);
                scanlineCycles    += masterCycles;
                totalMasterCycles += masterCycles;
            }

            // Set HBlank at end of scanline
            _bus.SetHvBjoy(inVBlank, inHBlank: true);
        }

        return totalMasterCycles;
    }

    public int StepOne()
    {
        int cpuCycles    = _cpu.Step();
        int masterCycles = cpuCycles * CpuCyclesMultiplier;
        _ppu.Clock(masterCycles);
        _apu.Clock(masterCycles);
        return masterCycles;
    }
}
