using Microsoft.Extensions.Logging;
using SnesEmulator.Core;
using SnesEmulator.Core.Interfaces;

namespace SnesEmulator.Emulation.Timing;

/// <summary>
/// The main emulation loop — coordinates CPU, PPU, and APU execution.
///
/// SNES timing:
///   Master clock: 21.477272 MHz (NTSC)
///   CPU: runs at masterClock / 6 (fast) or / 8 (slow) cycles
///   PPU: 1 dot = 4 master clocks; 341 dots per scanline; 262 scanlines/frame
///   APU: runs at masterClock / 21 (≈1.024 MHz)
///   Target: ~60.098 fps (NTSC)
///
/// The loop uses a fixed-timestep approach:
///   Execute CPU instructions in batches, driving PPU/APU by their
///   proportional master clock share. This isn't cycle-accurate but
///   maintains correct average timing.
/// </summary>
public sealed class EmulationLoop
{
    private readonly ICpu _cpu;
    private readonly IPpu _ppu;
    private readonly IApu _apu;
    private readonly ILogger<EmulationLoop> _logger;

    // How many master clock cycles to execute per "batch" (one loop iteration)
    // ~1/60th second worth of master clocks = 21_477_272 / 60 ≈ 357_954
    private const int MasterCyclesPerFrame = 357_954;

    // CPU cycles in master clocks (slow ROM region = 8 master cycles per CPU cycle)
    private const int CpuCyclesMultiplier = SnesConstants.CpuSlowCycles;

    // NMI is triggered once per frame at V-blank start
    private bool _nmiTriggered;

    // Accumulated master cycles for this frame
    private long _frameCycleAccum;

    public EmulationLoop(ICpu cpu, IPpu ppu, IApu apu, ILogger<EmulationLoop> logger)
    {
        _cpu = cpu;
        _ppu = ppu;
        _apu = apu;
        _logger = logger;
    }

    /// <summary>
    /// Executes one full frame worth of emulation.
    /// Returns the number of master clock cycles consumed.
    ///
    /// This is called by the emulator's run loop at ~60 fps.
    /// </summary>
    public long RunFrame()
    {
        long cyclesThisFrame = 0;
        _nmiTriggered = false;

        while (cyclesThisFrame < MasterCyclesPerFrame)
        {
            // Step the CPU by one instruction
            int cpuInstrCycles = _cpu.Step();
            int masterCycles   = cpuInstrCycles * CpuCyclesMultiplier;

            // Advance PPU by the same number of master cycles
            _ppu.Clock(masterCycles);

            // Advance APU proportionally
            _apu.Clock(masterCycles);

            cyclesThisFrame += masterCycles;

            // Trigger NMI once per frame when PPU enters V-blank
            if (!_nmiTriggered && _ppu.IsVBlank)
            {
                _cpu.TriggerNmi();
                _nmiTriggered = true;
            }
        }

        _frameCycleAccum += cyclesThisFrame;
        return cyclesThisFrame;
    }

    /// <summary>
    /// Executes exactly one CPU instruction (for step-debug mode).
    /// Also advances PPU/APU proportionally.
    /// </summary>
    public int StepOne()
    {
        int cpuCycles  = _cpu.Step();
        int masterCycles = cpuCycles * CpuCyclesMultiplier;
        _ppu.Clock(masterCycles);
        _apu.Clock(masterCycles);
        return masterCycles;
    }
}
