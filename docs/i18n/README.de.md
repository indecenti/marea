<p align="center"><img src="../../src/Marea.App/Assets/marea.png" width="104" alt="Marea-Logo"></p>

<h1 align="center">Marea</h1>

<p align="center"><b>Deine KI-Modelle fressen deine Festplatte. Marea gibt dir den Speicher zurück, ohne ein einziges Byte zu löschen.</b></p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <b>Deutsch</b> ·
  <a href="README.pt.md">Português</a> ·
  <a href="README.zh.md">简体中文</a>
</p>

---

📖 [Benutzerhandbuch (auf Englisch)](../MANUAL.md) · [Tester guide](../TESTING.md)

Lokale KI ist schwer. Hugging-Face-Checkpoints, ComfyUI-Modelle, LoRAs, GGUF-Dateien, dasselbe
Modell in fp32 **und** bf16, derselbe 3-GB-Text-Encoder in vier verschiedenen Apps… Eine 1-TB-Platte
ist in wenigen Wochen voll.

Auf einer echten Workstation hat Marea **125 GB Modelle** untersucht und **71 GB gefunden, die
ohne jedes Risiko die Platte verlassen konnten**:

- 18 GB byte-identische Duplikate;
- 44 GB, die identisch mit öffentlichen Dateien auf Hugging Face sind;
- 9 GB bf16-Gewichte, die sich **Bit für Bit** aus ihren fp32-Originalen berechnen lassen.

Marea löscht nichts davon, sondern lässt es **verdunsten**. Jede Datei bleibt mit Namen und
Größe in ihrem Ordner, belegt aber **0 Byte**. Öffnet ein Programm sie, holt Marea sie sofort
zurück und **prüft ihren SHA-256**. Deine Apps merken nichts.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/tour-dark.gif"><img src="../images/tour.gif" width="880" alt="Marea-Übersicht"></picture></p>

## Was daran neu ist

Aufräum-Tools raten anhand von Ordnernamen und fragen *„Löschen?“*. Deduplizierer sehen nur
byte-identische Dateien. Marea **beweist, woher sich jedes Byte wieder beschaffen lässt**, und
gibt nur frei, was es zurückholen kann.

| | Aufräum-Tools | Deduplizierer / Hardlinks | **Marea** |
|---|---|---|---|
| Weiß, woher eine Datei wiederkommt | ✗ | ✗ | **✓ kryptografischer Nachweis (SHA-256)** |
| Gibt Speicher frei, ohne zu löschen | ✗ | teilweise | **✓ 0-Byte-Platzhalter** |
| Holt Dateien beim Öffnen von selbst zurück | ✗ | — | **✓ transparent und geprüft** |
| Erkennt fp32 ↔ bf16-Kopien desselben Modells | ✗ | ✗ | **✓ bitgenaues Rezept** |
| Gleicht mit öffentlichen Hubs ab | ✗ | ✗ | **✓ Hugging Face, auf einen Commit fixiert** |
| Wählt nach Rückkehrkosten × letzter Nutzung aus | ✗ | ✗ | **✓ „die Flut“** |

## Modelle und Sicherheit

Die Seite **Modelle** fasst Dateien zu Modellen zusammen (Repositories im Hugging-Face-Cache,
Ollama-Tags, Modellordner, geteilte Checkpoints, Einzeldateien) und zeigt, was jedes ist, gelesen
aus seinem eigenen Header: **Format** (GGUF, safetensors, PyTorch, ONNX), **Quantisierung oder
Genauigkeit** (`Q4_K_M`, `BF16`), **Parameter**, Größe und Zustand.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models.png"><img src="../images/models.png" width="820" alt="Modelle: Format, Genauigkeit, Parameter, Größe, Zustand und Sicherheit"></picture></p>

Sie beantwortet auch eine immer wichtigere Frage: **Könnte das Laden dieses Modells Code
ausführen?** Ältere Formate (`.ckpt`, `.pt`, `.pth`, viele `.bin`) nutzen Python-Pickle. Marea liest
dessen Anweisungen, **ohne sie auszuführen**, und urteilt ruhig: *Sicheres Format*, *Kein Code
gefunden*, *Vor dem Laden prüfen* (ungewöhnliche Importe, TorchScript-Code, GGUF-Chat-Templates, die
aus ihrer Sandbox ausbrechen wollen) oder *Gefährlich*. Ein gerade geladenes Modell prüfen:
`mareactl check <Datei oder Ordner>`.

## Neu in 0.8

Marea gibt jetzt auch unter **Linux** (x64 und ARM64) Speicher frei, mit `mareactl`. Den Dienst einmal mit `sudo mareactl service install` installieren; danach funktionieren `add`, `scan` und `free` wie unter Windows. Eine freigegebene Datei bleibt mit derselben Größe an ihrem Platz und kommt geprüft zurück, sobald ein Programm sie öffnet. Dateien, die ein laufender Modellserver geöffnet hat, werden nie angefasst. Getestet mit Hugging Face (`huggingface_hub` 2.5) und mit Ollama, auch auf der GPU.

## Neu in 0.7

Ollama 0.40 hat einige GGUF-Modelle umgewandelt und das Original zusätzlich behalten, nur für die Rückkehr zu einer älteren Version: Diese Modelle belegen doppelt so viel Platz. Marea findet diese **Rollback-Kopien** jetzt und gibt sie frei, ohne sie zu löschen: Gehst du je zu einem älteren Ollama zurück, kommt das Original von selbst aus der Ollama-Registry zurück.

## Neu in 0.6

Die Sicherheitsprüfung für Modelle läuft jetzt auch auf **macOS und Linux**: `marea-check` unter Releases herunterladen (gleiche Urteile, JSON und Exit-Codes wie `mareactl check`). Beim ersten Start findet Marea auch die Ordner von **LM Studio, Jan und GPT4All**, und bei Dateien von Hugging Face zeigt es, was **die Sicherheitsscanner von Hugging Face** melden.

## Neu in 0.5

**Teilen** speichert ein Bild mit deinen Zahlen (freigegebener Speicher, Modelle, null gelöschte Bytes), ohne Pfade oder Dateinamen. Der Filter **Seit 90 Tagen ungenutzt** findet vergessene Modelle. Und wenn ein Programm (Virenscanner, Backup, andere Modellverwalter) in wenigen Minuten viele verdunstete Dateien zurückholt, sagt Marea dir welches und lässt dich es **blockieren**: Seine Lesezugriffe schlagen fehl und die Dateien bleiben verdunstet, während alle anderen Programme sie normal öffnen.

## Gefährdete Modelle

Modelle verschwinden: Repositories werden gelöscht, privat oder beschränkt, Dateien in einer neuen Version entfernt, Ollama-Tags zurückgezogen. Jeden Tag prüft Marea die öffentlichen Quellen und erkennt die Anzeichen früh, solange die fixierten Bytes noch geliefert werden: Verdunstete Dateien, die nur von einer verschwindenden Quelle abhängen, werden **zurückgeholt**, mögliche letzte Kopien werden **nicht mehr freigegeben** und auf den **Spiegel** (NAS) kopiert. Ein Netzwerkfehler zählt nie als Anzeichen. Auf der Kommandozeile: `mareactl watch --json`.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models-risk.png"><img src="../images/models-risk.png" width="820" alt="Ein Modell, das seine öffentliche Quelle entfernt hat: Marea behält es und bietet an, es auf den Spiegel zu kopieren"></picture></p>

## Gebaut für KI-Agenten

`mareactl` ist bereit für KI-Assistenten: versioniertes JSON (`--json`), klare Exit-Codes
(`2` Bestätigung nötig, `3` prüfen, `4` gefährlich, `5` nur lesen), der Modus `MAREA_READONLY=1`,
der jede Änderung verweigert, und nichts ändert sich ohne `--yes`. Der vollständige Vertrag steht in
[docs/AGENTS.md](../AGENTS.md) (auf Englisch).

## So funktioniert es

| Klasse | Nachweis | Kommt zurück von |
|---|---|---|
| **Duplikat** | gleicher SHA-256 wie eine andere Datei auf der Platte | dieser lokalen Kopie, in Sekunden |
| **Ableitbar** | wird aus anderen lokalen Dateien neu gebaut (z. B. bf16 aus fp32), bewiesen durch den Fingerabdruck des vollständigen Neuaufbaus | einer Neuberechnung |
| **Wiederbeschaffbar** | gleicher SHA-256 wie eine öffentliche, auf einen Commit fixierte Hugging-Face-Datei oder eine geprüfte Kopie auf dem NAS | Download / LAN |
| **Einzigartig** | keine bekannte Quelle | **verdunstet nie**: zuerst auf das NAS replizieren |

**Die Flut** hält ein Ziel an freiem Speicher (standardmäßig 100 GB) und ordnet Kandidaten nach
*erwarteten Kosten* = Wahrscheinlichkeit, dass du die Datei bald brauchst × Sekunden bis zur
Rückkehr ÷ freigegebene GB. Der Automatikmodus ist **standardmäßig aus**.

Marea nutzt die **Windows-Cloud-Files-API**, dieselbe Technik wie die „Nur online“-Dateien von
OneDrive. Statt der Cloud dient die Herkunft jeder Datei als Quelle. Keine Adminrechte, kein
eigener Treiber, kein Hintergrunddienst.

## Speicher in 3 Klicks freigeben

Wähle *wie viel* (10 GB, 50 GB, bis zum Ziel oder alles, was sicher ist) und *wie die Dateien
zurückkommen sollen*: **nur sofortige Rückkehr** (Duplikate und Rezepte, in Sekunden und offline)
oder auch vom NAS bzw. aus dem Internet. Drücke **Jetzt freigeben**; ein einziger Button **Diesen
Vorgang rückgängig machen** holt genau diese Gruppe zurück. Auf der Kommandozeile:
`mareactl free --all --instant --yes` und `mareactl undo`.

## Sicherheit

1. **Nichts verlässt die Platte ohne einen unabhängigen, geprüften Rückweg**, der danach noch existiert.
2. **Nachweis im letzten Moment**: exklusiver Zugriff, neuer Fingerabdruck jedes Bytes, die Quelle
   muss *jetzt* antworten.
3. **Jedes zurückkehrende Byte wird erneut geprüft.**
4. **Ein Ordner wird nie gelöst, solange er Platzhalter enthält**, denn Windows würde sie löschen.
5. **Du entscheidest**: Der erste Scan misst nur. „Alles zurückholen“ ist ein Klick, und das
   Deinstallationsprogramm führt es aus, bevor es irgendetwas entfernt.
6. **Minimales Netzwerk**: nur HTTPS zu Hugging Face, keine Telemetrie.

Details: [SECURITY.md](../../SECURITY.md).

## Neu in 0.2

- **Quellen werden selbst gefunden**: Hugging-Face-Repositorys werden aus Hinweisen auf der Platte
  abgeleitet (Cache, `base_model`, `_name_or_path`, git, Ollama-Modelle von `hf.co`).
- **Ollama als Quelle**: Ollama-Modelle kommen aus dessen Registry zurück, geprüft per
  Fingerabdruck. Ohne Ollama ändert sich nichts.
- **Update-Hinweise** (nur Benachrichtigung) und **von GitHub gebaute Releases** mit Attestierung:
  `gh attestation verify <Datei> --repo indecenti/marea`.
- **Praxistest**: 63,7 GB in 65 s analysiert, 19,9 GB in 11 s freigegeben, bf16 in 35 s neu
  berechnet und bitgenau identisch.

## Installation

> **Vorabversion.** Die Builds sind noch nicht signiert: SmartScreen kann beim ersten Start
> warnen. Prüfe den Download mit der `.sha256`-Datei.

1. Lade `Marea-<Version>-win-x64-portable.zip` unter [Releases](https://github.com/indecenti/marea/releases) herunter.
2. Entpacken und **`Marea.exe`** starten (.NET ist enthalten).
3. Vor dem Löschen des Ordners: *Einstellungen → Alles zurückholen und lösen*.

Voraussetzungen: Windows 10 1809+ oder Windows 11, x64, NTFS.

## FAQ

**Was passiert, wenn Marea nicht läuft und ein Programm eine verdunstete Datei öffnet?** Das
Programm erhält einen Fehler, wie bei geschlossenem OneDrive. Starte Marea, und alles
funktioniert wieder.

**Kann ich eine Datei verlieren?** Marea ist so gebaut, dass das nicht passiert: Es lässt nur
Dateien mit einer geprüften, fortbestehenden Quelle verdunsten und prüft bei der Rückkehr erneut.

**Braucht es Internet?** Nur für Dateien, deren Quelle Hugging Face ist.

## Mitmachen · Lizenz

Lies [CONTRIBUTING.md](../../CONTRIBUTING.md). Lizenz [MIT](../../LICENSE) © Marea contributors.
