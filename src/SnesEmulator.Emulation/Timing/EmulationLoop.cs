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

    private int _currentFrame;
    private int _currentScanline;
    private int _currentScanlineMasterCycles;

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
        _currentFrame = 0;
        _currentScanline = 0;
        _currentScanlineMasterCycles = 0;
        ApplyScanlineState();
        _bus.ClearNmiFlag();
    }

    public long RunFrame()
    {
        long totalMasterCycles = 0;
        int startFrame = _currentFrame;

        do
        {
            totalMasterCycles += StepOne();
        } while (_currentFrame == startFrame);

        return totalMasterCycles;
    }

    public int StepOne()
    {
        ApplyScanlineState();

        if (_bus.ConsumeNmi())
            _cpu.TriggerNmi();

        int cpuCycles    = _cpu.Step();
        int masterCycles = cpuCycles * CpuCyclesMultiplier;
        _ppu.Clock(masterCycles);
        _apu.Clock(masterCycles);

        AdvanceTiming(masterCycles);
        return masterCycles;
    }

    private void ApplyScanlineState()
    {
        bool inVBlank = _currentScanline >= VBlankStartScanline;
        // Use a wider synthetic HBlank window so ROMs polling $4212 do not miss it between CPU steps.
        bool inHBlank = _currentScanlineMasterCycles >= (MasterCyclesPerScanline - 256);
        _bus.SetHvBjoy(inVBlank, inHBlank);
        _bus.SetVBlankState(inVBlank, _currentFrame, _currentScanline);
    }

    private void AdvanceTiming(int masterCycles)
    {
        _currentScanlineMasterCycles += masterCycles;

        while (_currentScanlineMasterCycles >= MasterCyclesPerScanline)
        {
            bool inVBlank = _currentScanline >= VBlankStartScanline;
            _bus.SetHvBjoy(inVBlank, inHBlank: true);

            _currentScanlineMasterCycles -= MasterCyclesPerScanline;
            _currentScanline++;

            if (_currentScanline >= ScanlinesPerFrame)
            {
                _currentScanline = 0;
                _currentFrame++;
                _bus.ClearNmiFlag();
            }

            ApplyScanlineState();
        }
    }
}
