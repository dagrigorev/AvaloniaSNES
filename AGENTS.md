# AvaloniaSNES Agent Guide

This repository is a C#/.NET 8 SNES emulator with Avalonia desktop UI. Agents must work as maintainers of an emulator project, not as generic app generators.

## Fast project map

- `src/SnesEmulator.Core` — pure domain contracts, constants, models, exceptions, bit helpers. Do not add framework dependencies here.
- `src/SnesEmulator.Emulation` — 65C816 CPU, memory bus, WRAM, emulation loop, save-state manager.
- `src/SnesEmulator.Graphics` — PPU, framebuffer, tile/background/sprite rendering.
- `src/SnesEmulator.Audio` — APU/SPC700/DSP area. Currently mostly architectural stub.
- `src/SnesEmulator.Input` — SNES controller and host key mapping.
- `src/SnesEmulator.Hardware` — ROM loading, copier-header stripping, LoROM/HiROM detection.
- `src/SnesEmulator.Infrastructure` — dependency injection, facade, logging/orchestration.
- `src/SnesEmulator.Desktop` — Avalonia UI, controls, view models, dialogs, keyboard integration.
- `tests/*` — xUnit/FluentAssertions/Moq tests. Add tests near the subsystem changed.
- `docs/*` — architecture, hardware notes, roadmap. Update when behavior or scope changes.
- `.opencode/*` — Open Code agents, commands, workflows, checklists, backlog.

## Current product state

The project has a usable emulator foundation: ROM loader, 65C816 opcode table, WRAM/memory bus, PPU rendering (Mode 0/1 with BG/OBJ priority composition), input, save states, diagnostics and Avalonia UI. It is not yet a high-compatibility commercial SNES emulator.

All 168 tests across 4 test projects pass (Core: 41, Emulation: 75, Hardware: 9, Rendering: 43).

Most important unfinished work:

1. PPU accuracy and compatibility: BG modes 2-7, sprite priority, windowing, HDMA, color math, mosaic, hires/interlace, Mode 7.
2. APU/audio: real SPC700 CPU, DSP registers, BRR decoder, timers, host audio output.
3. Timing/compatibility: sub-instruction timing, open bus, exact DMA/HDMA timing, NMI/IRQ edge cases.
4. Persistence/input: SRAM auto-load/save, configurable bindings UI, gamepad support.
5. QA: test ROM harness, golden rendering snapshots, deterministic frame stepping, compatibility matrix.
6. UI polish: ROM metadata panel, debugger/disassembler tools, frame/performance diagnostics, save-state slots.

See `.opencode/tasks/project-backlog.md` and `docs/AI_AGENT_PROJECT_ANALYSIS.md` before starting large changes.

## Build and test commands

Use these commands from repository root:

```bash
dotnet restore
dotnet build SnesEmulator.sln -c Debug --no-restore
dotnet test SnesEmulator.sln -c Debug --no-build
dotnet run --project src/SnesEmulator.Desktop -c Debug
```

When changing a subsystem, run focused tests first, then the full suite:

```bash
dotnet test tests/SnesEmulator.Emulation.Tests -c Debug
dotnet test tests/SnesEmulator.Rendering.Tests -c Debug
dotnet test tests/SnesEmulator.Hardware.Tests -c Debug
dotnet test tests/SnesEmulator.Core.Tests -c Debug
```

If a command fails because the environment lacks .NET SDK, record the exact command and expected verification steps in the final response. Do not claim tests passed without running them.

## Coding rules

- Keep `Core` dependency-free.
- Do not make desktop UI call emulator internals directly; go through `IEmulator`/facade or add a clean interface.
- Prefer deterministic emulation APIs over wall-clock APIs for tests.
- Do not add ROM files, BIOS dumps, copyrighted assets, or test ROM binaries to the repository.
- Never hard-code user-specific paths.
- For hardware behavior, leave reference comments with register address and hardware meaning.
- For approximations, state them explicitly in code comments and docs.
- Every bug fix must include a regression test unless impossible; explain why if not.
- Every feature must include at least one focused test or diagnostic fixture.
- Preserve public contracts unless the change is intentionally architectural and documented.
- Keep changes small and commit-friendly.

## Safe git workflow for agents

Before editing:

```bash
git status --short
git branch --show-current
```

Do not overwrite user changes. If there are unrelated uncommitted changes, work around them or stop with a clear note.

For feature/fix work:

```bash
git checkout -b feature/<short-name>
# or
git checkout -b fix/<short-name>
```

Before committing:

```bash
dotnet build SnesEmulator.sln -c Debug
dotnet test SnesEmulator.sln -c Debug --no-build
git diff --check
git status --short
git diff --stat
git diff -- . ':!bin/**' ':!obj/**'
```

Commit only when requested or when the active command explicitly asks to commit:

```bash
git add <changed files>
git commit -m "fix(ppu): correct vblank nmi edge timing"
```

Do not push unless explicitly requested.

## Agent routing

Use these Open Code agents by task type:

- `@snes-orchestrator` — plan multi-step work, split tasks, coordinate subagents.
- `@emulator-core-agent` — CPU, memory bus, DMA, timing, save state.
- `@ppu-rendering-agent` — PPU, framebuffer, BG modes, OBJ/sprites, rendering tests.
- `@apu-audio-agent` — APU, SPC700, DSP, BRR, audio output.
- `@avalonia-ui-agent` — Avalonia UI/MVVM, diagnostics, input UX.
- `@debugger-agent` — reproduce crashes/hangs, instrument logs, minimize failing cases.
- `@test-verifier-agent` — tests, regression harness, snapshots, compatibility matrix.
- `@review-agent` — read-only review and risk assessment.
- `@git-commit-agent` — final diff review and commit creation.

## Preferred implementation order

For this codebase, prioritize:

1. Make current implementation verifiable and stable: build/test fixes, deterministic test harness, no flaky UI/emulation coupling.
2. PPU correctness: sprite/OAM priority, BG priority ladder, Mode 2/3/4/7 incremental support, HDMA/window/color math.
3. ROM compatibility diagnostics: trace toggles, compatibility matrix, test ROM runner.
4. SRAM persistence and input/gamepad UX.
5. APU/SPC700 and host audio.
6. Debugger UX: disassembly, memory viewer, PPU register viewer, breakpoints.

## Definition of done

A task is done only when:

- The behavior is implemented in the correct layer.
- Focused tests cover the new or fixed behavior.
- Full build and relevant tests were run, or inability to run was stated honestly.
- Docs/backlog are updated when scope or behavior changes.
- The final response lists changed files, verification commands and any remaining risks.
