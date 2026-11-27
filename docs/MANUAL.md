# Marea — User manual

Version 0.8 · Windows 10 1809+ / Windows 11 · Linux (command line) · English
([Italiano](i18n/README.it.md) · [Español](i18n/README.es.md) · [Français](i18n/README.fr.md) · [Deutsch](i18n/README.de.md) · [Português](i18n/README.pt.md) · [简体中文](i18n/README.zh.md))

1. [What Marea does](#1-what-marea-does)
2. [Install](#2-install)
3. [First run](#3-first-run)
4. [The overview](#4-the-overview)
5. [Free up space in 3 clicks](#5-free-up-space-in-3-clicks)
6. [The detailed plan](#6-the-detailed-plan)
7. [Files: why a file stays or may leave](#7-files-why-a-file-stays-or-may-leave)
8. [Models: what is on your disk, and is it safe to load?](#8-models-what-is-on-your-disk-and-is-it-safe-to-load)
9. [When a program opens an evaporated file](#9-when-a-program-opens-an-evaporated-file)
10. [Undo and bring files back](#10-undo-and-bring-files-back)
11. [Managed folders](#11-managed-folders)
12. [Settings](#12-settings)
13. [Sources: Hugging Face, Ollama, your NAS, recipes](#13-sources-hugging-face-ollama-your-nas-recipes)
14. [Activity journal](#14-activity-journal)
15. [Command line (mareactl)](#15-command-line-mareactl)
16. [Uninstall](#16-uninstall)
17. [Troubleshooting](#17-troubleshooting)
18. [Glossary](#18-glossary)

---

## 1. What Marea does

AI models are huge, and the same bytes often sit on disk several times: two apps with
their own copy of the same checkpoint, a model kept in fp32 *and* bf16, files you could
download again from Hugging Face in minutes.

Marea checks, with a SHA-256 fingerprint, **where every model file could be obtained again**.
Only files with such a way back can **evaporate**. An evaporated file:

- stays in its folder, with the same name and size as before;
- takes **0 bytes** on disk;
- comes back automatically the moment any program reads it, and Marea checks that its
  fingerprint is identical.

Files with no known source are **unique** and are never touched.

> **Marea never deletes your files.** Freeing space is reversible: one click brings
> everything back.

## 2. Install

1. Download `Marea-<version>-win-x64-portable.zip` from the
   [Releases page](https://github.com/indecenti/marea/releases).
2. *(Optional but recommended)* verify the download:
   - with the checksum: `Get-FileHash .\Marea-<version>-win-x64-portable.zip -Algorithm SHA256`
     must match the `.sha256` file next to the zip;
   - or, with the GitHub CLI, check it was built by this repository's CI:
     `gh attestation verify Marea-<version>-win-x64-portable.zip --repo indecenti/marea`.
3. Unzip anywhere (for example `C:\Tools\Marea`) and run **`Marea.exe`**.

The build is not code-signed yet, so Windows may show *"Windows protected your PC"*. Click
**More info → Run anyway**. No administrator rights are needed, and .NET is included.

Requirements: Windows 10 1809 or later / Windows 11, x64, folders on an **NTFS** local drive.

## 3. First run

![Welcome](images/welcome.png)

The first-run guide explains in three points what Marea does and never does. It then
**looks for your model folders** (folders named `checkpoints`, `models`, `hub`, `llm`, …
holding at least 1 GB of model weights, plus Ollama's `blobs` folder if you use Ollama).

- Untick any folder you don't want Marea to manage.
- *Add another folder…* lets you pick one by hand.
- Press **Start**: Marea fingerprints the files and looks for sources. **This first scan
  only measures; nothing is changed.**

Marea refuses folders where it could do harm: system folders, whole drives, your whole
user profile, network or removable drives, non-NTFS drives, and folders already synced by
OneDrive or similar services.


Besides folders named like model stores (`checkpoints`, `models`, `hub`...), the guide checks the
default folders of **Ollama**, **LM Studio** (`.lmstudio\models`), **Jan**, **GPT4All** and the
**Hugging Face cache** (`HF_HUB_CACHE` / `HF_HOME` are honoured), wherever they are.

## 4. The overview

![Overview](images/overview.png)

- **Drive card**: free space, used space and the *tide target* (the free space Marea aims to
  keep, 100 GB by default).
- **Safely freeable now**: how much can leave the disk right now with a verified way back.
- **Already evaporated** / **Unique: always stays** / **In the ledger**.
- **Where every byte would come back from**: the split between the four classes (see the
  [glossary](#18-glossary)).
- Buttons:
  - **Free up space in 3 clicks**: the simple guided path (next section);
  - **Free up space…**: the detailed plan, file by file;
  - **Scan and find sources**: re-scan after you add or download models;
  - **Undo last cleanup**: appears after a cleanup and brings back exactly those files.
- **Service running** (bottom left): evaporated files can only come back while Marea is
  running. Closing the window keeps Marea in the notification area.

### Share your result

**Share** (next to *Scan*) saves an image of your numbers in *Pictures\Marea*, copies it to the
clipboard and shows it in Explorer, ready to paste in a post or a chat. After a 3-click cleanup the
same button appears on the result screen with the space that batch freed. The image contains numbers
only: no paths, no file or model names.

![Share card](images/share-card.png)

## 5. Free up space in 3 clicks

![Free up space in 3 clicks](images/quick-free.png)

1. **How much space do you need?** Pick 10 GB, 50 GB, *Up to the target* or *Everything that
   is safe*. Under each choice Marea shows how much is really available.
2. **How must files come back?**
   - **Instant return only** *(recommended)*: only duplicates and recomputable files. They
     come back in seconds, even offline.
   - **Also from NAS or internet**: also files with a copy on your NAS or a public download.
     This frees more, but bringing them back depends on your network.
3. Read the one-sentence summary and press **Free up now**.

The first time, Marea asks whether it should start with Windows. Say **Yes** unless you
plan to keep it running yourself: evaporated files only open while Marea runs.

![Done](images/quick-free-done.png)

The last screen shows how much was freed. **Undo this cleanup** brings back exactly those
files and nothing else.

## 6. The detailed plan

![Detailed plan](images/plan.png)

*Free up space…* shows the same safe plan as a table: file, folder, size, class, estimated
time to come back, and the source that guarantees it. Tick *I understand* and press
**Evaporate**. Marea re-checks every file at the last moment, so a file can still stay on
disk: it may be in use, changed since the scan, or its source may not answer right now.
You get a short report of those cases.

## 7. Files: why a file stays or may leave

![Files](images/files.png)

The **Files** page lists every tracked file with its class and state. Select a file to see,
in plain words, why it stays or how it would come back:

- *unique: no verified source*: copy it to your backup folder (a NAS or another drive) to make it re-obtainable;
- *used N days ago*: recently used files are never evaporated (7 days by default);
- *needed to rebuild a file that is already evaporated*: the source of a recipe or the last
  local copy stays pinned;
- *its only source would evaporate together with it*: two copies never leave together
  unless a public or NAS copy exists.

From here you can also **Evaporate…** selected files, **Bring back to disk**, or
**Copy to backup folder**.

## 8. Models: what is on your disk, and is it safe to load?

![Models](images/models.png)

The **Models** page groups your files into models and tells you what each one is, read from its
own header (Marea never loads a model):

| Column | Where it comes from |
|---|---|
| Model | Hugging Face cache repository (`org/name`), Ollama tag (`llama3.2:3b`), model folder (with `config.json` or `model_index.json`), sharded checkpoint, or single file |
| Format | Detected from the bytes, not the extension: GGUF, safetensors, PyTorch, TorchScript, pickle, ONNX |
| Precision | GGUF quantization (`Q4_K_M`, `Q8_0`, …) or the dominant tensor type (`BF16`, `F16`, `F32`) |
| Parameters | Counted from the tensor table (GGUF and safetensors) |
| State | On disk, partly evaporated, evaporated |
| Safety | What loading the file could do (below) |

**Safety verdicts.** Old checkpoint formats (`.ckpt`, `.pt`, `.pth`, many `.bin`) use Python
*pickle*, which can run any code when a program loads the file. Marea reads the pickle's
instructions without running them and tells you what it found:

| Verdict | Meaning | What to do |
|---|---|---|
| **Safe format** | safetensors, GGUF, ONNX: the file holds only numbers | Nothing |
| **No code found** | Pickle that only rebuilds tensors with standard PyTorch/NumPy calls | Fine; prefer a safetensors version when one exists |
| **Check before loading** | Imports something unusual, contains TorchScript code, or could not be read fully. Also a GGUF chat template that tries to escape its sandbox | Load it only if you trust where it came from |
| **Dangerous** | Would run commands, open files or the network, or evaluate code | Don't load it. On disk it is harmless; delete it or keep it for analysis |

When some models need attention, the overview shows a bar with a **Review** button. The check is
a strong signal, not a proof: like every static scanner it can be fooled by an obfuscated
payload, so "No code found" means "nothing but standard tensor rebuilding was found".

To check a file **before** you even put it in a managed folder, for example a model you just
downloaded, use the command line: `mareactl check <file or folder>`. On macOS and Linux, use the
standalone `marea-check` from the Releases page: same verdicts and exit codes.

**Hugging Face's scanners.** For files whose source is a Hugging Face repository, the daily source
check also reads what the Hub's own security scanners report. A flagged file shows
*Hugging Face scan: unsafe* (or *caution*) among the findings and is rated *Check before loading*;
Marea never calls a file dangerous only because a hub scanner flagged it.

### Endangered models

![A model at risk](images/models-risk.png)

The **Source** column says where each model would come back from (local copy, recipe, mirror,
Hugging Face, Ollama, or *Only here*). Once a day, Marea also checks that Hugging Face and the Ollama
registry still publish the files it relies on, and looks for early signs that they are going away:
a file removed from the repository's latest revision, a repository that became gated, private or
disabled, an Ollama tag withdrawn. A failed check (offline, timeout) never counts as a signal.

| Source column | Meaning | What Marea does |
|---|---|---|
| **Source at risk** | The file is evaporated and its public source is disappearing, but still answers | Brings it back automatically (or press **Bring back now**) |
| **Maybe the last copy** | The file is on disk and its public source is disappearing or gone | Never frees it; copies it to the mirror if you have one (or press **Copy to mirror**) |
| **Can't come back** | The file is evaporated and every source is gone | Tells you; put a copy back in its folder if you have one elsewhere |

### Forgotten models

The filter **Not used in 90 days** lists the models none of whose files has been read or written in
the last 90 days. **Free these…** opens the detailed plan for their files still on disk: you choose,
and the safety gate still decides file by file what may leave.

### Ollama rollback copies

Ollama 0.40 converted some GGUF models on your PC so its new engine can load them, and kept the
original file next to the converted one, only so that you could go back to an older Ollama. Each
of those models then takes about twice its size.

When Marea finds such copies, the Models page shows a button like *Ollama keeps 6.6 GB only for
going back*. It opens the filter **Ollama rollback copies**; select a model to read which file is the
copy, then press **Free rollback copies…** to see the plan. Only the original copies leave, never the
converted file Ollama uses today, and the safety gate still decides file by file. If you ever
install an older Ollama, the original returns by itself from the Ollama registry, verified by
digest. From the command line: `mareactl models --rollback`, then `mareactl free --rollback --yes`.

**Check sources** on the Models page runs the check now. Both the daily check and the automatic
rescue can be turned off in *Settings → Sources*.

## 9. When a program opens an evaporated file

### Programs that read everything

Some programs read every file they find: antivirus scans, backup tools, indexers, other model
managers. Reading an evaporated file brings it back, so such a program can silently undo a cleanup.
When one program brings back 10 or more files (or 50 GB) within 10 minutes, Marea shows a
notification and a bar on the overview naming it:

- **Block it**: its reads of evaporated files fail from now on ("access denied to the cloud file"),
  and the files stay evaporated. Every other program keeps opening them normally.
- **It's fine**: Marea stops warning about that program.

Blocked programs are listed in *Settings → Blocked programs*, each with an **Unblock** button.
Marea itself (the app and `mareactl`) is never counted.

Nothing special happens on your side: ComfyUI, Python, Ollama or any other program opens the
file as usual. Windows asks Marea for the data, and Marea delivers it from the cheapest live
source:

1. an identical local copy (seconds);
2. a recipe, e.g. bf16 recomputed from fp32 (about 35 s for 9 GB on an NVMe drive);
3. your mirror / NAS;
4. a public download (Hugging Face, Ollama registry), pinned to the exact version.

A bar at the bottom of the window shows progress. When the whole file is back, Marea checks
its SHA-256. If it doesn't match, the problem is logged and the file is fetched again from
scratch.

> If Marea is **not running**, the program gets an error (like OneDrive files when OneDrive is
> closed). Start Marea and open the file again.

## 10. Undo and bring files back

- **Undo this cleanup** (end of the 3-click wizard) or **Undo last cleanup** (overview):
  bring back exactly the files of that cleanup.
- **Files → Bring back to disk**: bring back the selected files.
- **Settings → Bring everything back and detach**: the emergency exit. Every file comes
  back, becomes an ordinary file again, and the folders are detached from Windows. Marea
  checks free space first, and refuses to detach a folder that still contains evaporated files.

### Other space: caches and temporary files

The **Other space** page measures what else fills the disk and can be produced again: the caches
of pip, uv, npm, yarn, pnpm, NuGet, Go, Gradle and Cargo, the entries of the temporary folder
nothing has written to for 7 days, and crash dumps. The figure on each card is what the disk really
gets back: files a cache shares through hard links with your environments or projects stay on
disk when the cache is emptied, so they are left out and listed apart.

Marea deletes none of this. Each card shows the command of the program that owns the data
(**Copy command**) and **Open folder**; a cache is best emptied by its own program, which knows
what it is using. The hibernation file and virtual machine disks are listed under *Needs a
decision*: they are large but not disposable. From the command line: `mareactl clutter`.

## 11. Managed folders

![Managed folders](images/folders.png)

Marea works **only** inside the folders listed here. *Stop managing* brings every file of
that folder back first, then detaches it. *Find model folders* runs the discovery from the
first-run guide again.

## 12. Settings

![Settings](images/settings.png)

Every screenshot in this manual uses the light theme; the dark theme looks like this:

![Settings, dark theme](images/dark/settings.png)

| Setting | Default | Meaning |
|---|---|---|
| Language | Automatic, as Windows | English, Italiano, Español, Français, Deutsch, Português, 简体中文. *Automatic* takes the first of your Windows preferred languages that Marea speaks, otherwise English, and shows which one it picked |
| Theme | Follow Windows | Light, dark, or the Windows setting. Changes as soon as you pick it |
| Automatic cleanup | **Off** | Every 10 minutes, if free space is below the target, Marea frees on its own the files that are easiest to get back |
| Free space to keep | 100 GB | The target of automatic cleanup |
| Never evaporate files used in the last | 7 days | Protects files you're working with |
| Ignore files smaller than | 50 MB | Small files are not worth it |
| Download speed for estimates | 50 MB/s | Only used to estimate return times |
| Backup folder | — | A NAS or another drive for verified copies of unique files (shown as *mirror* in older versions) |
| Hugging Face repositories to compare with | — | Extra repositories that discovery can't guess (one per line) |
| Find out by itself where models come from | **On** | Discovery from evidence on disk (see §13) |
| Also search Hugging Face by folder name | **Off** | Finds more, but sends folder names to Hugging Face |
| Use Ollama as a source | On | Only matters if you manage Ollama's `blobs` folder |
| Watch public sources every day | **On** | Early warnings when a model is disappearing from Hugging Face or Ollama |
| Rescue models at risk automatically | **On** | Brings back evaporated files while their source still answers; copies possible last copies to the mirror |
| Start Marea with Windows | asked on first cleanup | Needed for evaporated files to open after a reboot |
| Tell me when a new version is out | On | Daily check on GitHub; notification only |
| Blocked programs | none | Programs that may not bring evaporated files back (see §9) |

## 13. Sources: Hugging Face, Ollama, your NAS, recipes

- **Duplicates** are found by fingerprint across all managed folders.
- **Recipes**: if a safetensors file can be rebuilt bit-for-bit from other local files (the
  typical case: a bf16 model and its fp32 original), Marea proves it by rebuilding the whole
  file once and comparing fingerprints.
- **Hugging Face**: Marea finds candidate repositories on its own, from your Hugging Face
  cache, `base_model` in model cards, `_name_or_path` in configs, git remotes and Ollama models
  pulled from `hf.co`. It also checks the same authors' related repositories. A file counts as
  re-obtainable only if its SHA-256 equals the public file, pinned to a commit. Only public
  repository and author names are sent; folder names are sent only if you enable the search
  by folder name.
- **Ollama**: if you manage `%USERPROFILE%\.ollama\models\blobs`, each blob listed in your
  Ollama manifests comes back from `registry.ollama.ai` (or `hf.co`) by digest. If you don't
  use Ollama, nothing changes.
- **Backup folder (NAS)**: *Copy to backup folder* copies unique files into a content-addressed store
  on your NAS (`marea-store\sha256\…`), verified while copying. Those files become
  re-obtainable from your own network.

## 14. Activity journal

![Activity](images/activity.png)

Everything Marea does is recorded: scans, verified recipes, evaporations, returns (with the
source used), undos and problems (marked ⚠). The journal is never erased and is translated
into the current language. Run `mareactl journal 50` to copy it into a bug report.

## 15. Command line (mareactl)

`mareactl.exe` sits next to `Marea.exe` and uses the same engine.

```
mareactl status [--json]                          disk and ledger status
mareactl add <folder>                             manage a folder
mareactl scan                                     fingerprints, sources, classes, model headers
mareactl models [--unused N|--rollback] [--json]  every model, unused ones, or Ollama rollback copies
mareactl check <file|folder>... [--json]          safety check before loading (no setup needed)
mareactl clutter [--days N] [--json]              caches, idle temp files, crash dumps (touches nothing)
mareactl watch [--no-rescue] [--json]            check public sources, rescue models at risk
mareactl plan [--gb N] [--instant] [--json]       what would evaporate (touches nothing)
mareactl free [--gb N|--all|--rollback] [--instant] --yes   quick cleanup, undoable
mareactl undo                                     undo the last cleanup
mareactl evaporate [<file>] [--gb N] --yes        evaporate per plan, or one file
mareactl restore <file>                           bring one file back now
mareactl replicate                                copy unique files to the mirror
mareactl restore-all --yes                        emergency exit
mareactl journal [n] [--json]                     recent activity
mareactl agent                                    serve evaporated files without the app
mareactl service install|uninstall                Linux: install the agent as a service (sudo)
```

Every command that changes something shows the plan first and does nothing without `--yes`
(exit code 2). `--json` prints a versioned JSON document for scripts and AI agents, and
`MAREA_READONLY=1` refuses every change (exit code 5). The full contract, with exit codes and
JSON schemas, is in [AGENTS.md](AGENTS.md).
Environment variables: `MAREA_DATA` (data folder, default `%LOCALAPPDATA%\Marea`, on Linux
`~/.local/share/Marea`) and `MAREA_LANG` (`en`, `it`, `es`, `fr`, `de`, `pt`, `zh`).

### On Linux

Download `mareactl-<version>-linux-x64.tar.gz` (or `linux-arm64`) from Releases, unpack it, then
install the agent once: `sudo ./mareactl service install`. It runs as a systemd service for your
user (`marea-agent@<user>`), starts at boot, and is what brings freed files back when a program
opens them. From then on the commands are the same as on Windows: `add`, `scan`, `free`, `undo`,
`restore`, `models`, `watch`.

- Freeing refuses every file while the agent is not running: a freed file could not come back.
- A file another program has open or memory-mapped (a running model server) is kept.
- A program that opens a freed file waits while it comes back, then reads the verified bytes. If
  no source delivers the right bytes, the open fails with *Operation not permitted* and the file
  stays freed: nothing half-written is ever handed to a program.
- Freed files keep their size; `du` shows the space they no longer use.
- To remove Marea: `mareactl restore-all --yes`, then `sudo mareactl service uninstall --yes`.
- Agent messages: `journalctl -u marea-agent@$USER`.

## 16. Uninstall

**Portable version:**

1. Open Marea → **Settings → Bring everything back and detach** (or run
   `mareactl restore-all --yes --uninstall`, which also removes the start-with-Windows entry).
2. Wait for *Done*, close Marea from the notification area (**Exit**).
3. Delete the Marea folder. Optionally delete `%LOCALAPPDATA%\Marea` (settings and journal).

> Never delete Marea while files are evaporated. Windows would keep empty placeholders
> that no program can open until Marea runs again.

## 17. Troubleshooting

| Symptom | What to do |
|---|---|
| A program says the file can't be opened / "cloud provider not running" | Start Marea, then open the file again |
| "Windows protected your PC" | The build is not signed yet: *More info → Run anyway* |
| A file I expected to free stays on disk | Select it in **Files**: the panel explains why |
| A model shows as *Unique* but it's public | Enable *Also search Hugging Face by folder name*, or add its repository in Settings, then *Scan and find sources* |
| A big file takes long to open the first time | It's coming back from its source; see the progress bar. Use *Instant return only* to evaporate only files that return in seconds |
| The folder I want is refused | System folders, whole drives, network drives and OneDrive folders are refused on purpose; pick a specific subfolder |
| Something looks wrong | Settings → *Bring everything back and detach*, then open an issue with `mareactl journal 50` |

## 18. Glossary

| Term | Meaning |
|---|---|
| **Evaporate** | Turn a file into a 0-byte placeholder that comes back on read |
| **Duplicate** | Same SHA-256 as another file on disk |
| **Derivable** | Rebuildable bit for bit from other local files (a *recipe*) |
| **Re-obtainable** | Same SHA-256 as a public file or a verified copy on your mirror |
| **Unique** | No known source: never evaporated |
| **Tide** | The planner that chooses what to free, cheapest to bring back first |
| **Ledger** | Marea's local record of files, fingerprints and sources (`%LOCALAPPDATA%\Marea\marea.db`) |
| **Mirror** | A folder (usually a NAS) holding verified copies, stored by fingerprint |
