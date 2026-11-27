<p align="center"><img src="../../src/Marea.App/Assets/marea.png" width="104" alt="Marea 标志"></p>

<h1 align="center">Marea</h1>

<p align="center"><b>你的 AI 模型正在吃掉你的硬盘。Marea 把空间还给你，而且不删除任何一个字节。</b></p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.pt.md">Português</a> ·
  <b>简体中文</b>
</p>

---

📖 [用户手册（英文）](../MANUAL.md) · [Tester guide](../TESTING.md)

本地 AI 很占空间。Hugging Face 的 checkpoint、ComfyUI 模型、LoRA、GGUF 文件，同一个模型既有 fp32 **又有** bf16，同一个 3 GB 的文本编码器被复制到四个应用里……一块 1 TB 的硬盘几周就满了。

在一台真实的工作站上，Marea 分析了 **125 GB 的模型**，发现其中 **71 GB 可以毫无风险地离开硬盘**：

- 18 GB 是逐字节完全相同的重复文件；
- 44 GB 与 Hugging Face 上的公开文件完全相同；
- 9 GB 是 bf16 权重，可以从对应的 fp32 原件**逐位**重新计算出来。

Marea 不会删除其中任何一个，而是让它们**蒸发**。每个文件仍然留在原来的文件夹里，名称和大小都不变，但在硬盘上只占 **0 字节**。当某个程序打开它时，Marea 会立即把它取回，并**校验它的 SHA-256**。你的应用不会察觉到任何变化。

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/tour-dark.gif"><img src="../images/tour.gif" width="880" alt="Marea 概览"></picture></p>

## 新在哪里

清理工具根据文件夹名称来猜，然后问你*“要删除吗？”*。去重工具只能看到逐字节完全相同的文件。Marea 则**证明每个字节可以从哪里重新获得**，并且只释放它能取回的内容。

| | 清理工具 | 去重工具 / 硬链接 | **Marea** |
|---|---|---|---|
| 知道文件可以从哪里取回 | ✗ | ✗ | **✓ 密码学证明（SHA-256）** |
| 不删除也能释放空间 | ✗ | 部分 | **✓ 0 字节占位符** |
| 打开文件时自动取回 | ✗ | — | **✓ 透明且经过校验** |
| 识别同一模型的 fp32 ↔ bf16 副本 | ✗ | ✗ | **✓ 逐位精确的配方** |
| 与公共模型库比对 | ✗ | ✗ | **✓ Hugging Face，固定到某个 commit** |
| 按取回成本 × 最近使用情况决定释放什么 | ✗ | ✗ | **✓ “潮汐”** |

## 模型与安全

**模型**页面把文件归并为模型（Hugging Face 缓存中的仓库、Ollama 标签、模型文件夹、分片的 checkpoint、单个文件），并显示每个模型是什么，这些信息直接读取自文件自身的头部：**格式**（GGUF、safetensors、PyTorch、ONNX）、**量化或精度**（`Q4_K_M`、`BF16`）、**参数量**、大小和状态。

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models.png"><img src="../images/models.png" width="820" alt="模型：格式、精度、参数量、大小、状态和安全性"></picture></p>

它还回答了一个越来越重要的问题：**加载这个模型会不会执行代码？** 较旧的格式（`.ckpt`、`.pt`、`.pth`，以及许多 `.bin`）使用 Python pickle。Marea 读取其中的指令但**不执行它们**，并给出冷静的结论：*安全格式*、*未发现代码*、*加载前请检查*（不常见的导入、TorchScript 代码、试图逃出沙箱的 GGUF 聊天模板）或*危险*。检查刚下载的模型：`mareactl check <文件或文件夹>`。

## 0.8 新功能

Marea 现在也可以在 **Linux**（x64 和 ARM64）上通过 `mareactl` 释放空间。先用 `sudo mareactl service install` 安装一次服务；之后 `add`、`scan` 和 `free` 的用法与 Windows 上相同。被释放的文件仍留在原位，大小不变，一旦有程序打开它，就会经过校验后取回。正在运行的模型服务器所打开的文件永远不会被触碰。已在 Hugging Face（`huggingface_hub` 2.5）和 Ollama 上测试，包括在 GPU 上运行。

## 0.7 新功能

Ollama 0.40 转换了部分 GGUF 模型，同时保留了原件，仅仅是为了能够回退到旧版本：这些模型因此占用了双倍空间。Marea 现在能找到这些**回滚副本**，并在不删除的情况下释放它们：如果你将来回退到更旧的 Ollama，原件会自动从 Ollama 的注册表取回。

## 0.6 新功能

模型安全检查现在也可以在 **macOS 和 Linux** 上运行：从 Releases 下载 `marea-check`（结论、JSON 和退出码与 `mareactl check` 相同）。首次启动时，Marea 还会找到 **LM Studio、Jan 和 GPT4All** 的文件夹；对于来自 Hugging Face 的文件，它会显示 **Hugging Face 安全扫描器**给出的结果。

## 0.5 新功能

**分享**会保存一张包含你的数据的图片（已释放的空间、模型数量、零字节被删除），其中不含路径和文件名。筛选器**90 天未使用**可以找出被遗忘的模型。如果某个程序（杀毒软件、备份工具、其他模型管理器）在几分钟内取回了大量已蒸发的文件，Marea 会告诉你是哪个程序，并允许你**阻止它**：它的读取会失败，文件保持蒸发状态，而其他所有程序仍可正常打开这些文件。

## 有风险的模型

模型会消失：仓库被删除、转为私有或受限，文件在新版本中被移除，Ollama 标签被撤回。Marea 每天检查公开来源，趁固定的字节仍可获取时及早发现迹象：仅依赖某个正在消失的来源的已蒸发文件会被**取回**，可能是最后一份副本的文件**不再被释放**，并被复制到**镜像**（NAS）。网络错误永远不会被当作迹象。命令行用法：`mareactl watch --json`。

<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="../images/dark/models-risk.png"><img src="../images/models-risk.png" width="820" alt="一个公开来源已被移除的模型：Marea 保留它，并提供复制到镜像的选项"></picture></p>

## 为 AI 智能体而生

`mareactl` 已为 AI 助手做好准备：带版本的 JSON（`--json`）、清晰的退出码（`2` 需要确认，`3` 需检查，`4` 危险，`5` 只读）、拒绝一切更改的 `MAREA_READONLY=1` 模式，而且没有 `--yes` 就不会发生任何更改。完整的约定见 [docs/AGENTS.md](../AGENTS.md)（英文）。

## 工作原理

| 类别 | 证明 | 从哪里取回 |
|---|---|---|
| **重复** | 与硬盘上另一个文件的 SHA-256 相同 | 那份本地副本，只需几秒 |
| **可推导** | 可由其他本地文件重建（例如由 fp32 得到 bf16），以完整重建结果的指纹为证 | 重新计算 |
| **可重新获取** | 与固定到某个 commit 的 Hugging Face 公开文件，或 NAS 上经过校验的副本 SHA-256 相同 | 下载 / 局域网 |
| **唯一** | 没有已知来源 | **永不蒸发**：请先复制到 NAS |

**潮汐**维持一个可用空间目标（默认 100 GB），并按*预期成本*对候选文件排序：预期成本 = 你近期需要该文件的概率 × 取回所需秒数 ÷ 释放的 GB 数。自动模式**默认关闭**。

Marea 使用 **Windows Cloud Files API**，也就是 OneDrive“仅联机”文件所用的同一技术，只是用每个文件的出处代替了云端。无需管理员权限，没有自带驱动，也没有后台服务。

## 3 次点击释放空间

选择*释放多少*（10 GB、50 GB、直到目标值，或所有安全的内容）以及*文件如何取回*：**仅限即时取回**（重复文件和配方，几秒完成且无需联网），或者也包括从 NAS 或互联网取回。点击**立即释放**；只需一个按钮**撤销此操作**，就能把这一组文件原样取回。命令行用法：`mareactl free --all --instant --yes` 和 `mareactl undo`。

## 安全

1. **没有独立且经过校验、事后依然存在的取回途径，任何东西都不会离开硬盘。**
2. **最后一刻的证明**：独占访问，对每个字节重新计算指纹，来源必须在*此刻*作出响应。
3. **每个取回的字节都会再次校验。**
4. **只要文件夹中还有占位符，就绝不会解除该文件夹的关联**，否则 Windows 会删除它们。
5. **由你决定**：首次扫描只做测量。“全部取回”只需一次点击，卸载程序在移除任何内容之前会先执行它。
6. **最少的网络访问**：只通过 HTTPS 连接 Hugging Face，没有遥测。

详情：[SECURITY.md](../../SECURITY.md)。

## 0.2 新功能

- **自动发现来源**：根据硬盘上已有的线索（缓存、`base_model`、`_name_or_path`、git、来自 `hf.co` 的 Ollama 模型）推断出 Hugging Face 仓库。
- **Ollama 作为来源**：Ollama 模型从它的注册表取回，并通过指纹校验。如果你不使用 Ollama，一切照旧。
- **更新提示**（仅通知）以及**由 GitHub 构建的发布版本**，带有证明：
  `gh attestation verify <文件> --repo indecenti/marea`。
- **实测**：65 秒分析 63.7 GB，11 秒释放 19.9 GB，35 秒重新计算出 bf16，且逐位完全相同。

## 安装

> **预发布版本。** 构建尚未签名：首次运行时 SmartScreen 可能会发出警告。请用 `.sha256` 文件校验下载内容。

1. 从 [Releases](https://github.com/indecenti/marea/releases) 下载 `Marea-<版本>-win-x64-portable.zip`。
2. 解压并运行 **`Marea.exe`**（已包含 .NET）。
3. 删除文件夹之前：*设置 → 全部取回并解除关联*。

系统要求：Windows 10 1809+ 或 Windows 11，x64，NTFS。

## 常见问题

**如果 Marea 没有运行，而某个程序打开了已蒸发的文件，会怎样？** 该程序会收到一个错误，就像 OneDrive 关闭时一样。启动 Marea，一切就恢复正常。

**我会丢失文件吗？** Marea 的设计就是为了避免这种情况：它只蒸发拥有经过校验且持续存在的来源的文件，并在取回时再次校验。

**需要联网吗？** 只有来源是 Hugging Face 的文件才需要。

## 参与贡献 · 许可证

请阅读 [CONTRIBUTING.md](../../CONTRIBUTING.md)。许可证 [MIT](../../LICENSE) © Marea contributors。
