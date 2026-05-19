# AvaloniaSNES — detailed project analysis for AI agents

## Executive summary

AvaloniaSNES is a layered .NET 8 emulator project for the Super Nintendo Entertainment System. It is already structured like a real maintainable emulator: domain contracts are separated from emulation, graphics, audio, input, hardware loading, infrastructure, UI and tests.

The project is not finished as a production-compatible SNES emulator. The current code is best described as a strong emulator foundation with partial compatibility: CPU and bus infrastructure are broad, PPU supports basic background rendering and some sprite groundwork, APU is a communication/IPL stub, and the UI is usable for loading/running/stepping ROMs and viewing diagnostics.

## What is already done

### Repository and architecture

- Solution: `SnesEmulator.sln`.
- Shared build settings: `Directory.Build.props` with .NET 8, C# 12, nullable enabled, implicit usings enabled.
- Clean project split:
  - `Core` for contracts and domain models.
  - `Emulation` for CPU/memory/timing/save states.
  - `Graphics` for PPU/framebuffer.
  - `Audio` for APU placeholder.
  - `Input` for controller emulation.
  - `Hardware` for ROM loading.
  - `Infrastructure` for DI/facade/logging.
  - `Desktop` for Avalonia UI.
- Documentation exists: `docs/architecture.md`, `docs/emulation-notes.md`, `docs/roadmap.md`.
- Tests exist for core utilities, CPU instructions, memory bus, ROM loader, save states and rendering diagnostics.

### CPU / 65C816

- CPU class: `src/SnesEmulator.Emulation/Cpu/Cpu65C816.cs`.
- Mutable internal state: `CpuState`.
- Immutable diagnostic snapshot: `CpuRegisters`.
- Addressing logic separated into `AddressingModes`.
- Opcode dispatch table exists for 256 opcode slots.
- Reset vector, NMI and IRQ service paths exist.
- Native/emulation-mode flags exist.
- Stack helpers, direct page behavior and common arithmetic/load/store/control flow are implemented.
- Diagnostic logging exists for startup/control-flow/WRAM execution tracing.

### Memory / bus / DMA

- Memory bus class: `src/SnesEmulator.Emulation/Memory/MemoryBus.cs`.
- WRAM implemented through `WorkRam`.
- LoROM/HiROM mapping support exists.
- I/O register routing exists for PPU/APU/input/timing registers.
- DMA foundation exists and has tests around DMA modes and source-register behavior.
- Some stubs remain for registers such as HDMA and IRQ/TIMEUP behavior.

### ROM loading

- ROM loader class: `src/SnesEmulator.Hardware/Rom/RomLoader.cs`.
- Supports `.smc` / `.sfc` style ROM loading.
- Copier header stripping is implemented.
- LoROM/HiROM detection uses candidate scoring.
- Header parsing and checksum metadata exist.
- Synthetic ROM loader tests exist.

### PPU / rendering

- PPU class: `src/SnesEmulator.Graphics/Ppu/Ppu.cs`.
- Framebuffer class: `src/SnesEmulator.Graphics/Framebuffer/SnesFrameBuffer.cs`.
- VRAM/CGRAM/OAM storage exists.
- PPU register model for `$2100-$213F` exists.
- Timing state exists for dots, scanlines, frame count and master-cycle accumulation.
- VBlank/HBlank status behavior is represented.
- BG Mode 0 and Mode 1 foundations exist.
- Some OBJ/sprite scanline buffering exists.
- Rendering diagnostic tests cover tile bitplanes, scroll latches, color conversion, NMI/HVBJOY behavior, OAM writes and BG priority cases.

### APU / audio

- APU class: `src/SnesEmulator.Audio/Apu/Apu.cs`.
- Communication ports `$2140-$2143` exist.
- IPL handshake behavior is stubbed so ROM initialization can progress.
- Audio buffer API exists, but does not generate real audio.

### Input

- Controller model exists in `src/SnesEmulator.Input/Controllers/Controllers.cs`.
- SNES serial joypad register behavior exists.
- Keyboard mapping model exists.
- UI keyboard integration exists through desktop view/control layer.

### Desktop UI

- Avalonia desktop app exists.
- Main screen includes menu/toolbar, game screen, CPU register panel, diagnostic log and status area.
- `MainViewModel` exposes load/run/pause/reset/step/save/load state commands.
- `GameScreen` subscribes to frame output and renders to `WriteableBitmap`.
- Diagnostic log sink exists and can start ROM-specific sessions.

### Save states

- `SaveStateManager` exists.
- Save/load operations are routed through facade.
- Tests verify file creation, round-trip component loading and invalid state behavior.

## What still needs to be done

### Critical emulator compatibility work

1. Complete PPU rendering correctness.
   - BG Modes 2, 3, 4, 5, 6 and 7.
   - Full OBJ rendering with OAM high table, size selection, palette, priority and flips.
   - Correct BG/OBJ 12-layer priority ordering.
   - Window masking and main/sub-screen compositing.
   - Color math: add/subtract/half, fixed color, layer enables.
   - Mosaic, hires, interlace and overscan.
   - HDMA per-scanline register changes.

2. Complete APU/audio.
   - SPC700 instruction set.
   - APU RAM and timers.
   - DSP register model.
   - BRR sample decoding.
   - ADSR/gain envelopes.
   - Gaussian interpolation.
   - Echo/reverb.
   - Host audio output via a chosen backend.

3. Improve timing accuracy.
   - Replace coarse instruction-level sync where needed with dot/sub-instruction timing.
   - Implement exact DMA/HDMA stalls.
   - Improve NMI/IRQ edge cases.
   - Implement open-bus behavior.
   - Add fast/slow/extra-slow CPU memory access timing.

4. Improve ROM compatibility infrastructure.
   - Add known test-ROM workflow without storing copyrighted binaries.
   - Add compatibility matrix.
   - Add deterministic frame-step API.
   - Add trace toggles for CPU/PPU/APU/bus without excessive always-on logging.

### Product work

1. SRAM persistence.
   - Auto-load battery saves on ROM load.
   - Auto-save battery RAM on exit/reset/ROM switch.
   - UI status for SRAM path.
   - Tests with temp save directory.

2. Input improvements.
   - Gamepad support.
   - Rebind UI.
   - Multiple controller ports.
   - Persisted input profile.

3. Debugger tools.
   - Disassembly view.
   - Breakpoints/watchpoints.
   - Memory viewer/editor.
   - PPU register viewer.
   - VRAM/CGRAM/OAM viewers.
   - Frame advance and scanline stepping.

4. UI polish and safety.
   - ROM metadata panel.
   - Save-state slots.
   - Clear error/recovery UX.
   - Explicit paused/running/debug status.
   - File dialog filters and recent ROM list.

5. Build/release hygiene.
   - Add CI workflow.
   - Add packaging/publish scripts.
   - Add analyzers if desired.
   - Add `LICENSE` file if README claims MIT.

## Architectural risks found

- README states a very high implementation level. Treat those claims as aspirational unless tests and compatibility ROMs confirm them.
- APU is intentionally a stub. Any task involving real game audio must start from architecture and tests.
- PPU code has multiple partial behaviors. Avoid large all-at-once rewrites; implement one register group or mode at a time.
- Timing is intentionally approximate. Commercial ROM bugs may look like CPU bugs but actually be PPU/APU/timing issues.
- UI and emulator threading must be handled carefully; frame events should always marshal to UI thread before touching Avalonia objects.
- ROM binaries must not be committed.

## Recommended implementation roadmap for agents

### Phase 0 — verification baseline

- Ensure `dotnet restore`, `dotnet build`, `dotnet test` pass locally.
- Add CI if absent.
- Add deterministic emulator harness capable of loading synthetic ROMs and stepping frames.
- Add a `docs/compatibility.md` matrix.

### Phase 1 — correctness foundations

- Tighten memory bus register behavior.
- Add open-bus model.
- Add more CPU instruction regression tests for width flags and native/emulation transitions.
- Make logging configurable.

### Phase 2 — PPU compatibility

- Finish OBJ/sprite renderer.
- Correct priority composition.
- Implement window masking and color math.
- Add Mode 2/3/4 features.
- Implement Mode 7 separately behind focused tests.

### Phase 3 — persistence/input/debug UX

- SRAM load/save.
- Save-state slots.
- Gamepad support and keybind UI.
- Disassembler and memory viewer.

### Phase 4 — APU

- Add SPC700 CPU skeleton and instruction tests.
- Add timers and APU RAM.
- Add DSP and BRR pipeline.
- Add host audio output.

## Recommended agent task size

Good tasks:

- "Implement SRAM auto-load/save with tests."
- "Fix PPU `$4210` NMI flag behavior and add regression tests."
- "Implement Mode 3 8bpp tile decode for BG1 only with tests."
- "Add gamepad input abstraction without UI configuration yet."
- "Add compatibility matrix docs and test ROM harness shell script."

Bad tasks:

- "Make all SNES games work."
- "Implement the whole PPU."
- "Rewrite the emulator for cycle accuracy."
- "Add full audio."

Split bad tasks into narrow subsystem increments.
