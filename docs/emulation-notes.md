# Emulation Notes

Technical notes on SNES hardware behavior and emulation accuracy.

---

## CPU — WDC 65C816

### Register Model
The 65C816 extends the 6502 with:
- 16-bit A/X/Y (switchable to 8-bit via M/X flags)
- 24-bit address space (PBR:PC for code, DBR:address for data)
- Direct Page register (DP) replaces the fixed zero page
- Stack Pointer (SP) is 16-bit in native mode
- Two new modes: **native** (E=0) and **emulation** (E=1, 6502-compatible)

### Mode Switching
`XCE` swaps the Carry flag with the Emulation bit. Most SNES games switch to native mode early in initialization:
```
CLC      ; clear carry
XCE      ; exchange C with E → enters native mode
REP #$30 ; clear M and X flags → 16-bit A, X, Y
```

### Interrupt Vectors
In emulation mode, vectors are at `$FFF8-$FFFF` (6502-compatible).
In native mode, vectors are at `$FFE0-$FFFF`:

| Vector     | Native    | Emulation |
|------------|-----------|-----------|
| COP        | $FFE4     | $FFF4     |
| BRK        | $FFE6     | $FFFE     |
| ABORT      | $FFE8     | $FFF8     |
| NMI        | $FFEA     | $FFFA     |
| RESET      | $FFFC     | $FFFC     |
| IRQ/BRK    | $FFEE     | $FFFE     |

### Cycle Timing
The 65C816 has three memory access speeds:
- **Slow** (2.68 MHz): most ROM + I/O = **8 master clocks per cycle**
- **Fast** (3.58 MHz): ROM banks $80-$FF when MEMSEL bit set = **6 master clocks**
- **Extra Slow** (1.78 MHz): I/O registers $4000-$41FF = **12 master clocks**

Our emulation uses the slow cycle count for all accesses as a conservative approximation.

---

## Memory Map

### LoROM Layout
```
$00-$3F : $0000-$7FFF → System (WRAM mirror, I/O, registers)
           $8000-$FFFF → ROM bank 0, 32KB
$40-$6F : $8000-$FFFF → ROM banks 32-79
$70-$7D : $0000-$7FFF → SRAM (if present)
$7E      : Full WRAM bank 0 (64 KB)
$7F      : Full WRAM bank 1 (64 KB)
$80-$BF : Mirror of $00-$3F (with fast ROM option)
$C0-$FF : ROM banks (upper mirror)
```

### HiROM Layout
```
$00-$3F : $0000-$5FFF → System area
           $6000-$7FFF → SRAM (if present)
           $8000-$FFFF → ROM (lower half)
$40-$7D : Full 64KB ROM banks
$7E-$7F : WRAM
$80-$BF : Mirror + fast option
$C0-$FF : Full ROM banks (upper)
```

### WRAM
- 128 KB total at `$7E0000-$7FFFFF`
- First 8 KB mirrored at `$0000-$1FFF` in every system bank
- CPU-accessible via direct addresses or the `$2180` port

---

## PPU — S-PPU1 + S-PPU2

### BG Modes

| Mode | BG1    | BG2    | BG3    | BG4    |
|------|--------|--------|--------|--------|
| 0    | 2bpp   | 2bpp   | 2bpp   | 2bpp   |
| 1    | 4bpp   | 4bpp   | 2bpp   | —      |
| 2    | 4bpp   | 4bpp   | OPT    | —      |
| 3    | 8bpp   | 4bpp   | —      | —      |
| 4    | 8bpp   | 2bpp   | OPT    | —      |
| 5    | 4bpp   | 2bpp   | —      | —      |
| 6    | 4bpp   | —      | OPT    | —      |
| 7    | 8bpp+rot| —     | —      | —      |

Implemented: **Mode 0** (fully functional) and **Mode 1** (BG1/BG2 rendering).

### Tile Format

**2bpp tile** (8×8 pixels, 16 bytes):
```
Byte 0: Row 0, bitplane 0 (MSB = left pixel)
Byte 1: Row 0, bitplane 1
...
Byte 14: Row 7, bitplane 0
Byte 15: Row 7, bitplane 1
```

**4bpp tile** (8×8 pixels, 32 bytes):
Bitplanes 0+1 occupy bytes 0–15 (same as 2bpp), bitplanes 2+3 occupy bytes 16–31.

### CGRAM / Palette
512 bytes = 256 × 16-bit colors. Colors are BGR555: `0BBBBBGGGGGRRRRR`.
- Palette 0, color 0 = backdrop color
- BG palettes: 2bpp uses entries 0–31 (8 palettes × 4 colors), 4bpp uses 0–255 (8 × 16)

### Tilemap Format
Each tilemap entry is 2 bytes:
```
Bit 15:   Vertical flip
Bit 14:   Horizontal flip
Bits 13-10: Palette number
Bit 9:    Priority
Bits 9-0: Tile number (10 bits → 1024 tiles)
```

### Timing
- 1 scanline = 341 dots × 4 master clocks = 1364 master clocks
- 262 scanlines/frame (NTSC) × 1364 = 357,368 master clocks/frame
- V-blank starts at scanline 225
- H-blank starts at dot 274

---

## APU — Sony SPC700

The SPC700 is a self-contained 8-bit processor with:
- 64 KB RAM (shared with DSP)
- 8-channel DSP producing 32 kHz stereo audio
- Communication with the main CPU via 4 ports at `$2140-$2143`

### IPL ROM Protocol
On power-on, the APU loads from the 64-byte IPL ROM at `$FFC0-$FFFF`:
1. APU signals ready: sets port 0 = `$AA`, port 1 = `$BB`
2. Main CPU writes `$CC` to port 0 to begin transfer
3. APU echoes `$CC` to acknowledge
4. Data transfer: main CPU writes address and data to ports 0–3

Our stub simulates step 2–3 to allow games to proceed past audio init.

---

## Timing Synchronization

The emulator uses **instruction-level synchronization**:

```
For each CPU instruction:
  cpuCycles = cpu.Step()           # e.g., 2-8 instruction cycles
  masterCycles = cpuCycles × 8     # slow-clock approximation
  ppu.Clock(masterCycles)
  apu.Clock(masterCycles)
```

Each CPU instruction consumes a known number of cycles. The PPU converts master cycles to dots (÷4) and advances its scanline/dot counter. NMI is triggered once per frame when the PPU transitions to V-blank.

This is not cycle-accurate (does not interleave PPU dots within a CPU instruction) but maintains correct average timing.
