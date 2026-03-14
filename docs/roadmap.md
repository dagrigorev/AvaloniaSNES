# Roadmap

## Current State — v1.0.0

Working foundation: CPU, memory, basic PPU rendering, APU stubs, input, save states, and UI.

---

## Milestone 2 — Improved PPU Accuracy

**Goal:** Run most commercial games to title screen.

- [ ] **BG Mode 2** — Offset-per-tile scrolling
- [ ] **BG Mode 3** — 256-color background (8bpp)
- [ ] **BG Mode 4** — 256-color + offset-per-tile
- [ ] **BG Mode 7** — Affine transformation (matrix rotation/scaling)
- [ ] **Sprite rendering** — OAM parsing, 8×8/16×16 sprite sizes, priority
- [ ] **Sprite/BG priority** — Correct 12-layer priority ordering
- [ ] **HDMA** — Horizontal DMA for per-scanline register updates
- [ ] **Color math** — Screen addition/subtraction/half operations
- [ ] **Window masking** — W1/W2 clip regions per layer
- [ ] **Hi-res mode** — 512-wide rendering ($2133 bit 3)
- [ ] **Mosaic** — Block averaging effect
- [ ] **Sub-screen** — Dual-screen compositing

---

## Milestone 3 — SPC700 / Audio

**Goal:** Correct music playback for SNES games.

- [ ] **SPC700 CPU** — Full instruction set (MOVE, ALU, branching, multiply/divide)
- [ ] **SPC700 timer** — Three programmable timers at 8 kHz / 64 kHz
- [ ] **DSP registers** — Voice control, ADSR envelopes, pitch
- [ ] **BRRA sample decoder** — SNES-BRR lossy audio codec
- [ ] **Gaussian interpolation** — Pitch interpolation matching hardware
- [ ] **Echo buffer** — Hardware reverb effect
- [ ] **Audio output** — Host audio via OpenAL or NAudio / BASS

---

## Milestone 4 — Accuracy & Compatibility

**Goal:** Pass standard test ROMs.

- [ ] **Sub-instruction cycle accuracy** — PPU dot timing within CPU instructions
- [ ] **Open bus** — Accurate open bus behavior on unmapped reads
- [ ] **DMA timing** — Stall cycles, HDMA priority during H-blank
- [ ] **Mid-frame palette writes** — CGRAM effects
- [ ] **Sprite overflow/priority quirks** — Time-over flag, sprite flickering
- [ ] **65C816 decimal mode** — BCD arithmetic (affects a few games)
- [ ] **Memory access speed** — Per-region cycle counts ($420D MEMSEL)

---

## Milestone 5 — UX & Features

- [ ] **Gamepad support** — XInput / SDL2 for controllers
- [ ] **Key remapping UI** — In-app controls configuration dialog
- [ ] **Multiple save slots** — Save state 1–9 with preview screenshots
- [ ] **Battery save auto-load** — Load `.srm` alongside ROM on open
- [ ] **Screen filters** — CRT scanline overlay, pixel-perfect mode
- [ ] **Full-screen mode**
- [ ] **Fast-forward** — Uncapped FPS mode
- [ ] **Rewind** — Short history ring buffer for real-time rewind
- [ ] **Settings persistence** — User preferences in `appsettings.json`

---

## Milestone 6 — Special Chips

Some SNES games used enhancement chips on the cartridge:

- [ ] **SA-1** — Secondary 65C816 co-processor (used in many major titles)
- [ ] **Super FX** — RISC graphics accelerator (Star Fox, Yoshi's Island)
- [ ] **DSP-1** — Math co-processor (Super Mario Kart, Pilotwings)
- [ ] **SDD1** — Real-time data decompressor (Street Fighter Alpha 2)
- [ ] **CX4** — Custom co-processor (Mega Man X2/X3)

---

## How to Contribute

1. Pick a milestone item
2. Create a branch: `feature/ppu-mode7` or `fix/cpu-adc-decimal`
3. Write tests alongside implementation
4. Open a PR with a description of what was changed and how it was tested

All contributions should:
- Keep the project building (no broken builds)
- Include at least one unit test for new logic
- Follow existing code style (`.editorconfig`)
- Update `docs/emulation-notes.md` if hardware behavior is clarified
