---
description: Implement a focused APU/audio task
agent: snes-orchestrator
---

Implement this APU/audio task:

$ARGUMENTS

Use `@apu-audio-agent`.

Do not claim real audio support unless actual generated samples reach a host audio backend. Prefer pure tests for SPC700/DSP/BRR primitives first.
