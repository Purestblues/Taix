use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use sqlx::sqlite::{SqliteConnectOptions, SqlitePoolOptions};
use sqlx::{Row, SqlitePool};
use tracing::{info, warn};

use crate::error::AppError;
use crate::migrations::{self, MIGRATION_STEPS};
use crate::services::backup_zip::{build_zip, read_zip, ZipEntryData};

static TEMP_SEQ: AtomicU64 = AtomicU64::new(1);

pub const BACKUP_FORMAT_VERSION: u32 = 1;
const MAX_ROLLBACK_FILES: usize = 10;
const REQUIRED_TABLES: [&str; 8] = [
    "CategoryModels",
    "AppModels",
    "DailyLogModels",
    "HoursLogModels",
    "WebSiteModels",
    "WebUrlModels",
    "WebBrowseLogModels",
    "_migrations",
];

#[derive(Debug, Clone)]
pub struct BackupPaths {
    pub data_dir: PathBuf,
    pub web_favicons_dir: PathBuf,
    pub app_icons_dirs: Vec<PathBuf>,
    pub device_name: String,
}

impl BackupPaths {
    pub fn from_env(data_dir: &Path, web_favicons_dir: &Path, exe_dir: &Path) -> Self {
        let mut app_icons_dirs = vec![data_dir.join("AppIcons"), exe_dir.join("Data").join("AppIcons")];
        if let Ok(cwd) = std::env::current_dir() {
            app_icons_dirs.push(cwd.join("Data").join("AppIcons"));
        }
        Self {
            data_dir: data_dir.to_path_buf(),
            web_favicons_dir: web_favicons_dir.to_path_buf(),
            app_icons_dirs,
            device_name: detect_device_name(),
        }
    }

    pub fn backups_dir(&self) -> PathBuf {
        self.data_dir.join("backups")
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ImportMode {
    Merge,
    Replace,
}

impl ImportMode {
    pub fn parse(raw: Option<&str>) -> Result<Self, AppError> {
        match raw.unwrap_or("merge").trim().to_ascii_lowercase().as_str() {
            "" | "merge" => Ok(Self::Merge),
            "replace" | "overwrite" => Ok(Self::Replace),
            other => Err(AppError::Business(format!(
                "不支持的导入模式 '{}'，可选值：merge / replace",
                other
            ))),
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct BackupCounts {
    pub apps: i64,
    pub categories: i64,
    pub daily_logs: i64,
    pub hours_logs: i64,
    pub app_sessions: i64,
    pub web_sites: i64,
    pub web_urls: i64,
    pub web_browse_logs: i64,
    pub merge_groups: i64,
    pub plugin_records: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BackupManifest {
    pub format_version: u32,
    pub app_name: String,
    pub app_version: String,
    pub created_at_utc: String,
    pub device_name: String,
    pub schema_version: usize,
    pub migration_count: usize,
    pub applied_migrations: Vec<String>,
    pub db_file_name: String,
    pub db_sha256: String,
    pub db_size_bytes: u64,
    pub includes_icons: bool,
    pub icon_count: usize,
    pub counts: BackupCounts,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BackupInspectResult {
    pub valid: bool,
    pub compatible: bool,
    pub compatibility_note: String,
    pub sha256_verified: bool,
    pub sqlite_integrity_ok: bool,
    pub manifest: BackupManifest,
    pub current_schema_version: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BackupImportResult {
    pub mode: ImportMode,
    pub rollback_file: String,
    pub imported_schema_version: usize,
    pub migrated_from_older: bool,
    pub restored_icons: usize,
    pub counts: BackupCounts,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RollbackItem {
    pub file_name: String,
    pub size_bytes: u64,
    pub modified_at_utc: String,
}

struct TempFileGuard {
    path: PathBuf,
}

impl TempFileGuard {
    fn new(prefix: &str, ext: &str) -> Self {
        let seq = TEMP_SEQ.fetch_add(1, Ordering::Relaxed);
        let pid = std::process::id();
        let name = format!("shijian_{}_{}_{}.{}", prefix, pid, seq, ext);
        Self {
            path: std::env::temp_dir().join(name),
        }
    }
}

impl Drop for TempFileGuard {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

pub fn detect_device_name() -> String {
    for key in ["COMPUTERNAME", "HOSTNAME"] {
        if let Ok(v) = std::env::var(key) {
            let trimmed = v.trim();
            if !trimmed.is_empty() {
                return trimmed.to_string();
            }
        }
    }
    "desktop".to_string()
}

pub struct BackupService;

impl BackupService {
    /// 生成 `.shijian-backup` 一致性快照归档（`VACUUM INTO` + `manifest.json` + SHA-256 + 可选图标）。
    pub async fn export_archive(
        pool: &SqlitePool,
        paths: &BackupPaths,
        app_version: &str,
        include_icons: bool,
    ) -> Result<(Vec<u8>, BackupManifest), AppError> {
        let temp_guard = TempFileGuard::new("export_snap", "db");
        vacuum_into(pool, &temp_guard.path).await?;

        let db_bytes = tokio::fs::read(&temp_guard.path).await?;
        let db_sha256 = sha256_hex(&db_bytes);
        let db_size_bytes = db_bytes.len() as u64;

        let applied_migrations = query_applied_migrations(pool).await?;
        let counts = query_counts(pool).await?;

        let mut entries = vec![ZipEntryData {
            name: "data.db".to_string(),
            data: db_bytes,
        }];

        let mut icon_count = 0usize;
        if include_icons {
            icon_count += collect_icon_entries(&paths.web_favicons_dir, "WebFavicons", &mut entries);
            let mut seen_app_icons = std::collections::HashSet::new();
            for dir in &paths.app_icons_dirs {
                if dir.is_dir() {
                    icon_count += collect_icon_entries_dedup(dir, "AppIcons", &mut entries, &mut seen_app_icons);
                    if !seen_app_icons.is_empty() {
                        break;
                    }
                }
            }
        }

        let manifest = BackupManifest {
            format_version: BACKUP_FORMAT_VERSION,
            app_name: "ShiJian".to_string(),
            app_version: if app_version.trim().is_empty() {
                env!("CARGO_PKG_VERSION").to_string()
            } else {
                app_version.trim().to_string()
            },
            created_at_utc: chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true),
            device_name: paths.device_name.clone(),
            schema_version: applied_migrations.len(),
            migration_count: applied_migrations.len(),
            applied_migrations,
            db_file_name: "data.db".to_string(),
            db_sha256,
            db_size_bytes,
            includes_icons: include_icons && icon_count > 0,
            icon_count,
            counts,
        };

        let manifest_bytes = serde_json::to_vec_pretty(&manifest)
            .map_err(|e| AppError::Internal(format!("序列化 manifest.json 失败: {}", e)))?;
        entries.insert(
            0,
            ZipEntryData {
                name: "manifest.json".to_string(),
                data: manifest_bytes,
            },
        );

        let zip_bytes = build_zip(&entries).map_err(AppError::Internal)?;
        info!(
            bytes = zip_bytes.len(),
            db_bytes = db_size_bytes,
            icons = icon_count,
            "已导出 .shijian-backup 一致性快照"
        );
        Ok((zip_bytes, manifest))
    }

    /// 校验并解析 `.shijian-backup` 归档包（不修改当前数据库）。
    pub async fn inspect_archive(archive_bytes: &[u8]) -> Result<BackupInspectResult, AppError> {
        let entries = read_zip(archive_bytes).map_err(AppError::Business)?;

        let manifest_bytes = entries
            .get("manifest.json")
            .ok_or_else(|| AppError::Business("备份包缺少 manifest.json".to_string()))?;
        let mut manifest: BackupManifest = serde_json::from_slice(manifest_bytes)
            .map_err(|e| AppError::Business(format!("manifest.json 解析失败: {}", e)))?;

        if manifest.format_version != BACKUP_FORMAT_VERSION {
            return Err(AppError::Business(format!(
                "不支持的备份包格式版本: {}（当前支持版本 {}）",
                manifest.format_version, BACKUP_FORMAT_VERSION
            )));
        }
        if manifest.app_name != "ShiJian" {
            return Err(AppError::Business(format!(
                "非时见备份文件（appName = {}）",
                manifest.app_name
            )));
        }

        let db_entry_name = if manifest.db_file_name.trim().is_empty() {
            "data.db"
        } else {
            manifest.db_file_name.trim()
        };
        let db_bytes = entries
            .get(db_entry_name)
            .ok_or_else(|| AppError::Business(format!("备份包缺少数据库文件 {}", db_entry_name)))?;

        let actual_sha256 = sha256_hex(db_bytes);
        if !actual_sha256.eq_ignore_ascii_case(manifest.db_sha256.trim()) {
            return Err(AppError::Business(
                "备份数据库 SHA-256 校验失败，文件可能已损坏或被篡改".to_string(),
            ));
        }

        let temp_guard = TempFileGuard::new("inspect_snap", "db");
        tokio::fs::write(&temp_guard.path, db_bytes).await?;

        let opts = SqliteConnectOptions::new()
            .filename(&temp_guard.path)
            .read_only(true);
        let snap_pool = SqlitePoolOptions::new()
            .max_connections(1)
            .connect_with(opts)
            .await
            .map_err(|e| AppError::Business(format!("备份数据库无法打开: {}", e)))?;

        let integrity: String = sqlx::query_scalar("PRAGMA integrity_check")
            .fetch_one(&snap_pool)
            .await
            .unwrap_or_else(|_| "error".to_string());
        if !integrity.eq_ignore_ascii_case("ok") {
            snap_pool.close().await;
            return Err(AppError::Business(format!(
                "备份数据库完整性检查失败: {}",
                integrity
            )));
        }

        for table in REQUIRED_TABLES {
            let exists: Option<i64> = sqlx::query_scalar(
                "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ?",
            )
            .bind(table)
            .fetch_optional(&snap_pool)
            .await?;
            if exists.is_none() {
                snap_pool.close().await;
                return Err(AppError::Business(format!(
                    "备份数据库缺少核心表 {}",
                    table
                )));
            }
        }

        let actual_migrations = query_applied_migrations(&snap_pool).await?;
        let actual_counts = query_counts(&snap_pool).await?;
        snap_pool.close().await;

        manifest.schema_version = actual_migrations.len();
        manifest.migration_count = actual_migrations.len();
        manifest.applied_migrations = actual_migrations;
        manifest.counts = actual_counts;

        let current_schema = MIGRATION_STEPS.len();
        let (compatible, compatibility_note) = if manifest.schema_version > current_schema {
            (
                false,
                format!(
                    "备份数据结构版本（schema v{}）高于当前应用（v{}），请先升级时见客户端后再导入",
                    manifest.schema_version, current_schema
                ),
            )
        } else if manifest.schema_version < current_schema {
            (
                true,
                format!(
                    "备份来自较早数据结构（schema v{} → v{}），导入时将自动平滑升级",
                    manifest.schema_version, current_schema
                ),
            )
        } else {
            (
                true,
                format!("校验通过（schema v{}，SHA-256 完整一致）", current_schema),
            )
        };

        Ok(BackupInspectResult {
            valid: true,
            compatible,
            compatibility_note,
            sha256_verified: true,
            sqlite_integrity_ok: true,
            manifest,
            current_schema_version: current_schema,
        })
    }

    /// 导入 `.shijian-backup`：先校验、自动创建回滚备份、平滑迁移 staging 库，再在单事务内合并或替换。
    pub async fn import_archive(
        pool: &SqlitePool,
        paths: &BackupPaths,
        archive_bytes: &[u8],
        mode: ImportMode,
        tz_id: &str,
    ) -> Result<BackupImportResult, AppError> {
        let inspected = Self::inspect_archive(archive_bytes).await?;
        if !inspected.compatible {
            return Err(AppError::Business(inspected.compatibility_note));
        }

        // 1. 导入前自动创建当前库一致性回滚备份
        let rollback_path = create_rollback_snapshot(pool, &paths.backups_dir(), "pre_import").await?;
        let rollback_file = rollback_path
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or_default()
            .to_string();

        // 2. 解包并将 staging db 升至当前 schema
        let entries = read_zip(archive_bytes).map_err(AppError::Business)?;
        let db_entry_name = if inspected.manifest.db_file_name.trim().is_empty() {
            "data.db"
        } else {
            inspected.manifest.db_file_name.trim()
        };
        let db_bytes = entries
            .get(db_entry_name)
            .ok_or_else(|| AppError::Business("备份包缺少数据库文件".to_string()))?;

        let temp_guard = TempFileGuard::new("import_stage", "db");
        tokio::fs::write(&temp_guard.path, db_bytes).await?;

        let stage_opts = SqliteConnectOptions::new()
            .filename(&temp_guard.path)
            .create_if_missing(false);
        let stage_pool = SqlitePoolOptions::new()
            .max_connections(1)
            .connect_with(stage_opts)
            .await
            .map_err(|e| AppError::Business(format!("无法打开临时导入库: {}", e)))?;

        let migrated_from_older = inspected.manifest.schema_version < MIGRATION_STEPS.len();
        if migrated_from_older {
            migrations::run(&stage_pool, tz_id)
                .await
                .map_err(|e| AppError::Business(format!("旧版本备份结构升级失败: {}", e)))?;
        }

        let import_res = match mode {
            ImportMode::Merge => merge_from_stage(pool, &stage_pool).await,
            ImportMode::Replace => replace_from_stage(pool, &stage_pool).await,
        };
        stage_pool.close().await;
        import_res?;

        // 3. 恢复归档中的图标资源（缺失时补齐）
        let restored_icons = restore_icons_from_entries(&entries, paths);

        // 4. 刷新服务端内存缓存
        crate::services::category::invalidate_category_cache().await;
        if let Err(e) = crate::services::web_data::WebDataService::warmup_url_match_cache(pool).await {
            warn!("导入后重建 URL 匹配缓存失败: {}", e);
        }

        let final_counts = query_counts(pool).await?;
        info!(
            mode = ?mode,
            rollback_file = %rollback_file,
            restored_icons = restored_icons,
            "备份导入完成"
        );

        Ok(BackupImportResult {
            mode,
            rollback_file,
            imported_schema_version: inspected.manifest.schema_version,
            migrated_from_older,
            restored_icons,
            counts: final_counts,
        })
    }

    pub async fn list_rollbacks(paths: &BackupPaths) -> Result<Vec<RollbackItem>, AppError> {
        let dir = paths.backups_dir();
        if !dir.is_dir() {
            return Ok(Vec::new());
        }
        let mut items = Vec::new();
        let mut rd = tokio::fs::read_dir(&dir).await?;
        while let Some(entry) = rd.next_entry().await? {
            let path = entry.path();
            if !path.is_file() {
                continue;
            }
            let Some(name) = path.file_name().and_then(|n| n.to_str()) else {
                continue;
            };
            if !name.ends_with(".db") {
                continue;
            }
            let meta = entry.metadata().await?;
            let modified = meta
                .modified()
                .ok()
                .map(|t| chrono::DateTime::<chrono::Utc>::from(t).to_rfc3339_opts(chrono::SecondsFormat::Secs, true))
                .unwrap_or_default();
            items.push(RollbackItem {
                file_name: name.to_string(),
                size_bytes: meta.len(),
                modified_at_utc: modified,
            });
        }
        items.sort_by(|a, b| b.modified_at_utc.cmp(&a.modified_at_utc));
        Ok(items)
    }

    pub async fn rollback_from_file(
        pool: &SqlitePool,
        paths: &BackupPaths,
        file_name: &str,
        tz_id: &str,
    ) -> Result<BackupImportResult, AppError> {
        let safe_name = Path::new(file_name)
            .file_name()
            .and_then(|n| n.to_str())
            .filter(|n| !n.is_empty() && n.ends_with(".db") && *n == file_name)
            .ok_or_else(|| AppError::Business("非法的回滚文件名".to_string()))?;

        let target = paths.backups_dir().join(safe_name);
        if !target.is_file() {
            return Err(AppError::Business(format!("回滚文件不存在: {}", safe_name)));
        }

        let safety_rollback = create_rollback_snapshot(pool, &paths.backups_dir(), "pre_rollback").await?;
        let rollback_file = safety_rollback
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or_default()
            .to_string();

        let opts = SqliteConnectOptions::new().filename(&target).read_only(false);
        let stage_pool = SqlitePoolOptions::new()
            .max_connections(1)
            .connect_with(opts)
            .await
            .map_err(|e| AppError::Business(format!("无法打开回滚数据库: {}", e)))?;

        migrations::run(&stage_pool, tz_id)
            .await
            .map_err(|e| AppError::Business(format!("回滚库迁移失败: {}", e)))?;

        let res = replace_from_stage(pool, &stage_pool).await;
        stage_pool.close().await;
        res?;

        crate::services::category::invalidate_category_cache().await;
        let _ = crate::services::web_data::WebDataService::warmup_url_match_cache(pool).await;
        let counts = query_counts(pool).await?;

        Ok(BackupImportResult {
            mode: ImportMode::Replace,
            rollback_file,
            imported_schema_version: MIGRATION_STEPS.len(),
            migrated_from_older: false,
            restored_icons: 0,
            counts,
        })
    }
}

async fn vacuum_into(pool: &SqlitePool, dest: &Path) -> Result<(), AppError> {
    if dest.exists() {
        let _ = tokio::fs::remove_file(dest).await;
    }
    if let Some(parent) = dest.parent() {
        tokio::fs::create_dir_all(parent).await?;
    }

    let _ = sqlx::query("PRAGMA wal_checkpoint(PASSIVE)")
        .execute(pool)
        .await;

    let path_str = dest
        .to_str()
        .ok_or_else(|| AppError::Internal("临时快照路径包含非 UTF-8 字符".to_string()))?;
    let escaped = path_str.replace('\'', "''");
    let sql = format!("VACUUM INTO '{}'", escaped);
    sqlx::query(&sql)
        .execute(pool)
        .await
        .map_err(|e| AppError::Internal(format!("生成 SQLite 一致性快照失败: {}", e)))?;
    if !dest.exists() {
        return Err(AppError::Internal(format!("VACUUM INTO 执行后未生成文件: {}", sql)));
    }
    Ok(())
}

async fn create_rollback_snapshot(
    pool: &SqlitePool,
    backups_dir: &Path,
    prefix: &str,
) -> Result<PathBuf, AppError> {
    tokio::fs::create_dir_all(backups_dir).await?;
    let ts = chrono::Utc::now().format("%Y%m%d_%H%M%S");
    let seq = TEMP_SEQ.fetch_add(1, Ordering::Relaxed);
    let path = backups_dir.join(format!("{}_{}_{}.db", prefix, ts, seq));
    vacuum_into(pool, &path).await?;
    prune_old_rollbacks(backups_dir).await;
    Ok(path)
}

async fn prune_old_rollbacks(backups_dir: &Path) {
    let Ok(mut rd) = tokio::fs::read_dir(backups_dir).await else {
        return;
    };
    let mut files = Vec::new();
    while let Ok(Some(entry)) = rd.next_entry().await {
        let p = entry.path();
        if p.is_file() && p.extension().and_then(|e| e.to_str()) == Some("db") {
            let mtime = entry
                .metadata()
                .await
                .ok()
                .and_then(|m| m.modified().ok());
            files.push((mtime, p));
        }
    }
    if files.len() <= MAX_ROLLBACK_FILES {
        return;
    }
    files.sort_by(|a, b| b.0.cmp(&a.0));
    for (_, old_path) in files.into_iter().skip(MAX_ROLLBACK_FILES) {
        let _ = tokio::fs::remove_file(old_path).await;
    }
}

fn sha256_hex(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    let digest = hasher.finalize();
    let mut out = String::with_capacity(digest.len() * 2);
    for b in digest {
        use std::fmt::Write;
        let _ = write!(&mut out, "{:02x}", b);
    }
    out
}

async fn query_applied_migrations(pool: &SqlitePool) -> Result<Vec<String>, AppError> {
    let rows: Vec<String> = sqlx::query_scalar("SELECT step FROM _migrations ORDER BY rowid ASC")
        .fetch_all(pool)
        .await
        .unwrap_or_default();
    Ok(rows)
}

async fn query_counts(pool: &SqlitePool) -> Result<BackupCounts, AppError> {
    async fn count_table(pool: &SqlitePool, table: &str) -> i64 {
        let sql = format!("SELECT COUNT(*) FROM {}", table);
        sqlx::query_scalar::<_, i64>(&sql)
            .fetch_one(pool)
            .await
            .unwrap_or(0)
    }

    Ok(BackupCounts {
        apps: count_table(pool, "AppModels").await,
        categories: count_table(pool, "CategoryModels").await,
        daily_logs: count_table(pool, "DailyLogModels").await,
        hours_logs: count_table(pool, "HoursLogModels").await,
        app_sessions: count_table(pool, "AppSessions").await,
        web_sites: count_table(pool, "WebSiteModels").await,
        web_urls: count_table(pool, "WebUrlModels").await,
        web_browse_logs: count_table(pool, "WebBrowseLogModels").await,
        merge_groups: count_table(pool, "EntityMergeGroups").await,
        plugin_records: count_table(pool, "PluginRecords").await,
    })
}

fn collect_icon_entries(dir: &Path, archive_prefix: &str, entries: &mut Vec<ZipEntryData>) -> usize {
    let mut seen = std::collections::HashSet::new();
    collect_icon_entries_dedup(dir, archive_prefix, entries, &mut seen)
}

fn collect_icon_entries_dedup(
    dir: &Path,
    archive_prefix: &str,
    entries: &mut Vec<ZipEntryData>,
    seen: &mut std::collections::HashSet<String>,
) -> usize {
    if !dir.is_dir() {
        return 0;
    }
    let Ok(rd) = std::fs::read_dir(dir) else {
        return 0;
    };
    let mut count = 0usize;
    for entry in rd.flatten() {
        let path = entry.path();
        if !path.is_file() {
            continue;
        }
        let Some(name) = path.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        let key = format!("{}/{}", archive_prefix, name);
        if !seen.insert(key.clone()) {
            continue;
        }
        if let Ok(meta) = entry.metadata() {
            if meta.len() == 0 || meta.len() > 2 * 1024 * 1024 {
                continue;
            }
        }
        if let Ok(bytes) = std::fs::read(&path) {
            entries.push(ZipEntryData { name: key, data: bytes });
            count += 1;
        }
    }
    count
}

fn restore_icons_from_entries(entries: &HashMap<String, Vec<u8>>, paths: &BackupPaths) -> usize {
    let mut restored = 0usize;
    let app_target = paths
        .app_icons_dirs
        .iter()
        .find(|d| d.is_dir())
        .cloned()
        .or_else(|| paths.app_icons_dirs.first().cloned());

    for (name, bytes) in entries {
        if bytes.is_empty() {
            continue;
        }
        if let Some(file_name) = name.strip_prefix("WebFavicons/") {
            if !file_name.contains('/') && std::fs::create_dir_all(&paths.web_favicons_dir).is_ok() {
                let target = paths.web_favicons_dir.join(file_name);
                if !target.exists() && std::fs::write(target, bytes).is_ok() {
                    restored += 1;
                }
            }
        } else if let (Some(file_name), Some(dir)) = (name.strip_prefix("AppIcons/"), app_target.as_ref()) {
            if !file_name.contains('/') && std::fs::create_dir_all(dir).is_ok() {
                let target = dir.join(file_name);
                if !target.exists() && std::fs::write(target, bytes).is_ok() {
                    restored += 1;
                }
            }
        }
    }
    restored
}

fn merge_json_string_arrays(a: Option<&str>, b: Option<&str>) -> String {
    let mut result = Vec::new();
    let mut seen = std::collections::HashSet::new();
    for src in [a, b].into_iter().flatten() {
        if let Ok(items) = serde_json::from_str::<Vec<String>>(src) {
            for item in items {
                let trimmed = item.trim();
                if trimmed.is_empty() {
                    continue;
                }
                if seen.insert(trimmed.to_ascii_lowercase()) {
                    result.push(trimmed.to_string());
                }
            }
        }
    }
    serde_json::to_string(&result).unwrap_or_else(|_| "[]".to_string())
}

/// 全量替换恢复：在单事务内把源数据库全部用户表按原 ID 写入目标库（避免 Windows 文件锁冲突）。
async fn replace_from_stage(dst: &SqlitePool, src: &SqlitePool) -> Result<(), AppError> {
    let categories = sqlx::query(
        "SELECT ID, Name, IconFile, Color, IsDirectoryMatch, Directories, IsSystem, IsUrlMatch, UrlPatterns FROM CategoryModels",
    )
    .fetch_all(src)
    .await?;

    let apps = sqlx::query(
        "SELECT ID, Name, Alias, Description, File, CategoryID, IconFile, TotalTime, Publisher FROM AppModels",
    )
    .fetch_all(src)
    .await?;

    let daily_logs = sqlx::query("SELECT ID, Date, AppModelID, Time FROM DailyLogModels")
        .fetch_all(src)
        .await?;

    let hours_logs = sqlx::query("SELECT ID, DataTime, AppModelID, Time FROM HoursLogModels")
        .fetch_all(src)
        .await?;

    let sessions = sqlx::query(
        "SELECT ID, AppModelID, StartTime, EndTime, Duration FROM AppSessions",
    )
    .fetch_all(src)
    .await?;

    let sites = sqlx::query(
        "SELECT ID, Title, Domain, Alias, CategoryID, IconFile, Duration FROM WebSiteModels",
    )
    .fetch_all(src)
    .await?;

    let urls = sqlx::query("SELECT ID, Title, Url, IconFile FROM WebUrlModels")
        .fetch_all(src)
        .await?;

    let browse_logs = sqlx::query(
        "SELECT ID, UrlId, LogTime, Duration, SiteId, BrowserRef FROM WebBrowseLogModels",
    )
    .fetch_all(src)
    .await?;

    let merge_groups = sqlx::query(
        "SELECT ID, Name, Kind, AutoKey, PrimaryRef, IsAuto, UpdatedAt FROM EntityMergeGroups",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();

    let merge_members = sqlx::query(
        "SELECT GroupID, MemberRef, OverrideCategoryRef, AddedAt FROM EntityMergeMembers",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();

    let suppressions = sqlx::query("SELECT Ref, Reason, CreatedAt FROM EntitySuppressions")
        .fetch_all(src)
        .await
        .unwrap_or_default();

    let plugin_records = sqlx::query(
        "SELECT PluginID, RecordKey, Value, UpdatedAt FROM PluginRecords",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();

    let web_cats = sqlx::query(
        "SELECT ID, Name, IconFile, Color, IsUrlMatch, UrlPatterns, IsSystem FROM WebSiteCategoryModels",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();

    let mut tx = dst.begin().await?;
    sqlx::query("PRAGMA foreign_keys = OFF").execute(&mut *tx).await?;

    for table in [
        "EntityMergeMembers",
        "EntityMergeGroups",
        "EntitySuppressions",
        "PluginRecords",
        "WebBrowseLogModels",
        "WebUrlModels",
        "WebSiteModels",
        "WebSiteCategoryModels",
        "AppSessions",
        "HoursLogModels",
        "DailyLogModels",
        "AppModels",
        "CategoryModels",
        "AppDurationRequests",
    ] {
        let sql = format!("DELETE FROM {}", table);
        let _ = sqlx::query(&sql).execute(&mut *tx).await;
    }

    for row in categories {
        sqlx::query(
            "INSERT INTO CategoryModels (ID, Name, IconFile, Color, IsDirectoryMatch, Directories, IsSystem, IsUrlMatch, UrlPatterns) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<String, _>("Name"))
        .bind(row.get::<Option<String>, _>("IconFile"))
        .bind(row.get::<Option<String>, _>("Color"))
        .bind(row.get::<i64, _>("IsDirectoryMatch"))
        .bind(row.get::<Option<String>, _>("Directories"))
        .bind(row.get::<i64, _>("IsSystem"))
        .bind(row.get::<i64, _>("IsUrlMatch"))
        .bind(row.get::<Option<String>, _>("UrlPatterns"))
        .execute(&mut *tx)
        .await?;
    }

    for row in web_cats {
        sqlx::query(
            "INSERT INTO WebSiteCategoryModels (ID, Name, IconFile, Color, IsUrlMatch, UrlPatterns, IsSystem) VALUES (?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<String, _>("Name"))
        .bind(row.get::<Option<String>, _>("IconFile"))
        .bind(row.get::<Option<String>, _>("Color"))
        .bind(row.get::<i64, _>("IsUrlMatch"))
        .bind(row.get::<Option<String>, _>("UrlPatterns"))
        .bind(row.get::<i64, _>("IsSystem"))
        .execute(&mut *tx)
        .await?;
    }

    for row in apps {
        sqlx::query(
            "INSERT INTO AppModels (ID, Name, Alias, Description, File, CategoryID, IconFile, TotalTime, Publisher) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<String, _>("Name"))
        .bind(row.get::<Option<String>, _>("Alias"))
        .bind(row.get::<Option<String>, _>("Description"))
        .bind(row.get::<Option<String>, _>("File"))
        .bind(row.get::<i64, _>("CategoryID"))
        .bind(row.get::<Option<String>, _>("IconFile"))
        .bind(row.get::<i64, _>("TotalTime"))
        .bind(row.get::<Option<String>, _>("Publisher"))
        .execute(&mut *tx)
        .await?;
    }

    for row in daily_logs {
        sqlx::query("INSERT INTO DailyLogModels (ID, Date, AppModelID, Time) VALUES (?, ?, ?, ?)")
            .bind(row.get::<i64, _>("ID"))
            .bind(row.get::<String, _>("Date"))
            .bind(row.get::<i64, _>("AppModelID"))
            .bind(row.get::<i64, _>("Time"))
            .execute(&mut *tx)
            .await?;
    }

    for row in hours_logs {
        sqlx::query("INSERT INTO HoursLogModels (ID, DataTime, AppModelID, Time) VALUES (?, ?, ?, ?)")
            .bind(row.get::<i64, _>("ID"))
            .bind(row.get::<String, _>("DataTime"))
            .bind(row.get::<i64, _>("AppModelID"))
            .bind(row.get::<i64, _>("Time"))
            .execute(&mut *tx)
            .await?;
    }

    for row in sessions {
        sqlx::query(
            "INSERT INTO AppSessions (ID, AppModelID, StartTime, EndTime, Duration) VALUES (?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<i64, _>("AppModelID"))
        .bind(row.get::<String, _>("StartTime"))
        .bind(row.get::<String, _>("EndTime"))
        .bind(row.get::<i64, _>("Duration"))
        .execute(&mut *tx)
        .await?;
    }

    for row in sites {
        sqlx::query(
            "INSERT INTO WebSiteModels (ID, Title, Domain, Alias, CategoryID, IconFile, Duration) VALUES (?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<Option<String>, _>("Title"))
        .bind(row.get::<String, _>("Domain"))
        .bind(row.get::<Option<String>, _>("Alias"))
        .bind(row.get::<i64, _>("CategoryID"))
        .bind(row.get::<Option<String>, _>("IconFile"))
        .bind(row.get::<i64, _>("Duration"))
        .execute(&mut *tx)
        .await?;
    }

    for row in urls {
        sqlx::query("INSERT INTO WebUrlModels (ID, Title, Url, IconFile) VALUES (?, ?, ?, ?)")
            .bind(row.get::<i64, _>("ID"))
            .bind(row.get::<Option<String>, _>("Title"))
            .bind(row.get::<String, _>("Url"))
            .bind(row.get::<Option<String>, _>("IconFile"))
            .execute(&mut *tx)
            .await?;
    }

    for row in browse_logs {
        sqlx::query(
            "INSERT INTO WebBrowseLogModels (ID, UrlId, LogTime, Duration, SiteId, BrowserRef) VALUES (?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<i64, _>("UrlId"))
        .bind(row.get::<String, _>("LogTime"))
        .bind(row.get::<i64, _>("Duration"))
        .bind(row.get::<i64, _>("SiteId"))
        .bind(row.get::<Option<String>, _>("BrowserRef"))
        .execute(&mut *tx)
        .await?;
    }

    for row in merge_groups {
        sqlx::query(
            "INSERT INTO EntityMergeGroups (ID, Name, Kind, AutoKey, PrimaryRef, IsAuto, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("ID"))
        .bind(row.get::<String, _>("Name"))
        .bind(row.get::<String, _>("Kind"))
        .bind(row.get::<Option<String>, _>("AutoKey"))
        .bind(row.get::<String, _>("PrimaryRef"))
        .bind(row.get::<i64, _>("IsAuto"))
        .bind(row.get::<String, _>("UpdatedAt"))
        .execute(&mut *tx)
        .await?;
    }

    for row in merge_members {
        sqlx::query(
            "INSERT INTO EntityMergeMembers (GroupID, MemberRef, OverrideCategoryRef, AddedAt) VALUES (?, ?, ?, ?)",
        )
        .bind(row.get::<i64, _>("GroupID"))
        .bind(row.get::<String, _>("MemberRef"))
        .bind(row.get::<Option<String>, _>("OverrideCategoryRef"))
        .bind(row.get::<String, _>("AddedAt"))
        .execute(&mut *tx)
        .await?;
    }

    for row in suppressions {
        sqlx::query("INSERT INTO EntitySuppressions (Ref, Reason, CreatedAt) VALUES (?, ?, ?)")
            .bind(row.get::<String, _>("Ref"))
            .bind(row.get::<Option<String>, _>("Reason"))
            .bind(row.get::<String, _>("CreatedAt"))
            .execute(&mut *tx)
            .await?;
    }

    for row in plugin_records {
        sqlx::query(
            "INSERT INTO PluginRecords (PluginID, RecordKey, Value, UpdatedAt) VALUES (?, ?, ?, ?)",
        )
        .bind(row.get::<String, _>("PluginID"))
        .bind(row.get::<String, _>("RecordKey"))
        .bind(row.get::<String, _>("Value"))
        .bind(row.get::<String, _>("UpdatedAt"))
        .execute(&mut *tx)
        .await?;
    }

    sqlx::query("PRAGMA foreign_keys = ON").execute(&mut *tx).await?;
    tx.commit().await?;
    Ok(())
}

/// 幂等增量合并：保留本地并补齐/合并备份中的数据，不重复叠加同一时间段或同一日志。
async fn merge_from_stage(dst: &SqlitePool, src: &SqlitePool) -> Result<(), AppError> {
    let src_cats = sqlx::query(
        "SELECT ID, Name, IconFile, Color, IsDirectoryMatch, Directories, IsSystem, IsUrlMatch, UrlPatterns FROM CategoryModels",
    )
    .fetch_all(src)
    .await?;
    let src_apps = sqlx::query(
        "SELECT ID, Name, Alias, Description, File, CategoryID, IconFile, TotalTime, Publisher FROM AppModels",
    )
    .fetch_all(src)
    .await?;
    let src_daily = sqlx::query("SELECT Date, AppModelID, Time FROM DailyLogModels")
        .fetch_all(src)
        .await?;
    let src_hours = sqlx::query("SELECT DataTime, AppModelID, Time FROM HoursLogModels")
        .fetch_all(src)
        .await?;
    let src_sessions = sqlx::query(
        "SELECT AppModelID, StartTime, EndTime, Duration FROM AppSessions",
    )
    .fetch_all(src)
    .await?;
    let src_sites = sqlx::query(
        "SELECT ID, Title, Domain, Alias, CategoryID, IconFile, Duration FROM WebSiteModels",
    )
    .fetch_all(src)
    .await?;
    let src_urls = sqlx::query("SELECT ID, Title, Url, IconFile FROM WebUrlModels")
        .fetch_all(src)
        .await?;
    let src_browse = sqlx::query(
        "SELECT UrlId, LogTime, Duration, SiteId, BrowserRef FROM WebBrowseLogModels",
    )
    .fetch_all(src)
    .await?;
    let src_groups = sqlx::query(
        "SELECT ID, Name, Kind, AutoKey, PrimaryRef, IsAuto, UpdatedAt FROM EntityMergeGroups",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();
    let src_members = sqlx::query(
        "SELECT GroupID, MemberRef, OverrideCategoryRef, AddedAt FROM EntityMergeMembers",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();
    let src_suppressions = sqlx::query("SELECT Ref, Reason, CreatedAt FROM EntitySuppressions")
        .fetch_all(src)
        .await
        .unwrap_or_default();
    let src_plugins = sqlx::query(
        "SELECT PluginID, RecordKey, Value, UpdatedAt FROM PluginRecords",
    )
    .fetch_all(src)
    .await
    .unwrap_or_default();

    let mut tx = dst.begin().await?;

    // 1. 合并 CategoryModels（先建立本地映射）
    let existing_cats = sqlx::query(
        "SELECT ID, Name, IsSystem, IsDirectoryMatch, Directories, IsUrlMatch, UrlPatterns FROM CategoryModels",
    )
    .fetch_all(&mut *tx)
    .await?;

    let mut default_cat_id: i64 = 0;
    let mut name_to_cat: HashMap<String, (i64, i64, Option<String>, i64, Option<String>)> = HashMap::new();
    for r in existing_cats {
        let id: i64 = r.get("ID");
        let name: String = r.get("Name");
        let is_sys: i64 = r.get("IsSystem");
        if is_sys == 1 && default_cat_id == 0 {
            default_cat_id = id;
        }
        name_to_cat.insert(
            name.trim().to_ascii_lowercase(),
            (
                id,
                r.get("IsDirectoryMatch"),
                r.get("Directories"),
                r.get("IsUrlMatch"),
                r.get("UrlPatterns"),
            ),
        );
    }

    let mut cat_map: HashMap<i64, i64> = HashMap::new();
    for r in src_cats {
        let src_id: i64 = r.get("ID");
        let name: String = r.get("Name");
        let is_sys: i64 = r.get("IsSystem");
        if is_sys == 1 && default_cat_id > 0 {
            cat_map.insert(src_id, default_cat_id);
            continue;
        }
        let key = name.trim().to_ascii_lowercase();
        let src_dir_match: i64 = r.get("IsDirectoryMatch");
        let src_dirs: Option<String> = r.get("Directories");
        let src_url_match: i64 = r.get("IsUrlMatch");
        let src_urls_pat: Option<String> = r.get("UrlPatterns");

        if let Some((dst_id, dst_dm, dst_dirs, dst_um, dst_urls)) = name_to_cat.get(&key).cloned() {
            let merged_dirs = merge_json_string_arrays(dst_dirs.as_deref(), src_dirs.as_deref());
            let merged_urls = merge_json_string_arrays(dst_urls.as_deref(), src_urls_pat.as_deref());
            let new_dm = if dst_dm != 0 || src_dir_match != 0 { 1 } else { 0 };
            let new_um = if dst_um != 0 || src_url_match != 0 { 1 } else { 0 };

            sqlx::query(
                "UPDATE CategoryModels SET IsDirectoryMatch = ?, Directories = ?, IsUrlMatch = ?, UrlPatterns = ? WHERE ID = ?",
            )
            .bind(new_dm)
            .bind(&merged_dirs)
            .bind(new_um)
            .bind(&merged_urls)
            .bind(dst_id)
            .execute(&mut *tx)
            .await?;

            name_to_cat.insert(key, (dst_id, new_dm, Some(merged_dirs), new_um, Some(merged_urls)));
            cat_map.insert(src_id, dst_id);
        } else {
            let inserted_id = sqlx::query(
                "INSERT INTO CategoryModels (Name, IconFile, Color, IsDirectoryMatch, Directories, IsSystem, IsUrlMatch, UrlPatterns) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(name.trim())
            .bind(r.get::<Option<String>, _>("IconFile"))
            .bind(r.get::<Option<String>, _>("Color"))
            .bind(src_dir_match)
            .bind(&src_dirs)
            .bind(is_sys)
            .bind(src_url_match)
            .bind(&src_urls_pat)
            .execute(&mut *tx)
            .await?
            .last_insert_rowid();

            name_to_cat.insert(key, (inserted_id, src_dir_match, src_dirs, src_url_match, src_urls_pat));
            cat_map.insert(src_id, inserted_id);
        }
    }

    // 2. 合并 AppModels
    let existing_apps = sqlx::query(
        "SELECT ID, Name, Alias, Description, File, CategoryID, IconFile, Publisher FROM AppModels",
    )
    .fetch_all(&mut *tx)
    .await?;
    let mut name_to_app: HashMap<String, (i64, i64, Option<String>, Option<String>, Option<String>, Option<String>, Option<String>)> =
        HashMap::new();
    for r in existing_apps {
        let name: String = r.get("Name");
        name_to_app.insert(
            name,
            (
                r.get("ID"),
                r.get("CategoryID"),
                r.get("Alias"),
                r.get("Description"),
                r.get("File"),
                r.get("IconFile"),
                r.get("Publisher"),
            ),
        );
    }

    let mut app_map: HashMap<i64, i64> = HashMap::new();
    for r in src_apps {
        let src_id: i64 = r.get("ID");
        let name: String = r.get("Name");
        let src_cat_id: i64 = r.get("CategoryID");
        let mapped_cat = cat_map.get(&src_cat_id).copied().unwrap_or(default_cat_id);
        let src_alias: Option<String> = r.get("Alias");
        let src_desc: Option<String> = r.get("Description");
        let src_file: Option<String> = r.get("File");
        let src_icon: Option<String> = r.get("IconFile");
        let src_pub: Option<String> = r.get("Publisher");
        let src_total: i64 = r.get("TotalTime");

        if let Some((dst_id, cur_cat, cur_alias, cur_desc, cur_file, cur_icon, cur_pub)) =
            name_to_app.get(&name).cloned()
        {
            let next_cat = if (cur_cat == 0 || cur_cat == default_cat_id) && mapped_cat != default_cat_id && mapped_cat != 0 {
                mapped_cat
            } else {
                cur_cat
            };
            sqlx::query(
                "UPDATE AppModels SET CategoryID = ?, Alias = ?, Description = ?, File = ?, IconFile = ?, Publisher = ? WHERE ID = ?",
            )
            .bind(next_cat)
            .bind(prefer_non_empty(cur_alias, src_alias))
            .bind(prefer_non_empty(cur_desc, src_desc))
            .bind(prefer_non_empty(cur_file, src_file))
            .bind(prefer_non_empty(cur_icon, src_icon))
            .bind(prefer_non_empty(cur_pub, src_pub))
            .bind(dst_id)
            .execute(&mut *tx)
            .await?;

            app_map.insert(src_id, dst_id);
        } else {
            let dst_id = sqlx::query(
                "INSERT INTO AppModels (Name, Alias, Description, File, CategoryID, IconFile, TotalTime, Publisher) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(&name)
            .bind(&src_alias)
            .bind(&src_desc)
            .bind(&src_file)
            .bind(mapped_cat)
            .bind(&src_icon)
            .bind(src_total)
            .bind(&src_pub)
            .execute(&mut *tx)
            .await?
            .last_insert_rowid();
            name_to_app.insert(name, (dst_id, mapped_cat, src_alias, src_desc, src_file, src_icon, src_pub));
            app_map.insert(src_id, dst_id);
        }
    }

    // 3. 合并 DailyLogModels / HoursLogModels / AppSessions
    for r in src_daily {
        let src_app_id: i64 = r.get("AppModelID");
        let Some(&dst_app_id) = app_map.get(&src_app_id) else {
            continue;
        };
        let date: String = r.get("Date");
        let time: i64 = r.get("Time");
        sqlx::query(
            "INSERT INTO DailyLogModels (Date, AppModelID, Time) VALUES (?, ?, ?) ON CONFLICT(Date, AppModelID) DO UPDATE SET Time = MAX(DailyLogModels.Time, excluded.Time)",
        )
        .bind(date)
        .bind(dst_app_id)
        .bind(time)
        .execute(&mut *tx)
        .await?;
    }

    for r in src_hours {
        let src_app_id: i64 = r.get("AppModelID");
        let Some(&dst_app_id) = app_map.get(&src_app_id) else {
            continue;
        };
        let dt: String = r.get("DataTime");
        let time: i64 = r.get("Time");
        sqlx::query(
            "INSERT INTO HoursLogModels (DataTime, AppModelID, Time) VALUES (?, ?, ?) ON CONFLICT(AppModelID, DataTime) DO UPDATE SET Time = MAX(HoursLogModels.Time, excluded.Time)",
        )
        .bind(dt)
        .bind(dst_app_id)
        .bind(time)
        .execute(&mut *tx)
        .await?;
    }

    for r in src_sessions {
        let src_app_id: i64 = r.get("AppModelID");
        let Some(&dst_app_id) = app_map.get(&src_app_id) else {
            continue;
        };
        let start_t: String = r.get("StartTime");
        let end_t: String = r.get("EndTime");
        let dur: i64 = r.get("Duration");
        sqlx::query(
            "INSERT INTO AppSessions (AppModelID, StartTime, EndTime, Duration) SELECT ?, ?, ?, ? WHERE NOT EXISTS (SELECT 1 FROM AppSessions WHERE AppModelID = ? AND StartTime = ? AND EndTime = ?)",
        )
        .bind(dst_app_id)
        .bind(&start_t)
        .bind(&end_t)
        .bind(dur)
        .bind(dst_app_id)
        .bind(&start_t)
        .bind(&end_t)
        .execute(&mut *tx)
        .await?;
    }

    // 4. 合并 WebSiteModels / WebUrlModels / WebBrowseLogModels
    let existing_sites = sqlx::query(
        "SELECT ID, Title, Domain, Alias, CategoryID, IconFile FROM WebSiteModels",
    )
    .fetch_all(&mut *tx)
    .await?;
    let mut domain_to_site: HashMap<String, (i64, i64, Option<String>, Option<String>, Option<String>)> =
        HashMap::new();
    for r in existing_sites {
        let domain: String = r.get("Domain");
        domain_to_site.insert(
            domain,
            (
                r.get("ID"),
                r.get("CategoryID"),
                r.get("Title"),
                r.get("Alias"),
                r.get("IconFile"),
            ),
        );
    }

    let mut site_map: HashMap<i64, i64> = HashMap::new();
    for r in src_sites {
        let src_id: i64 = r.get("ID");
        let domain: String = r.get("Domain");
        let src_cat_id: i64 = r.get("CategoryID");
        let mapped_cat = cat_map.get(&src_cat_id).copied().unwrap_or(default_cat_id);
        let src_title: Option<String> = r.get("Title");
        let src_alias: Option<String> = r.get("Alias");
        let src_icon: Option<String> = r.get("IconFile");
        let src_dur: i64 = r.get("Duration");

        if let Some((dst_id, cur_cat, cur_title, cur_alias, cur_icon)) =
            domain_to_site.get(&domain).cloned()
        {
            let next_cat = if (cur_cat == 0 || cur_cat == default_cat_id) && mapped_cat != default_cat_id && mapped_cat != 0 {
                mapped_cat
            } else {
                cur_cat
            };
            sqlx::query(
                "UPDATE WebSiteModels SET CategoryID = ?, Title = ?, Alias = ?, IconFile = ? WHERE ID = ?",
            )
            .bind(next_cat)
            .bind(prefer_non_empty(cur_title, src_title))
            .bind(prefer_non_empty(cur_alias, src_alias))
            .bind(prefer_non_empty(cur_icon, src_icon))
            .bind(dst_id)
            .execute(&mut *tx)
            .await?;
            site_map.insert(src_id, dst_id);
        } else {
            let dst_id = sqlx::query(
                "INSERT INTO WebSiteModels (Title, Domain, Alias, CategoryID, IconFile, Duration) VALUES (?, ?, ?, ?, ?, ?)",
            )
            .bind(&src_title)
            .bind(&domain)
            .bind(&src_alias)
            .bind(mapped_cat)
            .bind(&src_icon)
            .bind(src_dur)
            .execute(&mut *tx)
            .await?
            .last_insert_rowid();
            domain_to_site.insert(domain, (dst_id, mapped_cat, src_title, src_alias, src_icon));
            site_map.insert(src_id, dst_id);
        }
    }

    let existing_urls = sqlx::query("SELECT ID, Url FROM WebUrlModels")
        .fetch_all(&mut *tx)
        .await?;
    let mut url_to_id: HashMap<String, i64> = HashMap::new();
    for r in existing_urls {
        url_to_id.insert(r.get("Url"), r.get("ID"));
    }

    let mut url_map: HashMap<i64, i64> = HashMap::new();
    for r in src_urls {
        let src_id: i64 = r.get("ID");
        let url: String = r.get("Url");
        if let Some(&dst_id) = url_to_id.get(&url) {
            url_map.insert(src_id, dst_id);
        } else {
            let dst_id = sqlx::query("INSERT INTO WebUrlModels (Title, Url, IconFile) VALUES (?, ?, ?)")
                .bind(r.get::<Option<String>, _>("Title"))
                .bind(&url)
                .bind(r.get::<Option<String>, _>("IconFile"))
                .execute(&mut *tx)
                .await?
                .last_insert_rowid();
            url_to_id.insert(url, dst_id);
            url_map.insert(src_id, dst_id);
        }
    }

    for r in src_browse {
        let src_url_id: i64 = r.get("UrlId");
        let src_site_id: i64 = r.get("SiteId");
        let (Some(&dst_url_id), Some(&dst_site_id)) = (url_map.get(&src_url_id), site_map.get(&src_site_id)) else {
            continue;
        };
        let log_time: String = r.get("LogTime");
        let dur: i64 = r.get("Duration");
        let browser_ref: Option<String> = r.get("BrowserRef");
        sqlx::query(
            "INSERT INTO WebBrowseLogModels (UrlId, LogTime, Duration, SiteId, BrowserRef) VALUES (?, ?, ?, ?, ?) \
             ON CONFLICT(LogTime, UrlId) DO UPDATE SET Duration = MAX(WebBrowseLogModels.Duration, excluded.Duration), BrowserRef = COALESCE(WebBrowseLogModels.BrowserRef, excluded.BrowserRef)",
        )
        .bind(dst_url_id)
        .bind(log_time)
        .bind(dur)
        .bind(dst_site_id)
        .bind(browser_ref)
        .execute(&mut *tx)
        .await?;
    }

    // 5. 合并 EntityMergeGroups / Members / Suppressions
    let existing_groups = sqlx::query("SELECT ID, Name, AutoKey, PrimaryRef FROM EntityMergeGroups")
        .fetch_all(&mut *tx)
        .await
        .unwrap_or_default();
    let mut autokey_to_group: HashMap<String, i64> = HashMap::new();
    let mut name_primary_to_group: HashMap<(String, String), i64> = HashMap::new();
    for r in existing_groups {
        let gid: i64 = r.get("ID");
        if let Some(ak) = r.get::<Option<String>, _>("AutoKey") {
            if !ak.is_empty() {
                autokey_to_group.insert(ak, gid);
            }
        }
        name_primary_to_group.insert((r.get("Name"), r.get("PrimaryRef")), gid);
    }

    let mut group_map: HashMap<i64, i64> = HashMap::new();
    for r in src_groups {
        let src_gid: i64 = r.get("ID");
        let name: String = r.get("Name");
        let kind: String = r.get("Kind");
        let auto_key: Option<String> = r.get("AutoKey");
        let primary_ref: String = r.get("PrimaryRef");
        let is_auto: i64 = r.get("IsAuto");
        let updated_at: String = r.get("UpdatedAt");

        let existing = auto_key
            .as_ref()
            .and_then(|ak| autokey_to_group.get(ak).copied())
            .or_else(|| name_primary_to_group.get(&(name.clone(), primary_ref.clone())).copied());

        if let Some(dst_gid) = existing {
            group_map.insert(src_gid, dst_gid);
        } else {
            let dst_gid = sqlx::query(
                "INSERT INTO EntityMergeGroups (Name, Kind, AutoKey, PrimaryRef, IsAuto, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?)",
            )
            .bind(&name)
            .bind(&kind)
            .bind(&auto_key)
            .bind(&primary_ref)
            .bind(is_auto)
            .bind(&updated_at)
            .execute(&mut *tx)
            .await?
            .last_insert_rowid();
            if let Some(ak) = auto_key {
                if !ak.is_empty() {
                    autokey_to_group.insert(ak, dst_gid);
                }
            }
            name_primary_to_group.insert((name, primary_ref), dst_gid);
            group_map.insert(src_gid, dst_gid);
        }
    }

    for r in src_members {
        let src_gid: i64 = r.get("GroupID");
        let Some(&dst_gid) = group_map.get(&src_gid) else {
            continue;
        };
        sqlx::query(
            "INSERT OR IGNORE INTO EntityMergeMembers (GroupID, MemberRef, OverrideCategoryRef, AddedAt) VALUES (?, ?, ?, ?)",
        )
        .bind(dst_gid)
        .bind(r.get::<String, _>("MemberRef"))
        .bind(r.get::<Option<String>, _>("OverrideCategoryRef"))
        .bind(r.get::<String, _>("AddedAt"))
        .execute(&mut *tx)
        .await?;
    }

    for r in src_suppressions {
        sqlx::query("INSERT OR IGNORE INTO EntitySuppressions (Ref, Reason, CreatedAt) VALUES (?, ?, ?)")
            .bind(r.get::<String, _>("Ref"))
            .bind(r.get::<Option<String>, _>("Reason"))
            .bind(r.get::<String, _>("CreatedAt"))
            .execute(&mut *tx)
            .await?;
    }

    // 6. 合并 PluginRecords（UpdatedAt 更新者胜出）
    for r in src_plugins {
        sqlx::query(
            "INSERT INTO PluginRecords (PluginID, RecordKey, Value, UpdatedAt) VALUES (?, ?, ?, ?) \
             ON CONFLICT(PluginID, RecordKey) DO UPDATE SET \
             Value = CASE WHEN excluded.UpdatedAt > PluginRecords.UpdatedAt THEN excluded.Value ELSE PluginRecords.Value END, \
             UpdatedAt = CASE WHEN excluded.UpdatedAt > PluginRecords.UpdatedAt THEN excluded.UpdatedAt ELSE PluginRecords.UpdatedAt END",
        )
        .bind(r.get::<String, _>("PluginID"))
        .bind(r.get::<String, _>("RecordKey"))
        .bind(r.get::<String, _>("Value"))
        .bind(r.get::<String, _>("UpdatedAt"))
        .execute(&mut *tx)
        .await?;
    }

    // 7. 重算汇总总时长，保持幂等且一致
    sqlx::query(
        "UPDATE AppModels SET TotalTime = MAX(TotalTime, COALESCE((SELECT SUM(Time) FROM DailyLogModels WHERE DailyLogModels.AppModelID = AppModels.ID), 0))",
    )
    .execute(&mut *tx)
    .await?;

    sqlx::query(
        "UPDATE WebSiteModels SET Duration = MAX(Duration, COALESCE((SELECT SUM(Duration) FROM WebBrowseLogModels WHERE WebBrowseLogModels.SiteId = WebSiteModels.ID), 0))",
    )
    .execute(&mut *tx)
    .await?;

    tx.commit().await?;
    Ok(())
}

fn prefer_non_empty(current: Option<String>, candidate: Option<String>) -> Option<String> {
    if current.as_deref().is_some_and(|s| !s.trim().is_empty()) {
        current
    } else if candidate.as_deref().is_some_and(|s| !s.trim().is_empty()) {
        candidate
    } else {
        current.or(candidate)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use sqlx::sqlite::SqlitePoolOptions;

    async fn setup_test_db(paths: &BackupPaths) -> SqlitePool {
        std::fs::create_dir_all(&paths.data_dir).unwrap();
        let db_file = paths.data_dir.join("data.db");
        let opts = SqliteConnectOptions::new()
            .filename(&db_file)
            .create_if_missing(true)
            .journal_mode(sqlx::sqlite::SqliteJournalMode::Wal);
        let pool = SqlitePoolOptions::new()
            .max_connections(1)
            .connect_with(opts)
            .await
            .unwrap();
        migrations::run(&pool, "UTC").await.unwrap();
        pool
    }

    fn temp_backup_paths(label: &str) -> BackupPaths {
        let root = std::env::temp_dir().join(format!(
            "shijian_backup_test_{}_{}_{}",
            label,
            std::process::id(),
            TEMP_SEQ.fetch_add(1, Ordering::Relaxed)
        ));
        BackupPaths {
            data_dir: root.join("data"),
            web_favicons_dir: root.join("favicons"),
            app_icons_dirs: vec![root.join("app_icons")],
            device_name: "test-pc".to_string(),
        }
    }

    #[tokio::test]
    async fn export_inspect_merge_and_replace_roundtrip() {
        let src_paths = temp_backup_paths("src");
        let dst_paths = temp_backup_paths("dst");
        let src_pool = setup_test_db(&src_paths).await;
        let dst_pool = setup_test_db(&dst_paths).await;

        // 源库插入独特应用与网站记录 + 重叠应用记录
        let dev_cat_id: i64 = sqlx::query_scalar("SELECT ID FROM CategoryModels WHERE Name = '开发'")
            .fetch_one(&src_pool)
            .await
            .unwrap();
        let src_app_id = sqlx::query(
            "INSERT INTO AppModels (Name, Description, CategoryID, TotalTime) VALUES ('Code', 'VS Code', ?, 3600)",
        )
        .bind(dev_cat_id)
        .execute(&src_pool)
        .await
        .unwrap()
        .last_insert_rowid();
        sqlx::query("INSERT INTO DailyLogModels (Date, AppModelID, Time) VALUES ('2026-11-13', ?, 3600)")
            .bind(src_app_id)
            .execute(&src_pool)
            .await
            .unwrap();
        sqlx::query("INSERT INTO HoursLogModels (DataTime, AppModelID, Time) VALUES ('2026-11-13T10:00:00', ?, 1800)")
            .bind(src_app_id)
            .execute(&src_pool)
            .await
            .unwrap();
        sqlx::query(
            "INSERT INTO AppSessions (AppModelID, StartTime, EndTime, Duration) VALUES (?, '2026-11-13T10:00:00', '2026-11-13T10:30:00', 1800)",
        )
        .bind(src_app_id)
        .execute(&src_pool)
        .await
        .unwrap();

        let src_site_id = sqlx::query(
            "INSERT INTO WebSiteModels (Title, Domain, CategoryID, Duration) VALUES ('Rust Docs', 'doc.rust-lang.org', ?, 900)",
        )
        .bind(dev_cat_id)
        .execute(&src_pool)
        .await
        .unwrap()
        .last_insert_rowid();
        let src_url_id = sqlx::query("INSERT INTO WebUrlModels (Title, Url) VALUES ('Book', 'https://doc.rust-lang.org/book/')")
            .execute(&src_pool)
            .await
            .unwrap()
            .last_insert_rowid();
        sqlx::query(
            "INSERT INTO WebBrowseLogModels (UrlId, LogTime, Duration, SiteId, BrowserRef) VALUES (?, '2026-11-13T11:00:00', 900, ?, 'chrome')",
        )
        .bind(src_url_id)
        .bind(src_site_id)
        .execute(&src_pool)
        .await
        .unwrap();

        // 目标库先有一个本地应用及同天较小时间
        let uncat_id: i64 = sqlx::query_scalar("SELECT ID FROM CategoryModels WHERE IsSystem = 1")
            .fetch_one(&dst_pool)
            .await
            .unwrap();
        let dst_code_id = sqlx::query(
            "INSERT INTO AppModels (Name, Description, CategoryID, TotalTime) VALUES ('Code', '', ?, 1200)",
        )
        .bind(uncat_id)
        .execute(&dst_pool)
        .await
        .unwrap()
        .last_insert_rowid();
        sqlx::query("INSERT INTO DailyLogModels (Date, AppModelID, Time) VALUES ('2026-11-13', ?, 1200)")
            .bind(dst_code_id)
            .execute(&dst_pool)
            .await
            .unwrap();
        sqlx::query("INSERT INTO AppModels (Name, CategoryID, TotalTime) VALUES ('LocalOnlyApp', ?, 500)")
            .bind(uncat_id)
            .execute(&dst_pool)
            .await
            .unwrap();

        // 1. 导出 .shijian-backup 并校验
        let (archive, manifest) = BackupService::export_archive(&src_pool, &src_paths, "1.3.4-fork.14", false)
            .await
            .unwrap();
        assert_eq!(manifest.format_version, 1);
        assert_eq!(manifest.counts.apps, 1);
        assert_eq!(manifest.counts.web_sites, 1);

        let inspected = BackupService::inspect_archive(&archive).await.unwrap();
        assert!(inspected.valid);
        assert!(inspected.compatible);
        assert!(inspected.sha256_verified);
        assert!(inspected.sqlite_integrity_ok);

        // 2. Merge 导入：保留 LocalOnlyApp，同时 Code 继承分类并取最大时长 3600（幂等）
        let merge_res = BackupService::import_archive(&dst_pool, &dst_paths, &archive, ImportMode::Merge, "UTC")
            .await
            .unwrap();
        assert!(!merge_res.rollback_file.is_empty());
        assert!(dst_paths.backups_dir().join(&merge_res.rollback_file).is_file());

        let local_kept: Option<i64> = sqlx::query_scalar("SELECT ID FROM AppModels WHERE Name = 'LocalOnlyApp'")
            .fetch_optional(&dst_pool)
            .await
            .unwrap();
        assert!(local_kept.is_some());

        let (merged_time, merged_cat): (i64, i64) = sqlx::query_as(
            "SELECT TotalTime, CategoryID FROM AppModels WHERE Name = 'Code'",
        )
        .fetch_one(&dst_pool)
        .await
        .unwrap();
        assert_eq!(merged_time, 3600);
        let merged_cat_name: String = sqlx::query_scalar("SELECT Name FROM CategoryModels WHERE ID = ?")
            .bind(merged_cat)
            .fetch_one(&dst_pool)
            .await
            .unwrap();
        assert_eq!(merged_cat_name, "开发");

        // 重复合并同一份备份应保持幂等（不会翻倍）
        BackupService::import_archive(&dst_pool, &dst_paths, &archive, ImportMode::Merge, "UTC")
            .await
            .unwrap();
        let after_second_merge: i64 = sqlx::query_scalar("SELECT TotalTime FROM AppModels WHERE Name = 'Code'")
            .fetch_one(&dst_pool)
            .await
            .unwrap();
        assert_eq!(after_second_merge, 3600);

        // 3. Replace 导入：恢复成与源库完全一致（不含 LocalOnlyApp）
        BackupService::import_archive(&dst_pool, &dst_paths, &archive, ImportMode::Replace, "UTC")
            .await
            .unwrap();
        let after_replace: Option<i64> = sqlx::query_scalar("SELECT ID FROM AppModels WHERE Name = 'LocalOnlyApp'")
            .fetch_optional(&dst_pool)
            .await
            .unwrap();
        assert!(after_replace.is_none());

        src_pool.close().await;
        dst_pool.close().await;
        let _ = std::fs::remove_dir_all(src_paths.data_dir.parent().unwrap());
        let _ = std::fs::remove_dir_all(dst_paths.data_dir.parent().unwrap());
    }

    #[tokio::test]
    async fn inspect_rejects_tampered_sha256_or_future_schema() {
        let paths = temp_backup_paths("tamper");
        let pool = setup_test_db(&paths).await;
        let (archive, _) = BackupService::export_archive(&pool, &paths, "1.3.4", false)
            .await
            .unwrap();

        // 篡改 manifest 的 dbSha256
        let mut entries = read_zip(&archive).unwrap();
        let mut manifest: BackupManifest =
            serde_json::from_slice(entries.get("manifest.json").unwrap()).unwrap();
        manifest.db_sha256 = "0".repeat(64);
        entries.insert(
            "manifest.json".to_string(),
            serde_json::to_vec(&manifest).unwrap(),
        );
        let bad_zip = build_zip(
            &entries
                .into_iter()
                .map(|(name, data)| ZipEntryData { name, data })
                .collect::<Vec<_>>(),
        )
        .unwrap();
        assert!(BackupService::inspect_archive(&bad_zip).await.is_err());

        // 模拟未来更高 schema 版本（_migrations 比当前多一步）
        sqlx::query("INSERT INTO _migrations (step) VALUES ('future_v999_step')")
            .execute(&pool)
            .await
            .unwrap();
        let (future_archive, _) = BackupService::export_archive(&pool, &paths, "9.9.9", false)
            .await
            .unwrap();
        let inspected = BackupService::inspect_archive(&future_archive).await.unwrap();
        assert!(!inspected.compatible);
        assert!(BackupService::import_archive(&pool, &paths, &future_archive, ImportMode::Merge, "UTC").await.is_err());

        pool.close().await;
        let _ = std::fs::remove_dir_all(paths.data_dir.parent().unwrap());
    }
}
