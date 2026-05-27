---
description: Run and analyze build/test verification
agent: implementation
---

Run verification for the current AvaloniaSNES worktree.

Use @.opencode/checklists/verification.md.

Minimum:

```bash
dotnet restore
dotnet build SnesEmulator.sln -c Debug --no-restore
dotnet test SnesEmulator.sln -c Debug --no-build
```

If tests fail, summarize failures, likely subsystem, and the smallest next fix. Do not hide failures.
