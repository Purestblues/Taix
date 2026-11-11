# 时见 ShiJian

> 本项目原名 **Taix**，是 [NLick47/Taix](https://github.com/NLick47/Taix) 的个人 fork。
> 2026-11 起改名为**时见**（代码标识 `ShiJian`），与上游不再同步。旧版本的数据目录会在首次启动时自动迁移，详见[改名说明](#改名说明)。

## 项目介绍

首先感谢原作者的开源，原项目 Tai 是用于在 Windows 上统计**软件**使用时长和**网站**浏览时长。

时见（ShiJian）是 [Tai](https://github.com/Planshit/Tai) 的一次技术及架构的全面迁移：

- 当前客户端已完成 .NET 10 AOT 和 Avalonia 的迁移
- 已实现客户端与服务端的分离
- 服务端采用 Rust 进行全面重写，采用更高效准确的计时方案，资源占用可以忽略不计

### 协同组件

当前项目需要以下组件进行协同：

| 组件 | 说明 |
|------|------|
| shijian-shell | 创建客户端托盘，方便唤醒客户端并起到看门狗作用，协同 server 与 monitor 应用，做到客户端随用随关 |
| shijian-server | 提供数据访问接口，接收浏览器插件和平台计时统计数据并写入 SQLite |
| shijian-monitor | 监控应用睡眠及焦点切换，使用命名管道对 server 进行通讯 |

后台进程统一由 shijian-shell 看门狗管理，**不要手动单独启动** shijian-server 与 shijian-monitor：shijian-shell 会读取睡眠监控配置并通过 `--sleep-watch` 参数传给 shijian-monitor，手动启动的进程既不受看门狗守护，也缺失该参数。

### 常见问题

#### 客户端提示「无法连接到服务端」

本质是后台进程没在运行。正常情况下 shijian-server 与 shijian-monitor 由 shijian-shell 看门狗自动拉起并守护，**只需确保 `shijian-shell` 在运行即可**（为何不要手动启动见「协同组件」）。若 server 自身异常（如端口被占用、数据库损坏）导致启动即退出，看门狗虽会自动重试（间隔 5 秒起、退避至最长 30 秒），但服务始终不可用，此时应从 server 日志定位根因。

- **开机自启失效**：Windows 检查任务计划中 **ShiJianShell** 任务的路径是否与安装目录一致；Setup 安装器与绿色版均可用管理员终端 `shijian-shell install` 补注册。macOS 则运行 `/Applications/ShiJianTools/install-launchagent.sh`（DMG 中 `ShiJianTools/` 即后台组件与安装脚本集合）
- **端口占用**：server 默认占 `127.0.0.1:37091`、浏览器扩展 WebSocket 占 `8908`。若 37091 被占用，可设置环境变量 `SHIJIAN_SERVER` 换端口（如 `http://127.0.0.1:37100`，客户端与 server 均读取该变量）。server 日志按 `shijian-server.YYYY-MM-DD.log` 按天滚动：macOS 在 `~/Library/Logs/ShiJian/`；Windows 绿色版在程序目录的 `Logs\`（该目录不可写时不落盘，只输出到标准流，不会写到别处）。插件与 shell 日志也在同一目录，可用环境变量 `SHIJIAN_LOG_DIR` 覆盖

### 运行表现

3 个应用协作长期运行下的内存情况：

![内存占用](a1.png)

## Fork 维护说明

本仓库是 [NLick47/Taix](https://github.com/NLick47/Taix) 的个人 fork，改动集中在客户端（Avalonia），后续发版产物见 [Releases](https://github.com/Purestblues/shijian/releases)。

2026-11 起项目改名为**时见 / ShiJian**，同时**停止同步上游**：上游 v1.3.4 之后不再 rebase，`LICENSE` 与上游署名照旧保留。

后续计划与已知问题清单见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。

相对上游，当前已有的改动：

- 概览页重做：总览卡片（总使用时长 / 应用量 / 站点 / 网页，带比昨日、比上周）、使用时段堆叠柱图、分类占比环形图，图表区可在应用 / 网站之间切换
- 概览页周期支持「自定义」起止日期，单日按小时、多日按天，对比基准取等长的上一个区间
- 分类维度贯穿概览页：榜单 / 卡片显示分类徽章，点徽章进该分类在这段时间的汇总
- 时段对比：24 小时柱图叠加「昨日同小时」淡色柱，可开关
- 详细页改用环形时钟：圆周即周期（日 24h / 周 7 天 / 月当月天数 / 年 12 个月），扇区为真实使用时段，点击进详情
- 详细页加数据看板：顶部四张指标卡（总时长 / 应用·站点数 / 会话次数·网页数 / 最长单次，前两项带环比），下方时段分布柱图叠「上一周期」淡色柱；右侧洞察栏给使用习惯（首次·末次、峰值时段、活跃天/小时/月、时段偏好）、使用热力（日＝应用×小时、周＝星期×小时、月＝日历热力、年＝月×日）与分类占比；窗口变窄或变矮时右侧栏和柱图自动收起
- 图表配色统一：系统分类「未分类」默认色接近背景色，现改为跟随主题色；配色改按 WCAG 相对亮度算对比度（目标 3:1），深色主题下不再偏暗。时间轴、分类页也走同一套配色
- 窗口修复：双击标题栏最大化/还原、窗口最外缘可拖拽缩放、不再把满屏尺寸当成正常尺寸保存
- 图标修复：客户端改在 `<exe>\Data\AppIcons` 查找（此前只在 `<exe>\AppIcons` 找，导致全部回退默认图标）
- 图标健壮性：0 字节 / 解码失败的网站图标降级为域名首字母色块；修复图标缓存淘汰释放仍在渲染的位图导致的闪退
- 首屏请求从 16 个降到 10 个，`AppChanged` 事件去抖 100ms → 500ms
- 界面动效开关：设置 → 常规 → 界面动效（跟随系统 / 始终开启 / 始终关闭）。跟随系统时读系统的「减少动画」偏好，关掉后页面进场、图表生长、悬停过渡全部不播
- 键盘导航：Esc 返回上一页；侧边栏 Tab 可进、上下键换项、Enter 激活；周期/维度切换左右键；榜单方向键选中 + Enter 进详情；图表获得焦点后左右键切换选项并显示焦点环
- 空态与加载态统一：`EmptyData` 支持文案/说明/骨架/紧凑模式，颜色跟随主题；加载中显示骨架而不是先闪「暂无数据」

### 维护脚本

两个脚本都在 `script/` 下，需要 Windows PowerShell 5.1 及以上。

| 脚本 | 用途 |
| --- | --- |
| `script/dev-run.ps1` | 本地运行改动后的客户端（自动补齐图标目录后 `dotnet run -c Debug`） |
| `script/prepare-release.ps1` | 发版准备：按上一个 tag 自动递增版本号 + 用区间提交生成 release notes 草稿 |
| `script/release.ps1` | 一条命令打 tag 并触发 GitHub Actions 发 Release（版本号可省略，会自动递增） |

#### 本地调试

```powershell
.\script\dev-run.ps1              # 编译并启动
.\script\dev-run.ps1 -NoBuild     # 跳过编译直接启动
```

- 开发运行时的数据目录是 `ShiJian.Client\bin\Debug\net10.0\`，图标目录为空时脚本会从安装目录（默认 `C:/Program Files/ShiJian`）复制 `Data/AppIcons` 与 `WebFavicons`；安装位置不同时用 `-InstallDir` 指定
- 调试版直接连本机已在运行的 shijian-server（`127.0.0.1:37091`），不需要另起后端
- DEBUG 构建没有单实例锁，可与安装版同时运行；Release 构建有互斥锁，不能与安装版共存

#### 发布新版本

```powershell
.\script\prepare-release.ps1                 # 算出版本号 + 生成 notes 草稿到 .tmp/
.\script\prepare-release.ps1 -Apply          # 确认后的草稿写进 release_notes.txt
.\script\release.ps1 -DryRun                 # 只预检，打印将执行的命令
.\script\release.ps1 -Commit -Watch          # 发版并等待构建完成（版本号自动递增）
.\script\release.ps1 -Version 1.3.4-fork.5    # 也可以手工指定版本号
```

#### 跑测试

```powershell
dotnet test ShiJian.Tests/ShiJian.Tests.csproj -c Release
```

CI 里打包前会先跑这一步（独立 `test` job），失败就不发产物。

预检会拦下这些情况：不在 `main` 分支、工作区不干净（除非加 `-Commit`）、tag 已存在、Release 已存在。

> ⚠️ 在 fork 上 `git push` 一个 tag **不会**触发 Actions，脚本内部改用
> `gh workflow run release.yml --ref <tag>`；`--ref` 必须指向 tag，`github.ref` 才是 `refs/tags/...`，
> workflow 里的 release job 才会执行并上传产物。

> 本地打不出 `Setup.exe`：安装器（cargo）与客户端 AOT 发布都需要 MSVC 链接器（VS Build Tools），
> 因此发版统一走 GitHub Actions 的 windows-latest / macos-latest。

## 改名说明

2026-11 起本项目由 **Taix** 改名为**时见 / ShiJian**：产品显示名为「时见」，英文界面显示 ShiJian，代码标识统一为 `ShiJian` / `shijian-*`。只碰标识，不动业务逻辑。

| 项目 | 旧 | 新 |
| --- | --- | --- |
| 程序 / 目录 / crate | `Taix`、`taix-*` | `ShiJian`、`shijian-*` |
| 安装目录（Windows） | `…\Taix` | `…\ShiJian`（Setup 仍可自选） |
| 数据目录（macOS） | `~/Library/Application Support/Taix` | `~/Library/Application Support/ShiJian` |
| 计划任务 / LaunchAgent | `TaixShell` / `com.taix.shell` | `ShiJianShell` / `com.shijian.shell` |
| 环境变量 | `TAIX_SERVER` / `TAIX_LOG_DIR` | `SHIJIAN_SERVER` / `SHIJIAN_LOG_DIR` |
| 浏览器扩展 | Taix Web Sentry | ShiJian Web Sentry（需重新「加载已解压的扩展」，扩展 ID 会变） |
| 端口 | 37091（HTTP）/ 8908（WS） | 不变 |

**数据不会丢**：

- macOS：server 启动时把 `Application Support/Taix` 里缺失的条目补进 `ShiJian`（只补不覆盖），旧目录原地保留。
- Windows：Setup 安装前会停掉旧进程、摘掉旧自启动项，并把旧安装目录的 `Data/` 合并进新目录；旧的安装目录本身不删，确认新版本正常后可手动清理。
- 旧的任务计划 / 注册表自启动项会一并清掉，不会出现新旧两份同时采集。

## 对Tai数据兼容

- 在 1.2.0 前支持 Tai 最新版本的 db 文件，1.2.0 及后的新版本暂不确定。不过当前程序有对旧版数据有较为完整的数据迁移步骤，理论上是支持的
- 值得说明的是，如果进行迁移需要做好 db 文件备份

## 部分界面

![界面展示1](a2.png)

![界面展示2](a3.png)