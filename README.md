# 项目介绍

首先感谢原作者的开源，原项目 Tai 是用于在 Windows 上统计**软件**使用时长和**网站**浏览时长。

Taix 是 [Tai](https://github.com/Planshit/Tai) 的一次技术及架构的全面迁移：

- 当前客户端已完成 .NET 10 AOT 和 Avalonia 的迁移
- 已实现客户端与服务端的分离
- 服务端采用 Rust 进行全面重写，采用更高效准确的计时方案，资源占用可以忽略不计

### 协同组件

当前项目需要以下组件进行协同：

| 组件 | 说明 |
|------|------|
| taix-shell | 创建客户端托盘，方便唤醒客户端并起到看门狗作用，协同 server 与 monitor 应用，做到客户端随用随关 |
| taix-server | 提供数据访问接口，接收浏览器插件和平台计时统计数据并写入 SQLite |
| taix-monitor | 监控应用睡眠及焦点切换，使用命名管道对 server 进行通讯 |

后台进程统一由 taix-shell 看门狗管理，**不要手动单独启动** taix-server 与 taix-monitor：taix-shell 会读取睡眠监控配置并通过 `--sleep-watch` 参数传给 taix-monitor，手动启动的进程既不受看门狗守护，也缺失该参数。

### 常见问题

#### 客户端提示「无法连接到服务端」

本质是后台进程没在运行。正常情况下 taix-server 与 taix-monitor 由 taix-shell 看门狗自动拉起并守护，**只需确保 `taix-shell` 在运行即可**（为何不要手动启动见「协同组件」）。若 server 自身异常（如端口被占用、数据库损坏）导致启动即退出，看门狗虽会自动重试（间隔 5 秒起、退避至最长 30 秒），但服务始终不可用，此时应从 server 日志定位根因。

- **开机自启失效**：Windows 检查任务计划中 **TaixShell** 任务的路径是否与安装目录一致；Setup 安装器与绿色版均可用管理员终端 `taix-shell install` 补注册。macOS 则运行 `/Applications/TaixTools/install-launchagent.sh`（DMG 中 `TaixTools/` 即后台组件与安装脚本集合）
- **端口占用**：server 默认占 `127.0.0.1:37091`、浏览器扩展 WebSocket 占 `8908`。若 37091 被占用，可设置环境变量 `TAIX_SERVER` 换端口（如 `http://127.0.0.1:37100`，客户端与 server 均读取该变量）。server 日志按 `taix-server.YYYY-MM-DD.log` 按天滚动：macOS 在 `~/Library/Logs/Taix/`；Windows 绿色版在程序目录的 `Logs\`（该目录不可写时不落盘，只输出到标准流，不会写到别处）。插件与 shell 日志也在同一目录，可用环境变量 `TAIX_LOG_DIR` 覆盖

### 运行表现

3 个应用协作长期运行下的内存情况：

![内存占用](a1.png)

## Fork 维护说明

本仓库是 [NLick47/Taix](https://github.com/NLick47/Taix) 的个人 fork，改动集中在客户端（Avalonia），后续发版产物见 [Releases](https://github.com/Purestblues/Taix/releases)。

后续计划与已知问题清单见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。

相对上游，当前已有的改动：

- 概览页重做：总览卡片（总使用时长 / 应用量 / 站点 / 网页，带比昨日、比上周）、使用时段堆叠柱图、分类占比环形图，图表区可在应用 / 网站之间切换
- 概览页周期支持「自定义」起止日期，单日按小时、多日按天，对比基准取等长的上一个区间
- 分类维度贯穿概览页：榜单 / 卡片显示分类徽章，点徽章进该分类在这段时间的汇总
- 时段对比：24 小时柱图叠加「昨日同小时」淡色柱，可开关
- 详细页改用环形时钟：圆周即周期（日 24h / 周 7 天 / 月当月天数 / 年 12 个月），扇区为真实使用时段，点击进详情
- 图表配色统一：系统分类「未分类」默认色接近背景色，现改为跟随主题色；配色改按 WCAG 相对亮度算对比度（目标 3:1），深色主题下不再偏暗。时间轴、分类页也走同一套配色
- 窗口修复：双击标题栏最大化/还原、窗口最外缘可拖拽缩放、不再把满屏尺寸当成正常尺寸保存
- 图标修复：客户端改在 `<exe>\Data\AppIcons` 查找（此前只在 `<exe>\AppIcons` 找，导致全部回退默认图标）
- 图标健壮性：0 字节 / 解码失败的网站图标降级为域名首字母色块；修复图标缓存淘汰释放仍在渲染的位图导致的闪退
- 首屏请求从 16 个降到 10 个，`AppChanged` 事件去抖 100ms → 500ms

### 维护脚本

两个脚本都在 `script/` 下，需要 Windows PowerShell 5.1 及以上。

| 脚本 | 用途 |
| --- | --- |
| `script/dev-run.ps1` | 本地运行改动后的客户端（自动补齐图标目录后 `dotnet run -c Debug`） |
| `script/release.ps1` | 一条命令打 tag 并触发 GitHub Actions 发 Release |

#### 本地调试

```powershell
.\script\dev-run.ps1              # 编译并启动
.\script\dev-run.ps1 -NoBuild     # 跳过编译直接启动
```

- 开发运行时的数据目录是 `Taix.Client\bin\Debug\net10.0\`，图标目录为空时脚本会从安装目录（默认 `D:/Program Files/taix`）复制 `Data/AppIcons` 与 `WebFavicons`；安装位置不同时用 `-InstallDir` 指定
- 调试版直接连本机已在运行的 taix-server（`127.0.0.1:37091`），不需要另起后端
- DEBUG 构建没有单实例锁，可与安装版同时运行；Release 构建有互斥锁，不能与安装版共存

#### 发布新版本

```powershell
.\script\release.ps1 -Version 1.3.4-fork.2 -DryRun          # 只预检，打印将执行的命令
.\script\release.ps1 -Version 1.3.4-fork.2                  # 正常发版
.\script\release.ps1 -Version 1.3.4-fork.2 -Commit -Watch   # 代提交改动并等待构建完成
```

预检会拦下这些情况：不在 `main` 分支、工作区不干净（除非加 `-Commit`）、tag 已存在、Release 已存在。

> ⚠️ 在 fork 上 `git push` 一个 tag **不会**触发 Actions，脚本内部改用
> `gh workflow run release.yml --ref <tag>`；`--ref` 必须指向 tag，`github.ref` 才是 `refs/tags/...`，
> workflow 里的 release job 才会执行并上传产物。

> 本地打不出 `Setup.exe`：安装器（cargo）与客户端 AOT 发布都需要 MSVC 链接器（VS Build Tools），
> 因此发版统一走 GitHub Actions 的 windows-latest / macos-latest。

## 对Tai数据兼容

- 在 1.2.0 前支持 Tai 最新版本的 db 文件，1.2.0 及后的新版本暂不确定。不过当前程序有对旧版数据有较为完整的数据迁移步骤，理论上是支持的
- 值得说明的是，如果进行迁移需要做好 db 文件备份

## 部分界面

![界面展示1](a2.png)

![界面展示2](a3.png)