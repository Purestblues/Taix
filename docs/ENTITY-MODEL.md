# 应用实体模型（Entity Model）

> 决策：2026-11-11 用户拍板 5 条（自动判定依据、作用域、跨源合并、差额行、域名粒度）。
> 状态：**设计定稿，未实现**。`N6` / `N2` / `N5` 动手时以本文为准；与本文冲突的实现先回来改文档。
> 关联：[`PLAN-2026-11.md`](PLAN-2026-11.md)（排期）、[`ROADMAP.md`](ROADMAP.md)（M6）、[`ENTITY-MODEL.html`](ENTITY-MODEL.html)（关系图）

## 0. 一句话

系统现在有两套主语（应用表、网站表）。接下来统一成**一套实体**：一个实体 = 一段可以被归属的屏幕时间。本文定义实体是什么、怎么认身份、时间怎么扣到它头上、合并与分类怎么生效、开关怎么改视图。

## 1. 为什么必须先定模型

三处返工风险：

1. **身份**没定死 → 合并组、分类、图标缓存、详情页路由各自用不同的键，改一处崩三处。
2. **重复计**没解决 → 浏览器和它里面的网站是同一段时间的两次记录（Chrome 60 分钟里含 bilibili 40 分钟）。今天的页面靠「应用 / 网站」切换器回避这个问题；合成一张表后它无处可躲，直接表现为总额虚高。
3. **归属层**没定 → 合并到底是改数据还是改视图，决定要不要写全库重算、能不能回滚。

## 2. 实体类型

| 类型 | 业务身份 | 存储主键 | 时间来源 | 分类命名空间 | 可被合并 |
| --- | --- | --- | --- | --- | --- |
| **应用实体** `AppEntity` | `AppModels.Name`（进程名，如 `chrome.exe` / `com.apple.Safari`） | `AppModels.ID` | `AppSessions` / `HoursLogModels` / `DailyLogModels` | `CategoryModels` | 是（同类） |
| **网站实体** `SiteEntity` | `WebSiteModels.Domain`（回环地址保留端口，如 `localhost:3000`） | `WebSiteModels.ID` | `WebBrowseLogModels` | `WebSiteCategoryModels` | 是（同类） |
| **合并组** `MergeGroup` | `group:{ID}`（组名仅显示，可改） | `EntityMergeGroups.ID` | 由成员推导 | 跟随母成员 | 否（扁平，不嵌套） |

客户端 identityKey 约定：`app:{ID}` / `site:{ID}` / `grp:{ID}`。跨端持久化（配置、书签、插件）用业务身份（`Name` / `Domain` / `group:{ID}`），不用自增 ID——数据目录迁移后 ID 可能重排。

事实依据（读代码核过）：

- 应用按 `Name` 查找与自动创建，`File`（exe 路径）、`IconFile`、`Description` 是首报时回填的可空字段 —— `shijian-server/src/services/app_timer.rs`。
- 新建应用时按 `File` 做目录分类匹配（`CategoryService::match_category_by_path`），匹配不到落系统分类。
- 站点身份是 `Domain`，由 `extract_domain` 归一化（小写、去端口，回环地址保留端口）—— `shijian-server/src/services/web_domain.rs`。
- 分类是**两套互不相干的命名空间**：`CategoryModels` 与 `WebSiteCategoryModels`，ID 各自自增，同 ID 不代表同一分类。

### 2.1 显示名（只影响显示，不影响身份）

| 层 | 规则 | 位置 |
| --- | --- | --- |
| 应用 | `Alias` > `Description`（Windows）/ `Name`（macOS）> `Name` | `AppModelExtensions.GetDisplayName` |
| 网站 | `Alias` > `Title` > `Domain` | `ChartDataMapper.MapFromWebSites` |
| 合并组 | 组 `Name` > 母成员显示名 | 新增 |

`Alias` 是用户改的显示名，**永远不进身份**。用户改了别名不能让历史数据分家。

## 3. 时间归属：先承认重复

现有四张时间表：

| 表 | 粒度 | 时区 | 能回答 | 不能回答 |
| --- | --- | --- | --- | --- |
| `AppSessions` | 单次会话区间（Start/End/Duration） | UTC（RFC3339） | 时间轴、并发、会话数、最长单次 | 一次会话内部在干什么 |
| `HoursLogModels` | 应用 × UTC 小时 | UTC，查询期按 tz 折算 | 小时桶柱图、热力图 | 跨小时会话的精确边界（被拆桶） |
| `DailyLogModels` | 应用 × UTC 日期 | UTC 日期串 | 天粒度汇总 | 本地「今天」（需 tz 换算） |
| `WebBrowseLogModels` | 单次访问（`LogTime` + `Duration` + `SiteId` + `UrlId`） | UTC | 站点时长、浏览记录数、访问时段 | 属于哪个浏览器（**数据缺失**） |

`WebSiteModels.Duration` 是全量累计标量（`SUM(WebBrowseLogModels.Duration)` 的物化值），**只能用于「历史总计」**，任何按区间的视图都不能读它。

**不变式 I1**：应用时间与站点时间会重叠。同一分钟内 Chrome 记了一次会话、bilibili 记了一次访问，这是两行记录，直接相加等于把同一分钟算两次。统一实体表必须先扣减，再聚合。

## 4. 互斥归属规则（N2 的算法核心）

### 4.1 缺口：站点记录没有浏览器标识

浏览器扩展的上报体只有 `Url / Title / Icon / Duration / ActiveTime`（`shijian-server/src/routes/web_sentry.rs`），其中 `ActiveTime` 名字骗人，实际是**开始时间戳**（秒），不是活跃时长。因此今天无法回答「这条 bilibili 记录发生在 Chrome 还是 Edge」。这是 N2 开工前必须先补的一块数据。

### 4.2 已定：差额单列

对某个浏览器实体 `B`：

```
S = B 的 AppSessions 区间集合        （真实活跃时长）
W = 归属到 B 的站点访问区间集合       （每条 = [LogTime, LogTime + Duration]）
```

- `B` 在统一实体表里的时长 = `|S − W|`，显示为「**浏览器内其他页面**」（新标签页、未收录域名、扩展未上报的页面）。
- 每个站点实体的时长 = 自己的 `|W|`，不裁剪。
- `Σ 所有实体 = Σ 应用总时长`，两种视图下总额一致（差额行有名字，所以不蒸发）。

这就是用户拍板的「单列差额行」的直接结果：它把「开关一拨总额会变」这个坏性质消掉了——变的是浏览器行的大小，不是总额。

### 4.3 溢出与并发的处理

| 情况 | 处理 |
| --- | --- |
| `W` 超出 `S`（后台标签、无窗口、扩展比监控起得早） | 站点时长照实计入；`B` 的差额下限为 0；超出的量记入 `overflow` 计数器（设置页可见），**不改写原始数据** |
| 同一浏览器多标签同时被上报 | 视为数据异常，按 `min(1, 1/并发数)` 归一后再扣减；扩展侧已按 visibility 过滤，正常不会触发 |
| 多个浏览器同时前台（多屏） | 各浏览器独立算，互不扣减 |
| 站点无归属（历史数据、未上报 browser） | 落到「默认浏览器」设置项；该浏览器无对应会话时退化为不扣减，页面标注「部分站点时长未与浏览器对齐」 |

## 5. 合并组（MergeGroup）

### 5.1 作用域：视图期映射，不动数据（已定）

- 合并组**只在查询 / 组装时生效**，`AppModels`、`WebSiteModels`、`DailyLogModels` 一行不改。
- 好处：改设置立刻生效；随时拆回原样；不需要全库重算；历史数据不承担口径变更的风险。
- 代价：合并后的排序、Top N、「其他」归并必须在同一层做（服务端实体接口），不能一部分合并一部分不合并。

### 5.2 自动判定依据（已定：路径 + 发布者）

判定输入：

| 平台 | 现有 | 需补 |
| --- | --- | --- |
| Windows | `File`（exe 路径）、`Description`（FileDescription） | **`Publisher`（CompanyName / 数字签名主体）** |
| macOS | `File`（bundle 路径）、`Name`（bundle id，如 `com.apple.Safari`）、`Description`（displayName） | 无（bundle id 已够） |

规则（按顺序命中）：

1. 同一目录、同一 `Publisher` → 候选组（主成员 = 组内时长最长者，平局取路径最短、再平局取创建最早）。
2. 安装型应用（路径在 `Program Files` / `Applications` / `AppData\Local\Programs` 下）以「去掉版本号段的目录」为组锚点。
3. 显式排除：浏览器、系统组件、以及用户「解散过」的组（记 suppression，不重建）。

自动组**默认生效**（用户点名的痛点是列表散成十几条），管理页可一键固化、改名、拆出成员、整组解散。

### 5.3 跨源合并（已定：仅手动）

自动判定**只在同类内**跑（应用并应用、站点并站点）。`chatgpt.com` 与 `ChatGPT.exe` 要合成一个实体，只能手动绑定，组 `Kind = mixed`。

理由：语义匹配（图标 / 名字 / 域名）误判一次的代价是「时长凭空翻倍」，排查成本远高于手动绑一次。

### 5.4 分类归属（已定：子继承母，自身留档）

- 组的展示分类 = **母成员的分类**。
- 成员自己的 `CategoryID` **不改不删**（这就是「保留原有标签」的落地）。拆分后成员原地回到自己的分类，不需要重新打标签。
- 站点成员的分类来自 `WebSiteCategoryModels`，应用成员来自 `CategoryModels`。跨源组取母成员那一侧；另一侧成员在详情里显示为「(网站分类) 前端开发」，不参与组的分类判定。
- 管理页允许 per-member override（「这个成员按自己的分类算」），默认关。

**关于「标签」的澄清**：当前数据模型只有单值分类，没有多标签系统。用户说的「保留原有标签」在实现上等于「不覆盖成员自身的分类归属」。将来若加多标签，合并层必须保留成员的标签集合，同样不销毁。

## 6. 视图开关（网站并入浏览器）

| | 关闭（默认） | 打开 |
| --- | --- | --- |
| 站点行 | 独立成行（bilibili / chatgpt） | 消失 |
| 浏览器行 | 只剩余额（「其他页面」），为 0 时隐藏 | 真实活跃时长（含站点部分） |
| 差额行 | 存在 | 并入浏览器行 |
| 总额 | 应用总时长 | 应用总时长（不变） |

占比分母恒为「实体总时长」，所以两种视图占比之和都是 100%。

> 这一条修正了 11-11 早前的口头结论（「开关一拨总额会变」）。差额单列之后总额守恒；差额仍然存在，只是从「消失的几分钟」变成「一行看得见的时间」。

## 7. 数据源映射（现状 → 实体层）

| 页面 / 组件 | 今天读什么 | 实体层需要什么 |
| --- | --- | --- |
| 概览榜单 / 卡片 | `api/appdata` + `api/webdata/sites` 两套 | 一套实体列表（含分类、徽章、合并组） |
| 概览柱图（分类×时段） | `api/data/category-hours`、`api/data/category-range` | 小时桶需要覆盖站点 → 服务端聚合 browse log |
| 统计页 | `api/data/hours-range`、`api/webdata/*statistics` | 同上，两侧统一成一个矩阵 |
| 详细页指标卡 / 环形时钟 | `api/data/sessions`、`api/webdata/browse-log-list` | 组实体：成员会话合并；站点：浏览记录 |
| 详细页热力图 | 应用 × 小时 / 星期 × 小时 | 实体 × 时段，需站点小时桶 |
| 分类页 / 分类汇总 | `api/category/*summary` + `api/category/web/*` | 分类维度保留双命名空间，但列表要能混排实体 |
| 列表页 / 搜索 | `api/appdata`、`api/webdata/sites` | 实体索引（identityKey 搜一次） |

建议新增（不动旧接口，避免两套口径打架）：

```
GET /api/entity/list?start&end&tz&merge=1&browserMerged=0
GET /api/entity/hours?start&end&tz           → 实体 × 小时桶矩阵
GET /api/entity/summary?start&end&tz          → 指标卡所需（会话数 / 时长 / 最长单次）
GET /api/entity/merge-groups                  → 组列表（含自动候选）
```

## 8. Schema 增量（只加不改）

| 表 / 列 | 用途 | 备注 |
| --- | --- | --- |
| `AppModels.Publisher` | 自动判定 | 新列，历史行 NULL，按路径规则退化 |
| `AppModels.BundleID` | macOS 身份辅助 | 现在塞在 `Name` 里，独立出来后 `Name` 语义更清楚（这一步可以延后，改名批次一起做） |
| `WebBrowseLogModels.BrowserRef` | 站点 → 浏览器归属 | 新列，历史行 NULL → 落默认浏览器 |
| `EntityMergeGroups(ID, Name, Kind, PrimaryRef, IsAuto, UpdatedAt)` | 组 | `Kind ∈ app/site/mixed` |
| `EntityMergeMembers(GroupID, MemberRef, OverrideCategoryRef, AddedAt)` | 成员 | `MemberRef` 用业务身份字符串，与主键解耦 |
| `EntitySuppressions(Ref, Reason, CreatedAt)` | 解散过的组不重建 | 仅自动组用 |

迁移口径：全部 `NULL` 允许，历史数据不回填、不重算；首次打开新页面时按「无 publisher → 路径规则」「无 BrowserRef → 默认浏览器」运行。

## 9. 不变式（验收线）

1. **守恒**：`Σ 实体时长 = Σ 应用总时长`（两种视图都成立）。
2. **可逆**：合并 / 拆分 / 开关切换不写原始表，来回切十次数据一致。
3. **可下钻**：每个实体行都能点进详情；组合实体点进去先看到成员列表。
4. **身份不变**：改 `Alias`、改标题、改图标不影响归属。
5. **差额可见**：为 0 时隐藏，不为 0 时必须有一行说明它是什么。
6. **口径标注**：会话口径（环形时钟中心）与小时桶口径（指标卡/柱图）的历史差异继续在页面注明，实体化不负责消除它。

## 10. 已决策 / 待决策

**已决策（本文即定稿）**

| # | 问题 | 结论 |
| --- | --- | --- |
| Q1 | 自动判定依据 | `exe 路径 + Publisher`（macOS 用 bundle id） |
| Q2 | 合并作用域 | 视图期映射，不动数据 |
| Q3 | 跨源合并 | 仅手动绑定（`Kind = mixed`） |
| Q4 | 差额项 | 单列「浏览器内其他页面」 |
| Q5 | 站点粒度 | 保持域名（回环保留端口） |

**待决策（N2 开工前必须回答）**

| # | 问题 | 影响 |
| --- | --- | --- |
| Q7 | 扩展上报浏览器标识：加 `Browser`（chrome/firefox/safari，扩展自报）还是加 `BrowserProfile`（区分多用户/多开）？ | 决定 `B` 的粒度；多开浏览器（Chrome + Chrome Beta）会拆成两个实体 |
| Q8 | 默认浏览器怎么定：设置项手选 / 取最近一次前台浏览器 / 按时段推断 | 只影响历史无归属数据的兜底质量 |
| Q9 | 合并组的排序与「其他」归并放在服务端还是客户端 | 放服务端要与新实体接口一起做；放客户端要在 Top N 之后再合并，逻辑重复一份 |

## 11. 实现顺序

| 批次 | 步骤 | 依赖 |
| --- | --- | --- |
| `fork.9`（N6） | ① 上报加 `Publisher`（monitor win/mac + pipe + request）<br>② `AppModels.Publisher` 列 + 迁移<br>③ `EntityMergeGroups/Members` + 自动判定服务<br>④ 设置页「子母程序合并」管理页<br>⑤ 客户端实体适配层（只做合并，不改页面结构） | 无 |
| `fork.10`（N2） | ⑥ 扩展上报浏览器标识 + `BrowserRef` 列<br>⑦ `/api/entity/*` 聚合（互斥归属 + 差额行 + 小时桶）<br>⑧ 概览 / 统计 / 详细 / 分类四页切数据源<br>⑨ 「网站并入浏览器」开关 + 差额行 UI | ⑥⑦ 必须先于 ⑧ |
| `fork.11`（N5） | ⑩ 分类页「分类 / 应用」视图切换 + 快速打标签 | ⑧ |

每步可独立发布、独立回滚。`fork.9` 只加表与映射层，页面看到的是「合并后的应用列表」，风险最小；`fork.10` 才是口径切换，单独一个版本观察。
