# Feature development workflow

1. Check state:

```bash
git status --short
git branch --show-current
```

2. Define acceptance criteria in one paragraph.
3. Map feature to subsystem using `.opencode/project-map.md`.
4. Inspect existing source and tests before editing.
5. Implement smallest vertical slice.
6. Add tests:
   - Unit tests for pure logic.
   - Rendering diagnostics for PPU changes.
   - Facade or UI build verification for UI changes.
7. Run focused tests.
8. Run solution build.
9. Update docs or backlog if the feature changes capability.
10. Summarize files, tests and risks.
