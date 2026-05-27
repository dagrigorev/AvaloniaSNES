# Commit workflow

1. Check diff:

```bash
git status --short
git diff --check
git diff --stat
git diff -- . ':!bin/**' ':!obj/**'
```

2. Verify build/tests.
3. Ensure all changed files belong to one logical change.
4. Stage only relevant files.
5. Commit with conventional message.
6. Do not push.

Commit message examples:

- `fix(ppu): clear nmi flag only on status read`
- `feat(input): add configurable controller mapping model`
- `test(emulation): cover native mode stack behavior`
- `docs(agent): add opencode subsystem workflow`
