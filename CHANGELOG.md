# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.8.0] - 2026-11-27

### Added
- **Linux.** `mareactl` frees space on Linux (x64 and ARM64, one self-contained binary per
  architecture in Releases). A freed file keeps its name, size and write time; its blocks are
  released and it carries its sha256 in an extended attribute. The agent, installed once with
  `sudo mareactl service install` as the systemd service `marea-agent@<user>`, watches only freed
  files with fanotify: when a program opens one, the open waits while the agent refills it from its
  source and checks the sha256, and only then goes through. Every command works as on Windows.
- Linux safety rules: a file another program has open or memory-mapped is never freed (kernel write
  lease); nothing is freed while the agent is down; wrong bytes from a source are refused and the
  next source is tried, and if none delivers, the open fails and the file stays freed; the first
  block is written last, and a freed file read without the agent starts with a plain-text notice.
- `build/linux-selftest.sh`: end-to-end test on a real kernel (free, refill, concurrent readers,
  mapped files, corrupted source, agent stopped, the systemd service). CI runs it on every change.

### Changed
- The engine builds for Windows and Linux. Windows behaviour is unchanged, except that a folder
  Windows refuses to register now keeps its files with the reason instead of stopping the cleanup.

## [0.7.0] - 2026-11-27

### Added
- **Ollama rollback copies.** Ollama 0.40 converted some GGUF models on the PC and kept the original
  next to the converted copy, only for a downgrade, so those models take about twice their size.
  Marea reads Ollama's manifest lists, finds the originals no current runner or other tag uses, and
  shows them on the Models page (*Ollama keeps N GB only for going back*, filter *Ollama rollback
  copies*, **Free rollback copies…**). Freeing never deletes: an older Ollama reading the file gets it
  back from the registry, verified by digest. CLI: `mareactl models --rollback`,
  `mareactl free --rollback --yes`; `rollbackBytes` and `ollamaRollback` in `marea.models/1`.

### Fixed
- Converted Ollama models are named after the tag you pulled (`qwen3.5:9b`), not after Ollama's
  hidden `llamacpp:<digest>` tag; projector and draft layers are grouped with their model.
- Weights Ollama converted on the PC are no longer treated as available from the registry under
  the hidden tag: they have no public source and are never freed. (Evaporation already re-checked
  the source live and kept them, but the plan and totals counted them as freeable.)

## [0.6.1] - 2026-11-27

### Changed
- **Simpler overview**: source risks, models to check and programs reading everything are grouped
  in one *Needs your attention* card instead of separate bars.
- **Plain words** in every language: "ledger" became *watched files*, "tide" became *automatic
  cleanup* (the settings section is now *Free space*), "mirror" became *backup folder (a NAS or another
  drive)*.
- **Automatic language** now takes the first of the user's Windows preferred languages that Marea
  speaks (not only the display language), falling back to English; Settings shows which one it picked.
- The disabled *Copy to backup folder* button explains why when no backup folder is set.

## [0.6.0] - 2026-11-27

### Added
- **`marea-check` for macOS and Linux** (and Windows): the load-safety check as a standalone,
  self-contained binary for linux-x64, linux-arm64, osx-arm64, osx-x64 and win-x64, attached to every
  release with checksums and build attestations. Same verdicts, `marea.check/1` JSON and exit codes
  as `mareactl check`. CI builds and self-tests it on Ubuntu and macOS with real pickles made by Python.
- **Hugging Face scan results**: the daily source check reads the Hub's own security scan for the
  repositories your files come from; flagged files show *Hugging Face scan: unsafe/caution* and are
  rated *Check before loading* (never *dangerous* on that alone). `hubScan` in `marea.models/1`.
- **More model folders on first run**: LM Studio (`.lmstudio\models`, `.cache\lm-studio\models`),
  Jan, GPT4All and the Hugging Face cache (honouring `HF_HUB_CACHE` / `HF_HOME`), including inside
  AppData; extensionless blobs in `blobs` folders are counted as model weights.

## [0.5.0] - 2026-11-27

### Added
- **Share your result**: *Share* on the overview and on the 3-click result screen saves a 1200×630
  image with the numbers (space freed or freeable, models, instant returns, zero bytes deleted),
  copies it to the clipboard and shows it in Explorer. Numbers only: no paths or names.
- **Not used in 90 days** filter on the Models page, with *Free these…* opening the plan for their
  files; `mareactl models --unused N`, and `lastUsed` in `marea.models/1`.
- **Mass-read protection**: Windows now tells Marea which program reads an evaporated file. When one
  program brings back 10 files or 50 GB within 10 minutes, Marea notifies, logs `j.mass_read` and shows
  a bar with *Block it* / *It's fine*. Blocked programs get "access denied" on evaporated files (the
  files stay evaporated); they are listed in *Settings → Blocked programs* with *Unblock*.
  `mareactl agent` prints the alert too.

### Added (also in 0.5.0)
- Code signing prepared for the SignPath Foundation program: `CODE_SIGNING_POLICY.md` (roles,
  what is signed, privacy policy), artifact configuration, and release steps that sign Marea's own
  binaries after manual approval and verify every signature. Skipped until configured.
- `build/publish.ps1 -NoPackage` / `-PackageOnly`, so signing can happen between build and package.

### Security
- GitHub Actions in every workflow are pinned to the exact commit SHA of their release (version kept
  as a comment, still updated weekly by Dependabot), so a changed or hijacked tag cannot reach the
  release builds without a reviewed pull request.

## [0.4.0] - 2026-11-25

### Added
- **Endangered-model watch.** Once a day (and on demand with *Check sources* or `mareactl watch`)
  Marea probes every public source it relies on and looks for early signs that a model is going
  away while the pinned bytes are still served: file removed from the repository's latest revision,
  repository gated, private or disabled, Ollama tag withdrawn. A failed check never counts as a sign.
- **Automatic rescue**: evaporated files that depend only on a disappearing source are brought back
  while it still answers; files that may now be the last copy are never freed again and are copied to
  the mirror when one is set. Notification, overview bar, journal entries (`j.source_at_risk`,
  `j.source_gone`, `j.rescued`).
- Models page: **Source** column (local copy, recipe, mirror, Hugging Face, Ollama, only here, or the
  risk), *Source at risk* filter, *Bring back now* / *Copy to mirror* actions.
- `mareactl watch [--no-rescue] [--json]` (schema `marea.watch/1`, exit 3 at risk, 4 lost);
  `status --json` reports `sourcesAtRisk` and `lastWatch`.
- Settings: *Watch public sources every day* and *Rescue models at risk automatically* (both on).

### Changed
- The safety gate no longer counts a public source that is at risk or gone: such files stay on disk,
  and the local copies or recipe sources of evaporated files stay pinned.
- A file whose public sources are all gone is classified *Unique* ("this may be the last copy").
- Models page: format and precision share one column.

## [0.3.0] - 2026-11-25

### Added
- **Models page**: files grouped into models (Hugging Face cache repositories, Ollama tags,
  model folders, sharded checkpoints, single files) with format detected from the bytes, GGUF
  quantization and architecture, parameter count, safetensors dtype, size and residency.
- **Load-safety check**, without ever loading a model: pickle-based files (`.ckpt`, `.pt`,
  `.pth`, `.bin`, `.pkl`) are rated by walking their opcodes (GLOBAL/STACK_GLOBAL with memo
  resolution); TorchScript archives with code and GGUF chat templates with Jinja sandbox-escape
  patterns are flagged. Calm verdicts: safe format, no code found, check before loading,
  dangerous. The overview shows a bar when some models need attention.
- `mareactl models` and `mareactl check <file|folder>`: the latter works anywhere, without a
  ledger, and returns exit code 3 (check before loading) or 4 (dangerous).
- **Agent-ready CLI**: `--json` on `status`, `models`, `plan`, `journal`, `check`, with versioned
  schemas (`marea.models/1`, …); documented exit codes; `MAREA_READONLY=1` refuses every change
  (exit 5); untrusted names are stripped of control and bidi characters in text output.
  Contract in `docs/AGENTS.md`.
- Ledger table `meta` (model metadata by content hash, survives evaporation). Header reads during
  a scan are ignored when judging recent use.

### Changed
- `mareactl` commands that need `--yes` now exit with code 2 (was 0) and print the hint on
  stderr; a missing argument exits with 64 instead of a stack trace. `--gb` accepts `1.5` and `1,5`.
- **New look.** Every screen redesigned on the Windows 11 Fluent style, with
  [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons): icon navigation,
  a hero card with the safely freeable space, a rounded "where every byte would come back from"
  bar with a two-column legend, icon cards for the 3-click wizard choices, coloured class dots
  and state icons in the file list, icons in the activity journal, grouped settings.
- **Light and dark theme**: follows Windows by default, or pick one in *Settings → General*;
  it switches immediately.
- Sizes below 1 GB are shown in MB instead of "0,0 GB".
- Managed folders and first-run candidates show the user profile as `~`, so the account name
  doesn't appear on screen or in screenshots.
- File list: search by name or folder, an empty-state message, a detail card with the class
  icon; the toolbar no longer overlaps on narrow windows.
- Every icon button has an accessible name for screen readers and UI Automation.
- README: animated tour (light and dark), step-by-step quick start and a screenshot gallery.
- README and manual: Models and safety, AI agents, user-research roadmap; translations updated.

## [0.2.1] - 2026-11-25

### Added
- English **user manual** (`docs/MANUAL.md`) and **tester guide** (`docs/TESTING.md`), with
  screenshots of every screen.

### Changed
- File, plan and activity views show paths relative to the managed folder
  (`models\sub\file`) instead of full paths, so screenshots and bug reports don't expose the
  user profile path.
- Checksum files use LF line endings, so `sha256sum -c` works on Linux and macOS.

## [0.2.0] - 2026-11-25

Field-tested on a real workstation: 63.7 GB of ACE-Step / Side-Step models scanned in 65 s,
19.9 GB evaporated in 11 s, read back by `safetensors` + PyTorch bit-identical, then undone.

### Added
- **Automatic source discovery**: Hugging Face repositories are found from evidence already on
  disk (HF cache, README `base_model`, config `_name_or_path`, git remotes, Ollama `hf.co` models)
  and the same authors' related repositories. No hand-written list needed. Optional (off by
  default) search by folder name for the rest.
- **Ollama source**: blobs referenced by local manifests come back from `registry.ollama.ai`
  (or `hf.co`) by digest. Exact storage host pinned in the allow-list. The first-run guide proposes
  `.ollama\models\blobs`. Nothing changes for people who don't use Ollama.
- **Update notifications**: once a day the app asks GitHub for newer releases and only notifies.
- **Releases built by CI with build-provenance attestation** (`gh attestation verify`).
- `mareactl evaporate <file> --yes` to evaporate a single file.
- **"Free up space in 3 clicks"** wizard: choose how much (10 GB, 50 GB, up to the target,
  everything safe) and how files must come back (*instant only*: duplicates and recipes, offline
  in seconds; or also NAS / internet). A one-sentence summary replaces the file table.
- **One-click undo** of a cleanup: every evaporation is a batch; *Undo this cleanup* (wizard),
  *Undo last cleanup* (overview) and `mareactl undo` bring back exactly that batch.
- `mareactl free [--gb N|--all] [--instant] --yes`.
- Tray menu entry for the wizard.

### Fixed
- Recipes when two local models share tensor names (e.g. fp32 *sft* and bf16 *turbo*): every
  candidate model is tried on its own, with a fast spot check before the full verification.
- The plan never takes both a recipe target and the files it is rebuilt from (the safety gate
  already blocked it at execution time, but the plan overstated the space to be freed).

### Changed
- bf16 ← fp32 recomputation uses 256-bit SIMD and pooled buffers: 9.3 GB rebuilt in 35 s instead of 135 s.
- Default list of Hugging Face repositories is now empty (discovery replaces it).
- Gate verdicts and plan items now carry the return path (local copy, recipe, mirror, download).
- Ledger schema: new `batch` column, migrated automatically on existing ledgers.

## [0.1.0] - 2026-11-25

First public pre-release.

### Added
- Provenance ledger with four recoverability classes: duplicate, derivable, re-obtainable, unique.
- Sources: local duplicates, Hugging Face LFS files pinned to a commit, content-addressed
  mirror (NAS), bit-exact recipes (bf16 recomputed from fp32 shards).
- Safety gate, last-moment proof under an exclusive handle, verification of every returned file,
  refusal to detach folders that still hold placeholders.
- Transparent evaporation and return through the Windows Cloud Files API (no admin rights).
- Tide planner (expected cost of return × recency) with optional automatic mode, off by default.
- Desktop app: overview, files with per-file explanation, managed folders, activity journal,
  settings, first-run guide that finds model folders, plan confirmation, tray icon.
- `mareactl` command line, including `restore-all` for uninstallers.
- Five languages: English, Italian, Spanish, French, German.
- Portable build, per-user MSI authoring (WiX v7), winget templates, CI, CodeQL, Dependabot.
