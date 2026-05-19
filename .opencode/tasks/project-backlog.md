# Open Code backlog — AvaloniaSNES

This backlog is ordered for practical agent work. Prefer top items unless the user asks otherwise.

## P0 — make agent work safe and verifiable

- [ ] Add CI workflow for restore/build/test on Windows and Linux.
- [ ] Add `docs/compatibility.md` with legal test-ROM instructions and compatibility matrix template.
- [ ] Add deterministic emulation test harness for synthetic ROM/program execution.
- [ ] Add logging toggles so CPU/PPU/bus traces can be enabled per subsystem without always-on noise.
- [ ] Add `LICENSE` file or fix README license claim.

## P1 — stability and bug-fix targets

- [ ] Audit NMI/IRQ flag behavior through `$4210`, `$4211`, `$4212` and add focused tests.
- [ ] Implement or document open-bus behavior for unmapped reads.
- [ ] Tighten DMA source/destination timing and invalid mode behavior.
- [ ] Add regression tests for native/emulation mode transitions (`XCE`, `REP`, `SEP`) and index/accumulator width effects.
- [ ] Make save/load state versioning explicit and resilient to component changes.

## P2 — PPU compatibility

- [ ] Complete OBJ rendering: OAM high table, size selection, palette, flip, priority.
- [ ] Implement correct BG/OBJ priority composition.
- [ ] Implement window masking registers and clipping logic.
- [ ] Implement color math main/sub-screen compositing.
- [ ] Implement HDMA register updates per scanline.
- [ ] Implement BG Mode 2 offset-per-tile.
- [ ] Implement BG Mode 3 8bpp BG1 rendering.
- [ ] Implement BG Mode 4 8bpp + offset-per-tile.
- [ ] Implement Mode 7 affine transform.
- [ ] Add golden framebuffer tests for small synthetic VRAM/CGRAM/OAM fixtures.

## P3 — user-facing emulator features

- [ ] Implement SRAM battery save auto-load and auto-save.
- [ ] Add save-state slots in UI.
- [ ] Add recent ROM list.
- [ ] Add ROM metadata panel.
- [ ] Add configurable keyboard bindings UI.
- [ ] Add gamepad support through an abstraction that can be tested without hardware.

## P4 — debugger tools

- [ ] Add CPU disassembler service.
- [ ] Add breakpoint/watchpoint model.
- [ ] Add memory viewer.
- [ ] Add PPU register viewer.
- [ ] Add VRAM/CGRAM/OAM viewers.
- [ ] Add frame advance and scanline advance commands.

## P5 — APU/audio

- [ ] Add SPC700 CPU state and opcode dispatch table.
- [ ] Implement SPC700 memory map and timers.
- [ ] Implement SPC700 instruction groups with tests.
- [ ] Implement DSP register model.
- [ ] Implement BRR sample decoder with tests.
- [ ] Add host audio backend behind an interface.
- [ ] Add audio sync strategy with buffering and underrun diagnostics.
