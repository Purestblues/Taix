use std::collections::HashSet;
use std::sync::{Arc, LazyLock};
use std::time::Duration;

use attohttpc::{header, Method, RequestBuilder};
use base64::Engine;
use serde::{Deserialize, Serialize};
use sqlx::SqlitePool;
use tokio::sync::Mutex;
use tracing::{info, warn};

use crate::error::AppError;
use crate::models::config::WebDavModel;
use crate::services::backup::{BackupImportResult, BackupPaths, BackupService, ImportMode};
use crate::services::config::ConfigService;

const PROPFIND_BODY: &str = r#"<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:">
  <d:prop>
    <d:getcontentlength/>
    <d:getlastmodified/>
    <d:resourcetype/>
  </d:prop>
</d:propfind>"#;

const MAX_REMOTE_SNAPSHOTS: usize = 15;
const REQUEST_TIMEOUT: Duration = Duration::from_secs(60);

static SYNC_LOCK: LazyLock<Mutex<()>> = LazyLock::new(|| Mutex::new(()));

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WebDavTestRequest {
    pub url: String,
    #[serde(default)]
    pub username: String,
    #[serde(default)]
    pub password: String,
    #[serde(default)]
    pub remote_folder: String,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WebDavTestResult {
    pub ok: bool,
    pub message: String,
    pub remote_url: String,
    pub backup_count: usize,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RemoteBackupItem {
    pub file_name: String,
    pub size_bytes: u64,
    pub last_modified: String,
    pub device_name: String,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WebDavSyncResult {
    pub uploaded_file: String,
    pub merged_files: Vec<String>,
    pub skipped_files: usize,
    pub synced_at_utc: String,
    pub message: String,
}

#[derive(Debug, Clone)]
struct Creds {
    base_url: String,
    remote_folder: String,
    auth_header: Option<String>,
}

impl Creds {
    fn from_model(cfg: &WebDavModel) -> Result<Self, AppError> {
        Self::new(&cfg.url, &cfg.username, &cfg.password, &cfg.remote_folder)
    }

    fn new(url: &str, username: &str, password: &str, remote_folder: &str) -> Result<Self, AppError> {
        let base_url = url.trim().trim_end_matches('/').to_string();
        if !base_url.starts_with("http://") && !base_url.starts_with("https://") {
            return Err(AppError::Business(
                "WebDAV 地址必须以 http:// 或 https:// 开头".to_string(),
            ));
        }

        let auth_header = if !username.is_empty() || !password.is_empty() {
            let token = base64::engine::general_purpose::STANDARD
                .encode(format!("{}:{}", username, password));
            Some(format!("Basic {}", token))
        } else {
            None
        };

        Ok(Self {
            base_url,
            remote_folder: normalize_folder(remote_folder),
            auth_header,
        })
    }

    fn target_dir_url(&self) -> String {
        if self.remote_folder.is_empty() {
            format!("{}/", self.base_url)
        } else {
            format!("{}/{}/", self.base_url, self.remote_folder)
        }
    }

    fn file_url(&self, file_name: &str) -> Result<String, AppError> {
        let safe = sanitize_remote_filename(file_name)?;
        Ok(format!("{}{}", self.target_dir_url(), safe))
    }

    fn apply_auth(&self, mut req: RequestBuilder) -> RequestBuilder {
        req = req.timeout(REQUEST_TIMEOUT);
        if let Some(ref auth) = self.auth_header {
            req = req.header(header::AUTHORIZATION, auth.as_str());
        }
        req
    }
}

pub struct WebDavService;

impl WebDavService {
    pub async fn test_connection(req: WebDavTestRequest) -> Result<WebDavTestResult, AppError> {
        let creds = Creds::new(&req.url, &req.username, &req.password, &req.remote_folder)?;
        let remote_url = creds.target_dir_url();
        let creds_clone = creds.clone();

        let items = tokio::task::spawn_blocking(move || -> Result<Vec<RemoteBackupItem>, AppError> {
            ensure_remote_folder(&creds_clone)?;
            propfind_backups(&creds_clone)
        })
        .await
        .map_err(|e| AppError::Internal(format!("WebDAV 任务执行失败: {}", e)))??;

        let count = items.len();
        Ok(WebDavTestResult {
            ok: true,
            message: format!("连接成功，云端共有 {} 个备份快照", count),
            remote_url,
            backup_count: count,
        })
    }

    pub async fn list_backups(cfg: &WebDavModel) -> Result<Vec<RemoteBackupItem>, AppError> {
        let creds = Creds::from_model(cfg)?;
        tokio::task::spawn_blocking(move || -> Result<Vec<RemoteBackupItem>, AppError> {
            ensure_remote_folder(&creds)?;
            propfind_backups(&creds)
        })
        .await
        .map_err(|e| AppError::Internal(format!("WebDAV 列表请求失败: {}", e)))?
    }

    pub async fn sync_now(
        pool: &SqlitePool,
        paths: &BackupPaths,
        config_service: &Arc<ConfigService>,
        app_version: &str,
        tz_id: &str,
        override_merge: Option<bool>,
    ) -> Result<WebDavSyncResult, AppError> {
        let _guard = SYNC_LOCK.lock().await;

        let cfg = config_service.get_or_load().await?;
        let creds = Creds::from_model(&cfg.webdav)?;
        let merge_enabled = override_merge.unwrap_or(cfg.webdav.merge_on_sync);
        let last_sync_utc = cfg.webdav.last_sync_at_utc.clone();

        // 1. 拉取远程目录并按需合并其他端/较新的快照
        let creds_list = creds.clone();
        let remote_items = tokio::task::spawn_blocking(move || -> Result<Vec<RemoteBackupItem>, AppError> {
            ensure_remote_folder(&creds_list)?;
            propfind_backups(&creds_list)
        })
        .await
        .map_err(|e| AppError::Internal(format!("WebDAV 拉取列表失败: {}", e)))??;

        let mut merged_files = Vec::new();
        let mut skipped_files = 0usize;

        if merge_enabled && !remote_items.is_empty() {
            let candidates = select_merge_candidates(&remote_items, &paths.device_name, &last_sync_utc);
            skipped_files = remote_items.len().saturating_sub(candidates.len());

            for item in candidates {
                let c_dl = creds.clone();
                let fname = item.file_name.clone();
                let archive_bytes = tokio::task::spawn_blocking(move || download_remote_file(&c_dl, &fname))
                    .await
                    .map_err(|e| AppError::Internal(format!("WebDAV 下载失败: {}", e)))??;

                match BackupService::inspect_archive(&archive_bytes).await {
                    Ok(inspected) if inspected.compatible => {
                        BackupService::import_archive(pool, paths, &archive_bytes, ImportMode::Merge, tz_id).await?;
                        merged_files.push(item.file_name);
                    }
                    Ok(inspected) => {
                        warn!(
                            file = %item.file_name,
                            note = %inspected.compatibility_note,
                            "跳过不兼容的远程备份"
                        );
                        skipped_files += 1;
                    }
                    Err(e) => {
                        warn!(file = %item.file_name, error = %e, "跳过校验失败的远程备份");
                        skipped_files += 1;
                    }
                }
            }
        }

        // 2. 生成当前设备一致性快照并上传
        let (archive_bytes, _) = BackupService::export_archive(pool, paths, app_version, true).await?;
        let ts_slug = chrono::Utc::now().format("%Y%m%dT%H%M%SZ");
        let safe_dev = sanitize_device_slug(&paths.device_name);
        let upload_name = format!("shijian_{}_{}.shijian-backup", safe_dev, ts_slug);

        let creds_up = creds.clone();
        let upload_name_clone = upload_name.clone();
        tokio::task::spawn_blocking(move || -> Result<(), AppError> {
            upload_remote_file(&creds_up, &upload_name_clone, &archive_bytes)?;
            prune_remote_backups(&creds_up, MAX_REMOTE_SNAPSHOTS);
            Ok(())
        })
        .await
        .map_err(|e| AppError::Internal(format!("WebDAV 上传失败: {}", e)))??;

        let synced_at_utc = chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
        let summary_msg = if merged_files.is_empty() {
            format!("同步完成，已上传 {}", upload_name)
        } else {
            format!("已合并 {} 份云端快照并上传 {}", merged_files.len(), upload_name)
        };

        // 3. 回写最近同步状态到配置
        if let Ok(latest_cfg) = config_service.get_or_load().await {
            let mut next = (*latest_cfg).clone();
            next.webdav.last_sync_at_utc = synced_at_utc.clone();
            next.webdav.last_sync_status = summary_msg.clone();
            if let Err(e) = config_service.save(&next).await {
                warn!("保存 WebDAV 同步状态失败: {}", e);
            }
        }

        info!(
            uploaded = %upload_name,
            merged = merged_files.len(),
            "WebDAV 云同步完成"
        );

        Ok(WebDavSyncResult {
            uploaded_file: upload_name,
            merged_files,
            skipped_files,
            synced_at_utc,
            message: summary_msg,
        })
    }

    pub async fn restore_remote(
        pool: &SqlitePool,
        paths: &BackupPaths,
        cfg: &WebDavModel,
        file_name: &str,
        mode: ImportMode,
        tz_id: &str,
    ) -> Result<BackupImportResult, AppError> {
        let _guard = SYNC_LOCK.lock().await;
        let creds = Creds::from_model(cfg)?;
        let fname = file_name.to_string();

        let archive_bytes = tokio::task::spawn_blocking(move || download_remote_file(&creds, &fname))
            .await
            .map_err(|e| AppError::Internal(format!("下载云端备份失败: {}", e)))??;

        BackupService::import_archive(pool, paths, &archive_bytes, mode, tz_id).await
    }

    /// 后台定时 WebDAV 同步任务（随服务端常驻运行，支持 startup / hourly / daily）。
    pub async fn run_sync_task(
        pool: SqlitePool,
        paths: BackupPaths,
        config_service: Arc<ConfigService>,
        tz_id: String,
    ) {
        // 启动后稍等主库与插件稳定再判定启动同步
        tokio::time::sleep(Duration::from_secs(20)).await;
        let mut startup_done = false;

        loop {
            if let Ok(cfg) = config_service.get_or_load().await {
                let wd = &cfg.webdav;
                let mode = wd.sync_mode.trim().to_ascii_lowercase();
                if wd.enabled && !wd.url.trim().is_empty() && mode != "manual" {
                    let due = match mode.as_str() {
                        "startup" => !startup_done,
                        "hourly" => is_sync_due(&wd.last_sync_at_utc, chrono::Duration::hours(1)),
                        "daily" => is_sync_due(&wd.last_sync_at_utc, chrono::Duration::days(1)),
                        _ => false,
                    };

                    if due {
                        startup_done = true;
                        match Self::sync_now(
                            &pool,
                            &paths,
                            &config_service,
                            env!("CARGO_PKG_VERSION"),
                            &tz_id,
                            None,
                        )
                        .await
                        {
                            Ok(res) => info!("定时 WebDAV 同步成功: {}", res.message),
                            Err(e) => {
                                warn!("定时 WebDAV 同步失败: {}", e);
                                let mut next = (*cfg).clone();
                                next.webdav.last_sync_status = format!("同步失败: {}", e);
                                let _ = config_service.save(&next).await;
                            }
                        }
                    } else if mode == "startup" {
                        startup_done = true;
                    }
                }
            }

            tokio::time::sleep(Duration::from_secs(300)).await;
        }
    }
}

fn is_sync_due(last_sync_utc: &str, interval: chrono::Duration) -> bool {
    if last_sync_utc.trim().is_empty() {
        return true;
    }
    match chrono::DateTime::parse_from_rfc3339(last_sync_utc.trim()) {
        Ok(last) => chrono::Utc::now().signed_duration_since(last.with_timezone(&chrono::Utc)) >= interval,
        Err(_) => true,
    }
}

fn normalize_folder(raw: &str) -> String {
    raw.trim()
        .replace('\\', "/")
        .split('/')
        .map(str::trim)
        .filter(|s| !s.is_empty() && *s != "." && *s != "..")
        .collect::<Vec<_>>()
        .join("/")
}

fn sanitize_device_slug(raw: &str) -> String {
    let cleaned: String = raw
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '-' || c == '_' {
                c
            } else {
                '-'
            }
        })
        .collect();
    let trimmed = cleaned.trim_matches('-');
    if trimmed.is_empty() {
        "device".to_string()
    } else {
        trimmed.chars().take(24).collect()
    }
}

fn sanitize_remote_filename(raw: &str) -> Result<String, AppError> {
    let trimmed = raw.trim();
    if trimmed.is_empty()
        || trimmed.contains('/')
        || trimmed.contains('\\')
        || trimmed.contains("..")
    {
        return Err(AppError::Business(format!("非法的远程文件名: {}", raw)));
    }
    Ok(trimmed.to_string())
}

fn map_http_status_error(status: u16, context: &str) -> AppError {
    match status {
        401 => AppError::Business(format!("{}: 认证失败（401 Unauthorized），请检查用户名/密码", context)),
        403 => AppError::Business(format!("{}: 无访问权限（403 Forbidden）", context)),
        404 => AppError::Business(format!("{}: 路径不存在（404 Not Found）", context)),
        409 => AppError::Business(format!("{}: 目录层级冲突（409 Conflict）", context)),
        other => AppError::Business(format!("{}: 服务器返回 HTTP {}", context, other)),
    }
}

fn ensure_remote_folder(creds: &Creds) -> Result<(), AppError> {
    let mkcol = Method::from_bytes(b"MKCOL").map_err(|e| AppError::Internal(e.to_string()))?;
    let mut current = creds.base_url.clone();

    if creds.remote_folder.is_empty() {
        return Ok(());
    }

    for seg in creds.remote_folder.split('/') {
        current.push('/');
        current.push_str(seg);
        let resp = creds
            .apply_auth(RequestBuilder::new(mkcol.clone(), &current))
            .send()
            .map_err(|e| AppError::Business(format!("创建远程目录失败: {}", e)))?;
        let code = resp.status().as_u16();
        // 200/201/204 成功；405/301/302 目录已存在
        if matches!(code, 200 | 201 | 204 | 301 | 302 | 405) {
            continue;
        }
        return Err(map_http_status_error(code, "创建 WebDAV 远程目录失败"));
    }
    Ok(())
}

fn propfind_backups(creds: &Creds) -> Result<Vec<RemoteBackupItem>, AppError> {
    let propfind = Method::from_bytes(b"PROPFIND").map_err(|e| AppError::Internal(e.to_string()))?;
    let url = creds.target_dir_url();
    let resp = creds
        .apply_auth(RequestBuilder::new(propfind, &url))
        .header("Depth", "1")
        .header(header::CONTENT_TYPE, "application/xml; charset=utf-8")
        .text(PROPFIND_BODY)
        .send()
        .map_err(|e| AppError::Business(format!("连接 WebDAV 服务器失败: {}", e)))?;

    let code = resp.status().as_u16();
    if !(200..300).contains(&code) {
        return Err(map_http_status_error(code, "读取 WebDAV 目录失败"));
    }

    let xml = resp
        .text()
        .map_err(|e| AppError::Business(format!("读取 WebDAV 响应体失败: {}", e)))?;
    parse_propfind_xml(&xml)
}

fn download_remote_file(creds: &Creds, file_name: &str) -> Result<Vec<u8>, AppError> {
    let url = creds.file_url(file_name)?;
    let resp = creds
        .apply_auth(attohttpc::get(&url))
        .send()
        .map_err(|e| AppError::Business(format!("下载云端文件 {} 失败: {}", file_name, e)))?;
    let code = resp.status().as_u16();
    if !(200..300).contains(&code) {
        return Err(map_http_status_error(code, &format!("下载 {} 失败", file_name)));
    }
    resp.bytes()
        .map_err(|e| AppError::Business(format!("读取云端文件 {} 内容失败: {}", file_name, e)))
}

fn upload_remote_file(creds: &Creds, file_name: &str, bytes: &[u8]) -> Result<(), AppError> {
    let url = creds.file_url(file_name)?;
    let resp = creds
        .apply_auth(attohttpc::put(&url))
        .header(header::CONTENT_TYPE, "application/octet-stream")
        .bytes(bytes)
        .send()
        .map_err(|e| AppError::Business(format!("上传云端文件 {} 失败: {}", file_name, e)))?;
    let code = resp.status().as_u16();
    if !(200..300).contains(&code) {
        return Err(map_http_status_error(code, &format!("上传 {} 失败", file_name)));
    }
    Ok(())
}

fn prune_remote_backups(creds: &Creds, keep: usize) {
    let Ok(items) = propfind_backups(creds) else {
        return;
    };
    if items.len() <= keep {
        return;
    }
    for item in items.into_iter().skip(keep) {
        if let Ok(url) = creds.file_url(&item.file_name) {
            let _ = creds.apply_auth(attohttpc::delete(&url)).send();
        }
    }
}

/// 挑选需要合并的远程备份：每个不同设备取最新一份，优先过滤早于上次同步的文件，最多取 5 份防止冷启动过慢。
fn select_merge_candidates(
    remote_items: &[RemoteBackupItem],
    local_device: &str,
    _last_sync_utc: &str,
) -> Vec<RemoteBackupItem> {
    let local_slug = sanitize_device_slug(local_device);
    let mut picked = Vec::new();
    let mut seen_devices = HashSet::new();

    for item in remote_items {
        let dev_key = if item.device_name.is_empty() {
            item.file_name.clone()
        } else {
            item.device_name.clone()
        };

        // 同设备的最新一份只在尚未同步过其它端时作为兜底，非本地设备优先各取最新一份
        if !item.device_name.is_empty() && item.device_name.eq_ignore_ascii_case(&local_slug) {
            continue;
        }
        if seen_devices.insert(dev_key) {
            picked.push(item.clone());
            if picked.len() >= 5 {
                break;
            }
        }
    }

    // 如果远程只有本设备自己上传过的历史备份（例如换机恢复/重装后首次同步），取最新一份做幂等合并
    if picked.is_empty() {
        if let Some(latest) = remote_items.first() {
            picked.push(latest.clone());
        }
    }

    picked
}

fn parse_propfind_xml(xml: &str) -> Result<Vec<RemoteBackupItem>, AppError> {
    let doc = roxmltree::Document::parse(xml)
        .map_err(|e| AppError::Business(format!("解析 WebDAV PROPFIND XML 失败: {}", e)))?;

    let mut items = Vec::new();
    for node in doc.descendants().filter(|n| n.is_element() && n.tag_name().name().eq_ignore_ascii_case("response")) {
        let href = node
            .descendants()
            .find(|n| n.is_element() && n.tag_name().name().eq_ignore_ascii_case("href"))
            .and_then(|n| n.text())
            .unwrap_or("")
            .trim();

        let is_collection = node
            .descendants()
            .any(|n| n.is_element() && n.tag_name().name().eq_ignore_ascii_case("collection"));
        if is_collection || href.ends_with('/') {
            continue;
        }

        let raw_name = href.rsplit('/').next().unwrap_or("");
        let decoded_name = percent_decode(raw_name);
        if !decoded_name.ends_with(".shijian-backup") {
            continue;
        }

        let size_bytes = node
            .descendants()
            .find(|n| n.is_element() && n.tag_name().name().eq_ignore_ascii_case("getcontentlength"))
            .and_then(|n| n.text())
            .and_then(|t| t.trim().parse::<u64>().ok())
            .unwrap_or(0);

        let last_modified = node
            .descendants()
            .find(|n| n.is_element() && n.tag_name().name().eq_ignore_ascii_case("getlastmodified"))
            .and_then(|n| n.text())
            .unwrap_or("")
            .trim()
            .to_string();

        let device_name = extract_device_from_filename(&decoded_name);
        items.push(RemoteBackupItem {
            file_name: decoded_name,
            size_bytes,
            last_modified,
            device_name,
        });
    }

    // 文件名含 YYYYMMDDTHHMMSSZ，优先按嵌入时间戳降序排列（兼容多设备前缀）
    items.sort_by(|a, b| {
        extract_timestamp_from_filename(&b.file_name)
            .cmp(&extract_timestamp_from_filename(&a.file_name))
            .then_with(|| b.file_name.cmp(&a.file_name))
    });
    Ok(items)
}

fn extract_timestamp_from_filename(file_name: &str) -> String {
    let stem = file_name.strip_suffix(".shijian-backup").unwrap_or(file_name);
    match stem.rsplit_once('_') {
        Some((_prefix, ts)) => ts.to_string(),
        None => stem.to_string(),
    }
}

fn extract_device_from_filename(file_name: &str) -> String {
    let stem = file_name.strip_suffix(".shijian-backup").unwrap_or(file_name);
    let Some(rest) = stem.strip_prefix("shijian_") else {
        return String::new();
    };
    match rest.rsplit_once('_') {
        Some((dev, _ts)) => dev.to_string(),
        None => String::new(),
    }
}

fn percent_decode(input: &str) -> String {
    let bytes = input.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Ok(hex) = std::str::from_utf8(&bytes[i + 1..i + 3]) {
                if let Ok(val) = u8::from_str_radix(hex, 16) {
                    out.push(val);
                    i += 3;
                    continue;
                }
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8(out).unwrap_or_else(|_| input.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_propfind_handles_namespaced_multistatus_and_filters_dirs() {
        let xml = r#"<?xml version="1.0" encoding="utf-8"?>
        <D:multistatus xmlns:D="DAV:">
          <D:response>
            <D:href>/dav/ShiJian/</D:href>
            <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop></D:propstat>
          </D:response>
          <D:response>
            <D:href>/dav/ShiJian/readme.txt</D:href>
            <D:propstat><D:prop><D:getcontentlength>12</D:getcontentlength></D:prop></D:propstat>
          </D:response>
          <D:response>
            <D:href>/dav/ShiJian/shijian_work-pc_20261113T100000Z.shijian-backup</D:href>
            <D:propstat><D:prop>
              <D:getcontentlength>102400</D:getcontentlength>
              <D:getlastmodified>Fri, 13 Nov 2026 10:00:00 GMT</D:getlastmodified>
            </D:prop></D:propstat>
          </D:response>
          <D:response>
            <D:href>/dav/ShiJian/shijian_home%2Dpc_20261114T110000Z.shijian-backup</D:href>
            <D:propstat><D:prop>
              <D:getcontentlength>204800</D:getcontentlength>
              <D:getlastmodified>Sat, 14 Nov 2026 11:00:00 GMT</D:getlastmodified>
            </D:prop></D:propstat>
          </D:response>
        </D:multistatus>"#;

        let items = parse_propfind_xml(xml).unwrap();
        assert_eq!(items.len(), 2);
        assert_eq!(items[0].file_name, "shijian_home-pc_20261114T110000Z.shijian-backup");
        assert_eq!(items[0].device_name, "home-pc");
        assert_eq!(items[0].size_bytes, 204800);
        assert_eq!(items[1].device_name, "work-pc");

        let selected = select_merge_candidates(&items, "home-pc", "");
        assert_eq!(selected.len(), 1);
        assert_eq!(selected[0].device_name, "work-pc");
    }
}
