<p align="center"><img src="../../src/Marea.App/Assets/marea.png" width="104" alt="Logo do Marea"></p>

<h1 align="center">Marea</h1>

<p align="center"><b>Seus modelos de IA estão devorando seu disco. O Marea devolve o espaço, sem apagar um único byte.</b></p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <b>Português</b> ·
  <a href="README.zh.md">简体中文</a>
</p>

---

📖 [Manual do usuário (em inglês)](../MANUAL.md) · [Tester guide](../TESTING.md)

IA local pesa. Checkpoints do Hugging Face, modelos do ComfyUI, LoRAs, arquivos GGUF, o mesmo
modelo em fp32 **e** em bf16, o mesmo codificador de texto de 3 GB copiado em quatro aplicativos…
Um disco de 1 TB enche em poucas semanas.

Em uma estação de trabalho real, o Marea analisou **125 GB de modelos** e encontrou **71 GB que
podiam sair do disco sem nenhum risco**:

- 18 GB de duplicatas idênticas byte a byte;
- 44 GB idênticos a arquivos públicos do Hugging Face;
- 9 GB de pesos bf16 que podem ser recalculados **bit a bit** a partir dos originais fp32.

O Marea não apaga nada disso: ele **evapora**. Cada arquivo continua na sua pasta, com o mesmo
nome e o mesmo tamanho, mas ocupa **0 byte** no disco. Quando um programa o abre, o Marea o traz
de volta na hora e **verifica o SHA-256**. Seus aplicativos nem percebem.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/tour-dark.gif"><img src="../images/tour.gif" width="880" alt="Visão geral do Marea"></picture></p>

## O que há de novo nisso

As ferramentas de limpeza adivinham pelo nome das pastas e perguntam *"Apagar?"*. Os
desduplicadores só enxergam arquivos idênticos byte a byte. O Marea **prova de onde cada byte pode
ser recuperado** e só libera o que sabe trazer de volta.

| | Ferramentas de limpeza | Desduplicadores / hardlinks | **Marea** |
|---|---|---|---|
| Sabe de onde recuperar um arquivo | ✗ | ✗ | **✓ prova criptográfica (SHA-256)** |
| Libera espaço sem apagar | ✗ | em parte | **✓ marcadores de 0 byte** |
| Traz os arquivos de volta sozinho ao abrir | ✗ | — | **✓ transparente e verificado** |
| Reconhece cópias fp32 ↔ bf16 do mesmo modelo | ✗ | ✗ | **✓ receita exata ao bit** |
| Compara com hubs públicos | ✗ | ✗ | **✓ Hugging Face, fixado em um commit** |
| Escolhe o que liberar por custo de retorno × uso recente | ✗ | ✗ | **✓ "a maré"** |

## Modelos e segurança

A página **Modelos** agrupa os arquivos em modelos (repositórios do cache do Hugging Face, tags do
Ollama, pastas de modelos, checkpoints divididos em partes, arquivos avulsos) e mostra o que é
cada um, lido do próprio cabeçalho: **formato** (GGUF, safetensors, PyTorch, ONNX), **quantização
ou precisão** (`Q4_K_M`, `BF16`), **parâmetros**, tamanho e estado.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models.png"><img src="../images/models.png" width="820" alt="Modelos: formato, precisão, parâmetros, tamanho, estado e segurança"></picture></p>

Ela também responde a uma pergunta cada vez mais importante: **carregar este modelo poderia
executar código?** Os formatos mais antigos (`.ckpt`, `.pt`, `.pth`, muitos `.bin`) usam pickle do
Python. O Marea lê as instruções **sem executá-las** e dá um veredito sereno: *Formato seguro*,
*Nenhum código encontrado*, *Verificar antes de carregar* (imports incomuns, código TorchScript,
templates de chat GGUF que tentam escapar da sandbox) ou *Perigoso*. Para verificar um modelo
recém-baixado: `mareactl check <arquivo ou pasta>`.

## Novidades da 0.8

O Marea agora libera espaço também no **Linux** (x64 e ARM64), com o `mareactl`. Instale o serviço uma vez com `sudo mareactl service install`; depois disso, `add`, `scan` e `free` funcionam como no Windows. Um arquivo liberado continua no lugar com o mesmo tamanho e volta, verificado, assim que um programa o abre. Arquivos que um servidor de modelos em execução mantém abertos nunca são tocados. Testado com Hugging Face (`huggingface_hub` 2.5) e com Ollama, também na GPU.

## Novidades da 0.7

O Ollama 0.40 converteu alguns modelos GGUF e guardou também o original, só para permitir a volta a uma versão anterior: esses modelos ocupam o dobro do espaço. O Marea agora encontra essas **cópias de reversão** e as libera sem apagá-las: se um dia você voltar a um Ollama mais antigo, o original retorna sozinho a partir do registro do Ollama.

## Novidades da 0.6

A verificação de segurança de modelos agora funciona também no **macOS e no Linux**: baixe o `marea-check` em Releases (mesmos vereditos, JSON e códigos de saída do `mareactl check`). Na primeira execução, o Marea encontra também as pastas do **LM Studio, Jan e GPT4All** e, para os arquivos do Hugging Face, mostra o que dizem **os scanners de segurança do Hugging Face**.

## Novidades da 0.5

**Compartilhar** salva uma imagem com seus números (espaço liberado, modelos, zero bytes apagados), sem caminhos nem nomes de arquivo. O filtro **Sem uso há 90 dias** encontra os modelos esquecidos. E se um programa (antivírus, backup, outros gerenciadores de modelos) trouxer de volta muitos arquivos evaporados em poucos minutos, o Marea diz qual foi e deixa você **bloqueá-lo**: as leituras dele falham e os arquivos continuam evaporados, enquanto todos os outros programas os abrem normalmente.

## Modelos em risco

Modelos desaparecem: repositórios apagados, tornados privados ou restritos, arquivos removidos em uma nova versão, tags do Ollama retiradas. Todos os dias o Marea verifica as fontes públicas e percebe os sinais a tempo, enquanto os bytes fixados ainda são servidos: os arquivos evaporados que dependem apenas de uma fonte que está desaparecendo são **trazidos de volta**, as possíveis últimas cópias **deixam de ser liberadas** e são copiadas para o **espelho** (NAS). Uma falha de rede nunca conta como sinal. Na linha de comando: `mareactl watch --json`.

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models-risk.png"><img src="../images/models-risk.png" width="820" alt="Um modelo cuja fonte pública foi removida: o Marea o conserva e oferece copiá-lo para o espelho"></picture></p>

## Feito para agentes de IA

O `mareactl` está pronto para assistentes de IA: JSON versionado (`--json`), códigos de saída
claros (`2` confirmação necessária, `3` verificar, `4` perigoso, `5` somente leitura), o modo
`MAREA_READONLY=1`, que recusa qualquer alteração, e nada muda sem `--yes`. O contrato completo
está em [docs/AGENTS.md](../AGENTS.md) (em inglês).

## Como funciona

| Classe | Prova | Volta a partir de |
|---|---|---|
| **Duplicata** | mesmo SHA-256 de outro arquivo no disco | essa cópia local, em segundos |
| **Derivável** | é reconstruído a partir de outros arquivos locais (p. ex. bf16 a partir de fp32), provado pela impressão digital da reconstrução completa | um recálculo |
| **Recuperável** | mesmo SHA-256 de um arquivo público do Hugging Face fixado em um commit, ou de uma cópia verificada no NAS | download / rede local |
| **Único** | nenhuma fonte conhecida | **nunca evapora**: replique antes no NAS |

**A maré** mantém uma meta de espaço livre (100 GB por padrão) e ordena os candidatos por *custo
esperado* = probabilidade de você precisar do arquivo em breve × segundos para trazê-lo de volta ÷
GB liberados. O modo automático vem **desativado por padrão**.

O Marea usa a **API Cloud Files do Windows**, a mesma técnica dos arquivos "somente online" do
OneDrive, com a procedência de cada arquivo no lugar da nuvem. Sem permissões de administrador,
sem driver próprio e sem serviço em segundo plano.

## Liberar espaço em 3 cliques

Escolha *quanto* (10 GB, 50 GB, até a meta ou tudo o que for seguro) e *como os arquivos devem
voltar*: **somente retorno instantâneo** (duplicatas e receitas, em segundos e offline) ou também
a partir do NAS ou da internet. Clique em **Liberar agora**; um único botão **Desfazer esta
operação** traz de volta exatamente esse grupo. Na linha de comando:
`mareactl free --all --instant --yes` e `mareactl undo`.

## Segurança

1. **Nada sai do disco sem um caminho de volta independente e verificado** que continue existindo depois.
2. **Prova no último instante**: acesso exclusivo, nova impressão digital de cada byte, a fonte
   precisa responder *agora*.
3. **Cada byte que volta é verificado de novo.**
4. **Uma pasta nunca é desvinculada enquanto contiver marcadores**, porque o Windows os apagaria.
5. **Você decide**: a primeira varredura apenas mede. "Trazer tudo de volta" é um clique, e o
   desinstalador faz isso antes de remover qualquer coisa.
6. **Rede mínima**: somente HTTPS para o Hugging Face, sem telemetria.

Detalhes: [SECURITY.md](../../SECURITY.md).

## Novidades da 0.2

- **Fontes encontradas sozinhas**: os repositórios do Hugging Face são deduzidos de indícios que
  já estão no disco (cache, `base_model`, `_name_or_path`, git, modelos do Ollama vindos de `hf.co`).
- **Ollama como fonte**: os modelos do Ollama voltam a partir do registro dele, verificados por
  impressão digital. Se você não usa o Ollama, nada muda.
- **Aviso de atualizações** (somente aviso) e **versões compiladas pelo GitHub** com atestação:
  `gh attestation verify <arquivo> --repo indecenti/marea`.
- **Teste real**: 63,7 GB analisados em 65 s, 19,9 GB liberados em 11 s, bf16 recalculado em 35 s
  e idêntico bit a bit.

## Instalação

> **Versão preliminar.** As compilações ainda não são assinadas: o SmartScreen pode exibir um
> aviso na primeira execução. Verifique o download com o arquivo `.sha256`.

1. Baixe `Marea-<versão>-win-x64-portable.zip` em [Releases](https://github.com/indecenti/marea/releases).
2. Descompacte e execute **`Marea.exe`** (o .NET já está incluído).
3. Antes de apagar a pasta: *Configurações → Trazer tudo de volta e desvincular*.

Requisitos: Windows 10 1809+ ou Windows 11, x64, NTFS.

## Perguntas frequentes

**E se o Marea não estiver em execução quando um programa abrir um arquivo evaporado?** O programa
recebe um erro, como acontece com o OneDrive fechado. Inicie o Marea e tudo volta a funcionar.

**Posso perder um arquivo?** O Marea foi projetado para que isso não aconteça: só evapora arquivos
com uma fonte verificada que continua existindo, e verifica de novo na volta.

**Precisa de internet?** Somente para os arquivos cuja fonte é o Hugging Face.

## Contribuir · Licença

Leia [CONTRIBUTING.md](../../CONTRIBUTING.md). Licença [MIT](../../LICENSE) © Marea contributors.
