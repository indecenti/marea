# Contributing to Marea

Thanks for helping! Marea moves people's files in and out of their disks, so the bar is
**"never lose a byte"**: correctness and safety come before features.

## Getting started

```
git clone <this repo>
cd marea
dotnet test tests/Marea.Tests --filter "Category!=Network"
```

Requirements: Windows 10 1809+ / 11 on NTFS, .NET 10 SDK. The Cloud Files tests use the
real Windows cloud filter driver on temporary folders and clean up after themselves.

## Ground rules

- **Safety first.** Any change to evaporation, hydration or folder registration needs a test
  that fails without it. Read [SECURITY.md](SECURITY.md) before touching `Marea.Core`.
- **One problem per pull request**, small and reviewable.
- **Translations**: user-facing text lives in `tools/i18n_table.py` (it, en, es, fr, de).
  Run `python tools/i18n_table.py`; `LocalizationTests` fail if a key or placeholder is missing.
- **No telemetry, no new network destinations** without discussion in an issue first.
- Code style: see `.editorconfig`; public members have XML doc comments.

## Good first contributions

- A new source resolver (Ollama registry by digest, Civitai by hash, PyPI wheels from `uv.lock`).
- Translations into more languages.
- A macOS (File Provider) placeholder backend, following the Linux one in `src/Marea.Core/Platform/Linux`.
