<p align="center"><img src="../../src/Marea.App/Assets/marea.png" width="104" alt="Logo de Marea"></p>

<h1 align="center">Marea</h1>

<p align="center"><b>Tus modelos de IA se comen tu disco. Marea te devuelve el espacio, sin borrar un solo byte.</b></p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.it.md">Italiano</a> ·
  <b>Español</b> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.pt.md">Português</a> ·
  <a href="README.zh.md">简体中文</a>
</p>

---

📖 [Manual de usuario (en inglés)](../MANUAL.md) · [Tester guide](../TESTING.md)

La IA local pesa. Checkpoints de Hugging Face, modelos de ComfyUI, LoRA, archivos GGUF, el
mismo modelo en fp32 **y** en bf16, el mismo codificador de texto de 3 GB copiado en cuatro
aplicaciones… Un disco de 1 TB se llena en semanas.

En una estación de trabajo real, Marea analizó **125 GB de modelos** y encontró **71 GB que
podían salir del disco sin ningún riesgo**:

- 18 GB de duplicados idénticos byte a byte;
- 44 GB idénticos a archivos públicos de Hugging Face;
- 9 GB de pesos bf16 que se recalculan **bit a bit** desde sus originales fp32.

Marea no borra nada de eso: lo **evapora**. Cada archivo se queda en su carpeta, con su nombre
y su tamaño, pero ocupa **0 bytes** en el disco. Cuando un programa lo abre, Marea lo devuelve
al instante y **verifica su SHA-256**. Tus aplicaciones no lo notan.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/tour-dark.gif"><img src="../images/tour.gif" width="880" alt="Resumen de Marea"></picture></p>

## Qué tiene de nuevo

Los limpiadores adivinan por el nombre de las carpetas y preguntan *"¿lo borro?"*. Los
deduplicadores solo ven archivos idénticos byte a byte. Marea **demuestra de dónde se puede
recuperar cada byte** y solo libera lo que sabe devolver.

| | Limpiadores | Deduplicadores / enlaces duros | **Marea** |
|---|---|---|---|
| Sabe de dónde recuperar un archivo | ✗ | ✗ | **✓ prueba criptográfica (SHA-256)** |
| Libera espacio sin borrar | ✗ | en parte | **✓ marcadores de 0 bytes** |
| Devuelve los archivos solo al abrirlos | ✗ | — | **✓ transparente y verificado** |
| Reconoce copias fp32 ↔ bf16 del mismo modelo | ✗ | ✗ | **✓ receta exacta al bit** |
| Compara con hubs públicos | ✗ | ✗ | **✓ Hugging Face, fijado a un commit** |
| Decide qué liberar por coste de regreso × uso reciente | ✗ | ✗ | **✓ "la marea"** |

## Modelos y seguridad

La página **Modelos** agrupa los archivos en modelos (repositorios de la caché de Hugging Face,
etiquetas de Ollama, carpetas de modelos, checkpoints en partes, archivos sueltos) y muestra qué es
cada uno, leído de su propia cabecera: **formato** (GGUF, safetensors, PyTorch, ONNX),
**cuantización o precisión** (`Q4_K_M`, `BF16`), **parámetros**, tamaño y estado.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models.png"><img src="../images/models.png" width="820" alt="Modelos: formato, precisión, parámetros, tamaño, estado y seguridad"></picture></p>

También responde a una pregunta cada vez más importante: **¿cargar este modelo podría ejecutar
código?** Los formatos antiguos (`.ckpt`, `.pt`, `.pth`, muchos `.bin`) usan pickle de Python. Marea
lee sus instrucciones **sin ejecutarlas** y da un veredicto sereno: *Formato seguro*, *Sin código*,
*Revisar antes de cargar* (imports inusuales, código TorchScript, plantillas de chat GGUF que
intentan salir de su sandbox) o *Peligroso*. Para revisar un modelo recién descargado:
`mareactl check <archivo o carpeta>`.

## Novedades de la 0.8

Marea libera espacio también en **Linux** (x64 y ARM64), con `mareactl`. Se instala el servicio una vez con `sudo mareactl service install`; después `add`, `scan` y `free` funcionan como en Windows. Un archivo liberado sigue en su sitio con el mismo tamaño y vuelve, verificado, en cuanto un programa lo abre. Los archivos que tiene abiertos un servidor de modelos en marcha nunca se tocan. Probado con Hugging Face (`huggingface_hub` 2.5) y con Ollama, también en la GPU.

## Novedades de la 0.7

Ollama 0.40 convirtió algunos modelos GGUF y guardó también el original, solo para poder volver a una versión anterior: esos modelos ocupan el doble. Marea ahora encuentra estas **copias de reversión** y las libera sin borrarlas: si algún día vuelves a un Ollama más antiguo, el original regresa solo desde el registro de Ollama.

## Novedades de la 0.6

La revisión de seguridad de modelos ahora funciona también en **macOS y Linux**: descarga `marea-check` desde Releases (mismos veredictos, JSON y códigos de salida que `mareactl check`). En el primer inicio Marea encuentra también las carpetas de **LM Studio, Jan y GPT4All**, y para los archivos de Hugging Face muestra lo que dicen **los escáneres de seguridad de Hugging Face**.

## Novedades de la 0.5

**Compartir** guarda una imagen con tus números (espacio liberado, modelos, cero bytes borrados), sin rutas ni nombres de archivo. El filtro **Sin usar en 90 días** encuentra los modelos olvidados. Y si un programa (antivirus, copias de seguridad, otros gestores de modelos) trae de vuelta muchos archivos evaporados en pocos minutos, Marea te lo dice y te deja **bloquearlo**: sus lecturas fallan y los archivos siguen evaporados, mientras los demás programas los abren con normalidad.

## Modelos en riesgo

Los modelos desaparecen: repositorios borrados, privados o restringidos, archivos retirados en una nueva versión, etiquetas de Ollama retiradas. Cada día Marea revisa las fuentes públicas y detecta las señales a tiempo, mientras los bytes fijados aún se sirven: **trae de vuelta** los archivos evaporados que solo dependen de una fuente que desaparece, **no vuelve a liberar** los que pueden ser la última copia y los **copia al espejo** (NAS). Un fallo de red nunca cuenta como señal. Desde la línea de comandos: `mareactl watch --json`.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models-risk.png"><img src="../images/models-risk.png" width="820" alt="Un modelo que su fuente pública retiró: Marea lo conserva y ofrece copiarlo al espejo"></picture></p>

## Pensado para agentes de IA

`mareactl` está listo para asistentes de IA: JSON versionado (`--json`), códigos de salida claros
(`2` requiere confirmación, `3` revisar, `4` peligroso, `5` solo lectura), modo `MAREA_READONLY=1`
que rechaza todo cambio, y nada cambia sin `--yes`. El contrato completo está en
[docs/AGENTS.md](../AGENTS.md) (en inglés).

## Cómo funciona

| Clase | Prueba | Vuelve desde |
|---|---|---|
| **Duplicado** | mismo SHA-256 que otro archivo del disco | esa copia local, en segundos |
| **Derivable** | se reconstruye desde otros archivos locales (p. ej. bf16 desde fp32), probado con la huella de la reconstrucción completa | un recálculo |
| **Recuperable** | mismo SHA-256 que un archivo público de Hugging Face fijado a un commit, o una copia verificada en el NAS | descarga / red local |
| **Único** | sin fuente conocida | **nunca se evapora**: replícalo antes en el NAS |

**La marea** mantiene un umbral de espacio libre (100 GB por defecto) y ordena los candidatos
por *coste esperado* = probabilidad de necesitarlo pronto × segundos para recuperarlo ÷ GB
liberados. El modo automático está **desactivado por defecto**.

Marea usa la **API Cloud Files de Windows**, la misma de los archivos "solo en línea" de
OneDrive, con la procedencia de cada archivo en lugar de la nube. Sin permisos de
administrador, sin controlador propio y sin servicio en segundo plano.

## Liberar espacio en 3 clics

Elige *cuánto* (10 GB, 50 GB, hasta el umbral o todo lo que es seguro) y *cómo deben volver los
archivos*: **solo regreso instantáneo** (duplicados y recetas, en segundos y sin internet) o también
desde el NAS o internet. Pulsa **Liberar ahora**; un solo botón **Deshacer esta operación** devuelve
exactamente ese grupo. En la línea de comandos: `mareactl free --all --instant --yes` y `mareactl undo`.

## Seguridad

1. **Nada sale sin una vía de regreso independiente y verificada** que siga existiendo después.
2. **Prueba en el último momento**: acceso exclusivo, nueva huella de cada byte, la fuente debe
   responder *ahora*.
3. **Cada byte que vuelve se verifica de nuevo.**
4. **Una carpeta nunca se desvincula mientras contenga marcadores**, porque Windows los borraría.
5. **Tú decides**: el primer escaneo solo mide. "Devolverlo todo" es un clic, y el desinstalador
   lo ejecuta antes de quitar nada.
6. **Red mínima**: solo HTTPS hacia Hugging Face, sin telemetría.

Detalles: [SECURITY.md](../../SECURITY.md).

## Novedades de la 0.2

- **Fuentes encontradas solas**: los repositorios de Hugging Face se deducen de indicios que ya
  están en el disco (caché, `base_model`, `_name_or_path`, git, modelos de Ollama desde `hf.co`).
- **Fuente Ollama**: los modelos de Ollama vuelven desde su registro, verificados por huella.
  Si no usas Ollama, no cambia nada.
- **Aviso de actualizaciones** (solo aviso) y **versiones compiladas por GitHub** con atestación:
  `gh attestation verify <archivo> --repo indecenti/marea`.
- **Prueba real**: 63,7 GB analizados en 65 s, 19,9 GB liberados en 11 s, bf16 recalculado en 35 s
  e idéntico bit a bit.

## Instalación

> **Versión preliminar.** Las compilaciones aún no están firmadas: SmartScreen puede avisar en
> la primera ejecución. Verifica la descarga con el archivo `.sha256`.

1. Descarga `Marea-<versión>-win-x64-portable.zip` desde [Releases](https://github.com/indecenti/marea/releases).
2. Descomprímelo y ejecuta **`Marea.exe`** (incluye .NET).
3. Antes de borrar la carpeta: *Ajustes → Devolver todo y desvincular*.

Requisitos: Windows 10 1809+ o Windows 11, x64, NTFS.

## Preguntas frecuentes

**¿Y si Marea no está activa cuando un programa abre un archivo evaporado?** El programa recibe
un error, como con OneDrive cerrado. Inicia Marea y vuelve a funcionar.

**¿Puedo perder un archivo?** Marea está diseñada para impedirlo: solo evapora archivos con una
fuente verificada que sobrevive y verifica de nuevo al volver.

**¿Necesita internet?** Solo para los archivos cuya fuente es Hugging Face.

## Contribuir · Licencia

Lee [CONTRIBUTING.md](../../CONTRIBUTING.md). Licencia [MIT](../../LICENSE) © Marea contributors.
