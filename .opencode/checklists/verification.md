# Verification checklist

- [ ] `git status --short` inspected.
- [ ] Relevant focused tests run.
- [ ] `dotnet build SnesEmulator.sln -c Debug` run.
- [ ] `dotnet test SnesEmulator.sln -c Debug` run or inability stated.
- [ ] `git diff --check` run before commit.
- [ ] No ROMs, binaries, local logs or generated build folders added.
- [ ] Docs/backlog updated if capability changed.
