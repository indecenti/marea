## What and why

<!-- One problem per pull request. -->

## Safety checklist

- [ ] No path can evaporate a file without a verified, independent source (see `SafetyGate`).
- [ ] No folder can be detached while placeholders remain (see `MareaEngine.ReleaseRoot`).
- [ ] New user-facing text goes through `tools/i18n_table.py` in all 7 languages.
- [ ] Tests added or updated; `dotnet test tests/Marea.Tests` passes locally.
