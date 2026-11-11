# 改名方案：Taix → 时见 / ShiJian

> 决策：2026-11-11，用户选择 **C · 全量重命名（含数据目录与安装标识）**，并且 **GitHub 仓库也要一并改为 `shijian`**；明确 **不再同步上游**。
> 状态：**已执行**，随 `fork.6` 发布。执行结果与方案偏离见文末「执行记录」。

## 执行记录（2026-11-11）

实际做过的事，以及和上面方案的差别：

**结果**

- 代码：15 个 `taix-*` 目录、`Taix.Client` / `Taix.Tests` / `Taix.PageState.Generator` 三个 C# 工程、`Taix.sln` 已 `git mv`；`Taix` → `ShiJian`、`taix` → `shijian`、`TAIX` → `SHIJIAN` 全量替换（629 个文本文件）。
- 验证：`dotnet build ShiJian.sln -c Release` 0 错误；`dotnet test` 66/66 通过（headless 主题加载测试同时验证了 `avares://ShiJian/` 没漏改）。Rust 本机无法编译（`C:\Windows\System32` 的 MSVC `link.exe` 被 Git Bash 的 `/usr/bin/link` 抢先），只能靠 CI 验证。
- 图标资源：`tai.ico` / `tai32.ico` / `taibusy.ico` 一并改名（4 处引用同步改，含 `shijian-shell/resources/icons`）。
- 文案：「时见」用在中文界面（窗口标题、关于页、安装器、托盘、错误弹窗），英文界面保持 `ShiJian`。

**偏离项（方案里没写或写得不一样）**

| # | 方案 | 实际 | 理由 |
| --- | --- | --- | --- |
| 1 | 「迁移」整目录 | 实现为**只补不覆盖的合并**（目标里已有的文件不动） | 客户端可能先于 server 建好新目录，直接 `rename` 会因目标存在而失败，反而丢数据 |
| 2 | 数据迁移放首次启动 | macOS 放 server 启动时（打开 DB 之前）；Windows 放**安装器**里（`shijian-installer/src/legacy.rs`） | Windows 的数据在安装目录里，客户端自己没有权限搬；且必须先停旧进程再搬 |
| 3 | 未提旧安装目录处置 | **不删除**：只摘掉旧自启动项（任务计划 / LaunchAgent / Run 键）并停进程，旧目录留给用户确认后手动清理 | 遵守「不经允许不删文件」；同时靠摘自启动避免新旧双份采集 |
| 4 | 应用内链指向自己仓库 | 更新检查 / 错误上报 / 设置页 / 安装器页脚 → `Purestblues/shijian`；README 与 docs 的 `NLick47/Taix` **保留** | 顺手修了一个隐患：原来的「检查更新」指向上游 releases，点下去会装回上游旧版 |
| 5 | 未提 Toast | Windows Toast AppUserModelId `Taix.Plugin` → `ShiJian.Plugin`；旧通知记录无法清理（一次性遗留） | 系统侧旧记录不在我们手里 |
| 6 | 扩展 ID 会变 | 已确认：服务端写接口用 Origin 白名单而非扩展 ID，所以要改的是**用户自己的白名单配置**，不是代码 | `origin_check.rs` 只比对配置里的允许来源 |

**发布后发现的一个回归（已修复并重传资产）**

`fork.6` 的 `Setup.exe` 装不了，界面直接报「安装程序无效」。

根因：SFX 的 payload 尾标记是 `TAIX_PAYLOAD_END`（16 字节），改名后变成 `SHIJIAN_PAYLOAD_END`（18 字节），
但读取端 `sfx.rs` 仍写死 `[0u8; 16]`。偏移用的是 `PAYLOAD_MARKER.len()`（算对了），
比较却只读了前 16 字节，于是永远不相等。

典型形态：**长度与名字绑定的魔法值，改名时静默失效，CI 也照常绿**。

修法（提交 `53a5c2c`）：

- 新增 `shijian-installer/src/payload.rs` 作为标记与尾部长度的**唯一出处**，
  打包器（`bin/pack-payload`）与安装器（`sfx`）共用，不再各自写一份字面量
- 标记固定为 16 字节 `SHIJIAN_PAYLOAD!`，读取端按 `PAYLOAD_MARKER.len()` 分配缓冲
- 用 `fork.6` 的 Setup 实测确认：文件尾确实是 `SHIJIAN_PAYLOAD_END` + 8 字节长度
- 重新构建后用 `gh release upload --clobber` 替换 `fork.6` 的 `Setup.exe`（当时三个资产下载数均为 0）；
  zip 与 dmg 不经 SFX，未动

**本机仍需人工确认的两项**

1. Windows 实机装一次 Setup，确认旧 `Data/` 被合进新目录、历史统计没丢、旧任务计划消失。
2. macOS 产机（暂时没有）确认 `Application Support/Taix` 被补进 `ShiJian`。


## 命名规范

| 用途 | 取值 |
| --- | --- |
| 产品显示名（窗口标题、关于页、安装器、商店） | **时见**（英文界面显示 ShiJian） |
| 代码标识（命名空间、程序集、目录、crate、二进制） | `ShiJian` |
| 可执行文件 | `ShiJian.exe` / `shijian-server` / `shijian-monitor-windows` / `shijian-shell` |
| 进程间/系统标识 | Task Scheduler `ShiJianShell`、LaunchAgent `com.shijian.shell`、Toast AppUserModelId 同步改 |

注：拼音 `shijian` 与"时间/事件/实践"同音，命令行里搜到别的东西属正常现象，不影响内部标识。

## 影响面（实测）

- **571 个文件**含 `Taix`；按目录：`Taix.Client` 399、`Taix.Tests` 52、`taix-server` 27、`Taix.PageState.Generator` 23、`taix-installer` 12、`taix-browser-extension` 8、脚本 7、`taix-shell` 6、其余零散。
- 关键标识点：
  - `Taix.Client.csproj` 的 `<AssemblyName>Taix</AssemblyName>`、`<InternalsVisibleTo Include="Taix.Tests" />`、`$(TaixWindowKitDir)` 等 MSBuild 变量；
  - 资源 URI `avares://Taix/...`（主题、样式、字体全引用它）；
  - `Taix.PageState.Generator` 生成的命名空间；
  - Rust crate 名即二进制名，CI/installer/`script/*.ps1` 全按文件名硬编码；
  - 数据目录：Windows = `<exe 同级>/Data`（随安装目录走），macOS = `~/Library/Application Support/Taix` + 配置缓存 `Application Support/Taix/app.config.cache.json`；
  - 端口 `37091`（HTTP）/ `8908`（WS）：插件 SDK、浏览器扩展、webui 代理都写死。
- 合规：上游为开源项目，**`LICENSE` 与上游署名必须保留**，README 里说明这是 fork（改名不改变许可证义务）。

## 执行顺序（三步，每步都能独立构建/回滚）

### P1 · 代码层（不含身份，改完能编译能跑测试）

1. 打基线 tag，开独立分支 `rename/shijian`。
2. 目录改名用 `git mv`（保留历史）：`taix-server` → `shijian-server` 等 7 个目录，`Taix.sln` → `ShiJian.sln`。
3. 命名空间与代码标识：`Taix` → `ShiJian`（含 `using`、`x:Class`、`avares://Taix/` → `avares://ShiJian/`、`InternalsVisibleTo`、MSBuild 变量）。**必须排除** `bin/` `obj/` `node_modules/` `.git/` `.tmp/`，改完人工过一遍 diff。
4. Rust：crate 名、`[[bin]]` 名、CI 里 `Copy-Item .../*.exe` 的源/目标名、installer 的文件清单、`script/install.ps1` / `uninstall.ps1` / `dev-run.ps1`。
5. 同步改：`.github/workflows/release.yml`、`taix-browser-extension` 的清单与 WS 路径、插件 SDK 的 `TAIX_SERVER` 环境变量（→ `SHIJIAN_SERVER`）。
6. 验证：`dotnet build`、`dotnet test`、`cargo check`（本机 `link.exe` 被抢，最终以 CI 为准）。

### P2 · 身份层（影响安装与数据，必须带迁移）

1. 窗口标题 / 关于页 / 托盘提示 → 时见。
2. 系统标识：Task Scheduler 任务名、LaunchAgent plist（`com.taix.shell` → `com.shijian.shell`）、Toast AppUserModelId、注册表卸载项。
3. **数据迁移（不能跳）**：
   - macOS：首次启动检测 `Application Support/Taix`，存在则迁移到 `ShiJian`（复制 → 校验 → 保留旧目录一个版本周期）；`app.config.cache.json` 同理。
   - Windows：数据在安装目录下的 `Data/`，需 installer 检测旧安装目录，把 `Data/` 搬过去，再走卸载旧版。
4. 端口暂不改（`37091` / `8908`）：改端口要连带改插件 SDK 默认值、浏览器扩展、webui 代理，收益不抵风险。
5. 浏览器扩展：unpacked 扩展的 ID 取决于路径，改名后 ID 会变 → 检查服务端是否按 origin 校验，需要放宽或重新登记。

### P3 · 发布与文档

1. **GitHub 仓库改名 `Taix` → `shijian`**（用户已定）。连带要改：
   - `git remote set-url origin`（本地仓库目录名无所谓，remote 必须对）；
   - README 里的自仓库链接与 badge（现指向 `Purestblues/Taix`，注意上游溯源链接 `NLick47/Taix`、`Planshit/Tai` **不能改**，那是别人的仓库）；
   - `script/release.ps1` 用 `gh workflow run --ref <tag>`，仓库取自 remote，改名后仍可用；
   - 旧 release 页面链接、外部引用：GitHub 会对旧仓库名做 301 重定向，但 badge 与直链图片可能失效，发布后用 `Invoke-WebRequest` 逐个验一遍。
2. `release_notes.txt`、`README.md`、`docs/*` 更新；**历史 release notes 与旧 tag 不改**（保留史实）。
3. 发版一次 `fork.N` 之后，版本号策略可顺带改成自己的编号。

## 风险清单

| 风险 | 处理 |
| --- | --- |
| 全量替换误伤历史文档、外部 URL、第三方包名 | 只替换代码/配置/脚本，`docs/` 与 `release_notes` 手动确认 |
| `avares://Taix/` 漏改 → 运行时资源全空（不报编译错） | 全量搜 `avares:` 并核对；headless 测试会因主题加载失败直接挂 |
| macOS 数据目录改名导致历史统计"消失" | P2 的迁移逻辑必须先于发布 |
| 旧版残留进程（`taix-shell`）与新装并存，双份采集 | installer 检测旧进程/旧路径，先停再迁 |
| 未换签名，macOS Gatekeeper / Windows SmartScreen 继续报警 | 与改名无关，本次不解决，doc 里记一句 |
