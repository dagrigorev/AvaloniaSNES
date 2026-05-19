using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Emulation.Cpu;
using SnesEmulator.Emulation.Memory;

namespace SnesEmulator.Emulation.Tests.Helpers;

public sealed class EmulationHarness
{
    private readonly IMemoryBus _bus;
    private readonly Cpu65C816 _cpu;
    private readonly MemoryBus? _realBus;
    private readonly RecordingPpu? _ppu;

    public EmulationHarness(byte[] program, ushort resetVector)
    {
        var mem = new TestMemory();
        for (int i = 0; i < program.Length; i++)
            mem[resetVector + i] = program[i];
        mem[0xFFFC] = (byte)(resetVector & 0xFF);
        mem[0xFFFD] = (byte)(resetVector >> 8);

        _bus = new FlatMemoryBus(mem);
        _cpu = new Cpu65C816(_bus, NullLogger<Cpu65C816>.Instance);
        _cpu.Reset();
    }

    public EmulationHarness(RomData romData)
    {
        var wram = new WorkRam();
        wram.Reset();
        _realBus = new MemoryBus(wram, new TestInputManager(), NullLogger<MemoryBus>.Instance);
        _ppu = new RecordingPpu();
        _realBus.AttachDevices(_ppu, new StubApu());
        _realBus.LoadRom(romData);
        _realBus.Reset();

        _bus = _realBus;
        _cpu = new Cpu65C816(_bus, NullLogger<Cpu65C816>.Instance);
        _cpu.Reset();
    }

    public Cpu65C816 Cpu => _cpu;
    public CpuRegisters Registers => _cpu.Registers;
    public long TotalCycles => _cpu.TotalCycles;
    public IMemoryBus Bus => _bus;
    public IPpu? Ppu => _ppu;

    public int StepOnce() => _cpu.Step();

    public int Step(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += _cpu.Step();
        return total;
    }

    public int StepUntil(Func<CpuRegisters, bool> predicate, int maxSteps = 10000)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            _cpu.Step();
            if (predicate(_cpu.Registers))
                return i + 1;
        }
        throw new InvalidOperationException($"Condition not met within {maxSteps} steps. Last PC = ${_cpu.Registers.PC:X4}");
    }

    public byte ReadByte(uint address) => _bus.Read(address);
    public void WriteByte(uint address, byte value) => _bus.Write(address, value);
    public ushort ReadWord(uint address) => _bus.ReadWord(address);
    public void WriteWord(uint address, ushort value) => _bus.WriteWord(address, value);
}
