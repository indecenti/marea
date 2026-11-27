# Testing Marea — a guide for early testers

Thanks for trying Marea! This takes about **15 minutes**. Marea is a pre-release, so please
follow the safe path below. Everything you do can be undone with one click.

## Before you start

- Windows 10 1809+ or Windows 11, x64, an **NTFS** drive.
- Some AI model files (`.safetensors`, `.gguf`, `.ckpt`, Ollama models…) of at least a few GB.
- Download the latest `Marea-<version>-win-x64-portable.zip` from
  [Releases](https://github.com/indecenti/marea/releases), unzip it and run `Marea.exe`.
  The build is unsigned: *More info → Run anyway*.

## Safe path (recommended)

Start with a **test folder** rather than your production models:

1. Create a folder, e.g. `D:\marea-test` (or anywhere on an NTFS drive except the root of a drive).
2. Copy one model into it **twice**, in two subfolders (e.g. `a\model.safetensors` and
   `b\model.safetensors`). That gives Marea a duplicate to work with.
3. Optional: add a model you downloaded from Hugging Face (to test public sources), or point
   it at your Ollama `blobs` folder if you use Ollama.

Once you're comfortable, try a real model folder. Marea only touches files with a verified way
back, and every cleanup can be undone.

## What to try (checklist)

- [ ] **First run**: did the guide find your model folders? Were the suggestions sensible?
- [ ] **Scan**: how long did it take, for how many GB?
- [ ] **Classes**: are duplicates, derivable, re-obtainable and unique files what you expected?
      Select a file in **Files**: is the explanation clear?
- [ ] **Free up space in 3 clicks** with *Instant return only*. How much was freed?
- [ ] **Open an evaporated file** with the program you normally use (ComfyUI, Python, Ollama…).
      Did it work? How long did the first open take?
- [ ] **Undo this cleanup**: are all files back? (Right-click a file → Properties: an evaporated
      file shows its full *Size* but *Size on disk* 0 bytes; after undo both are the same again.)
- [ ] **Language**: switch language in Settings; does everything look right?
- [ ] **Reboot test** (optional): evaporate something, say *Yes* to *start with Windows*,
      reboot, open the file.
- [ ] **Emergency exit**: Settings → *Bring everything back and detach*.

## What to report

Open an [issue](https://github.com/indecenti/marea/issues/new/choose) (or send a message) with:

- what you did, what you expected, what happened;
- Marea version (Settings, bottom) and Windows version;
- the output of `mareactl journal 50` (run it in the Marea folder). Remove any paths you
  don't want to share. The app itself already shortens paths to the managed folder.

Feedback that helps most:

- anything confusing in the wording;
- a file you think should have been freeable (or shouldn't);
- antivirus or other tools reacting to evaporated files;
- timings on HDDs, network drives or very large models (>20 GB).

## Known limitations of this pre-release

- Not code-signed yet (SmartScreen warning).
- Evaporated files only open while Marea is running (like OneDrive "online-only" files).
- Opening an evaporated file for reading brings the **whole** file back, including when an
  antivirus scans it on open.
- This guide covers the Windows app. On Linux there is `mareactl` with a systemd agent (see the
  README, *On Linux*); `build/linux-selftest.sh` tests it end to end. macOS is on the roadmap.

## When you're done

Settings → **Bring everything back and detach**, close Marea from the notification area
(**Exit**), then delete the folder. Your files are exactly as they were.
