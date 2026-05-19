---
description: Verify, stage and commit current coherent changes
agent: snes-orchestrator
---

Commit the current coherent AvaloniaSNES changes with this requested message or topic:

$ARGUMENTS

Use `@git-commit-agent` after review.

Before committing:

```bash
git status --short
git diff --check
git diff --stat
dotnet build SnesEmulator.sln -c Debug
dotnet test SnesEmulator.sln -c Debug --no-build
```

If build/tests cannot run, state that in the commit summary. Do not push.
