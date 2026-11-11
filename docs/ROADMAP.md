# 路线图

> 本仓库是 [NLick47/Taix](https://github.com/NLick47/Taix) 的个人 fork。
> 改动集中在客户端（`Taix.Client`）；服务端只有一处配合改动：`General` 配置里新增 `MotionMode`
> 字段（配置版本 v4 → v5），否则动效开关会被服务端配置回写抹掉。监控 / 托盘 / 插件未动。

## 现状

| 维度 | 状态 |
| --- | --- |
| 发布 | `v1.3.4-fork.3`（Setup.exe / 绿色版 zip / macOS dmg，均由 GitHub Actions 产出） |
| 验证程度 | Windows 客户端逐项实测；macOS 产物只验证过「能构建」，未实机运行 |
| 自动化测试 | 无（CI 只打包，不跑测试） |
| 上游同步 | 与 `upstream/main`（v1.3.4）无偏差，随时可 rebase |
| 代码规模 | 客户端 296 个 `.cs`、67 个 `.axaml`，0 处 TODO/FIXME |

## 已知问题

编号用于提交信息引用，例如 `修复 G1 深色主题下图表对比度不足`。
G1 / G3 / G6 已在 M1 修复，G4 核验为「已经具备」；G8 / G9 已在 M3 修复；G10 / G11 是 M3 收尾时发现的，G10 已规避、G11 留待继续查；下表保留原始描述用于追溯。

| # | 问题 | 根因 | 影响 |
| --- | --- | --- | --- |
| G1 | 深色主题下图表偏暗 | `Librarys/ChartColorHelper.cs` 的压暗阈值按浅色背景校准（相对亮度 > 0.82 才压暗） | 深色主题下分类色与背景对比不足 |
| G2 | 概览页没有分类维度 | 榜单与卡片只消费 `DailyLogModel`，未带 `Category` | 有分类的用户看不出「哪类花的时间多」 |
| G3 | 部分网站图标裂图 | `WebFavicons` 目录里存在 0 字节 / 损坏的 ico，Skia 解码抛 `NullReferenceException` | 回退默认图标（功能正常，观感差、日志噪音） |
| G4 | 统计页柱子点击无响应 | `Charts/ColumnChart` 只实现了悬停，没有点击选中与下钻 | 想看「某小时用了哪些应用」必须换页 |
| G5 | 时间轴 / 分类页配色不统一 | 这两处仍直接用 `category.Color`，未走 `ChartColorHelper` | 同一个分类在不同页显示成不同颜色 |
| G6 | 首页请求偏多、事件全量重载 | 首屏 14 个并发请求；每次 `AppChanged` 都重拉全部数据 | 频繁切换应用时产生无效请求与重绘 |
| G7 | 动效无法关闭 | 未检测系统「减少动画」设置，也没有手动开关 | 对晕动症用户不友好 |
| G8 | 详细页时间轴不好用 | 上方是可缩放平移的条状时间轴，下方是每应用一条泳道，两处信息重复且要手动对齐 | 想看清「今天几点在干什么」需要来回操作 |
| G9 | 点网站分类必定闪退 | `Imager` 图标缓存淘汰时释放了仍在渲染的位图，`Image.Render` 抛 `ObjectDisposedException` | 整个客户端进程直接退出 |
| G10 | 调试版启动弹「预料之外的错误」，主界面空白 | `AvaloniaUI.DiagnosticsSupport` 2.2.3 的样式里用了嵌套选择器，与 Avalonia 12.1.1 不兼容（`Nesting selector ... cannot determine parent selector`） | 只影响 Debug 构建；现已默认不挂 DevTools，需要时设 `TAIX_DEVTOOLS=1`。Release 构建不受影响 |
| G11 | 自绘图表用 Tab 走不到 | Avalonia 12 的 Tab 遍历没有把这类自定义 `Control` 当成候选（`Focusable` / `IsTabStop` 都已置真） | 鼠标点一下图表即可获得焦点，之后方向键、Enter 都正常；纯键盘用户暂时进不去 |

## 里程碑

### M1 · 正确性收口（已完成，已随 `fork.2` 发布）

- [x] **G1** 配色改为按主题背景的对比度判定（用相对亮度差，而不是固定阈值）
      `ChartColorHelper` 改用 WCAG 相对亮度算对比度（目标 3:1）：浅色背景把过浅的分类色压暗、
      深色背景把过暗的提亮，只调到刚好够看。主题切换时 `ThemeServicer` 发 `ThemeChanged`，
      概览页用缓存数据就地重算，统计页重新拉取当前页面数据。
- [x] **G3** favicon 解码前跳过 0 字节文件；失败时降级为域名首字母色块
      `Imager` 对 0 字节文件直接当作「没有图标」，解码失败也不再刷 Error 日志；
      `Img` 控件新增 `FallbackText`，加载失败时显示按域名稳定着色的首字母色块
      （榜单、卡片、柱状图图例、时间轴、网站详情页均已接上）。
- [x] **G4** 核验结论：统计页柱图已经支持点击选中 + 该时段明细（`ColumnChart.OnPointerPressed`
      + `IsCanColumnSelect`，默认开启），无需改动。
- [x] **G6** `AppChanged` 加 500ms 去抖；首屏可合并的请求合并
      事件去抖统一放在 `ModelBase.SubscribeRefresh`（100ms → 500ms）；
      榜单接口本身就带回每个应用 / 网站的时长，应用数、网站数、总时长改成本地汇总，
      概览页首屏请求从 16 个降到 10 个。

### M2 · 数据表达深化（已完成，已随 `fork.2` 发布）

- [x] **G2** 分类维度贯穿首页：榜单 / 更多卡片显示分类，可点击筛选
      `ChartDataMapper` 产出 `CategoryName / CategoryColor` 与分类徽章（同色淡底 + 同色文字，
      不再用饱和度很高的实心色块），榜单开启 `IsShowBadge`，卡片右上角显示「圆点 + 分类名」；
      点徽章直接进「分类详情」页（该分类在这段时间的汇总）。徽章点击走隧道事件，
      不会被「点整行进详情」吃掉。
- [x] 网站侧分类图表——核验结论：早已实现。概览页的应用/网站切换、统计页的网站分类环图与
      堆叠柱图、分类页的网站分类都在用网站分类体系，无需改动。
- [x] 时段对比：24 小时柱图叠加「昨日同小时」淡色柱
      `StackedBarChart` 新增 `CompareValues / CompareBrush`，在当前柱后面画一根略宽的淡色柱；
      概览页多拉一份「上一周期」的分类×时段矩阵并在本地按列汇总，右上角开关默认开启；
      网站维度没有同粒度数据，开关自动隐藏。
- [x] **G5** 时间轴 / 分类页接入统一配色
      详细页时间轴、分类页圆点、分类详情页的分类 chip 与图表都改走 `ChartColorHelper`；
      主题切换时各页收到 `ThemeChanged` 重新解析，不用重启。
      （顺手修了一个隐患：`ChartColorHelper` 现在把背景色 / 主题色缓存成静态值，
      否则后台线程组装图表数据时访问 `Application` 资源会直接抛异常。）
- [x] 概览页周期从「今日 / 本周」扩展到自定义范围
      周期切换新增「自定义」，展开起止日期选择器；单日按小时、多日按天出柱；
      对比基准取等长的上一个区间，文案为「比上期」。

### M3 · 体验与无障碍（已完成，已随 `fork.3` 发布）

- [x] **G8** 详细页改用环形时钟，去掉多轨泳道
      `Controls/Timeline/ClockRing` 自绘表盘：圆周就是周期的时间轴（日 = 24 小时、
      周 = 7 天、月 = 当月天数、年 = 12 个月），扇区是真实使用时段。
      数据源按粒度分开：日 = 会话（`AppSessions`），周 / 月 = 小时桶（`api/data/hours-range`），
      年 = 逐月各查一次 `api/data/range`（服务端的范围查询只按应用汇总，不给到天；
      整年的小时桶是几 MB 的响应，不划算）。
      日页下方原本的每应用泳道删除，改成表盘 + 右侧「前 5 名 + 其他」图例（可点进应用详情 / 分类汇总）；
      周 / 月 / 年 保留原榜单，表盘叠在上方。悬停显示该段的时间与名称，点击扇区进应用详情。
- [x] **G9** 分类页「点网站分类必定闪退」
      根因不在分类本身：`Imager` 的图标 LRU 淘汰时 `Dispose()` 了位图，而 `Img` 控件
      还拿着同一张 `Bitmap` 渲染，下一次绘制在 `Avalonia.Controls.Image.Render` 里抛
      `ObjectDisposedException (Ref<IBitmapImpl>)`，异常冒到渲染循环直接带走进程。
      修法两层：缓存淘汰 / `Clear()` 只丢引用不再释放（交给 GC）；新增 `Controls/Base/SafeImage`
      自己画图，绘制前验活位图、失败回退默认图标，把异常挡在控件内部。
- [x] **G7** 动效统一开关（跟随系统设置 + 设置页可手动关闭）
      `GeneralModel.MotionMode`（跟随系统 / 始终开启 / 始终关闭，默认跟随系统）驱动
      `MotionServicer`：Windows 读 `SPI_GETCLIENTAREAANIMATION`，macOS 读
      `NSWorkspace.accessibilityDisplayShouldReduceMotion`，读不到当开启；窗口每次激活重读一次系统偏好。
      执行端两条线：代码动画（页面进场、总览卡错峰、图表生长、数字滚动）统一读 `Motion.IsEnabled`；
      XAML 里的 `Transitions`（含内联写死在元素上的）由 `MotionTransitions` 在运行时摘掉、重开时还原，
      并持续扫描新实例化的列表行（带退避，不会空转）。
- [x] 键盘导航
      Esc：搜索层优先，其余情况返回上一页（焦点在输入框时先退到页面，避免误退页）。
      侧边栏：Tab 可进、上下键换项、Enter 激活；`TabSwitch`（周期 / 维度切换）左右键改选项；
      榜单列表方向键选中 + Enter 进详情（`KeyboardListActivation` 附加行为，分类页与详情列表都接上了）。
      图表：`ColumnChart` / `DonutChart` / `StackedBarChart` / `ClockRing` 均可聚焦，
      左右键移动选中项、`ColumnChart` 的 Enter 即下钻，自绘控件画统一的焦点环（`FocusVisual`）。
      其余交互控件补了 `:focus-visible` 焦点样式（`Themes/Base/FocusStyles.axaml`）。
- [x] 空态与加载态统一
      `EmptyData` 重写：支持 `Text` / `Description` / `IsLoading` / `IsCompact`，颜色全走主题资源
      （原本写死的浅灰在深色主题下几乎看不见）；加载中显示骨架条，不再先闪一下「暂无数据」。
      各页（概览 / 统计 / 详细 / 分类 / 分类汇总 / 列表页）统一接入，`ListChart` 也支持
      `IsLoading` / `EmptyText` / `EmptyCompact`，加载时由页面 VM 的 `IsLoading` 驱动。

### M4 · 工程化（约 2–3 天）

- [ ] 增加测试工程（xunit），先覆盖纯逻辑：`ChartColorHelper`、分类聚合、`WindowStateTracker` 状态机
- [ ] `release.yml` 打包前加一步 `dotnet test`，失败不发布
- [ ] 发版半自动：版本号从上一个 tag 递增、release notes 由 commit 生成草稿后人工确认
- [ ] macOS 产物实机验证：确认 `Taix.app` 与 `TaixTools/` 均可正常启动

## 优先级建议

**先 M1，再 M2；M4 的最小版本（只覆盖配色与聚合逻辑）建议在 M1 结束后立刻插入。**

理由：

- M1 全是「看起来像 bug」的问题，改动面小、体验收益直接
- M2 依赖 M1 的配色基础，且改动会扩散到多个页面，先有测试兜底更稳
- M4 越晚做，M2 的改动越难补测

## 明确不做

| 想法 | 原因 |
| --- | --- |
| 重写 Rust 服务端 | 现有三进程内存占用已很低，重写风险远大于收益 |
| 自研图表库 | 现有自绘控件够用，引入复杂度没有必要 |
| 插件市场 / 生态建设 | 个人使用的 fork，投入产出不成立 |
| 追上游所有分支 | 上游 11 个分支多为已合并历史，只需对齐 `upstream/main` |

## 发版节奏

- 里程碑合并发布：`fork.2` = M1 + M2 + M3 前两项，`fork.3` = M3 收尾（动效开关、键盘导航、空态），`fork.4` 起按 M4 进度发
- 每条命令即可发布：`.\script\release.ps1 -Version 1.3.4-fork.N -Commit -Watch`
- 同步上游：`git fetch upstream && git rebase upstream/main`
  （冲突大概率集中在 `Views/IndexPage.*` 与 `Views/ChartPage.*`，这两处是本仓库改动最重的地方）

## 环境速查

| 事项 | 命令 / 说明 |
| --- | --- |
| 本地跑改动后的客户端 | `.\script\dev-run.ps1`（自动补齐图标目录，直连本机 taix-server） |
| 发版 | `.\script\release.ps1 -Version 1.3.4-fork.N [-Commit] [-Watch] [-DryRun]` |
| 本地能否打 Setup.exe | 不能：安装器（cargo）与客户端 AOT 都需要 MSVC 链接器（VS Build Tools），故统一走 GitHub Actions |
| fork 上 tag 触发 Actions | 不会触发，`release.ps1` 内部改用 `gh workflow run --ref <tag>` |
