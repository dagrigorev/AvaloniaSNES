# Testing workflow

Use this order:

```bash
dotnet restore
dotnet build SnesEmulator.sln -c Debug --no-restore
```

Then focused tests:

```bash
dotnet test tests/SnesEmulator.Core.Tests -c Debug --no-build
dotnet test tests/SnesEmulator.Emulation.Tests -c Debug --no-build
dotnet test tests/SnesEmulator.Hardware.Tests -c Debug --no-build
dotnet test tests/SnesEmulator.Rendering.Tests -c Debug --no-build
```

Then full tests:

```bash
dotnet test SnesEmulator.sln -c Debug --no-build
```

If `--no-build` fails because tests were not built, rerun without `--no-build` and record it.
