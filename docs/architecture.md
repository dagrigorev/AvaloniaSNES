# Architecture

## Overview

The emulator is structured as an 8-layer solution following clean architecture principles. The dependency graph flows inward: outer layers depend on inner ones, never the reverse.

```
SnesEmulator.Desktop
       │
SnesEmulator.Infrastructure  ← Facade, DI wiring, logging
       │
 ┌─────┴──────┬──────────────┬────────────┐
 │            │              │            │
Emulation   Hardware      Graphics     Audio    Input
(CPU/Mem)  (ROM/Bus)     (PPU/FB)    (APU)   (Ctrl)
 │            │              │            │
 └─────────────────────┬─────────────────┘
                       │
               SnesEmulator.Core
           (interfaces, models, exceptions)
```

## Layer Responsibilities

### `SnesEmulator.Core`
Pure domain layer. No external dependencies.
- **Interfaces** — `IEmulator`, `ICpu`, `IPpu`, `IApu`, `IMemoryBus`, `IController`, etc.
- **Models** — `CpuRegisters` (immutable record), `RomData`, `RomHeader`, `SnesButton`, event args
- **Exceptions** — `RomLoadException`, `InvalidOpcodeException`, `SaveStateException`
- **Utilities** — `BitHelper` (bit manipulation inlines)
- **Constants** — `SnesConstants` (timing, memory sizes, vectors)

### `SnesEmulator.Emulation`
All CPU and memory emulation logic.
- **`Cpu65C816`** — 65C816 interpreter with 256-entry dispatch table (delegate array). All opcodes handled; unimplemented ones log a warning and return a safe cycle count.
- **`CpuState`** — Mutable working register file.
- **`AddressingModes`** — Resolves 24-bit effective addresses for all 65C816 modes.
- **`MemoryBus`** — Routes reads/writes across WRAM, ROM, I/O registers, APU ports. Handles DMA.
- **`WorkRam`** — 128 KB backing store + WRAM port ($2180–$2183).
- **`EmulationLoop`** — Fixed-timestep frame runner; drives CPU/PPU/APU co-scheduling.
- **`SaveStateManager`** — Binary serialization of all `IStateful` components.

### `SnesEmulator.Hardware`
ROM loading and format detection.
- **`RomLoader`** — Loads `.smc`/`.sfc`, strips copier headers, scores LoROM vs HiROM candidates, parses the 32-byte internal header.

### `SnesEmulator.Graphics`
PPU emulation and rendering.
- **`Ppu`** — Full register model ($2100–$213F), VRAM/CGRAM/OAM, BG tile rendering (2bpp/4bpp), V-blank/H-blank timing, `FrameReady` event.
- **`SnesFrameBuffer`** — 256×224 ARGB32 pixel buffer + BGR555→ARGB conversion.

### `SnesEmulator.Audio`
APU architecture.
- **`Apu`** — SPC700 communication port model, IPL ROM stub, audio buffer API.

### `SnesEmulator.Input`
Controller emulation.
- **`SnesController`** — 16-bit shift register model for serial joypad reads.
- **`InputManager`** — Maps host key names to `SnesButton` flags using a configurable dictionary.

### `SnesEmulator.Infrastructure`
Cross-cutting concerns.
- **`SnesEmulatorFacade`** — Orchestrates all subsystems; implements `IEmulator` facade.
- **`ServiceCollectionExtensions`** — Wires the DI container (singleton lifetimes for all hardware).
- **`DiagnosticLogSink`** — Thread-safe circular log buffer; raises `LogAdded` event for the UI.

### `SnesEmulator.Desktop`
Avalonia UI.
- **MVVM** — `MainViewModel` (commands, state), `CpuStateViewModel`, `LogViewModel`
- **`GameScreen`** — Custom control; subscribes to `IPpu.FrameReady`, converts ARGB→BGRA, renders to `WriteableBitmap` scaled 4:3
- **`MainWindow.axaml`** — Toolbar, menu bar, game area, CPU panel, log panel, status bar

## Design Decisions

### Opcode Table vs Switch Statement
The 65C816 decoder uses a `Func<int>[]` table (256 slots) rather than a giant `switch`. This gives O(1) dispatch, is trivially extensible (just assign a new delegate), and avoids JIT issues with enormous switch statements.

### Immutable CpuRegisters vs Mutable CpuState
`CpuState` is mutable for performance (no allocation per cycle). `CpuRegisters` (record) is the immutable snapshot returned by `ICpu.Registers` for diagnostics and save states. `ToSnapshot()` / `FromSnapshot()` handle conversion.

### Event-Driven Frame Output
The PPU raises `FrameReady` when a frame is complete. The UI's `GameScreen` control subscribes and marshals to the UI thread. This decouples emulation speed from UI rendering, and allows headless use in tests without a display.

### DI with Singleton Lifetimes
All hardware components are singletons — there is exactly one of each per emulator instance. The `SnesEmulatorFacade` owns the run loop and manages the lifecycle.

### Memory Bus Routing
`MemoryBus.Read/Write` handles the full SNES 24-bit address space in a single method with a structured chain of range checks. Routing order follows SNES hardware priority (WRAM mirrors → I/O registers → ROM).

## Testing Strategy

- **Unit tests** test each subsystem in isolation using `NullLogger` and mock implementations.
- **CPU tests** use a `FlatMemoryBus` (simple byte array) to avoid dependency on the real bus.
- **ROM loader tests** construct minimal synthetic ROM images in memory.
- **Save state tests** use `Moq` to verify that `LoadState` is called with the exact data previously saved.
