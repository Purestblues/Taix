//! 统一实体表（N2）：把应用与网站扣成一张互斥的屏幕时间表。
//!
//! 口径（见 `docs/ENTITY-MODEL.md`）：
//! - 应用时长来自 `HoursLogModels`（UTC 小时桶），网站时长来自 `WebBrowseLogModels`；
//! - 浏览器的真实活跃时长来自 `AppSessions`，属于它的站点日志按区间求交后**扣减**，
//!   余额行叫「浏览器内其他页面」（新标签页、未收录域名、扩展没上报的页面）；
//! - 关掉「网站并入浏览器」时：站点独立成行，浏览器只剩余额；
//!   打开时：站点行消失，浏览器拿回完整活跃时长（站点作为成员挂在它下面）；
//! - 总额在两种视图下都是「应用总时长」：差额行有名字，所以不会蒸发。
//!
//! 合并组（子母程序）在这一层应用：母成员保留一行，其余成员折进来并在 `members` 里交代。

use chrono::{DateTime, Datelike, Duration, NaiveDate, Utc};
use sqlx::SqlitePool;
use std::collections::{HashMap, HashSet};

use crate::error::AppError;
use crate::models::app::AppJoinCols;
use crate::models::entity::{
    BrowserOptionModel, EntityHoursModel, EntityHoursRow, EntityListModel, EntityMemberModel,
    EntityModel, MergeGroupListModel, MergeGroupModel,
};
use crate::models::web::WebSiteJoinCols;
use crate::services::app_merge::{self, MergeCandidate};
use crate::services::config::ConfigService;
use crate::utils::{parse_timezone, tz_date_to_utc_range, tz_naive_to_utc};

/// 每个浏览器标识的候选进程名片段：扩展按构建目标上报 chrome / firefox / safari，
/// 而 Chromium 系（Edge / Brave / Arc）装 chrome 版扩展时也会上报 chrome，
/// 所以这里允许一族进程名映射到同一个标识，取时长最长的那个当实体。
const BROWSER_FAMILIES: [(&str, &[&str]); 3] = [
    (
        "chrome",
        &[
            "chrome", "chromium", "msedge", "brave", "vivaldi", "arc", "thorium", "360se",
            "360chrome", "qqbrowser",
        ],
    ),
    ("firefox", &["firefox", "librewolf", "waterfox", "zen"]),
    ("safari", &["safari"]),
];

pub struct EntityService;

#[derive(Debug, Clone)]
struct AppAgg {
    app: AppJoinCols,
    seconds: i64,
}

#[derive(Debug, Clone, Default)]
struct SiteAgg {
    domain: String,
    title: Option<String>,
    alias: Option<String>,
    icon_file: Option<String>,
    category_id: i64,
    category_name: Option<String>,
    category_color: Option<String>,
    /// 已按互斥归属扣减后的秒数（可能来自多个浏览器，累加）
    credited: i64,
    /// 归属到哪些浏览器（应用 ID + 秒数），`browserMerged=1` 时作为成员展示
    by_browser: HashMap<i64, i64>,
}

impl EntityService {
    pub async fn list(
        pool: &SqlitePool,
        config_service: &ConfigService,
        start: NaiveDate,
        end: NaiveDate,
        tz_id: &str,
        browser_merged: bool,
        take: Option<usize>,
    ) -> Result<EntityListModel, AppError> {
        let tz = parse_timezone(tz_id);
        let utc_start = tz_date_to_utc_range(start, &tz).0;
        let utc_end = tz_date_to_utc_range(end, &tz).1;
        let config = config_service.get_or_load().await?;
        let configured_browser = config.general.default_browser.trim().to_lowercase();
        let excluded = config_service.get_excluded_app_id_set(pool).await;

        // 1. 应用时长（UTC 小时桶，与既有页面同口径）
        let apps = load_app_aggregates(pool, utc_start, utc_end).await?;
        let mut app_seconds: i64 = 0;
        let mut browser_apps: HashMap<String, i64> = HashMap::new();
        for agg in &apps {
            if excluded.contains(&agg.app.app_id.unwrap_or(0)) {
                continue;
            }
            app_seconds += agg.seconds;
        }
        for (reference, app_id) in resolve_browser_apps(&apps, &excluded) {
            browser_apps.insert(reference, app_id);
        }
        let browser_app_ids: HashSet<i64> = browser_apps.values().copied().collect();

        // 没配默认浏览器时：先取浏览日志里出现最多的标识；一条都没有、又只识别到一个浏览器，
        // 就归给它（老版本扩展上报的记录全是 NULL，这一步能让历史数据立刻有归属）
        let default_browser = if configured_browser.is_empty() {
            recommend_default_browser(pool, &browser_apps).await?
        } else {
            configured_browser.clone()
        };

        // 2. 浏览器会话区间（用于互斥扣减）
        let sessions = load_session_intervals(pool, utc_start, utc_end).await?;

        // 3. 站点日志按「站点 + 浏览器」求交
        let logs = load_browse_logs(pool, utc_start, utc_end).await?;
        let mut sites: HashMap<i64, SiteAgg> = HashMap::new();
        let mut credited_by_browser: HashMap<i64, i64> = HashMap::new();
        let mut overflow_seconds: i64 = 0;
        let mut unmatched_seconds: i64 = 0;

        for log in logs {
            let clipped = clip_interval(log.start, log.end, utc_start.timestamp(), utc_end.timestamp());
            if clipped <= 0 {
                continue;
            }
            let reference = if log.browser.trim().is_empty() {
                default_browser.clone()
            } else {
                log.browser.trim().to_lowercase()
            };
            let browser_app = browser_apps.get(&reference).copied();

            let (credited, overflow, unmatched) = match browser_app {
                Some(app_id) => {
                    let merged = sessions.get(&app_id);
                    match merged {
                        Some(intervals) if !intervals.is_empty() => {
                            let overlap = overlap_seconds(
                                intervals,
                                log.start.timestamp(),
                                log.start.timestamp() + clipped,
                            );
                            let credited = overlap.min(clipped).max(0);
                            (credited, clipped - credited, 0)
                        }
                        // 该浏览器在这段时间没有任何会话：不退化成扣减，站点照实计入并标记未对齐
                        _ => (clipped, 0, clipped),
                    }
                }
                // 没有归属浏览器（默认浏览器也没跑），照实计入
                None => (clipped, 0, clipped),
            };

            if credited <= 0 {
                overflow_seconds += overflow;
                continue;
            }
            overflow_seconds += overflow;
            unmatched_seconds += unmatched;

            let site = sites.entry(log.site_id).or_default();
            site.credited += credited;
            if let Some(app_id) = browser_app {
                *site.by_browser.entry(app_id).or_insert(0) += credited;
                *credited_by_browser.entry(app_id).or_insert(0) += credited;
            }
        }

        // 补全站点静态信息（只查有归属的站点）
        fill_site_details(pool, &mut sites).await?;

        // 4. 合并组：先把自动组落库（按全量应用判定，避免「今天没跑的成员看不到组」），
        //    再把本窗口的行按组折叠
        sync_auto_groups(pool).await?;
        let merged_apps = apply_merge_groups(pool, &apps, &excluded).await?;

        // 5. 组装行
        let mut rows: Vec<EntityModel> = Vec::new();
        let mut balance_seconds: i64 = 0;
        for (primary, group) in merged_apps {
            let app_id = primary.app.app_id.unwrap_or(0);
            let is_browser = browser_app_ids.contains(&app_id);
            let site_credited = credited_by_browser.get(&app_id).copied().unwrap_or(0);

            if is_browser && !browser_merged {
                // 关闭「网站并入浏览器」：浏览器行只剩余额
                let balance = (primary.seconds - site_credited).max(0);
                if balance > 0 {
                    balance_seconds += balance;
                    rows.push(EntityModel {
                        key: format!("other:{}", app_id),
                        kind: "other".to_string(),
                        name: other_row_name(&primary.app, browser_apps.len()),
                        icon: primary.app.app_icon_file.clone(),
                        seconds: balance,
                        // 差额行属于浏览器这个实体：分类跟着浏览器走，
                        // 否则「分类页各分类之和」会比总额少掉浏览器剩下的那段时间
                        category_id: primary.app.app_category_id.unwrap_or(0),
                        category_kind: "app".to_string(),
                        category_name: primary.app.cat_name.clone(),
                        category_color: primary.app.cat_color.clone(),
                        is_browser: true,
                        members: Vec::new(),
                    });
                }
                continue;
            }

            let mut members: Vec<EntityMemberModel> = Vec::new();
            let kept_name = primary.app.app_name.clone().unwrap_or_default();
            if let Some(group) = &group {
                for member in group.members.iter().filter(|m| m.as_str() != kept_name) {
                    if let Some(agg) = apps.iter().find(|a| names_match(a, member)) {
                        members.push(EntityMemberModel {
                            key: format!("app:{}", agg.app.app_id.unwrap_or(0)),
                            name: display_name(&agg.app),
                            seconds: agg.seconds,
                            icon: agg.app.app_icon_file.clone(),
                        });
                    }
                }
            }
            if is_browser && browser_merged {
                for (site_id, per_browser) in &sites {
                    if let Some(seconds) = per_browser.by_browser.get(&app_id) {
                        let agg = &sites[site_id];
                        members.push(EntityMemberModel {
                            key: format!("site:{}", site_id),
                            name: site_display_name(agg),
                            seconds: *seconds,
                            icon: agg.icon_file.clone(),
                        });
                    }
                }
            }

            let name = group
                .as_ref()
                .map(|g| g.name.clone())
                .unwrap_or_else(|| display_name(&primary.app));

            rows.push(EntityModel {
                key: format!("app:{}", app_id),
                kind: "app".to_string(),
                name,
                icon: primary.app.app_icon_file.clone(),
                seconds: primary.seconds,
                category_id: primary.app.app_category_id.unwrap_or(0),
                category_kind: "app".to_string(),
                category_name: primary.app.cat_name.clone(),
                category_color: primary.app.cat_color.clone(),
                is_browser,
                members,
            });
        }

        // 网站侧总时长（含并入浏览器的部分）
        let site_seconds: i64 = sites.values().map(|agg| agg.credited).sum();

        // 6. 站点行：关闭合并时全部独立成行；打开合并时只有「找不到归属浏览器」的站点还需要留行
        if !browser_merged || sites.values().any(|agg| agg.by_browser.is_empty()) {
            for (site_id, agg) in &sites {
                if agg.credited <= 0 {
                    continue;
                }
                if browser_merged && !agg.by_browser.is_empty() {
                    continue;
                }
                rows.push(EntityModel {
                    key: format!("site:{}", site_id),
                    kind: "site".to_string(),
                    name: site_display_name(agg),
                    icon: agg.icon_file.clone(),
                    seconds: agg.credited,
                    category_id: agg.category_id,
                    category_kind: "site".to_string(),
                    category_name: agg.category_name.clone(),
                    category_color: agg.category_color.clone(),
                    is_browser: false,
                    members: Vec::new(),
                });
            }
        }

        // 关闭合并时，被站点吃掉的那部分已经从浏览器行里扣掉了，站点秒数也计入展示总量
        rows.sort_by(|a, b| {
            b.seconds
                .cmp(&a.seconds)
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        });

        // Top N 与「其他」归并放在服务端：排序规则只有一处，客户端不需要再切一次
        if let Some(take) = take {
            if take > 0 && rows.len() > take {
                let keep = take - 1;
                let rest = rows.split_off(keep);
                rows.push(EntityModel {
                    key: "aggregate:0".to_string(),
                    kind: "aggregate".to_string(),
                    name: "其他".to_string(),
                    icon: None,
                    seconds: rest.iter().map(|row| row.seconds).sum(),
                    category_id: 0,
                    category_kind: "app".to_string(),
                    category_name: None,
                    category_color: None,
                    is_browser: false,
                    members: Vec::new(),
                });
            }
        }

        // 两种视图的总额都等于「应用总时长 + 未对齐的站点时长」：
        // 差额行有名字，所以关掉合并时总额也不会少
        let total_seconds = app_seconds + unmatched_seconds;

        Ok(EntityListModel {
            rows,
            total_seconds,
            app_seconds,
            site_seconds,
            balance_seconds,
            overflow_seconds,
            unmatched_seconds,
            browser_merged,
            default_browser,
        })
    }

    pub async fn merge_groups(pool: &SqlitePool) -> Result<MergeGroupListModel, AppError> {
        // 设置页可能先于概览页被打开：这里也跑一次自动判定，组才不会等到进概览页才出现
        sync_auto_groups(pool).await?;
        let groups = load_groups(pool).await?;
        let suppressed = load_suppressions(pool).await?;
        Ok(MergeGroupListModel {
            groups,
            suppressed: suppressed.into_iter().collect(),
        })
    }

    /// 解散一个自动组：写 suppression，删组。manual 组同样删掉。
    pub async fn dissolve_group(pool: &SqlitePool, id: i64) -> Result<(), AppError> {
        let row: Option<(Option<String>,)> =
            sqlx::query_as("SELECT AutoKey FROM EntityMergeGroups WHERE ID = ?")
                .bind(id)
                .fetch_optional(pool)
                .await?;
        let Some((auto_key,)) = row else {
            return Ok(());
        };
        if let Some(key) = auto_key {
            sqlx::query(
                "INSERT OR REPLACE INTO EntitySuppressions (Ref, Reason, CreatedAt) VALUES (?, 'dissolved', datetime('now'))",
            )
            .bind(key)
            .execute(pool)
            .await?;
        }
        sqlx::query("DELETE FROM EntityMergeMembers WHERE GroupID = ?")
            .bind(id)
            .execute(pool)
            .await?;
        sqlx::query("DELETE FROM EntityMergeGroups WHERE ID = ?")
            .bind(id)
            .execute(pool)
            .await?;
        Ok(())
    }

    pub async fn restore_suppressed(pool: &SqlitePool) -> Result<(), AppError> {
        sqlx::query("DELETE FROM EntitySuppressions")
            .execute(pool)
            .await?;
        Ok(())
    }

    pub async fn rename_group(pool: &SqlitePool, id: i64, name: &str) -> Result<(), AppError> {
        let name = name.trim();
        if name.is_empty() {
            return Ok(());
        }
        sqlx::query("UPDATE EntityMergeGroups SET Name = ?, UpdatedAt = datetime('now') WHERE ID = ?")
            .bind(name.chars().take(64).collect::<String>())
            .bind(id)
            .execute(pool)
            .await?;
        Ok(())
    }

    /// 设置页选「默认浏览器」用：浏览日志里出现过的浏览器 + 应用侧识别到的浏览器实体
    pub async fn browsers(
        pool: &SqlitePool,
        config_service: &ConfigService,
    ) -> Result<Vec<BrowserOptionModel>, AppError> {
        let config = config_service.get_or_load().await?;
        let default_browser = config.general.default_browser.trim().to_lowercase();
        let excluded = config_service.get_excluded_app_id_set(pool).await;

        #[derive(sqlx::FromRow)]
        struct RefRow {
            #[sqlx(rename = "Ref")]
            reference: String,
            #[sqlx(rename = "Seconds")]
            seconds: i64,
        }

        let refs: Vec<RefRow> = sqlx::query_as(
            r#"
            SELECT COALESCE(NULLIF(BrowserRef, ''), '') AS Ref, SUM(Duration) AS Seconds
            FROM WebBrowseLogModels
            GROUP BY COALESCE(NULLIF(BrowserRef, ''), '')
            "#,
        )
        .fetch_all(pool)
        .await?;

        let apps = load_app_aggregates_all(pool).await?;
        let resolved = resolve_browser_apps(&apps, &excluded);

        let mut result: Vec<BrowserOptionModel> = Vec::new();
        let mut seen: HashSet<String> = HashSet::new();
        for row in refs {
            let reference = row.reference.trim().to_lowercase();
            if reference.is_empty() {
                continue;
            }
            seen.insert(reference.clone());
            let app_id = resolved.get(&reference).copied();
            let app = app_id.and_then(|id| {
                apps.iter()
                    .find(|a| a.app.app_id == Some(id))
                    .map(|a| (display_name(&a.app), a.seconds))
            });
            result.push(BrowserOptionModel {
                is_default: default_browser == reference,
                reference,
                app_key: app_id.map(|id| format!("app:{}", id)),
                app_name: app.map(|(name, _)| name),
                seconds: row.seconds,
            });
        }
        // 应用侧识别到、但还没有浏览日志的浏览器也列出来（用户可能刚装上扩展）
        for (reference, app_id) in resolved {
            if seen.contains(&reference) {
                continue;
            }
            let app = apps.iter().find(|a| a.app.app_id == Some(app_id));
            result.push(BrowserOptionModel {
                is_default: default_browser == reference,
                reference,
                app_key: Some(format!("app:{}", app_id)),
                app_name: app.map(|a| display_name(&a.app)),
                seconds: app.map(|a| a.seconds).unwrap_or(0),
            });
        }
        result.sort_by(|a, b| b.seconds.cmp(&a.seconds).then_with(|| a.reference.cmp(&b.reference)));
        Ok(result)
    }
}

impl EntityService {
    /// 实体 × 时段矩阵（`/api/entity/hours`）。
    ///
    /// 桶由服务端按本地时区切好：单日 = 24 小时、区间 = 每天、整年 = 12 个月。
    /// 互斥归属在这里同样是必需的：站点日志按浏览器会话求交后从浏览器里扣掉，
    /// 否则「分类 × 时段」柱图会把同一分钟画两次。
    pub async fn hours(
        pool: &SqlitePool,
        config_service: &ConfigService,
        start: NaiveDate,
        end: NaiveDate,
        tz_id: &str,
        buckets: &str,
        browser_merged: bool,
    ) -> Result<EntityHoursModel, AppError> {
        let tz = parse_timezone(tz_id);
        let windows = bucket_windows(start, end, &tz, buckets);
        let bucket_count = windows.len();
        if bucket_count == 0 {
            return Ok(EntityHoursModel {
                labels: Vec::new(),
                rows: Vec::new(),
                browser_merged,
                bucket_count: 0,
            });
        }
        let utc_start = DateTime::from_timestamp(windows[0].0, 0)
            .ok_or_else(|| AppError::Business("无效的时间范围".to_string()))?;
        let utc_end = DateTime::from_timestamp(windows[bucket_count - 1].1, 0)
            .ok_or_else(|| AppError::Business("无效的时间范围".to_string()))?;

        let config = config_service.get_or_load().await?;
        let configured_browser = config.general.default_browser.trim().to_lowercase();
        let excluded = config_service.get_excluded_app_id_set(pool).await;

        let all_apps = load_app_aggregates_all(pool).await?;
        let no_excluded: HashSet<i64> = HashSet::new();
        let browser_apps = resolve_browser_apps(&all_apps, &no_excluded);
        let browser_app_ids: HashSet<i64> = browser_apps.values().copied().collect();
        let default_browser = if configured_browser.is_empty() {
            recommend_default_browser(pool, &browser_apps).await?
        } else {
            configured_browser.clone()
        };

        // 1. 应用 × 桶（UTC 小时桶直接落到对应桶里）
        #[derive(sqlx::FromRow)]
        struct HourRow {
            #[sqlx(rename = "AppModelID")]
            app_id: i64,
            #[sqlx(rename = "Hour")]
            hour: DateTime<Utc>,
            #[sqlx(rename = "total")]
            total: i64,
        }

        let hour_rows: Vec<HourRow> = sqlx::query_as(
            r#"
            SELECT AppModelID, DataTime AS Hour, SUM(Time) AS total
            FROM HoursLogModels
            WHERE DataTime >= ? AND DataTime < ?
            GROUP BY AppModelID, DataTime
            "#,
        )
        .bind(utc_start)
        .bind(utc_end)
        .fetch_all(pool)
        .await?;

        let mut app_matrix: HashMap<i64, Vec<i64>> = HashMap::new();
        for row in hour_rows {
            if excluded.contains(&row.app_id) {
                continue;
            }
            if let Some(index) = bucket_index(&windows, row.hour.timestamp()) {
                let entry = app_matrix
                    .entry(row.app_id)
                    .or_insert_with(|| vec![0_i64; bucket_count]);
                entry[index] += row.total;
            }
        }

        // 2. 站点 × 桶：只记与浏览器会话重叠的部分
        let sessions = load_session_intervals(pool, utc_start, utc_end).await?;
        let logs = load_browse_logs(pool, utc_start, utc_end).await?;
        let mut site_matrix: HashMap<i64, Vec<i64>> = HashMap::new();
        let mut browser_site_matrix: HashMap<i64, Vec<i64>> = HashMap::new();
        let mut attributed_sites: HashSet<i64> = HashSet::new();

        for log in logs {
            let Some(index) = bucket_index(&windows, log.start.timestamp()) else {
                continue;
            };
            let (window_start, window_end) = windows[index];
            let clip_start = log.start.timestamp().max(window_start);
            let clip_end = log.end.timestamp().min(window_end);
            let clipped = clip_end - clip_start;
            if clipped <= 0 {
                continue;
            }

            let reference = if log.browser.trim().is_empty() {
                default_browser.clone()
            } else {
                log.browser.trim().to_lowercase()
            };
            let browser_app = browser_apps.get(&reference).copied();

            let credited = match browser_app {
                Some(app_id) => match sessions.get(&app_id) {
                    Some(intervals) if !intervals.is_empty() => {
                        overlap_seconds(intervals, clip_start, clip_end).min(clipped)
                    }
                    _ => clipped,
                },
                None => clipped,
            };
            if credited <= 0 {
                continue;
            }

            let site = site_matrix
                .entry(log.site_id)
                .or_insert_with(|| vec![0_i64; bucket_count]);
            site[index] += credited;

            if let Some(app_id) = browser_app {
                attributed_sites.insert(log.site_id);
                let entry = browser_site_matrix
                    .entry(app_id)
                    .or_insert_with(|| vec![0_i64; bucket_count]);
                entry[index] += credited;
            }
        }

        // 3. 站点静态信息 + 合并组
        let mut site_aggs: HashMap<i64, SiteAgg> = site_matrix
            .keys()
            .map(|site_id| (*site_id, SiteAgg::default()))
            .collect();
        fill_site_details(pool, &mut site_aggs).await?;

        let app_meta: HashMap<i64, &AppAgg> = all_apps
            .iter()
            .filter_map(|agg| agg.app.app_id.map(|id| (id, agg)))
            .collect();
        let window_apps: Vec<AppAgg> = app_matrix
            .iter()
            .filter_map(|(app_id, values)| {
                app_meta.get(app_id).map(|agg| {
                    let mut app = (*agg).clone();
                    app.seconds = values.iter().sum();
                    app
                })
            })
            .collect();
        sync_auto_groups(pool).await?;
        let merged_apps = apply_merge_groups(pool, &window_apps, &excluded).await?;
        let name_to_id: HashMap<String, i64> = all_apps
            .iter()
            .filter_map(|agg| agg.app.app_name.clone().map(|name| (name, agg.app.app_id.unwrap_or(0))))
            .collect();

        // 4. 组装行
        let mut rows: Vec<EntityHoursRow> = Vec::new();
        for (primary, group) in merged_apps {
            let app_id = primary.app.app_id.unwrap_or(0);
            let is_browser = browser_app_ids.contains(&app_id);
            let kept_name = primary.app.app_name.clone().unwrap_or_default();

            let mut values = if let Some(group) = &group {
                let mut summed = vec![0_i64; bucket_count];
                for member in &group.members {
                    if let Some(member_id) = name_to_id.get(member) {
                        if let Some(member_values) = app_matrix.get(member_id) {
                            for (index, value) in member_values.iter().enumerate() {
                                summed[index] += *value;
                            }
                        }
                    }
                }
                summed
            } else {
                app_matrix.get(&app_id).cloned().unwrap_or_else(|| vec![0_i64; bucket_count])
            };

            if is_browser && !browser_merged {
                if let Some(credited) = browser_site_matrix.get(&app_id) {
                    for (index, value) in credited.iter().enumerate() {
                        values[index] = (values[index] - *value).max(0);
                    }
                }
                // 浏览器行在这个口径下只剩「其他页面」：全为 0 就不占一行
                if values.iter().all(|value| *value <= 0) {
                    continue;
                }
            }
            if values.iter().all(|value| *value <= 0) && group.is_none() {
                continue;
            }

            let name = group
                .as_ref()
                .map(|group| group.name.clone())
                .unwrap_or_else(|| display_name(&primary.app));
            let name = if is_browser && !browser_merged {
                other_row_name(&primary.app, browser_apps.len())
            } else {
                name
            };

            let mut members: Vec<EntityMemberModel> = Vec::new();
            if let Some(group) = &group {
                for member in group.members.iter().filter(|member| member.as_str() != kept_name) {
                    if let Some(agg) = window_apps
                        .iter()
                        .find(|candidate| candidate.app.app_name.as_deref() == Some(member.as_str()))
                    {
                        members.push(EntityMemberModel {
                            key: format!("app:{}", agg.app.app_id.unwrap_or(0)),
                            name: display_name(&agg.app),
                            seconds: agg.seconds,
                            icon: agg.app.app_icon_file.clone(),
                        });
                    }
                }
            }

            rows.push(EntityHoursRow {
                key: if is_browser && !browser_merged {
                    format!("other:{}", app_id)
                } else {
                    format!("app:{}", app_id)
                },
                kind: if is_browser && !browser_merged { "other" } else { "app" }.to_string(),
                name,
                icon: primary.app.app_icon_file.clone(),
                // 差额行的分类跟着浏览器走（与 list 一致），否则分类维度会漏掉「其他页面」
                category_id: primary.app.app_category_id.unwrap_or(0),
                category_kind: "app".to_string(),
                category_name: primary.app.cat_name.clone(),
                category_color: primary.app.cat_color.clone(),
                is_browser,
                values,
                members,
            });
        }

        // 站点行：关闭合并时全部出现；打开合并时只留「归属不到浏览器」的那些
        for (site_id, values) in &site_matrix {
            if values.iter().all(|value| *value <= 0) {
                continue;
            }
            if browser_merged && attributed_sites.contains(site_id) {
                continue;
            }
            let agg = site_aggs.get(site_id);
            rows.push(EntityHoursRow {
                key: format!("site:{}", site_id),
                kind: "site".to_string(),
                name: agg.map(site_display_name).unwrap_or_default(),
                icon: agg.and_then(|agg| agg.icon_file.clone()),
                category_id: agg.map(|agg| agg.category_id).unwrap_or(0),
                category_kind: "site".to_string(),
                category_name: agg.and_then(|agg| agg.category_name.clone()),
                category_color: agg.and_then(|agg| agg.category_color.clone()),
                is_browser: false,
                values: values.clone(),
                members: Vec::new(),
            });
        }

        rows.sort_by(|a, b| {
            let sum_a: i64 = a.values.iter().sum();
            let sum_b: i64 = b.values.iter().sum();
            sum_b
                .cmp(&sum_a)
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        });

        let labels = windows
            .iter()
            .filter_map(|(from, _)| {
                DateTime::from_timestamp(*from, 0).map(|dt| dt.with_timezone(&tz).to_rfc3339())
            })
            .collect();

        Ok(EntityHoursModel {
            labels,
            rows,
            browser_merged,
            bucket_count: bucket_count as i32,
        })
    }
}

/// 把本地时间轴切成桶：单日=24 小时 / 区间=每天 / 整年=12 个月
fn bucket_windows(
    start: NaiveDate,
    end: NaiveDate,
    tz: &chrono_tz::Tz,
    buckets: &str,
) -> Vec<(i64, i64)> {
    let mut result: Vec<(i64, i64)> = Vec::new();
    match buckets {
        "hour" => {
            for hour in 0..24u32 {
                let from_local = start
                    .and_hms_opt(hour, 0, 0)
                    .unwrap();
                let to_local = if hour == 23 {
                    start.succ_opt().unwrap().and_hms_opt(0, 0, 0).unwrap()
                } else {
                    start.and_hms_opt(hour + 1, 0, 0).unwrap()
                };
                result.push((
                    tz_naive_to_utc(from_local, tz).timestamp(),
                    tz_naive_to_utc(to_local, tz).timestamp(),
                ));
            }
        }
        "month" => {
            for month in 1..=12u32 {
                let from_local = NaiveDate::from_ymd_opt(start.year(), month, 1)
                    .unwrap()
                    .and_hms_opt(0, 0, 0)
                    .unwrap();
                let next = if month == 12 {
                    NaiveDate::from_ymd_opt(start.year() + 1, 1, 1)
                } else {
                    NaiveDate::from_ymd_opt(start.year(), month + 1, 1)
                };
                result.push((
                    tz_naive_to_utc(from_local, tz).timestamp(),
                    tz_naive_to_utc(next.unwrap().and_hms_opt(0, 0, 0).unwrap(), tz).timestamp(),
                ));
            }
        }
        _ => {
            let mut day = start;
            while day <= end {
                let from_local = day.and_hms_opt(0, 0, 0).unwrap();
                let to_local = day.succ_opt().unwrap().and_hms_opt(0, 0, 0).unwrap();
                result.push((
                    tz_naive_to_utc(from_local, tz).timestamp(),
                    tz_naive_to_utc(to_local, tz).timestamp(),
                ));
                match day.succ_opt() {
                    Some(next) => day = next,
                    None => break,
                }
            }
        }
    }
    result
}

fn bucket_index(windows: &[(i64, i64)], timestamp: i64) -> Option<usize> {
    windows
        .iter()
        .position(|(from, to)| timestamp >= *from && timestamp < *to)
}

// ---------------------------------------------------------------- 数据装载

#[derive(Debug, Clone, sqlx::FromRow)]
struct AppAggRow {
    #[sqlx(rename = "total")]
    total: i64,
    #[sqlx(flatten)]
    app: AppJoinCols,
}

const APP_AGG_SQL: &str = r#"
SELECT h.AppModelID, SUM(h.Time) AS total, {cols}
FROM HoursLogModels h
JOIN AppModels a ON h.AppModelID = a.ID
LEFT JOIN CategoryModels c ON a.CategoryID = c.ID
WHERE h.DataTime >= ? AND h.DataTime < ?
GROUP BY h.AppModelID
"#;

async fn load_app_aggregates(
    pool: &SqlitePool,
    utc_start: DateTime<Utc>,
    utc_end: DateTime<Utc>,
) -> Result<Vec<AppAgg>, AppError> {
    let sql = APP_AGG_SQL.replace("{cols}", crate::models::app::APP_JOIN_COLS_SQL);
    let rows: Vec<AppAggRow> = sqlx::query_as(&sql)
        .bind(utc_start)
        .bind(utc_end)
        .fetch_all(pool)
        .await?;
    Ok(rows
        .into_iter()
        .filter_map(|row| {
            let app = row.app;
            if app.app_id.is_none() {
                return None;
            }
            Some(AppAgg {
                app,
                seconds: row.total,
            })
        })
        .collect())
}

/// 不分时间范围的应用时长（用于「默认浏览器」候选列表）
async fn load_app_aggregates_all(pool: &SqlitePool) -> Result<Vec<AppAgg>, AppError> {
    let rows: Vec<AppAggRow> = sqlx::query_as(
        r#"
        SELECT a.ID AS "AppModelID", a.TotalTime AS total, a.ID AS app_id, a.Name AS app_name,
               a.Alias AS app_alias, a.Description AS app_description, a.File AS app_file,
               a.CategoryID AS app_category_id, a.IconFile AS app_icon_file, a.TotalTime AS app_total_time,
               a.Publisher AS app_publisher,
               c.ID AS cat_id, c.Name AS cat_name, c.IconFile AS cat_icon_file, c.Color AS cat_color,
               c.IsDirectoryMatch AS cat_is_directory_match, c.Directories AS cat_directories, c.IsSystem AS cat_is_system
        FROM AppModels a
        LEFT JOIN CategoryModels c ON a.CategoryID = c.ID
        "#,
    )
    .fetch_all(pool)
    .await?;
    Ok(rows
        .into_iter()
        .filter_map(|row| {
            let app = row.app;
            if app.app_id.is_none() {
                return None;
            }
            Some(AppAgg {
                app,
                seconds: row.total,
            })
        })
        .collect())
}

#[derive(Debug, Clone, sqlx::FromRow)]
struct SessionRow {
    #[sqlx(rename = "AppModelID")]
    app_model_id: i64,
    #[sqlx(rename = "StartTime")]
    start_time: DateTime<Utc>,
    #[sqlx(rename = "EndTime")]
    end_time: DateTime<Utc>,
}

/// 每个应用的会话区间，已按窗口裁剪并按开始时间排序、合并重叠
async fn load_session_intervals(
    pool: &SqlitePool,
    utc_start: DateTime<Utc>,
    utc_end: DateTime<Utc>,
) -> Result<HashMap<i64, Vec<(i64, i64)>>, AppError> {
    let rows: Vec<SessionRow> = sqlx::query_as(
        "SELECT AppModelID, StartTime, EndTime FROM AppSessions WHERE EndTime > ? AND StartTime < ?",
    )
    .bind(utc_start)
    .bind(utc_end)
    .fetch_all(pool)
    .await?;

    let mut by_app: HashMap<i64, Vec<(i64, i64)>> = HashMap::new();
    for row in rows {
        let start = row.start_time.timestamp().max(utc_start.timestamp());
        let end = row.end_time.timestamp().min(utc_end.timestamp());
        if end > start {
            by_app.entry(row.app_model_id).or_default().push((start, end));
        }
    }
    // 会话可能因为崩溃恢复而重叠，先合并再求交，否则同一秒会被算两次
    for intervals in by_app.values_mut() {
        intervals.sort_unstable();
        let mut merged: Vec<(i64, i64)> = Vec::with_capacity(intervals.len());
        for &(start, end) in intervals.iter() {
            match merged.last_mut() {
                Some(last) if start <= last.1 => {
                    if end > last.1 {
                        last.1 = end;
                    }
                }
                _ => merged.push((start, end)),
            }
        }
        *intervals = merged;
    }
    Ok(by_app)
}

#[derive(Debug, Clone)]
struct BrowseLogRow {
    site_id: i64,
    browser: String,
    start: DateTime<Utc>,
    end: DateTime<Utc>,
}

async fn load_browse_logs(
    pool: &SqlitePool,
    utc_start: DateTime<Utc>,
    utc_end: DateTime<Utc>,
) -> Result<Vec<BrowseLogRow>, AppError> {
    #[derive(sqlx::FromRow)]
    struct Row {
        #[sqlx(rename = "SiteId")]
        site_id: i64,
        #[sqlx(rename = "Browser")]
        browser: String,
        #[sqlx(rename = "LogTime")]
        log_time: DateTime<Utc>,
        #[sqlx(rename = "Duration")]
        duration: i64,
    }

    // LogTime 是整点桶：窗口起点可能落在桶中间，往前多取一小时再按区间求交
    let rows: Vec<Row> = sqlx::query_as(
        r#"
        SELECT SiteId, COALESCE(BrowserRef, '') AS Browser, LogTime, Duration
        FROM WebBrowseLogModels
        WHERE LogTime >= ? AND LogTime < ?
        "#,
    )
    .bind(utc_start - Duration::hours(1))
    .bind(utc_end)
    .fetch_all(pool)
    .await?;

    Ok(rows
        .into_iter()
        .filter(|row| row.duration > 0)
        .map(|row| BrowseLogRow {
            site_id: row.site_id,
            browser: row.browser,
            start: row.log_time,
            end: row.log_time + Duration::seconds(row.duration),
        })
        .collect())
}

async fn fill_site_details(
    pool: &SqlitePool,
    sites: &mut HashMap<i64, SiteAgg>,
) -> Result<(), AppError> {
    if sites.is_empty() {
        return Ok(());
    }
    let ids: Vec<i64> = sites.keys().copied().collect();
    let placeholders = vec!["?"; ids.len()].join(",");
    let sql = format!(
        r#"
        SELECT ws.ID AS site_id, ws.Title AS site_title, ws.Domain AS site_domain, ws.Alias AS site_alias,
               ws.CategoryID AS site_category_id, ws.IconFile AS site_icon_file, ws.Duration AS site_duration,
               c.ID AS cat_id, c.Name AS cat_name, c.IconFile AS cat_icon_file, c.Color AS cat_color,
               c.IsUrlMatch AS cat_is_url_match, c.UrlPatterns AS cat_url_patterns, c.IsSystem AS cat_is_system
        FROM WebSiteModels ws
        LEFT JOIN WebSiteCategoryModels c ON ws.CategoryID = c.ID
        WHERE ws.ID IN ({})
        "#,
        placeholders
    );
    let mut query = sqlx::query_as::<_, WebSiteJoinCols>(&sql);
    for id in &ids {
        query = query.bind(*id);
    }
    let rows = query.fetch_all(pool).await?;

    for row in rows {
        let Some(site_id) = row.site_id else { continue };
        let Some(agg) = sites.get_mut(&site_id) else {
            continue;
        };
        agg.domain = row.site_domain.clone().unwrap_or_default();
        agg.title = row.site_title.clone();
        agg.alias = row.site_alias.clone();
        agg.icon_file = row.site_icon_file.clone();
        agg.category_id = row.site_category_id.unwrap_or(0);
        agg.category_name = row.cat_name.clone();
        agg.category_color = row.cat_color.clone();
    }
    Ok(())
}

// ---------------------------------------------------------------- 合并组

const GROUP_ORDER_MAX: usize = 32;

async fn load_groups(pool: &SqlitePool) -> Result<Vec<MergeGroupModel>, AppError> {
    #[derive(sqlx::FromRow)]
    struct GroupRow {
        #[sqlx(rename = "ID")]
        id: i64,
        #[sqlx(rename = "Name")]
        name: String,
        #[sqlx(rename = "Kind")]
        kind: String,
        #[sqlx(rename = "PrimaryRef")]
        primary_ref: String,
        #[sqlx(rename = "IsAuto")]
        is_auto: i64,
    }

    let groups: Vec<GroupRow> =
        sqlx::query_as("SELECT ID, Name, Kind, PrimaryRef, IsAuto FROM EntityMergeGroups ORDER BY ID")
            .fetch_all(pool)
            .await?;

    let members: Vec<(i64, String)> =
        sqlx::query_as("SELECT GroupID, MemberRef FROM EntityMergeMembers ORDER BY GroupID, AddedAt")
            .fetch_all(pool)
            .await?;

    let mut by_group: HashMap<i64, Vec<String>> = HashMap::new();
    for (group_id, member_ref) in members {
        by_group.entry(group_id).or_default().push(member_ref);
    }

    Ok(groups
        .into_iter()
        .map(|row| MergeGroupModel {
            id: row.id,
            name: row.name,
            kind: row.kind,
            is_auto: row.is_auto != 0,
            primary_ref: row.primary_ref,
            members: by_group.remove(&row.id).unwrap_or_default(),
        })
        .collect())
}

async fn load_suppressions(pool: &SqlitePool) -> Result<HashSet<String>, AppError> {
    let rows: Vec<(String,)> = sqlx::query_as("SELECT Ref FROM EntitySuppressions")
        .fetch_all(pool)
        .await?;
    Ok(rows.into_iter().map(|(r,)| r).collect())
}

/// 自动组落库（幂等）。判定用全量应用表：只看本窗口会让「今天没跑的成员」整组消失。
async fn sync_auto_groups(pool: &SqlitePool) -> Result<(), AppError> {
    let all = load_app_aggregates_all(pool).await?;
    let no_excluded: HashSet<i64> = HashSet::new();
    let browser_ids: HashSet<i64> = resolve_browser_apps(&all, &no_excluded)
        .values()
        .copied()
        .collect();

    let candidates: Vec<MergeCandidate> = all
        .iter()
        .filter(|agg| !browser_ids.contains(&agg.app.app_id.unwrap_or(0)))
        .map(|agg| MergeCandidate {
            name: agg.app.app_name.clone().unwrap_or_default(),
            file: agg.app.app_file.clone(),
            publisher: agg.app.app_publisher.clone(),
            seconds: agg.seconds,
        })
        .collect();

    let suppressed = load_suppressions(pool).await?;
    let auto_groups = app_merge::build_groups(&candidates, &suppressed);
    if auto_groups.is_empty() {
        return Ok(());
    }

    let existing_keys: HashSet<String> = {
        let rows: Vec<(String,)> =
            sqlx::query_as("SELECT AutoKey FROM EntityMergeGroups WHERE AutoKey IS NOT NULL")
                .fetch_all(pool)
                .await?;
        rows.into_iter().map(|(key,)| key).collect()
    };

    for group in &auto_groups {
        if existing_keys.contains(&group.key) {
            continue;
        }
        let primary = group
            .members
            .first()
            .cloned()
            .unwrap_or_else(|| group.name.clone());
        let result = sqlx::query(
            "INSERT INTO EntityMergeGroups (Name, Kind, AutoKey, PrimaryRef, IsAuto, UpdatedAt) VALUES (?, 'app', ?, ?, 1, datetime('now'))",
        )
        .bind(&group.name)
        .bind(&group.key)
        .bind(&primary)
        .execute(pool)
        .await?;
        let group_id = result.last_insert_rowid();
        for member in group.members.iter().take(GROUP_ORDER_MAX) {
            sqlx::query(
                "INSERT OR IGNORE INTO EntityMergeMembers (GroupID, MemberRef, AddedAt) VALUES (?, ?, datetime('now'))",
            )
            .bind(group_id)
            .bind(member)
            .execute(pool)
            .await?;
        }
    }

    Ok(())
}

/// 把本窗口的应用行按持久化的组合并：组里保留一行（母成员，或母成员不在时的最长可见成员），
/// 组时长 = 本窗口可见成员之和。
async fn apply_merge_groups(
    pool: &SqlitePool,
    apps: &[AppAgg],
    excluded: &HashSet<i64>,
) -> Result<Vec<(AppAgg, Option<MergeGroupModel>)>, AppError> {
    let groups = load_groups(pool).await?;
    if groups.is_empty() {
        return Ok(apps
            .iter()
            .filter(|agg| !excluded.contains(&agg.app.app_id.unwrap_or(0)))
            .map(|agg| (agg.clone(), None))
            .collect());
    }

    let visible: HashSet<String> = apps
        .iter()
        .map(|agg| agg.app.app_name.clone().unwrap_or_default())
        .collect();
    let seconds_of = |name: &str| -> i64 {
        apps.iter()
            .find(|agg| agg.app.app_name.as_deref() == Some(name))
            .map(|agg| agg.seconds)
            .unwrap_or(0)
    };

    let mut member_to_group: HashMap<String, i64> = HashMap::new();
    let mut keep: HashMap<i64, String> = HashMap::new();
    for group in groups.iter().filter(|group| group.kind == "app") {
        for member in &group.members {
            member_to_group.insert(member.clone(), group.id);
        }
        let chosen = if visible.contains(&group.primary_ref) {
            Some(group.primary_ref.clone())
        } else {
            group
                .members
                .iter()
                .filter(|member| visible.contains(*member))
                .max_by_key(|member| seconds_of(member.as_str()))
                .cloned()
        };
        if let Some(chosen) = chosen {
            keep.insert(group.id, chosen);
        }
    }

    let mut result: Vec<(AppAgg, Option<MergeGroupModel>)> = Vec::new();
    for agg in apps {
        let app_id = agg.app.app_id.unwrap_or(0);
        if excluded.contains(&app_id) {
            continue;
        }
        let name = agg.app.app_name.clone().unwrap_or_default();
        match member_to_group.get(&name) {
            Some(group_id) => {
                if keep.get(group_id) == Some(&name) {
                    let group = groups.iter().find(|group| group.id == *group_id).cloned();
                    result.push((agg.clone(), group));
                }
            }
            None => result.push((agg.clone(), None)),
        }
    }

    // 组时长 = 本窗口内所有可见成员之和（母成员没跑的日子，子成员的时长也不能丢）
    for (agg, group) in result.iter_mut() {
        let Some(group) = group else { continue };
        let total: i64 = group
            .members
            .iter()
            .filter_map(|member| {
                apps.iter()
                    .find(|candidate| candidate.app.app_name.as_deref() == Some(member.as_str()))
                    .map(|candidate| candidate.seconds)
            })
            .sum();
        agg.seconds = total.max(agg.seconds);
    }

    Ok(result)
}

/// 默认浏览器的自动推荐：浏览日志里出现最多的标识 → 只有一条就归它 → 都没就只有唯一识别到的浏览器
async fn recommend_default_browser(
    pool: &SqlitePool,
    browser_apps: &HashMap<String, i64>,
) -> Result<String, AppError> {
    let row: Option<(String,)> = sqlx::query_as(
        r#"
        SELECT BrowserRef FROM WebBrowseLogModels
        WHERE BrowserRef IS NOT NULL AND BrowserRef <> ''
        GROUP BY BrowserRef
        ORDER BY COUNT(*) DESC
        LIMIT 1
        "#,
    )
    .fetch_optional(pool)
    .await?;

    if let Some((reference,)) = row {
        return Ok(reference.trim().to_lowercase());
    }

    if browser_apps.len() == 1 {
        if let Some((reference, _)) = browser_apps.iter().next() {
            return Ok(reference.clone());
        }
    }

    Ok(String::new())
}

// ---------------------------------------------------------------- 纯函数

fn clip_interval(
    start: DateTime<Utc>,
    end: DateTime<Utc>,
    window_start: i64,
    window_end: i64,
) -> i64 {
    let start = start.timestamp().max(window_start);
    let end = end.timestamp().min(window_end);
    (end - start).max(0)
}

/// 一个区间与一串（已排序、可重叠的）会话区间的交集长度
fn overlap_seconds(intervals: &[(i64, i64)], start: i64, end: i64) -> i64 {
    let mut total = 0;
    for &(s, e) in intervals {
        if e <= start {
            continue;
        }
        if s >= end {
            break;
        }
        let from = s.max(start);
        let to = e.min(end);
        if to > from {
            total += to - from;
        }
    }
    total
}

fn names_match(agg: &AppAgg, name: &str) -> bool {
    agg.app.app_name.as_deref() == Some(name)
}

fn display_name(app: &AppJoinCols) -> String {
    app.app_alias
        .as_deref()
        .filter(|v| !v.trim().is_empty())
        .or_else(|| app.app_description.as_deref().filter(|v| !v.trim().is_empty()))
        .or_else(|| app.app_name.as_deref())
        .unwrap_or("")
        .to_string()
}

fn site_display_name(agg: &SiteAgg) -> String {
    agg.alias
        .as_deref()
        .filter(|v| !v.trim().is_empty())
        .or_else(|| agg.title.as_deref().filter(|v| !v.trim().is_empty()))
        .unwrap_or(&agg.domain)
        .to_string()
}

fn other_row_name(app: &AppJoinCols, browser_count: usize) -> String {
    let name = display_name(app);
    if browser_count > 1 {
        format!("{} · 其他页面", name)
    } else {
        "浏览器内其他页面".to_string()
    }
}

/// 浏览器标识 → 应用 ID。同一标识族里取时长最长的进程（Chrome 本体优先于渲染器）
fn resolve_browser_apps(
    apps: &[AppAgg],
    excluded: &HashSet<i64>,
) -> HashMap<String, i64> {
    let mut resolved: HashMap<String, (i64, i64)> = HashMap::new();
    for (reference, family) in BROWSER_FAMILIES {
        for agg in apps {
            let app_id = match agg.app.app_id {
                Some(id) => id,
                None => continue,
            };
            if excluded.contains(&app_id) {
                continue;
            }
            let name = agg.app.app_name.clone().unwrap_or_default().to_lowercase();
            if name.is_empty() {
                continue;
            }
            if !family.iter().any(|stem| name.contains(stem)) {
                continue;
            }
            let entry = resolved.entry(reference.to_string()).or_insert((app_id, agg.seconds));
            if agg.seconds > entry.1 {
                *entry = (app_id, agg.seconds);
            }
        }
    }
    resolved
        .into_iter()
        .map(|(reference, (app_id, _))| (reference, app_id))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::TimeZone;

    fn ts(value: i64) -> DateTime<Utc> {
        Utc.timestamp_opt(value, 0).unwrap()
    }

    #[test]
    fn clip_respects_window() {
        assert_eq!(clip_interval(ts(10), ts(20), 0, 100), 10);
        assert_eq!(clip_interval(ts(10), ts(20), 15, 100), 5);
        assert_eq!(clip_interval(ts(10), ts(20), 0, 15), 5);
        assert_eq!(clip_interval(ts(30), ts(20), 0, 100), 0);
    }

    #[test]
    fn overlap_sums_only_intersections() {
        let intervals = vec![(0, 60), (120, 180)];
        assert_eq!(overlap_seconds(&intervals, 30, 150), 30 + 30);
        assert_eq!(overlap_seconds(&intervals, 60, 120), 0);
        assert_eq!(overlap_seconds(&intervals, -10, 200), 120);
        assert_eq!(overlap_seconds(&[], 0, 100), 0);
    }

    #[test]
    fn overlap_handles_overlapping_sessions() {
        // 重叠会话由 load_session_intervals 合并，这里只处理已合并区间
        let merged = vec![(0, 150)];
        assert_eq!(overlap_seconds(&merged, 0, 150), 150);
    }

    /// 集成测试：在内存库上跑真实迁移 + 真实 SQL。
    /// 纯函数测不出「列名写错 / 迁移没建表」这类错，只有真的查一次才知道。
    async fn test_pool() -> SqlitePool {
        let pool = sqlx::sqlite::SqlitePoolOptions::new()
            .max_connections(1)
            .connect("sqlite::memory:")
            .await
            .expect("open in-memory sqlite");
        crate::migrations::run(&pool, "Asia/Shanghai")
            .await
            .expect("run migrations");
        pool
    }

    fn test_config_dir(tag: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("shijian-entity-test-{}-{}", tag, std::process::id()));
        std::fs::create_dir_all(&dir).expect("create temp dir");
        dir
    }

    /// 造一份最小数据：chrome 一小时，其中半小时是 example.com，另半小时是「其他页面」
    async fn seed(pool: &SqlitePool) -> (i64, i64) {
        let category_id: Option<(i64,)> = sqlx::query_as("SELECT ID FROM CategoryModels LIMIT 1")
            .fetch_optional(pool)
            .await
            .unwrap();
        let category_id = match category_id {
            Some((id,)) => (id,),
            None => {
                sqlx::query("INSERT INTO CategoryModels (Name, IconFile, Color, IsDirectoryMatch, Directories, IsSystem) VALUES ('未分类', NULL, '#e4e3df', 0, NULL, 1)")
                    .execute(pool)
                    .await
                    .unwrap();
                sqlx::query_as("SELECT ID FROM CategoryModels LIMIT 1")
                    .fetch_one(pool)
                    .await
                    .unwrap()
            }
        };
        let app = sqlx::query(
            r#"INSERT INTO AppModels (Name, Alias, CategoryID, TotalTime, File, IconFile, Description, Publisher)
               VALUES ('chrome', NULL, ?, 3600, 'C:\Program Files\Google\Chrome\chrome.exe', NULL, 'Google Chrome', NULL)"#,
        )
        .bind(category_id.0)
        .execute(pool)
        .await
        .unwrap();
        let app_id = app.last_insert_rowid();

        sqlx::query(
            "INSERT INTO HoursLogModels (DataTime, AppModelID, Time) VALUES ('2026-11-12T04:00:00+00:00', ?, 3600)",
        )
        .bind(app_id)
        .execute(pool)
        .await
        .unwrap();

        // 会话 04:00–05:00（整点，正好覆盖浏览日志所在的小时桶）
        sqlx::query(
            "INSERT INTO AppSessions (AppModelID, StartTime, EndTime, Duration) VALUES (?, '2026-11-12T04:00:00+00:00', '2026-11-12T05:00:00+00:00', 3600)",
        )
        .bind(app_id)
        .execute(pool)
        .await
        .unwrap();

        let site = sqlx::query(
            "INSERT INTO WebSiteModels (Title, Domain, Alias, CategoryID, IconFile, Duration) VALUES ('Example', 'example.com', NULL, 0, NULL, 1800)",
        )
        .execute(pool)
        .await
        .unwrap();
        let site_id = site.last_insert_rowid();

        let url = sqlx::query("INSERT INTO WebUrlModels (Title, Url, IconFile) VALUES ('Example', 'https://example.com/', NULL)")
            .execute(pool)
            .await
            .unwrap();
        let url_id = url.last_insert_rowid();

        sqlx::query(
            "INSERT INTO WebBrowseLogModels (UrlId, LogTime, Duration, SiteId, BrowserRef) VALUES (?, '2026-11-12T04:00:00+00:00', 1800, ?, 'chrome')",
        )
        .bind(url_id)
        .bind(site_id)
        .execute(pool)
        .await
        .unwrap();

        (app_id, site_id)
    }

    #[tokio::test]
    async fn list_splits_browser_into_balance_and_sites() {
        let pool = test_pool().await;
        let (_app_id, _site_id) = seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("off"));

        let list = EntityService::list(
            &pool,
            &config,
            chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap(),
            chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap(),
            "Asia/Shanghai",
            false,
            None,
        )
        .await
        .expect("entity list");

        assert_eq!(list.app_seconds, 3600);
        assert_eq!(list.site_seconds, 1800);
        assert_eq!(list.balance_seconds, 1800);
        assert_eq!(list.total_seconds, 3600);
        assert_eq!(list.overflow_seconds, 0);

        let other = list.rows.iter().find(|row| row.kind == "other").expect("balance row");
        assert_eq!(other.seconds, 1800);
        let site = list.rows.iter().find(|row| row.kind == "site").expect("site row");
        assert_eq!(site.seconds, 1800);
        // 关闭合并时浏览器本身不占行，它的时间全在站点行与差额行里
        assert!(!list.rows.iter().any(|row| row.kind == "app"));
        assert_eq!(list.rows.iter().map(|row| row.seconds).sum::<i64>(), 3600);
    }

    #[tokio::test]
    async fn hours_matrix_subtracts_sites_from_browser() {
        let pool = test_pool().await;
        let (_app_id, _site_id) = seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("hours"));
        let day = chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap();

        let model = EntityService::hours(&pool, &config, day, day, "Asia/Shanghai", "hour", false)
            .await
            .expect("entity hours");

        assert_eq!(model.bucket_count, 24);
        assert_eq!(model.labels.len(), 24);
        // 04:00 UTC = 12:00 本地，站点半小时、浏览器余额半小时
        let site = model.rows.iter().find(|row| row.kind == "site").expect("site row");
        assert_eq!(site.values[12], 1800);
        let other = model.rows.iter().find(|row| row.kind == "other").expect("balance row");
        assert_eq!(other.values[12], 1800);
        assert_eq!(
            model.rows.iter().flat_map(|row| row.values.iter()).sum::<i64>(),
            3600
        );
    }

    #[tokio::test]
    async fn hours_can_fold_sites_into_browser() {
        let pool = test_pool().await;
        seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("hours-merged"));
        let day = chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap();

        let model = EntityService::hours(&pool, &config, day, day, "Asia/Shanghai", "hour", true)
            .await
            .expect("entity hours");

        assert!(!model.rows.iter().any(|row| row.kind == "site"));
        let browser = model.rows.iter().find(|row| row.is_browser).expect("browser row");
        assert_eq!(browser.values[12], 3600);
    }

    #[tokio::test]
    async fn list_take_folds_rest_into_aggregate_row() {
        let pool = test_pool().await;
        seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("take"));
        let day = chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap();

        let list = EntityService::list(&pool, &config, day, day, "Asia/Shanghai", false, Some(1))
            .await
            .expect("entity list");

        assert_eq!(list.rows.len(), 1);
        let row = &list.rows[0];
        assert_eq!(row.kind, "aggregate");
        assert_eq!(row.seconds, 3600);
    }

    #[tokio::test]
    async fn list_merges_sites_into_browser() {
        let pool = test_pool().await;
        let (_app_id, _site_id) = seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("on"));

        let list = EntityService::list(
            &pool,
            &config,
            chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap(),
            chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap(),
            "Asia/Shanghai",
            true,
            None,
        )
        .await
        .expect("entity list");

        assert_eq!(list.total_seconds, 3600);
        assert!(!list.rows.iter().any(|row| row.kind == "site"));

        let browser = list
            .rows
            .iter()
            .find(|row| row.is_browser)
            .expect("browser row");
        assert_eq!(browser.seconds, 3600);
        assert_eq!(browser.members.len(), 1);
        assert_eq!(browser.members[0].seconds, 1800);
    }

    #[tokio::test]
    async fn auto_groups_are_persisted_and_dissolving_sticks() {
        let pool = test_pool().await;
        let (app_id, _site_id) = seed(&pool).await;
        let config = ConfigService::new(&test_config_dir("group"));

        // 再插一个同目录、同品牌（PiDeck）的变体，凑成一个自动组。
        // CategoryID 必须真实存在：AppModels 的外键在迁移里是开的
        let category: (i64,) = sqlx::query_as("SELECT ID FROM CategoryModels LIMIT 1")
            .fetch_one(&pool)
            .await
            .unwrap();
        let setup = sqlx::query(
            r#"INSERT INTO AppModels (Name, Alias, CategoryID, TotalTime, File, IconFile, Description, Publisher)
               VALUES ('PiDeck-0.9.5-setup', NULL, ?, 600, 'C:\Program Files\PiDeck\PiDeck-0.9.5-setup.exe', NULL, 'PiDeck', NULL)"#,
        )
        .bind(category.0)
        .execute(&pool)
        .await
        .unwrap()
        .last_insert_rowid();
        let main = sqlx::query(
            r#"INSERT INTO AppModels (Name, Alias, CategoryID, TotalTime, File, IconFile, Description, Publisher)
               VALUES ('PiDeck', NULL, ?, 900, 'C:\Program Files\PiDeck\PiDeck.exe', NULL, 'PiDeck', NULL)"#,
        )
        .bind(category.0)
        .execute(&pool)
        .await
        .unwrap()
        .last_insert_rowid();

        for (id, seconds) in [(main, 900_i64), (setup, 600_i64)] {
            sqlx::query(
                "INSERT INTO HoursLogModels (DataTime, AppModelID, Time) VALUES ('2026-11-12T04:00:00+00:00', ?, ?)",
            )
            .bind(id)
            .bind(seconds)
            .execute(&pool)
            .await
            .unwrap();
        }

        let day = chrono::NaiveDate::from_ymd_opt(2026, 11, 12).unwrap();
        let list = EntityService::list(&pool, &config, day, day, "Asia/Shanghai", false, None)
            .await
            .expect("entity list");
        let group_row = list
            .rows
            .iter()
            .find(|row| row.name == "PiDeck")
            .expect("merged group row");
        assert_eq!(group_row.seconds, 1500);
        assert_eq!(group_row.members.len(), 1);

        let groups = EntityService::merge_groups(&pool).await.expect("groups");
        let group = groups
            .groups
            .iter()
            .find(|group| group.name == "PiDeck")
            .expect("persisted group");
        assert!(group.is_auto);

        EntityService::dissolve_group(&pool, group.id)
            .await
            .expect("dissolve");

        let list = EntityService::list(&pool, &config, day, day, "Asia/Shanghai", false, None)
            .await
            .expect("entity list after dissolve");
        assert!(!list.rows.iter().any(|row| row.name == "PiDeck" && !row.members.is_empty()));

        let groups = EntityService::merge_groups(&pool).await.expect("groups");
        assert!(groups.groups.is_empty());
        assert_eq!(groups.suppressed.len(), 1);
        let _ = app_id;
    }

    #[test]
    fn other_row_name_mentions_browser_when_ambiguous() {
        let app = AppJoinCols {
            app_id: Some(1),
            app_name: Some("chrome".to_string()),
            app_alias: None,
            app_description: Some("Google Chrome".to_string()),
            app_file: None,
            app_category_id: Some(0),
            app_icon_file: None,
            app_total_time: Some(0),
            app_publisher: None,
            cat_id: None,
            cat_name: None,
            cat_icon_file: None,
            cat_color: None,
            cat_is_directory_match: None,
            cat_directories: None,
            cat_is_system: None,
        };
        assert_eq!(other_row_name(&app, 1), "浏览器内其他页面");
        assert_eq!(other_row_name(&app, 3), "Google Chrome · 其他页面");
    }
}
