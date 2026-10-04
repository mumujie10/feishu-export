# 飞书导出

> 作者：木木杰

「飞书导出」是一个支持 **Windows / macOS / Linux** 的飞书文档导出工具，可以把飞书**知识库**或**个人空间云文档**的全部文档同步到本地，目录结构与原知识库保持一致。支持导出 `docx`、`pdf`、`markdown` 三种格式，表格类文档导出为 `xlsx`，云空间里的普通文件（pdf/zip/图片等）按原格式下载。

提供两种使用方式：

- **图形界面（推荐）**：`feishu-doc-export-gui`，填表 → 选知识库 → 点开始，实时看进度和失败清单。
- **命令行**：`feishu-doc-export`，适合挂机批量跑、写进脚本或定时任务。

实测 700 多个文档导出耗时约 25 分钟（后台运行，不影响正常工作）。

---

## 一、准备工作（两种方式都需要）

> 用图形界面的话这一节可以先跳过：软件首次启动会弹出「使用引导」，一步一步带你做完下面的全部事情。这里写的是给命令行用户和想手动配置的人的参考。

### 1. 获取 AppId 和 AppSecret

- 进入飞书[开发者后台](https://open.feishu.cn/app)，创建**企业自建应用**，信息随意填写。
- 打开**权限管理**，开通以下权限（注意有分页）：
  - 查看新版文档
  - 查看、评论和下载云空间中所有文件
  - 查看、评论和导出文档
  - 查看、评论、编辑和管理云空间中所有文件
  - 查看、评论、编辑和管理多维表格
  - 查看、编辑和管理知识库
  - 查看、评论、编辑和管理电子表格
  - 导出云文档
- 打开**添加应用能力**，添加**机器人**。
- 在**版本管理与发布**中创建版本并申请发布上线（等待管理员审核；仅测试时可创建测试企业与测试版本）。
- 回到**凭证与基础信息**，获取 `App ID` 和 `App Secret`。

### 2. 给应用授权知识库

- 在飞书桌面客户端创建一个群组，把上面创建的应用作为**群机器人**加进去。
- 打开知识库 → 左下角设置 → **成员设置** → 选「可编辑的成员」或「可阅读的成员」→ 在搜索框里输入**刚才那个群的名称**并添加。

> 搜索框里输应用名字是搜不到的——这个弹窗只接受用户、群组、部门、用户组，应用是靠它所在的**群**拿到权限的。
>
> 成员里也加不进群的话改用节点分享：左侧目录里右键顶层那一篇 → 分享 → 添加该群为可阅读。
>
> 授权完成后界面里的「获取列表」**仍可能是空的**：飞书的知识空间列表接口只认空间级授权。这不是失败，把知识库链接贴进输入框照样能导。

> 个人空间云文档不支持列举文件夹，必须先把目标文件夹**分享给自建应用**，再把分享链接贴进工具（会自动提取 `folderToken`）。

### 3. 获取知识库 Id / folderToken

知识库 Id（`spaceId`）可以从知识库设置页的 URL 中获取；`folderToken` 可以从文件夹分享链接中获取。

---

## 二、图形界面使用

![界面截图](docs/screenshot-macos.png)

启动 `feishu-doc-export-gui` 后，左侧按 1→2→3 的顺序填写：

1. **应用凭证**：AppId、AppSecret；开放平台地址默认飞书（国内），国际版 Lark 选第二项，也可以选「自定义」手填。
2. **导出什么**：选「知识库」后点 **获取列表** 从下拉框挑一个；**列表为空也没关系**，把知识库主页地址栏那条链接（`…/wiki/space/xxx`）整条贴进输入框即可，工具会自动提取 `spaceId`。选「个人空间云文档」时同理，直接贴文件夹分享链接。
3. **导出设置**：选择导出目录、保存格式；如果导出 markdown，建议指定 Aspose.Words 许可证文件。

**教程就在软件里**：首次启动会自动弹出 5 节「使用引导」（建应用 → 开权限 → 授权知识库 → 选目标 → 选格式开跑），看完关掉即可，之后点顶栏「使用引导」随时重看。每个卡片右上角还有个 `?`，只讲这一项该怎么填。引导页里可以一键复制需要开通的 8 项权限名单、直接打开飞书开发者后台。

顶栏右侧的「外观」可选 **跟随系统 / 浅色 / 深色**，选完记在本机配置里。

点 **开始导出** 后，右侧会显示进度条、当前文档、运行日志，以及「未导出清单」（含不支持的类型和导出失败原因）。随时可以点 **取消** 安全中断。

界面配置会自动保存到本机：

| 系统 | 配置文件位置 |
| --- | --- |
| Windows | `%APPDATA%\FeishuDocExport\settings.json` |
| macOS | `~/Library/Application Support/FeishuDocExport/settings.json` |
| Linux | `~/.config/FeishuDocExport/settings.json` |

> `AppSecret` 以明文保存在该文件中。如果不希望落盘，取消勾选「在本机记住 AppSecret」。

---

## 三、命令行使用

```
用法：
  feishu-doc-export --appId=<AppId> --appSecret=<AppSecret> --exportPath=<目录> [其它参数]
  feishu-doc-export                       # 不带参数时进入交互式向导
  feishu-doc-export --help                # 显示帮助

必填参数：
  --appId           飞书自建应用的 AppId
  --appSecret       飞书自建应用的 AppSecret
  --exportPath      文档导出的本地目录（需为绝对路径，不存在会自动创建）

可选参数：
  --type            导出对象：wiki（知识库，默认）或 cloudDoc（个人空间云文档）
  --spaceId         知识库 Id，也可以直接粘贴知识库链接（…/wiki/space/xxx）
                    不传则列出所有知识库由你选择
  --folderToken     个人空间文件夹 Token，type=cloudDoc 时必填
                    同样支持直接粘贴文件夹分享链接
  --saveType        文档保存格式：docx（默认）、pdf、md
  --apiEndpoint     开放平台地址，默认 https://open.feishu.cn
                    国际版 Lark 请传 https://open.larksuite.com
  --licensePath     Aspose.Words 许可证文件路径（仅导出 md 时需要）
  --skipExisting    目标文件已存在时跳过，便于中断后重跑
  --incremental     增量导出：只导新增和改动过的文档（见下方「定时增量导出」）
  --interval        常驻定时：每隔多久跑一次，如 45s / 30m / 6h / 1d
  --at              常驻定时：每天固定时刻跑一次，如 03:00（本地时区）
  --quit            执行完直接退出，不等待按键
```

也可以通过环境变量提供凭证，避免 AppSecret 进入 shell 历史与进程列表：

```bash
export FEISHU_APP_ID=xxx
export FEISHU_APP_SECRET=xxx
./feishu-doc-export --spaceId=xxx --exportPath=/home/user/docs
```

示例：

```bash
# 导出指定知识库为 docx
./feishu-doc-export --appId=xxx --appSecret=xxx --spaceId=xxx --exportPath=/home/user/docs

# 不指定知识库，启动后从列表里选
./feishu-doc-export --appId=xxx --appSecret=xxx --exportPath=/home/user/docs

# 导出为 markdown
./feishu-doc-export --appId=xxx --appSecret=xxx --spaceId=xxx \
    --saveType=md --licensePath=/path/Aspose.Words.lic --exportPath=/home/user/docs

# 导出个人空间云文档
./feishu-doc-export --appId=xxx --appSecret=xxx --type=cloudDoc \
    --folderToken=xxx --exportPath=/home/user/docs

# 国际版 Lark
./feishu-doc-export --appId=xxx --appSecret=xxx --spaceId=xxx \
    --apiEndpoint=https://open.larksuite.com --exportPath=/home/user/docs
```

### 定时增量导出

飞书接口返回的节点数据本来就带编辑时间（知识库的 `obj_edit_time`、云文档的 `modified_time`），
所以工具可以在导出目录里记一份 `.feishu-export-state.json`，下次只导**新增**和**编辑时间变过**的文档：

```bash
# 手动跑一次增量
./feishu-doc-export --spaceId=xxx --exportPath=/data/docs --incremental

# 常驻：每天 03:00 增量一次（Ctrl+C 停止）
FEISHU_APP_ID=xxx FEISHU_APP_SECRET=xxx \
  ./feishu-doc-export --spaceId=xxx --exportPath=/data/docs --at=03:00

# 常驻：每 6 小时一次
./feishu-doc-export --spaceId=xxx --exportPath=/data/docs --interval=6h
```

几点约定：

- `--interval` 与 `--at` 二选一；给了其中之一就进入常驻模式，并自动启用增量。
- 常驻模式必须显式给出 `--spaceId`（或 `--folderToken`）——后台没有人在终端里替它挑知识库。
- 状态文件放在**导出目录里**而不是用户配置目录，所以换机器、换挂载路径（Docker 尤其如此）都能继续增量，不会从头重导。
- 判定标准是「编辑时间没变 + 落盘路径没变 + 文件确实还在」，任一条不满足就重导；拿不到编辑时间的文档一律重导，宁可多导不可漏导。
- 单轮失败不会让常驻进程退出（否则容器会被重启策略反复拉起、每轮都从头再来），错误进日志，下一轮照跑。
- 每成功 20 篇落一次状态文件；`Ctrl+C` 和 `SIGTERM` 都会先写完状态再退出。
- 文档在飞书里被删掉时，本地文件和状态都保留不动——这个工具不删你的东西。

### Docker 部署

仓库根目录带了 `Dockerfile`、`docker/entrypoint.sh` 和 `docker-compose.yml`。镜像里只编译命令行版（容器没有显示设备，图形界面不进镜像）。

```bash
git clone https://github.com/mumujie10/feishu-export.git
cd feishu-export

cat > .env <<'EOF'
FEISHU_APP_ID=cli_xxxxxxxxxxxxx
FEISHU_APP_SECRET=xxxxxxxxxxxxxxxx
SPACE_ID=6872xxxxxxxxxxxxx
EOF

docker compose up -d --build      # 默认每天 03:00 增量导出到 ./exported
docker compose logs -f            # 看进度和下一轮时间
```

常用环境变量（`docker-compose.yml` 里都有注释）：

| 变量 | 说明 |
| --- | --- |
| `FEISHU_APP_ID` / `FEISHU_APP_SECRET` | 必填。只走环境变量，不出现在命令行和镜像层里 |
| `SPACE_ID` / `FOLDER_TOKEN` | 二选一，配合 `DOC_TYPE=wiki` 或 `DOC_TYPE=cloudDoc` |
| `SAVE_TYPE` | `docx`（默认）/ `pdf` / `md` |
| `AT` / `INTERVAL` | 定时方式，二选一；都不设就是跑一次就退出 |
| `TZ` | 决定 `AT` 按哪个时区解释，默认 `Asia/Shanghai` |
| `INCREMENTAL` | 默认 `1`；设成 `0` 就是每轮全量重导 |
| `LICENSE_PATH` | 导出 md 时的 Aspose.Words 许可证路径，需要额外挂一个 volume |

导出结果和状态文件都落在挂载出来的 `./exported` 里，容器删掉重建也不会触发全量重导。

> 镜像基于 `mcr.microsoft.com/dotnet/runtime:8.0` 并额外装了 `fontconfig`，因为 Aspose.Words 做 docx → markdown 转换时要读字体。
> **Linux 下导出 md 这条路径需要你在目标机器上实测一次**：如果只有 md 失败、docx 正常，基本就是字体或许可证的问题。

### 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 全部成功 |
| `1` | 参数错误 |
| `2` | 部分文档导出失败（失败原因会打印在末尾清单里） |
| `3` | 致命错误或被用户取消 |

首次在 Linux / macOS 上运行需要授权：

```bash
chmod +x ./feishu-doc-export
```

---

## 四、导出格式说明

| 飞书文档类型 | 导出的文件 |
| --- | --- |
| 新版文档（docx）、旧版文档（doc） | `.docx` / `.pdf` / `.md` |
| 电子表格（sheet）、多维表格（bitable） | `.xlsx` |
| 云空间文件（file） | 按原格式下载 |
| 思维笔记、幻灯片、妙记 | **不支持**，会记入「未导出清单」 |

几处实现细节：

- 飞书开放平台**不提供 markdown 导出接口**。导出 `md` 的流程是「先让飞书导出 docx → 本地用 Aspose.Words 转成 markdown」，因此下列格式会有损失：引用语法、表格、行内代码块。工具会尽力把代码块和跨文档引用修正回来。
- markdown 里的图片会保存在**与文档同名的 `.assets` 目录**中（例如 `需求说明.md` 配 `需求说明.assets/`），同名不同文档的图片不会互相覆盖。
- 如果引用的文档也在本次导出范围内，markdown 里会写成**相对路径**；引用其他知识库或外链则保持原样。
- 同一目录下的同名文档会自动加序号（`周报.docx`、`周报 (2).docx`），不会互相覆盖。
- 文件名中的非法字符会被替换为 `-`，超长文件名按 UTF-8 字节数安全截断。

### 关于 Aspose.Words 许可证

只有**导出 markdown** 才需要 Aspose.Words（商业库）。工具按以下顺序查找许可证文件：

1. 界面上指定的路径 / 命令行的 `--licensePath`
2. 环境变量 `ASPOSE_WORDS_LICENSE`
3. 程序所在目录下的 `Aspose.Words.lic` 或 `License.lic`

**找不到许可证不会导致程序崩溃**，但转换出来的 markdown 会带上评估水印并限制文档长度，界面和日志里会给出明确提示。

---

## 五、项目结构

```
feishu-doc-export.sln
├── src/
│   ├── FeishuDocExport.Core/     核心库：飞书接口客户端、导出编排、路径生成、增量状态、markdown 转换
│   ├── FeishuDocExport.Cli/      命令行入口（产物名 feishu-doc-export）
│   └── FeishuDocExport.Gui/      Avalonia 图形界面（产物名 feishu-doc-export-gui）
├── tests/
│   └── FeishuDocExport.Tests/    单元测试
├── docker/entrypoint.sh          容器入口：把环境变量拼成命令行参数
├── Dockerfile                    命令行版的容器镜像
├── docker-compose.yml            定时增量同步的部署示例
└── scripts/build-dmg.sh          macOS .dmg 打包
```

核心库不依赖任何界面框架，命令行和图形界面共用同一套导出逻辑。

---

## 六、从源码构建

需要 .NET 8 SDK。

```bash
# 编译
dotnet build feishu-doc-export.sln -c Release

# 运行测试
dotnet test tests/FeishuDocExport.Tests/FeishuDocExport.Tests.csproj

# 发布单文件可执行程序（以 macOS Apple Silicon 为例）
dotnet publish src/FeishuDocExport.Gui/FeishuDocExport.Gui.csproj \
    -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o dist/osx-arm64

dotnet publish src/FeishuDocExport.Cli/FeishuDocExport.Cli.csproj \
    -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o dist/osx-arm64
```

可用的运行时标识符：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。

> 注意：命令行版本若要启用 `-p:PublishTrimmed=true` 裁剪，需要保留 `System.Xml` / `System.Private.Xml` 等程序集（已在 csproj 中配置），否则 Aspose.Words 的 markdown 转换会失败。

### 打包 macOS 安装包（.dmg）

```bash
./scripts/build-dmg.sh                  # 当前机器架构（Apple Silicon → osx-arm64）
./scripts/build-dmg.sh osx-x64          # Intel 版
./scripts/build-dmg.sh osx-arm64 1.1.1  # 指定版本号
```

产出 `dist/feishu-export-<版本>-<架构>.dmg`，双击挂载后把「飞书导出.app」拖进 Applications 即可。DMG 里还包含：

- `命令行工具/feishu-doc-export`：同样的功能，适合挂机批量和写脚本
- `使用说明.txt`：安装步骤与首次打开的放行方法

> **首次打开被系统拦下是正常的**：本项目没有 Apple 开发者证书，只能做 ad-hoc 签名、无法公证（notarization）。第一次打开时请右键点击 App →「打开」→ 再点一次「打开」，或在「系统设置 → 隐私与安全性」里点击「仍要打开」。只需放行一次。
>
> 应用图标由 `scripts/make-icon.py` 生成，改图标只需改脚本后重新打包。

---

## 七、常见问题

**导出 markdown 后图片或文档链接打不开？**
检查 `xxx.assets` 目录是否和 `.md` 文件在一起被移动了；跨文档引用是相对路径，移动单个文件会导致链接失效。

**提示「没有阅读或导出权限」（错误码 1069902）？**
该文档没有授权给自建应用。请把文档/知识库加进应用所在的群组，或在飞书中手动下载。这类文档会被记入「未导出清单」，不影响其它文档。

**提示「应用凭证无效」？**
检查 AppId / AppSecret 是否正确，以及应用版本是否已发布并通过审核。

**导出很慢？**
速度取决于网速、飞书服务端响应和磁盘写入。`docx` 最快，`pdf` 最慢（图片内嵌）。文档是串行导出的，遇到限流会自动退避重试。

**中断后想接着跑？**
命令行加 `--skipExisting`，界面上勾选「跳过已存在的文件」，已经导出过的文档会直接跳过。

---

## 八、更新日志

### v1.1.0（定时与容器化）

> **请从本版本开始使用。** v1.0.0 存在一个导致知识库导出失败的缺陷（见下方第二条），那个版本发布的安装包不可用。

- **新增：直接粘贴链接**。知识库链接（`…/wiki/space/7100…`）和文件夹分享链接（`…/folder/xxxx`）整条贴进来，工具自动提取 spaceId / folderToken；贴成单篇文档链接时会明确告诉你贴错了该贴什么。
- **新增增量导出 `--incremental`**：按飞书返回的编辑时间判断，只导新增和改动过的文档；状态记在导出目录的 `.feishu-export-state.json` 里，换机器、换挂载路径都能续跑，不会从头再来。
- **新增常驻定时 `--interval=6h` / `--at=03:00`**：单轮失败不退出进程；`Ctrl+C` 和 `SIGTERM` 都会先写完状态文件再退，每 20 篇做一次检查点。
- **新增 Docker 部署**：`Dockerfile` + `docker/entrypoint.sh` + `docker-compose.yml`，凭证与目标全部走环境变量，导出目录挂 volume，适合放在 NAS 或常开的机器上。
- **修复（严重）**：列知识库和知识空间节点时抛 `The JSON property name for 'WikiSpacePagedList.Items' collides with another property`，导致**知识库导出完全不可用**——派生属性 `Items` 与映射成 `items` 的 `ItemList` 在大小写不敏感匹配下判为同名冲突，已加 `[JsonIgnore]` 解决。
- **修复（严重）**：命令行版导出 markdown 时进程直接崩溃。Aspose.Words 21.6 会把托管 SkiaSharp 拖到 2.80.1，与原生库版本错配，异常从终结器线程抛出、绕过所有异常处理。现已把托管与原生统一钉到 **2.88.9**（顺带修掉 2.88.3 的 libwebp 高危漏洞告警）。
- **修复**：在 macOS 上交叉发布 Linux 包时会漏掉 `libSkiaSharp.so`——原生库的引用条件是按「构建主机」而不是按「目标 RID」判断的，导致 Linux 下导出 markdown 失败。
- **变更**：界面里的「跳过已存在的文件」勾选框改为**同步方式**三选（全部重导 / 跳过已存在 / 只导新增和改动的，默认后者）；新增「自动同步」卡片，可按固定间隔或每天定点自动跑增量。
- **文档**：使用引导第 3、4 节按实测结果重写——成员弹窗里要搜的是**群名称而不是应用名称**；飞书的知识空间列表接口只认空间级授权，**列表为空不代表没授权**，贴链接即可正常导出。
- **实测**：真实知识库 42 篇，docx 全量 105 秒、markdown 全量 143 秒（298 张图片落到 42 个 `.assets` 目录）；增量重跑 7 秒全部跳过；伪造改动可被准确识别并只重导那一篇。

### v1.0.0（图形界面版本）

- **新增界面内使用引导**：首次启动弹 5 节向导（建应用 → 开权限 → 授权知识库 → 选目标 → 选格式），每个配置项旁另有 `?` 只看当前这一项；权限名单一键复制、飞书后台直接打开。示意图是矢量线框，跟随主题、也不会因飞书后台改版而过期。
- **重做视觉体系**：设计 token + 浅色/深色双主题（跟随系统或手动切换）、分组卡片与步骤序号、主/次按钮层级、hover 与进度条过渡、日志与未导出清单的空状态提示。
- **新增 Avalonia 跨平台图形界面**：可视化配置、知识库下拉选择、实时进度条、运行日志、未导出清单、一键打开导出目录、配置持久化。
- **新增 macOS `.dmg` 安装包**：`scripts/build-dmg.sh` 一键产出带图标、带 Applications 快捷方式的安装包，Apple Silicon 与 Intel 双架构，内置命令行版与使用说明。
- **重构为三层结构**：核心库 / 命令行 / 图形界面共用同一套导出逻辑，命令行用法保持向后兼容。
- **修复**：Aspose 许可证路径硬编码 `/private/tmp/License.lic`（在 Windows / Linux 上必然失败）→ 改为可配置 + 自动查找 + 优雅降级。
- **修复**：`--apiEndpoint` 参数文档里有写但代码从未读取，国际版 Lark 实际不可用 → 现在真正生效。
- **修复**：下载普通文件失败后代码会继续往下走，用文件 token 去创建导出任务，产生二次报错并重复记入失败清单。
- **修复**：导出任务只识别错误码 1069902，其它异常一律吞掉并返回 null，导致文档静默丢失 → 现在全部抛出并计入清单。
- **修复**：markdown 后处理在内容不含 `|` 时抛 `ArgumentOutOfRangeException`。
- **修复**：文件名超长时用 `PadLeft` 生成「截断后」的名字，但对超长字符串 `PadLeft` 不会截断，等于没有截断 → 改为按 UTF-8 字节数安全截断。
- **修复**：同目录同名文档互相覆盖；所有文档的图片共用 `images` 目录导致图片互相覆盖 → 改为按文档独立 `.assets` 目录 + 同名自动加序号。
- **修复**：同一文档挂在多个知识库节点下时只保留最后一个路径，导致前面的引用指向不存在的文件。
- **修复**：导出任务轮询没有超时上限，飞书侧卡住时程序永久挂起 → 加入总超时与退避。
- **修复**：下载接口返回 HTTP 200 + JSON 错误体时，会把错误 JSON 当成文档写到磁盘。
- **修复**：选择知识库时 `int.Parse` 无校验、参数缺失时 `Environment.Exit(0)`（出错也返回成功退出码）。
- **改进**：新增 HTTP 限流/5xx 自动重试、`tenant_access_token` 校验与缓存、`--licensePath`/`--skipExisting`/`--help` 参数、规范的退出码、单元测试覆盖。

---

## 许可证

本项目基于 [Apache License 2.0](LICENSE) 开源。

注意：项目中引用的 **Aspose.Words 是商业收费库**，需要自行购买或申请临时许可证，其授权条款不受本仓库的开源协议覆盖。
