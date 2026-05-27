# Review checklist

- [ ] Does the change belong to the correct layer?
- [ ] Does `Core` stay dependency-free?
- [ ] Are CPU width/native/emulation edge cases covered where relevant?
- [ ] Are PPU register side effects and timing documented where relevant?
- [ ] Are UI updates marshalled safely?
- [ ] Are tests present for bug fixes/features?
- [ ] Are save-state format changes safe?
- [ ] Are logs/traces gated and not noisy by default?
- [ ] Are no ROMs or copyrighted assets committed?
- [ ] Is the commit atomic?
