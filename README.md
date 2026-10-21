# LinkMend

**Windows 快捷方式诊断与修复工具 / Windows shortcut diagnostics and repair.**

中文优先、本地运行、先解释原因再修改，写入前备份，支持预览与安全撤销。

Chinese-first and local-first. Explain before changing, back up before writing, and support preview and safe undo.

![CI](https://github.com/qinmuran0316/LinkMend/actions/workflows/ci.yml/badge.svg)

[简体中文](#简体中文) · [English](#english)

---

## 简体中文

LinkMend 面向常见的“快捷方式坏了”场景：目标程序被移动或卸载、图标变白、起始位置失效、磁盘暂时离线、同名程序难以辨认，以及多个快捷方式使用了不同的数据目录。它不会把所有读取失败都粗暴地判成“快捷方式失效”，也不会在证据不足时自动改文件。

> 界面截图来自项目自带的隔离测试快捷方式，不包含真实用户数据。

![诊断界面](docs/screenshots/diagnosis.png)

### 核心能力

| 能力 | 具体行为 |
| --- | --- |
| 扫描 | 默认桌面与开始菜单，支持自定义文件夹、拖入 `.lnk`，显示进度并可取消 |
| 目标诊断 | 区分目标不存在、磁盘未就绪、网络离线、权限不足、路径无效和未知读取错误 |
| 白图标诊断 | 检查自定义图标源是否存在、索引是否可取；只对可验证的图标问题提供修复 |
| 起始位置 | 目标仍存在时，将无效起始位置改到程序目录；磁盘离线等情况下不擅自修改 |
| 搬家定位 | 结合 App Paths、卸载记录和有限目录扫描，给出候选程序与证据，由用户选择 |
| 数据目录检查 | 分析显式启动参数，报告不可用目录和同目标快捷方式之间的配置冲突，不自动改参数 |
| 修复预览 | 逐字段展示目标、起始位置、图标的修改前后值 |
| 安全修复 | 修复前备份原始 `.lnk`，写入后重新读取验证；验证失败自动恢复 |
| 撤销 | 按批次撤销；撤销前检查文件是否在修复后又被修改，发现变化则停止 |
| 审计 | 修复与撤销历史保存在本地，包含快照、校验值和每项结果 |

#### 定位候选

![查找新位置](docs/screenshots/relocation.png)

#### 修改预览

![预览修复](docs/screenshots/preview.png)

#### 备份与撤销

![修复历史](docs/screenshots/history.png)

### 安全模型

LinkMend 把“准确判断”和“安全写入”拆成独立步骤：

1. **扫描不启动目标程序。** 只读取 `.lnk` 字段并用文件系统元数据验证路径；不执行链接目标。
2. **不把未知当成损坏。** 磁盘/网络不可用、权限不足、解析失败会分别报告，不会触发“改为另一个程序”的建议。
3. **不自动选择候选。** 同名程序可能有多个；界面展示路径、可信度、元数据和匹配理由，最终由用户确认。
4. **修改前先备份。** 原始 `.lnk` 字节和 SHA-256 写入本地批次目录；历史记录同时保存修改前后的字段。
5. **扫描后若文件变化，拒绝覆盖。** 修复前会重新计算当前哈希；与扫描时不一致就跳过。
6. **写入后重新读取验证。** 目标、起始位置、图标不符合预期时自动用快照恢复。
7. **撤销不覆盖新变化。** 撤销前逐一比对当前文件哈希与修复后的哈希；任何冲突都会中止整次撤销并列出原因。
8. **不主动提权。** 应用以当前用户权限运行；权限不足会如实提示，需要时由用户自行决定是否以管理员身份运行。

### 快速开始

#### 运行环境

- Windows 10 1809+ 或 Windows 11，x64
- 运行时采用自包含发布时，用户不需要预装 .NET；从源码构建需要 **.NET 10 SDK**

#### 从源码构建

```powershell
dotnet restore LinkMend.slnx
dotnet build LinkMend.slnx -c Release
dotnet test tests/LinkMend.Tests/LinkMend.Tests.csproj -c Release
dotnet run --project src/LinkMend.App/LinkMend.App.csproj -c Debug
```

#### 生成免安装发布包

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

输出：

```text
artifacts/release/LinkMend-v0.1.0-win-x64.zip
```

发布包为自包含单文件 WPF 应用，解压后运行 `LinkMend.exe` 即可。GitHub Actions 也会在每次构建时上传同名 ZIP 作为构建产物。

### 使用流程

1. 选择桌面、开始菜单，或添加你要检查的文件夹；也可以把 `.lnk` 文件/文件夹拖入窗口。
2. 点击“开始检查”。扫描只读取快捷方式，不会启动任何程序。
3. 在左侧查看问题列表，在右侧阅读判断依据、路径对照和建议。
   - 勾选可自动修复的项，然后点击“预览并修复已选”。
   - 目标丢失的项先点击“查找新位置”，确认候选后再修复。
4. 在预览窗口逐项确认修改前后的值，再应用修复。
5. 在“修复历史”中查看备份或撤销整个批次。

> 只有“目标丢失”才会进入新位置搜索。磁盘离线、权限不足等情况需先恢复访问后重新扫描，避免把可恢复的配置永久改坏。

### 诊断规则

#### 目标路径

| 情况 | 判定 | 可做什么 |
| --- | --- | --- |
| 文件或文件夹存在 | 正常 | 无需处理 |
| 目标不存在 | 失效，可定位 | 搜索候选；用户确认后改目标 |
| 磁盘未就绪 / 光驱无介质 | 当前不可用，**不是永久失效** | 恢复磁盘后重新扫描 |
| 网络共享/映射盘离线 | 当前不可用 | 恢复网络后重新扫描 |
| 拒绝访问 | 状态未知 | 检查权限；可尝试以管理员身份重新扫描 |
| 路径格式无效 | 无法解析 | 手动重建或指定 |
| Shell 命名空间 / 特殊托管项 | 不支持 | 保留现状，不自动修改 |

#### 图标

- 自定义图标文件不存在：在目标可用时可改为目标自带图标，或恢复系统默认图标。
- 图标文件存在但记录的索引取不到：可改用目标程序图标或清除失效的自定义图标。
- 目标丢失且未单独指定图标：白图标是目标缺失的表现，先解决目标。
- **目标与图标路径都正常但图标仍为白**：可能是 Windows 图标缓存问题。本版不做强制缓存清理，也不会假装修复。

#### 起始位置

- 目标存在、起始位置不存在：可安全改为目标所在目录。
- 起始位置位于离线磁盘/网络或无权访问：只提示，不自动修改，以免覆盖有意的配置。
- 起始位置为空：使用系统默认行为，不报问题。

#### 数据目录参数

LinkMend 只分析以下**显式启动参数**：

```text
--user-data-dir
--data-dir
--datadir
--user-data-path
```

- 目录所在磁盘/网络离线或无权限访问：警告，不自动改启动参数。
- 同一目标文件的多个快捷方式使用不同的数据目录：提示可能存在配置冲突；也可能是有意隔离。由用户判断。
- 目录暂时不存在不单独判为故障，应用可能自行创建。

这不是应用配置的通用解析器。它只提供可解释的线索，不会猜测某个程序的私有配置文件格式。

### 新位置搜索如何工作

候选来源：

1. Windows **App Paths** 注册表项；
2. 卸载记录中的 `InstallLocation` / `DisplayIcon`；
3. 用户添加的搜索目录；
4. 原目标所在目录、快捷方式所在目录；
5. 各固定磁盘上的常见安装目录（Program Files、Programs、Apps、Software、Tools 等）和当前用户的 `LocalAppData\Programs`。

搜索有深度、目录数、结果数和单目录时间上限；不跟随重解析点。候选按以下证据排序：

- 文件名一致；
- 系统登记来源；
- 所在文件夹与原位置同名；
- 产品名、发布者、文件说明一致（可从原文件读取时）；
- 文件大小接近。

所有同名候选都会明确标出可信度，**不会自动应用**。

### 项目结构

```text
src/
  LinkMend.Core/      领域模型、诊断规则、定位评分、备份/修复/撤销、扫描编排
  LinkMend.Windows/   IShellLink COM 读写、文件/图标/磁盘探测、安装索引、有限目录搜索、STA 执行器
  LinkMend.App/       WPF 中文界面、设置、对话框、视图模型
tests/
  LinkMend.Tests/     单元测试 + ShellLink COM 真实读写测试
docs/
  architecture.md     分层、流程、数据格式与安全不变量
scripts/
  publish.ps1         自包含发布与 ZIP 打包
  Create-Icon.ps1     生成应用图标
```

核心服务不依赖 WPF；Windows 专用实现与 UI 分离。ShellLink COM 调用统一在专用 STA 线程上执行，避免在异步续体里跨套间使用 COM。

更多设计细节见 [docs/architecture.md](docs/architecture.md)。

### 测试

```powershell
dotnet test tests/LinkMend.Tests/LinkMend.Tests.csproj --nologo
```

当前测试覆盖：

- 中文、空格、环境变量和相对路径；
- 目标丢失、磁盘离线、权限不足、图标源/索引失效、特殊快捷方式；
- 数据目录冲突只标记真正携带该参数的快捷方式；
- 候选排序、去重、取消和文件消失；
- 预览计划生成；
- 修复备份、扫描后变化拒绝、写入校验失败自动回滚；
- 撤销成功、修复后再次变化拒绝撤销、重复撤销拒绝；
- 备份路径穿越防护、带 BOM 的历史文件；
- 通过真实 `IShellLink` / `WScript.Shell` 创建、读取、修改和清除图标。

### 数据位置

默认目录：

```text
%LOCALAPPDATA%\LinkMend\
  Backups\<批次号>\item-001.bak ...
  History\<批次号>.json
  settings.json
  linkmend.log
```

- 备份是原始 `.lnk` 的明文快照。
- 历史记录包含路径、修改前后字段、SHA-256 和结果。
- LinkMend 运行时不发起网络请求，不上传快捷方式或程序信息。

### 已知限制

- 只处理经典 `.lnk`；不处理 `.url`、MSI 公告式快捷方式、Shell 命名空间项和 UWP 专用链接。
- 不清理 Windows 图标缓存，因此“路径都正常但图标仍白”的情况会保留原状。
- 不搜索全盘；搜索范围有限，找不到时请添加软件所在目录。
- 同名程序的元数据可能相同，最终需要用户确认，不能保证语义上一定是同一个程序。
- 撤销以批次为单位；批次内任一文件在修复后被修改，整次撤销会停止。
- 备份未加密，能读取当前用户 `%LOCALAPPDATA%` 的其他进程也可能读取这些快照。
- LinkMend 使用 ShellLink 加载并保存 `.lnk`；不理解的 Shell 扩展字段是否保留取决于系统实现，因此所有写入都有备份。
- 扫描是只读的，但网络路径不可达时单次文件探测可能受系统超时影响。

### 设计参考

以下项目只作为实现思路和界面方向参考，本项目未复制其源代码，也没有引入其为运行时依赖：

- [securifybv/ShellLink](https://github.com/securifybv/ShellLink) — .NET 中 ShellLink 字段处理的思路
- [Matmaus/LnkParse3](https://github.com/Matmaus/LnkParse3) — 异常/损坏 `.lnk` 的解析视角
- [lepoco/wpfui](https://github.com/lepoco/wpfui) — Fluent 风格方向

界面使用原生 WPF 自绘样式，避免额外 UI 依赖并缩小发布包。运行时依赖只有 .NET，测试依赖 xUnit 与 Microsoft.NET.Test.Sdk。

### 许可证

[MIT](LICENSE)

---

## English

LinkMend addresses the common failure modes of Windows shortcuts: a target application moved or uninstalled, a white icon, a broken working directory, a temporarily unavailable disk, ambiguous programs with the same filename, and shortcuts that point the same program at different data directories. It does not treat every read failure as a permanently broken shortcut, and it does not modify files when the evidence is insufficient.

> Screenshots use isolated test shortcuts created by the project. No real user data is included.

![Diagnosis window](docs/screenshots/diagnosis.png)

The application UI is currently Chinese-first; this English section documents the complete behavior and workflow.

### Core capabilities

| Capability | Behavior |
| --- | --- |
| Scan | Desktop and Start Menu by default; custom folders and dropped `.lnk` files; progress and cancellation |
| Target diagnosis | Distinguishes a missing target, offline disk, offline network path, access denied, invalid path, and unknown read errors |
| White-icon diagnosis | Checks whether a custom icon source exists and whether its index can be read; only verifiable icon problems get automatic fixes |
| Working directory | Changes an invalid start-in directory to the program folder when the target exists; does not modify it when a disk is offline |
| Relocation | Combines App Paths, uninstall records, and a bounded directory search into candidates with evidence; the user makes the choice |
| Data directory checks | Analyzes explicit command-line switches; reports unavailable directories and conflicts between shortcuts for the same target; never changes arguments automatically |
| Repair preview | Shows before/after values for target, working directory, and icon field by field |
| Safe repair | Backs up the original `.lnk`, reads back and verifies after writing, and restores the backup when verification fails |
| Undo | Undo is grouped by batch; before restoring, it checks whether files changed again after repair and stops on conflict |
| Audit | Repair and undo history is stored locally with snapshots, checksums, and per-item results |

#### Relocation candidates

![Find a new location](docs/screenshots/relocation.png)

#### Repair preview

![Preview repair](docs/screenshots/preview.png)

#### Backup and undo

![Repair history](docs/screenshots/history.png)

### Safety model

LinkMend separates accurate diagnosis from safe writes:

1. **Scanning never starts the target.** It reads `.lnk` fields and validates paths against filesystem metadata; it never executes the linked target.
2. **Unknown is not broken.** Offline disks or networks, access denied, and parse failures are reported separately and never trigger a "point it at another program" suggestion.
3. **No automatic candidate selection.** Several programs may share a filename. The UI shows paths, confidence, metadata, and matching evidence; the user makes the final choice.
4. **Back up before modifying.** The original `.lnk` bytes and SHA-256 are stored in a local batch directory, and the history records before/after fields.
5. **Refuse to overwrite changes made after the scan.** The current hash is recomputed before repair; a mismatch is skipped.
6. **Read back and verify after writing.** If the target, working directory, or icon does not match the expected values, the original snapshot is restored automatically.
7. **Undo never overwrites new changes.** Before each restore, the current hash is compared with the post-repair hash. Any conflict aborts the whole undo and lists the reason.
8. **No automatic elevation.** The app runs with the current user's permissions. Permission failures are reported, and running elevated is the user's decision.

### Quick start

#### Requirements

- Windows 10 1809+ or Windows 11, x64
- A self-contained release needs no preinstalled .NET runtime. Building from source requires the **.NET 10 SDK**.

#### Build from source

```powershell
dotnet restore LinkMend.slnx
dotnet build LinkMend.slnx -c Release
dotnet test tests/LinkMend.Tests/LinkMend.Tests.csproj -c Release
dotnet run --project src/LinkMend.App/LinkMend.App.csproj -c Debug
```

#### Build a portable release

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

Output:

```text
artifacts/release/LinkMend-v0.1.0-win-x64.zip
```

The package is a self-contained, single-file WPF application. Extract it and run `LinkMend.exe`. GitHub Actions also uploads a ZIP with the same name as a build artifact on every build.

### Usage workflow

1. Select Desktop or Start Menu, or add the folders you want to inspect. You can also drop `.lnk` files or folders onto the window.
2. Click **开始检查 (Start Check)**. Scanning only reads shortcuts and never launches any program.
3. Review the issue list on the left and the evidence, path comparison, and suggested action on the right.
   - Check the items that can be fixed automatically, then click **预览并修复已选 (Preview and Repair Selected)**.
   - For an item with a missing target, click **查找新位置 (Find New Location)** and confirm a candidate first.
4. In the preview window, review every before/after value, then apply the repair.
5. Open **修复历史 (Repair History)** to inspect backups or undo a whole batch.

> Relocation search only runs for a missing target. For offline disks or permission failures, restore access and rescan instead, so a recoverable configuration is not permanently changed.

### Diagnostic rules

#### Target paths

| Situation | Assessment | What you can do |
| --- | --- | --- |
| File or folder exists | Healthy | No action needed |
| Target does not exist | Broken; relocation available | Search for candidates; change the target after user confirmation |
| Disk not ready / no media in optical drive | Temporarily unavailable, **not permanently broken** | Restore the disk, then rescan |
| Network share or mapped drive offline | Temporarily unavailable | Restore the network, then rescan |
| Access denied | Unknown state | Check permissions; optionally rescan as administrator |
| Invalid path format | Cannot be parsed | Rebuild or specify the shortcut manually |
| Shell namespace / special managed item | Unsupported | Leave it unchanged; no automatic modification |

#### Icons

- A custom icon file does not exist: when the target is available, use the target's own icon or restore the system default.
- The icon file exists but the recorded index cannot be read: use the target program's icon or clear the invalid custom icon.
- The target is missing and no separate icon was specified: the white icon is a symptom of the missing target; fix the target first.
- **The target and icon paths both look valid but the icon is still white**: this may be a Windows icon-cache issue. This version does not force a cache rebuild and does not pretend to fix it.

#### Working directory

- The target exists but the working directory does not: safely change it to the target's folder.
- The working directory is on an offline disk/network or is not accessible: report it only; do not change it automatically, because it may be intentional.
- The working directory is empty: use the system default behavior and do not report a problem.

#### Data directory arguments

LinkMend analyzes only these **explicit command-line switches**:

```text
--user-data-dir
--data-dir
--datadir
--user-data-path
```

- The directory's disk/network is offline or inaccessible: warn and do not change the command-line arguments.
- Several shortcuts for the same target use different data directories: report a possible configuration conflict; it may also be intentional isolation. The user decides.
- A directory that does not exist yet is not treated as a failure; the application may create it.

This is not a general parser for application configuration. It provides explainable clues only and does not guess a program's private configuration format.

### How relocation search works

Candidate sources:

1. Windows **App Paths** registry entries;
2. `InstallLocation` / `DisplayIcon` from uninstall records;
3. search folders added by the user;
4. the original target directory and the shortcut's directory;
5. common installation folders on fixed drives (Program Files, Programs, Apps, Software, Tools, and similar) plus the current user's `LocalAppData\Programs`.

Search depth, directory count, result count, and per-directory time are bounded. Reparse points are not followed. Candidates are ranked by:

- same filename;
- a registered system source;
- same parent folder name as the original location;
- matching product name, publisher, and file description (when readable from the original file);
- similar file size.

Every same-name candidate is labeled with a confidence level and is **never applied automatically**.

### Project layout

```text
src/
  LinkMend.Core/      domain models, diagnosis rules, relocation scoring, backup/repair/undo, scan orchestration
  LinkMend.Windows/   IShellLink COM access, file/icon/disk probes, installation index, bounded search, STA executor
  LinkMend.App/       Chinese WPF UI, settings, dialogs, view models
tests/
  LinkMend.Tests/     unit tests + real ShellLink COM read/write tests
docs/
  architecture.md     layering, flows, data formats, safety invariants
scripts/
  publish.ps1         self-contained publishing and ZIP packaging
  Create-Icon.ps1     application icon generation
```

Core services do not depend on WPF; Windows-specific implementations are separated from the UI. ShellLink COM calls run on a dedicated STA thread, so COM objects are never used across an await or apartment boundary.

See [docs/architecture.md](docs/architecture.md) for more design details.

### Tests

```powershell
dotnet test tests/LinkMend.Tests/LinkMend.Tests.csproj --nologo
```

Current coverage includes:

- Chinese and space-containing paths, environment variables, and relative paths;
- missing targets, offline disks, access denied, missing/invalid icon sources, and special shortcuts;
- data-directory conflicts only flagging shortcuts that actually carry the switch;
- candidate ranking, deduplication, cancellation, and disappearing files;
- repair-plan generation;
- backup, refusal after post-scan changes, and automatic rollback after verification failure;
- successful undo, refusal after post-repair changes, and refusal of a second undo;
- backup path-traversal protection and history JSON with a UTF-8 BOM;
- creating, reading, modifying, and clearing icons through real `IShellLink` / `WScript.Shell`.

### Data location

Default directory:

```text
%LOCALAPPDATA%\LinkMend\
  Backups\<batch-id>\item-001.bak ...
  History\<batch-id>.json
  settings.json
  linkmend.log
```

- Backups are plain snapshots of the original `.lnk` files.
- History includes paths, before/after fields, SHA-256 hashes, and results.
- LinkMend makes no network requests at runtime and does not upload shortcuts or program information.

### Known limitations

- Only classic `.lnk` shortcuts are supported. `.url`, advertised MSI shortcuts, Shell namespace entries, and UWP-specific links are not handled.
- Windows icon caches are not cleared, so a white icon with valid paths is left as-is.
- The search is not a full-disk scan. If the software is not found, add its directory and search again.
- Programs with the same filename may share identical metadata; the user must confirm the match, and LinkMend cannot guarantee semantic identity.
- Undo is batch-based. If any file in the batch changed after repair, the whole undo stops.
- Backups are not encrypted. Other processes that can read the current user's `%LOCALAPPDATA%` may be able to read these snapshots.
- LinkMend loads and saves `.lnk` files through ShellLink. Whether unfamiliar Shell extension fields survive depends on the system implementation, which is why every write has a backup.
- Scanning is read-only, but a single file probe may be delayed by system timeouts when a network path is unreachable.

### Design references

The following projects informed implementation ideas and UI direction only. No source code was copied, and none is a runtime dependency:

- [securifybv/ShellLink](https://github.com/securifybv/ShellLink) — ideas for handling ShellLink fields in .NET
- [Matmaus/LnkParse3](https://github.com/Matmaus/LnkParse3) — a perspective on parsing unusual or damaged `.lnk` files
- [lepoco/wpfui](https://github.com/lepoco/wpfui) — Fluent visual direction

The interface uses custom native WPF styles to avoid an extra UI dependency and keep the package smaller. The only runtime dependency is .NET; tests use xUnit and Microsoft.NET.Test.Sdk.

### License

[MIT](LICENSE)
