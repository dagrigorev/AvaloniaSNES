# Open Code configuration for AvaloniaSNES

This directory contains project-level Open Code agents, commands, workflows, checklists and backlog.

## Model setup

Default model in `opencode.json`:

- Main: `deepseek/deepseek-v4-pro`
- Small: `deepseek/deepseek-v4-flash`

Set API key before starting Open Code:

```powershell
$env:DEEPSEEK_API_KEY = "sk-..."
opencode
```

Alternative temporary model override:

```powershell
$env:OPENCODE_MODEL = "deepseek/deepseek-v4-pro"
opencode --model $env:OPENCODE_MODEL
```

## Main commands

- `/analyze-project` — refresh project understanding.
- `/feature <description>` — implement a feature in a branch-safe way.
- `/bugfix <description>` — reproduce and fix a bug.
- `/debug <symptom>` — investigate crash/hang/bad emulation.
- `/test` — run focused and full verification.
- `/review` — read-only review of current diff.
- `/commit <message>` — verify, stage and commit current change.
- `/ppu-task <description>` — PPU-specific implementation path.
- `/apu-task <description>` — APU-specific implementation path.

## Recommended workflow

1. Start with `/analyze-project` or `/review` for unfamiliar tasks.
2. Use `/feature`, `/bugfix`, `/ppu-task`, `/apu-task`, or `/debug`.
3. Run `/test`.
4. Run `/review`.
5. Run `/commit <conventional commit message>` only when the diff is clean.

Do not store ROM files in the repository.
