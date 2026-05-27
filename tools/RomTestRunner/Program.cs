using Microsoft.Extensions.Logging.Abstractions;
using SnesEmulator.Core.Interfaces;
using SnesEmulator.Core.Models;
using SnesEmulator.Emulation.Cpu;
using SnesEmulator.Emulation.Memory;
using SnesEmulator.Emulation.Timing;
using SnesEmulator.Graphics.Ppu;
using SnesEmulator.Audio.Apu;
using SnesEmulator.Hardware.Rom;
using SnesEmulator.Input.Controllers;

string path = @"D:\sources\repos\personal\AvaloniaSNES\smw.sfc";
if (!File.Exists(path))
{
    Console.WriteLine($"File not found: {path}");
    return 1;
}

var bus = new MemoryBus(new WorkRam(), new StubInputManager(), NullLogger<MemoryBus>.Instance);
var romLoader = new RomLoader(NullLogger<RomLoader>.Instance);
var romData = romLoader.LoadRom(path);
bus.LoadRom(romData);

var cpu = new Cpu65C816(bus, NullLogger<Cpu65C816>.Instance);
var ppu = new Ppu(NullLogger<Ppu>.Instance);
var apu = new Apu(NullLogger<Apu>.Instance);
bus.AttachDevices(ppu, apu);
cpu.Reset();
ppu.Reset();
apu.Reset();
bus.Reset();
var loop = new EmulationLoop(cpu, ppu, apu, bus, NullLogger<EmulationLoop>.Instance);
loop.Reset();

// Check if frame counter $13 changes (inc at main loop 8070)
// And check $0D9B changes
Console.WriteLine("Frame | PC      A     X     Y     $10 $13 $0D9B NMIEN");
Console.WriteLine("------+---------------------------------------------");
for (int i = 0; i < 200; i++)
{
    loop.RunFrame();
    var r = cpu.Registers;
    uint wram10 = bus.Read(0x000010);
    uint wram13 = bus.Read(0x000013);
    uint d9b = bus.Read(0x000D9B);
    if (i < 80 || i >= 190)
        Console.WriteLine($"{i+1,5} | {r.PC:X4} {r.A:X4} {r.X:X4} {r.Y:X4}  {wram10:X2}  {wram13:X2}   {d9b:X2}    {bus.NmitimenRead():X2}");
}

return 0;

internal sealed class StubInputManager : IInputManager
{
    public IController GetController(int port) => new SnesController();
    public void HandleKeyDown(string key) { }
    public void HandleKeyUp(string key) { }
    public void UpdateMappings(InputMappingConfig key) { }
}
