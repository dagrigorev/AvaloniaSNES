using SnesEmulator.Core.Models;

namespace SnesEmulator.Core.Interfaces;

/// <summary>
/// Abstraction for the SNES CPU (65C816).
/// Provides fetch-decode-execute stepping and register access for diagnostics.
/// </summary>
public interface ICpu : IEmulatorComponent, IStateful
{
    /// <summary>
    /// Current state of all CPU registers.
    /// Used for diagnostics, debugger display, and save states.
    /// </summary>
    CpuRegisters Registers { get; }

    /// <summary>
    /// Total master clock cycles elapsed since last reset.
    /// Used for synchronizing CPU with PPU/APU.
    /// </summary>
    long TotalCycles { get; }

    /// <summary>
    /// Fetches, decodes, and executes a single instruction.
    /// Returns the number of master clock cycles consumed.
    /// </summary>
    int Step();

    /// <summary>
    /// Triggers a Non-Maskable Interrupt (NMI).
    /// On SNES, this is triggered by PPU V-blank.
    /// </summary>
    void TriggerNmi();

    /// <summary>
    /// Triggers a maskable hardware interrupt (IRQ).
    /// Only fires if the I flag in the status register is clear.
    /// </summary>
    void TriggerIrq();

    /// <summary>Gets a human-readable disassembly of the instruction at the given address.</summary>
    string Disassemble(uint address);
}
