# Marea — Security model

Marea moves gigabytes of the user's files in and out of the disk. The one thing it
must never do is lose data. This document lists what can go wrong and what stops it.

## Guarantees

1. **Nothing leaves the disk without an independent, verified way back.**
   The safety gate (`SafetyGate.cs`) allows evaporation only when, *after* the
   evaporation, at least one of these still exists:
   - another local copy with the same SHA-256, still on disk and unchanged;
   - a recipe that rebuilt the exact bytes (verified by hashing the full rebuild)
     whose source files stay on disk ("pinned");
   - a verified copy in the content-addressed mirror (e.g. a NAS);
   - a public Hugging Face file with the same SHA-256, pinned to a commit.
   Files with no source are **unique** and are never evaporated.
2. **Last-moment proof.** Right before evaporating, Marea opens the file with an
   **exclusive handle** (nobody can write it), re-hashes every byte, checks that a
   source answers *now* (HEAD request for the Hub, presence and size for the
   mirror, unchanged sources for recipes), refuses hard-linked files, and only then
   converts the file to a placeholder on the same handle.
3. **Every byte that comes back is verified.** After a file is fully hydrated it is
   hashed again. A mismatch is logged loudly, the file is evaporated again so it
   can be fetched from scratch, and the source that served it is distrusted.
4. **Windows never gets the chance to delete placeholders.**
   `CfUnregisterSyncRoot` deletes placeholders that are not hydrated. Marea detaches
   a folder only after a full walk of that folder proves it holds **only ordinary
   files** (`MareaEngine.ReleaseRoot`); otherwise it refuses and keeps the folder
   registered. A test covers the case where the ledger itself "forgets" a file.
5. **The user is always in control.** The first scan only measures. Evaporation
   needs an explicit confirmation of a visible plan, or the automatic tide, which is
   **off by default**. "Bring everything back" is one click (`mareactl restore-all`).
   It checks free space first and returns exit code 0 only when nothing is left
   evaporated.
6. **Uninstalling cannot strand files.** The installer runs
   `mareactl restore-all --yes --uninstall` *before* removing any file and aborts
   the uninstall if it fails.

## Attack surface

| Area | Exposure | Mitigation |
|---|---|---|
| Network | Outbound HTTPS only, to `huggingface.co` / `*.huggingface.co` / `*.hf.co`, `registry.ollama.ai` + its exact blob storage host (`dd20bb891979d25aebc8bec07b2b3bbc.r2.cloudflarestorage.com`, pinned, not a wildcard), and `api.github.com` (update check) | Host allow-list enforced on every request **and every redirect**, HTTPS only, no cookies, no credentials, no inbound listener |
| Data sent | Public repository / author names found on disk or in settings, requests for public files by digest, one GitHub API call a day; the daily source watch sends the same public repository names and Ollama tag names already used as sources | Never local paths or hashes of the user's files. Folder names are sent **only** if the user enables *search by folder name* (off by default) |
| Repo names | Taken from settings | Validated against `owner/name` pattern before use |
| Local IPC | Named events `Local\Marea.App.*` (show / quit) | Session-local; at worst another process of the same user can close Marea, which it could do anyway |
| Provider role | Lock file in `%LOCALAPPDATA%\Marea` | Released by Windows if the process dies |
| Privileges | None: per-user, no admin, no service, no driver | Uses the built-in cloud filter driver of Windows |
| Folders | Only user-chosen folders | Rejected: system folders, whole drives, profile roots, network/removable drives, non-NTFS, overlaps, folders owned by OneDrive or other sync engines |
| Ledger | SQLite in the user profile | WAL journal; append-only activity log |
| Dependencies | `Microsoft.Data.Sqlite`, `SQLitePCLRaw` (pinned past GHSA-2m69-gcr7-jv3q), `Microsoft.Windows.CsWin32` (build-time only) | `dotnet list package --vulnerable --include-transitive` in CI |
| Model files | Headers and pickle data of the user's models are parsed (Models page, `mareactl check`) | Never loaded, unpickled or executed. Parsers are bounded (string, count, entry and opcode limits) and fail closed to *check before loading*; a hostile header cannot allocate unbounded memory |
| CLI used by agents | Commands may be run by AI agents | Changes need `--yes` (exit 2 without it); `MAREA_READONLY=1` refuses every change (exit 5); file and model names are untrusted text: control and bidi characters are stripped in text output, escaped in JSON |
| Telemetry | None | — |
| Updates | Notification only | The app never downloads or runs an update; it links to the official releases page |
| Release integrity | Built by GitHub Actions from the tagged commit; every third-party Action pinned to a commit SHA, updated only through reviewed Dependabot pull requests | Build-provenance attestation (Sigstore): `gh attestation verify <file> --repo indecenti/marea`, plus `.sha256` files |

## Known limits

- Evaporated files open only while Marea runs (like OneDrive "online-only" files).
  The app offers to start with Windows; without it, programs get a "cloud provider
  not running" error (0x8007016A) until Marea starts.
- Opening a placeholder for reading (including an antivirus scan on open) brings
  the whole file back. That is by design: progressive hydration, verified at the end.
- A program that reads every file (antivirus, backup, indexer) brings evaporated files back. Marea
  detects it (10 files or 50 GB from one program within 10 minutes), names the program and lets the
  user block it: blocked programs get "access denied" on evaporated files, which stay evaporated.
- The load-safety check is static, like picklescan: an obfuscated pickle may evade it.
  "No code found" means only standard tensor rebuilding was found, not a guarantee.
- Unsigned builds trigger SmartScreen. See `docs/DISTRIBUTION.md` for signing options.
- Linux: freed files come back only while the agent (`marea-agent@<user>`) runs. Without it a
  freed file reads as a short text notice followed by zeros, never as plausible model data, and
  Marea refuses to free anything. The refill writes the first block last, so a file left half
  refilled by a crash still starts with the notice.
- Linux: the agent runs as the user with `CAP_SYS_ADMIN` (fanotify permission events),
  `CAP_DAC_OVERRIDE`, `CAP_FOWNER` and `CAP_LEASE`, `NoNewPrivileges`, from a root-owned binary in
  `/usr/local/bin`. Its socket sits in the user's data folder (mode 0600) and checks the caller's
  uid (`SO_PEERCRED`); it only watches regular files of its own user that carry Marea's identity
  attribute. Before freeing, a kernel write lease proves no other program has the file open or
  mapped.

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** (Security tab → *Report a
vulnerability*) instead of a public issue. You will get an answer within a week.
