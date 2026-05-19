# Bugfix workflow

1. Check state:

```bash
git status --short
```

2. Capture symptom and expected behavior.
3. Reproduce with the smallest command/test possible.
4. Locate root cause, not just failing line.
5. Add failing regression test first when practical.
6. Implement minimal fix.
7. Ensure the regression test fails before fix when possible and passes after fix.
8. Run related test project.
9. Run solution build.
10. Summarize root cause, changed files and verification.
