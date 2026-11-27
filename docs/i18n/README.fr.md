<p align="center"><img src="../../src/Marea.App/Assets/marea.png" width="104" alt="Logo de Marea"></p>

<h1 align="center">Marea</h1>

<p align="center"><b>Vos modèles d'IA dévorent votre disque. Marea vous rend l'espace, sans effacer un seul octet.</b></p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.es.md">Español</a> ·
  <b>Français</b> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.pt.md">Português</a> ·
  <a href="README.zh.md">简体中文</a>
</p>

---

📖 [Manuel utilisateur (en anglais)](../MANUAL.md) · [Tester guide](../TESTING.md)

L'IA locale pèse lourd. Checkpoints Hugging Face, modèles ComfyUI, LoRA, fichiers GGUF, le
même modèle en fp32 **et** en bf16, le même encodeur de texte de 3 Go copié dans quatre
applications… Un disque de 1 To se remplit en quelques semaines.

Sur une vraie station de travail, Marea a analysé **125 Go de modèles** et en a trouvé
**71 Go pouvant quitter le disque sans aucun risque** :

- 18 Go de doublons identiques octet par octet ;
- 44 Go identiques à des fichiers publics de Hugging Face ;
- 9 Go de poids bf16 recalculables **bit à bit** depuis leurs originaux fp32.

Marea n'efface rien : elle **fait s'évaporer**. Chaque fichier reste dans son dossier, avec son
nom et sa taille, mais occupe **0 octet**. Quand un programme l'ouvre, Marea le ramène à la
volée et **vérifie son SHA-256**. Vos applications ne remarquent rien.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/tour-dark.gif"><img src="../images/tour.gif" width="880" alt="Vue d'ensemble de Marea"></picture></p>

## Ce qui est nouveau

Les nettoyeurs devinent d'après le nom des dossiers et demandent *« je supprime ? »*. Les
outils de déduplication ne voient que les fichiers identiques. Marea **prouve d'où chaque octet
peut être récupéré** et ne libère que ce qu'elle sait ramener.

| | Nettoyeurs | Déduplication / liens physiques | **Marea** |
|---|---|---|---|
| Sait d'où récupérer un fichier | ✗ | ✗ | **✓ preuve cryptographique (SHA-256)** |
| Libère de l'espace sans effacer | ✗ | en partie | **✓ fichiers fantômes de 0 octet** |
| Ramène les fichiers seuls à l'ouverture | ✗ | — | **✓ transparent et vérifié** |
| Reconnaît les copies fp32 ↔ bf16 d'un modèle | ✗ | ✗ | **✓ recette exacte au bit** |
| Compare avec les hubs publics | ✗ | ✗ | **✓ Hugging Face, figé à un commit** |
| Choisit quoi libérer selon coût du retour × usage récent | ✗ | ✗ | **✓ « la marée »** |

## Modèles et sécurité

La page **Modèles** regroupe les fichiers en modèles (dépôts du cache Hugging Face, tags Ollama,
dossiers de modèles, checkpoints en plusieurs parties, fichiers isolés) et montre ce qu'est chacun,
lu dans son propre en-tête : **format** (GGUF, safetensors, PyTorch, ONNX), **quantification ou
précision** (`Q4_K_M`, `BF16`), **paramètres**, taille et état.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models.png"><img src="../images/models.png" width="820" alt="Modèles : format, précision, paramètres, taille, état et sécurité"></picture></p>

Elle répond aussi à une question de plus en plus importante : **charger ce modèle pourrait-il
exécuter du code ?** Les anciens formats (`.ckpt`, `.pt`, `.pth`, beaucoup de `.bin`) utilisent le
pickle de Python. Marea lit ses instructions **sans les exécuter** et donne un verdict posé :
*Format sûr*, *Aucun code trouvé*, *À vérifier avant de charger* (imports inhabituels, code
TorchScript, modèles de chat GGUF qui tentent de sortir de leur sandbox) ou *Dangereux*. Pour
vérifier un modèle tout juste téléchargé : `mareactl check <fichier ou dossier>`.

## Nouveautés de la 0.8

Marea libère aussi de l'espace sous **Linux** (x64 et ARM64), avec `mareactl`. On installe le service une fois avec `sudo mareactl service install` ; ensuite `add`, `scan` et `free` fonctionnent comme sous Windows. Un fichier libéré reste à sa place avec la même taille et revient, vérifié, dès qu'un programme l'ouvre. Les fichiers ouverts par un serveur de modèles en marche ne sont jamais touchés. Testé avec Hugging Face (`huggingface_hub` 2.5) et avec Ollama, y compris sur GPU.

## Nouveautés de la 0.7

Ollama 0.40 a converti certains modèles GGUF et a aussi gardé l'original, seulement pour revenir à une version antérieure : ces modèles prennent deux fois plus de place. Marea trouve maintenant ces **copies de retour arrière** et les libère sans les supprimer : si un jour vous revenez à un Ollama plus ancien, l'original revient seul depuis le registre d'Ollama.

## Nouveautés de la 0.6

La vérification de sécurité des modèles fonctionne aussi sur **macOS et Linux** : téléchargez `marea-check` depuis Releases (mêmes verdicts, JSON et codes de sortie que `mareactl check`). Au premier lancement, Marea trouve aussi les dossiers de **LM Studio, Jan et GPT4All**, et pour les fichiers venant de Hugging Face il affiche ce qu'en disent **les scanners de sécurité de Hugging Face**.

## Nouveautés de la 0.5

**Partager** enregistre une image avec vos chiffres (espace libéré, modèles, zéro octet supprimé), sans chemins ni noms de fichiers. Le filtre **Inutilisés depuis 90 jours** trouve les modèles oubliés. Et si un programme (antivirus, sauvegarde, autre gestionnaire de modèles) ramène beaucoup de fichiers évaporés en quelques minutes, Marea vous le signale et vous permet de le **bloquer** : ses lectures échouent et les fichiers restent évaporés, tandis que les autres programmes les ouvrent normalement.

## Modèles menacés

Les modèles disparaissent : dépôts supprimés, rendus privés ou restreints, fichiers retirés dans une nouvelle version, tags Ollama retirés. Chaque jour, Marea vérifie les sources publiques et repère les signes à l'avance, tant que les octets épinglés sont encore servis : il **ramène** les fichiers évaporés qui ne dépendent que d'une source qui disparaît, **ne libère plus** ceux qui sont peut-être la dernière copie et les **copie sur le miroir** (NAS). Un échec réseau ne compte jamais comme un signe. En ligne de commande : `mareactl watch --json`.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models-risk.png"><img src="../images/models-risk.png" width="820" alt="Un modèle retiré par sa source publique : Marea le garde et propose de le copier sur le miroir"></picture></p>

## Conçu pour les agents IA

`mareactl` est prêt pour les assistants IA : JSON versionné (`--json`), codes de sortie clairs
(`2` confirmation requise, `3` à vérifier, `4` dangereux, `5` lecture seule), mode
`MAREA_READONLY=1` qui refuse tout changement, et rien ne change sans `--yes`. Le contrat complet est
dans [docs/AGENTS.md](../AGENTS.md) (en anglais).

## Comment ça marche

| Classe | Preuve | Revient depuis |
|---|---|---|
| **Doublon** | même SHA-256 qu'un autre fichier du disque | cette copie locale, en quelques secondes |
| **Dérivable** | reconstruit depuis d'autres fichiers locaux (p. ex. bf16 depuis fp32), prouvé par l'empreinte de la reconstruction complète | un recalcul |
| **Récupérable** | même SHA-256 qu'un fichier public Hugging Face figé à un commit, ou une copie vérifiée sur le NAS | téléchargement / réseau local |
| **Unique** | aucune source connue | **ne s'évapore jamais** : répliquez-le d'abord sur le NAS |

**La marée** maintient un seuil d'espace libre (100 Go par défaut) et classe les candidats par
*coût attendu* = probabilité d'en avoir besoin bientôt × secondes pour le récupérer ÷ Go libérés.
Le mode automatique est **désactivé par défaut**.

Marea s'appuie sur l'**API Cloud Files de Windows**, celle des fichiers « en ligne uniquement »
de OneDrive. La provenance de chaque fichier remplace le cloud. Pas de droits administrateur,
pas de pilote propre, pas de service en arrière-plan.

## Libérer de l'espace en 3 clics

Choisissez *combien* (10 Go, 50 Go, jusqu'au seuil ou tout ce qui est sûr) et *comment les fichiers
doivent revenir* : **retour instantané uniquement** (doublons et recettes, en quelques secondes et hors
ligne) ou aussi depuis le NAS ou internet. Appuyez sur **Libérer maintenant** ; un seul bouton
**Annuler cette opération** ramène exactement ce lot. En ligne de commande :
`mareactl free --all --instant --yes` et `mareactl undo`.

## Sécurité

1. **Rien ne sort sans un chemin de retour indépendant et vérifié** qui existera encore après.
2. **Preuve au dernier moment** : accès exclusif, nouvelle empreinte de chaque octet, la source
   doit répondre *maintenant*.
3. **Chaque octet qui revient est revérifié.**
4. **Un dossier n'est jamais détaché tant qu'il contient des fichiers fantômes**, car Windows les supprimerait.
5. **Vous décidez** : la première analyse ne fait que mesurer. « Tout ramener » se fait en un clic,
   et le désinstalleur le lance avant de retirer quoi que ce soit.
6. **Réseau minimal** : HTTPS vers Hugging Face uniquement, aucune télémétrie.

Détails : [SECURITY.md](../../SECURITY.md).

## Nouveautés de la 0.2

- **Sources trouvées seules** : les dépôts Hugging Face sont déduits d'indices déjà présents sur le
  disque (cache, `base_model`, `_name_or_path`, git, modèles Ollama depuis `hf.co`).
- **Source Ollama** : les modèles Ollama reviennent depuis son registre, vérifiés par empreinte.
  Sans Ollama, rien ne change.
- **Avis de mise à jour** (notification seulement) et **versions compilées par GitHub** avec
  attestation : `gh attestation verify <fichier> --repo indecenti/marea`.
- **Test réel** : 63,7 Go analysés en 65 s, 19,9 Go libérés en 11 s, bf16 recalculé en 35 s et
  identique bit à bit.

## Installation

> **Préversion.** Les builds ne sont pas encore signées : SmartScreen peut avertir au premier
> lancement. Vérifiez le téléchargement avec le fichier `.sha256`.

1. Téléchargez `Marea-<version>-win-x64-portable.zip` depuis [Releases](https://github.com/indecenti/marea/releases).
2. Décompressez puis lancez **`Marea.exe`** (.NET inclus).
3. Avant de supprimer le dossier : *Paramètres → Tout ramener et détacher*.

Prérequis : Windows 10 1809+ ou Windows 11, x64, NTFS.

## FAQ

**Et si Marea n'est pas lancée quand un programme ouvre un fichier évaporé ?** Le programme
reçoit une erreur, comme avec OneDrive fermé. Relancez Marea et tout refonctionne.

**Puis-je perdre un fichier ?** Marea est conçue pour l'empêcher : elle n'évapore que des fichiers
ayant une source vérifiée qui survit, et revérifie au retour.

**Faut-il internet ?** Seulement pour les fichiers dont la source est Hugging Face.

## Contribuer · Licence

Lisez [CONTRIBUTING.md](../../CONTRIBUTING.md). Licence [MIT](../../LICENSE) © Marea contributors.
