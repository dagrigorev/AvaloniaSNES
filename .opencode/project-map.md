# Project map for Open Code agents

Start here, then jump to the right subsystem.

```text
Task received
├─ Need plan only? → @review or @snes-orchestrator
├─ CPU / 65C816 / addressing / interrupts → @emulator-core-agent
├─ Memory map / WRAM / I/O / DMA / HDMA → @emulator-core-agent + @ppu-rendering-agent for PPU registers
├─ PPU / tiles / sprites / palette / framebuffer → @ppu-rendering-agent
├─ APU / SPC700 / DSP / audio output → @apu-audio-agent
├─ Avalonia UI / MVVM / dialogs / UX → @avalonia-ui-agent
├─ Crash / hang / bad ROM behavior → @debugger-agent, then subsystem agent
├─ Tests / harness / regression snapshots → @test-verifier-agent
└─ Commit finished changes → @git-commit-agent
```

## Important files by subsystem

### CPU

- `src/SnesEmulator.Emulation/Cpu/Cpu65C816.cs`
- `src/SnesEmulator.Emulation/Cpu/CpuState.cs`
- `src/SnesEmulator.Emulation/Cpu/AddressingModes.cs`
- `tests/SnesEmulator.Emulation.Tests/CpuInstructionTests.cs`

### Memory / DMA

- `src/SnesEmulator.Emulation/Memory/MemoryBus.cs`
- `src/SnesEmulator.Emulation/Memory/WorkRam.cs`
- `tests/SnesEmulator.Emulation.Tests/MemoryBusTests.cs`
- `tests/SnesEmulator.Rendering.Tests/RenderingDiagnosticTests.cs`

### ROM

- `src/SnesEmulator.Hardware/Rom/RomLoader.cs`
- `src/SnesEmulator.Core/Models/RomModels.cs`
- `tests/SnesEmulator.Hardware.Tests/RomLoaderTests.cs`

### PPU / Rendering

- `src/SnesEmulator.Graphics/Ppu/Ppu.cs`
- `src/SnesEmulator.Graphics/Framebuffer/SnesFrameBuffer.cs`
- `src/SnesEmulator.Core/Interfaces/IPpu.cs`
- `tests/SnesEmulator.Rendering.Tests/RenderingDiagnosticTests.cs`

### APU / Audio

- `src/SnesEmulator.Audio/Apu/Apu.cs`
- `src/SnesEmulator.Core/Interfaces/IHardwareInterfaces.cs`

### Input

- `src/SnesEmulator.Input/Controllers/Controllers.cs`
- `src/SnesEmulator.Core/Models/InputModels.cs`
- Desktop keyboard handling in `src/SnesEmulator.Desktop/Views/MainWindow.axaml.cs`

### Facade / Loop

- `src/SnesEmulator.Infrastructure/DependencyInjection/SnesEmulatorFacade.cs`
- `src/SnesEmulator.Emulation/Timing/EmulationLoop.cs`
- `src/SnesEmulator.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`

### Desktop

- `src/SnesEmulator.Desktop/ViewModels/MainViewModel.cs`
- `src/SnesEmulator.Desktop/ViewModels/DiagnosticViewModels.cs`
- `src/SnesEmulator.Desktop/Controls/GameScreen.cs`
- `src/SnesEmulator.Desktop/Views/MainWindow.axaml`
- `src/SnesEmulator.Desktop/App.axaml`

## Verification matrix

| Change area | Minimum verification |
|---|---|
| Core models/utilities | `dotnet test tests/SnesEmulator.Core.Tests -c Debug` |
| CPU/addressing | `dotnet test tests/SnesEmulator.Emulation.Tests -c Debug --filter CpuInstructionTests` |
| Memory/DMA | `dotnet test tests/SnesEmulator.Emulation.Tests -c Debug --filter MemoryBusTests` |
| ROM loader | `dotnet test tests/SnesEmulator.Hardware.Tests -c Debug` |
| PPU/rendering | `dotnet test tests/SnesEmulator.Rendering.Tests -c Debug` |
| Save states | `dotnet test tests/SnesEmulator.Emulation.Tests -c Debug --filter SaveStateManagerTests` |
| Desktop UI | `dotnet build src/SnesEmulator.Desktop/SnesEmulator.Desktop.csproj -c Debug` plus manual app smoke test |
| Cross-cutting | `dotnet build SnesEmulator.sln -c Debug` and `dotnet test SnesEmulator.sln -c Debug --no-build` |
