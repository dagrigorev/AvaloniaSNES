# Compatibility Testing Guide

## Purpose

This document defines how the AvaloniaSNES project tracks emulation compatibility, how developers can legally test changes, and what must not be committed to the repository.

---

## Legal Testing Resources

Only **copyright-free, homebrew, or public-domain** test ROMs may be referenced or linked here. Do not upload any ROM file to this repository.

### Recommended test ROMs (legal)

| Name | Author | Purpose |
|------|--------|---------|
| **65C816 Test Suite** | Peter Lemon | CPU instruction accuracy — all opcodes, all addressing modes |
| **BRR Test ROMs** | Various | BRR sample decoding verification (APU milestone) |
| **PPU Test ROMs** | Near (byuu) / blargg | PPU timing, BG modes, windowing, HDMA |
| **SPC700 Test Suite** | Peter Lemon / byuu | SPC700 instruction set and DSP |
| **SNES Test Program** | NO CARRIER | Memory mapping, DMA, HDMA edge cases |
| **blargg's test ROMs** | blargg | CPU, memory, timing regression suite |
| **240p Test Suite** | Artemio / Nostalgia | Rendering accuracy, 240p output verification |

All above are available from their authors' repositories. Search by name to locate the current distribution URLs.

---

## What NOT to Commit

The following must never appear in the repository:

- **Commercial ROM images** — any `.smc`, `.sfc`, `.fig`, `.spc` file containing copyrighted game data
- **BIOS dumps** — SPC700 IPL ROM, DSP firmware, or any console BIOS
- **ROM patches** — `.ips`, `.bps`, `.ups` patches that can be applied to commercial ROMs
- **Save files** — `.srm`, `.save`, or any battery-save file from copyrighted games
- **Screenshots of copyrighted game content** unless cropped to show only diagnostic/UI elements

---

## Local ROM Storage

Keep test ROMs outside the repository tree to avoid accidental commits:

```
~/snes-roms/         # Recommended: outside the repo
  ├── homebrew/
  ├── test-suites/
  └── licensed-games/  # Your own dumps
```

If you must place ROMs inside the working directory, use a `roms/` folder (already gitignored):

```
roms/
  └── (place ROMs here — will be ignored by git)
```

---

## Compatibility Levels

| Level | Label | Meaning |
|-------|-------|---------|
| 0 | **Not Tested** | ROM has not been tried yet |
| 1 | **Does Not Boot** | Emulator crashes, hangs, or fails to enter the game loop |
| 2 | **Boots** | Title screen or intro sequence appears |
| 3 | **Menu** | Game menus, file select, or options screens are navigable |
| 4 | **In Game** | Gameplay begins, but may be broken (missing sprites, gfx glitches, wrong colours) |
| 5 | **Playable** | Game reaches playable state with minor graphical/audio glitches |
| 6 | **Glitches** | Game runs but has noticeable issues (flicker, wrong palette, sound pops) |
| 7 | **Pass** | All known test ROM assertions pass |

For test ROMs (homebrew), use only **Pass** or **Does Not Boot** — they have deterministic pass/fail conditions.

---

## Compatibility Matrix Template

Maintain a single table in this file or in a separate `compatibility-matrix.csv`. Update it when a change affects compatibility.

| ROM | Status | Last Tested | Build | Notes |
|-----|--------|-------------|-------|-------|
| `65c816-test` | Pass | 2026-01-15 | `abc1234` | All 256 opcodes verified |
| `blargg-cpu` | Pass | 2026-01-15 | `abc1234` | |
| `240p-test-suite` | Not Tested | — | — | |
| *Super Mario World* | Boots | 2026-01-10 | `def5678` | Title screen, corrupted BG1 tiles |
| *F-Zero* | In Game | 2026-01-10 | `def5678` | Missing sprite layer, audio stubs |

---

## Per-ROM Entry Template

Use this template when adding a new ROM result:

```markdown
### ROM: <game-or-test-name>
- **File hash (SHA-256):** `<omit for commercial games, include for homebrew>`
- **Status:** <level label>
- **Build:** `<commit hash>`
- **Date:** `<YYYY-MM-DD>`
- **Log:** `logs/rom-name-YYYY-MM-DD.log`
- **Screenshot:** `screenshots/rom-name-YYYY-MM-DD.png`

#### Observations
- What works:
- What is broken:
- Regression from previous build (if any):

#### Emulator Configuration
- FRAMERATE_SKIP: false
- TRACE_CPU: false
- TRACE_PPU: false
```

---

## Attaching Logs and Screenshots

- **Logs:** Save diagnostic output as `.log` files in a `logs/` directory. Logs are text-only and safe to commit. Enable CPU/PPU tracing sparingly — traces can be very large.
- **Screenshots:** Save PNG screenshots in a `screenshots/` directory. Only capture images from homebrew or test ROMs, or UI elements without copyrighted game content.
- Both `logs/` and `screenshots/` should be listed in `.gitignore` or committed only when showing non-copyrighted content.

---

## Updating the Matrix

1. After every change that may affect compatibility, run at least the top 3 test ROMs from the matrix.
2. Update the status, build hash, and date for each tested ROM.
3. If a regression is found, create a bug issue and link the failing ROM.
4. When a milestone is completed, run the full matrix and snapshot the result.
