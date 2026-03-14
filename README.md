# SNES Emulator

A **Super Nintendo Entertainment System** emulator built from scratch in **C# 10 / .NET 8**, featuring a modern multi-layer architecture, a full 65C816 CPU instruction set, tile-based PPU rendering, and a polished Avalonia desktop UI.

---

## Screenshots

```
┌──────────────────────────────────────────────────────────────┐
│  File  Emulator  Help                              [−][□][×] │
├──────────────────────────────────────────────────────────────┤
│ [📂 Open] [▶ Run] [⏸ Pause] [↺ Reset] [⏭ Step] [💾] [📤]   │
├───────────────────────────────────────┬──────────────────────┤
│                                       │ CPU REGISTERS        │
│                                       │ PC:  00:8000         │
│          S N E S                      │ A:   0000            │
│        E M U L A T O R               │ X:   0000            │
│                                       │ Y:   0000            │
│      Open a ROM to start              │ SP:  01FF            │
│      File → Open ROM  or  Ctrl+O     │ DP:  0000            │
│                                       │ DBR: 00              │
│                                       │ P:   NvMXdIzc        │
├───────────────────────────────────────┤ E:   1 (6502)        │
│ DIAGNOSTIC LOG                 [Clear]│──────────────────────│
│ 12:34:56.789 INF RomLoader ROM loaded │ CONTROLS            │
│ 12:34:56.790 INF Cpu65C816  Reset ... │ D-Pad  Arrow Keys   │
└───────────────────────────────────────┴──────────────────────┘
```

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10+, macOS 12+, or Linux (Ubuntu 20.04+)
- Git

---

## Quick Start

```bash
# Clone
git clone https://github.com/yourname/SnesEmulator.git
cd SnesEmulator

# Restore & Build
dotnet restore
dotnet build

# Run the emulator
dotnet run --project src/SnesEmulator.Desktop

# Run tests
dotnet test
```

---

## Project Structure

```
SnesEmulator/
├── SnesEmulator.sln
├── Directory.Build.props        # Shared build config (.NET 8, C# 12, nullable)
├── .editorconfig
├── .gitignore
│
├── src/
│   ├── SnesEmulator.Core/          # Domain models, interfaces, constants
│   │   ├── Interfaces/             # IEmulator, ICpu, IPpu, IApu, IMemoryBus …
│   │   ├── Models/                 # CpuRegisters, RomData, SnesButton …
│   │   ├── Exceptions/             # RomLoadException, InvalidOpcodeException …
│   │   ├── Utilities/              # BitHelper
│   │   └── SnesConstants.cs        # Timing, sizes, vectors
│   │
│   ├── SnesEmulator.Emulation/     # CPU + Memory subsystem
│   │   ├── Cpu/                    # Cpu65C816, CpuState, AddressingModes
│   │   ├── Memory/                 # MemoryBus, WorkRam
│   │   ├── Timing/                 # EmulationLoop (frame runner)
│   │   └── SaveState/              # SaveStateManager
│   │
│   ├── SnesEmulator.Hardware/      # ROM loading and mapping
│   │   └── Rom/                    # RomLoader (LoROM / HiROM detection)
│   │
│   ├── SnesEmulator.Graphics/      # PPU and framebuffer
│   │   ├── Ppu/                    # Ppu (register model, tile renderer)
│   │   └── Framebuffer/            # SnesFrameBuffer, color conversion
│   │
│   ├── SnesEmulator.Audio/         # APU subsystem
│   │   └── Apu/                    # Apu (SPC700 stub, IPL handshake)
│   │
│   ├── SnesEmulator.Input/         # Controller emulation
│   │   └── Controllers/            # SnesController, InputManager
│   │
│   ├── SnesEmulator.Infrastructure/ # DI, logging, orchestration
│   │   ├── DependencyInjection/    # ServiceCollectionExtensions, SnesEmulatorFacade
│   │   └── Logging/                # DiagnosticLogSink
│   │
│   └── SnesEmulator.Desktop/       # Avalonia UI
│       ├── Views/                  # MainWindow.axaml + code-behind
│       ├── ViewModels/             # MainViewModel, CpuStateViewModel, LogViewModel
│       ├── Controls/               # GameScreen (PPU render surface)
│       ├── App.axaml               # Application + styles
│       └── Program.cs              # Entry point
│
├── tests/
│   ├── SnesEmulator.Core.Tests/    # BitHelper, models, framebuffer tests
│   ├── SnesEmulator.Emulation.Tests/ # CPU instructions, memory bus, save states
│   └── SnesEmulator.Hardware.Tests/  # ROM loader tests
│
├── docs/
│   ├── architecture.md
│   ├── emulation-notes.md
│   └── roadmap.md
│
└── assets/
    └── icons/
```

---

## Building

```bash
# Debug build
dotnet build -c Debug

# Release build
dotnet build -c Release

# Run the desktop app
dotnet run --project src/SnesEmulator.Desktop -c Release

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/SnesEmulator.Emulation.Tests
dotnet test tests/SnesEmulator.Hardware.Tests
dotnet test tests/SnesEmulator.Core.Tests
```

---

## Loading a ROM

1. Launch the emulator
2. Press `Ctrl+O` or go to **File → Open ROM**
3. Select a `.smc` or `.sfc` ROM file
4. Press `F1` or click **▶ Run** to start emulation

> **Note:** You must supply your own legally-obtained ROM files. ROM files are not included.

---

## Controls (Default Keyboard Mapping)

| SNES Button | Key         |
|-------------|-------------|
| B           | Z           |
| Y           | A           |
| A           | X           |
| X           | S           |
| L           | Q           |
| R           | W           |
| Start       | Enter       |
| Select      | Backspace   |
| D-Pad Up    | ↑           |
| D-Pad Down  | ↓           |
| D-Pad Left  | ←           |
| D-Pad Right | →           |

---

## Debug Controls

| Action       | Shortcut |
|--------------|----------|
| Run          | F1       |
| Pause        | F2       |
| Reset        | F3       |
| Step         | F6       |
| Save State   | F5       |
| Load State   | F7       |

---

## Currently Implemented

- ✅ **ROM Loading** — `.smc` / `.sfc`, LoROM + HiROM detection, header parsing, checksum validation
- ✅ **CPU** — Full 65C816 instruction set (all documented opcodes including rare ones), native and emulation mode, interrupts (NMI/IRQ/BRK/COP), DMA support
- ✅ **Memory Bus** — WRAM (128 KB), ROM mapping (LoROM/HiROM), SRAM, I/O register routing, simplified DMA
- ✅ **PPU** — Register model ($2100–$213F), VRAM/CGRAM/OAM, BG Mode 0 (2bpp tiles) and Mode 1 (4bpp), V-blank/H-blank, framebuffer output
- ✅ **APU** — SPC700 architecture, IPL ROM handshake stub, communication ports
- ✅ **Input** — Keyboard-to-controller mapping, configurable bindings
- ✅ **Emulation Loop** — Fixed-timestep ~60 fps, CPU/PPU/APU co-scheduling
- ✅ **Save/Load State** — Binary serialization of all stateful components
- ✅ **Desktop UI** — Avalonia MVVM, SNES-themed dark UI, game screen, CPU panel, log panel
- ✅ **Diagnostics** — Thread-safe log sink, CPU register display, PPU status bar

---

## Known Limitations

- **PPU** — Only BG Mode 0 and Mode 1 are rendered; Modes 2–7, HDMA, and per-pixel color math are not yet complete
- **Sprites (OBJ)** — OAM structure is in place but sprite rendering pipeline is not yet implemented
- **APU** — SPC700 CPU is not fully emulated; audio output is silence; IPL handshake stub allows initialization to proceed
- **Cycle accuracy** — Instruction-level timing is correct; sub-instruction (micro-op) accuracy and open-bus behavior are approximated
- **SRAM persistence** — Save to disk is stubbed but battery-save auto-load is not yet wired to the UI
- **Controller** — Keyboard only; no gamepad/joystick support yet
- **Overscan / hi-res modes** — 512-wide and interlaced modes are not rendered

---

## Architecture Summary

See [`docs/architecture.md`](docs/architecture.md) for the full design rationale.

**Key patterns used:**
- **Facade** — `SnesEmulatorFacade` / `IEmulator` hides subsystem complexity from the UI
- **Strategy** — ROM mapping (LoROM vs HiROM) as pluggable logic inside `MemoryBus`
- **Observer** — PPU `FrameReady`, emulator `StateChanged` events drive UI updates
- **MVVM** — Avalonia + ReactiveUI; ViewModels never reference View types
- **DI** — `Microsoft.Extensions.DependencyInjection` wires the entire object graph

---

## License

MIT — see `LICENSE` file.
