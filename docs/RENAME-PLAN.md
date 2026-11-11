# 改名方案：Taix → 时见 / ShiJian

> 决策：2026-11-11，用户选择 **C · 全量重命名（含数据目录与安装标识）**，并且 **GitHub 仓库也要一并改为 `shijian`**；明确 **不再同步上游**。
> 状态：**方案已定，排入 `fork.6`，未执行**。

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
