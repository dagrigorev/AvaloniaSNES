---
description: Review current diff without editing files
agent: review
---

Review the current worktree diff for AvaloniaSNES.

Use @.opencode/checklists/review.md.

Inspect:

```bash
git status --short
git diff --stat
git diff -- . ':!bin/**' ':!obj/**'
```

Report blocking issues, non-blocking issues, missing tests, and suggested commit message. Do not edit files.
