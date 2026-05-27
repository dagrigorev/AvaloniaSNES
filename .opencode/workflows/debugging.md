# Debugging workflow

1. Record exact symptom, command, ROM/test fixture and logs.
2. Check whether failure is CPU, bus, PPU, APU, input, UI or timing.
3. Prefer deterministic reproduction over manual UI reproduction.
4. Add temporary traces only behind flags or remove them before final.
5. Reduce the case to one opcode, register sequence, frame, scanline or UI command.
6. Convert reproduction into a test.
7. Fix or hand off to owning subsystem.
8. Run focused tests and solution build.

Never commit copyrighted ROMs or local logs containing private paths unless intentionally sanitized.
