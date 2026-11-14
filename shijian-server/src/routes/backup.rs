use axum::{
    body::Bytes,
    extract::{DefaultBodyLimit, Extension, Query, State},
    http::{header, HeaderValue, StatusCode},
    response::{IntoResponse, Response},
    routing::{get, post},
    Json, Router,
};
use serde::Deserialize;
use sqlx::SqlitePool;
use std::sync::Arc;

use crate::error::AppError;
use crate::response::ApiResponse;
use crate::services::backup::{
    BackupImportResult, BackupInspectResult, BackupPaths, BackupService, ImportMode, RollbackItem,
};
use crate::services::config::ConfigService;
use crate::services::webdav::{
    RemoteBackupItem, WebDavService, WebDavSyncResult, WebDavTestRequest, WebDavTestResult,
};
use crate::utils;

const MAX_BACKUP_BODY_BYTES: usize = 512 * 1024 * 1024;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ExportQuery {
    #[serde(default = "default_true")]
    include_icons: bool,
    #[serde(default)]
    app_version: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ImportQuery {
    mode: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RollbackRequest {
    file_name: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct WebDavSyncRequest {
    merge_on_sync: Option<bool>,
    app_version: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct WebDavRestoreRequest {
    file_name: String,
    mode: Option<String>,
}

fn default_true() -> bool {
    true
}

pub fn router() -> Router<SqlitePool> {
    let upload_routes = Router::new()
        .route("/api/data/backup/inspect", post(inspect_backup))
        .route("/api/data/backup/import", post(import_backup))
        .layer(DefaultBodyLimit::max(MAX_BACKUP_BODY_BYTES));

    Router::new()
        .route("/api/data/backup/export", get(export_backup))
        .route("/api/data/backup/rollbacks", get(list_rollbacks))
        .route("/api/data/backup/rollback", post(rollback_backup))
        .route("/api/webdav/test", post(test_webdav))
        .route("/api/webdav/sync", post(sync_webdav))
        .route("/api/webdav/backups", get(list_webdav_backups))
        .route("/api/webdav/restore", post(restore_webdav_backup))
        .merge(upload_routes)
}

async fn export_backup(
    State(pool): State<SqlitePool>,
    Extension(paths): Extension<Arc<BackupPaths>>,
    Query(query): Query<ExportQuery>,
) -> Result<Response, AppError> {
    let (bytes, _manifest) = BackupService::export_archive(
        &pool,
        &paths,
        &query.app_version,
        query.include_icons,
    )
    .await?;

    let filename = format!(
        "ShiJian_backup_{}.shijian-backup",
        chrono::Local::now().format("%Y%m%d_%H%M%S")
    );
    let disposition = format!("attachment; filename=\"{}\"", filename);

    let mut resp = (StatusCode::OK, bytes).into_response();
    let headers = resp.headers_mut();
    headers.insert(
        header::CONTENT_TYPE,
        HeaderValue::from_static("application/octet-stream"),
    );
    if let Ok(val) = HeaderValue::from_str(&disposition) {
        headers.insert(header::CONTENT_DISPOSITION, val);
    }
    Ok(resp)
}

async fn inspect_backup(body: Bytes) -> Result<Json<ApiResponse<BackupInspectResult>>, AppError> {
    if body.is_empty() {
        return Err(AppError::Business("上传的备份文件为空".to_string()));
    }
    let result = BackupService::inspect_archive(&body).await?;
    Ok(Json(ApiResponse::ok(result)))
}

async fn import_backup(
    State(pool): State<SqlitePool>,
    Extension(paths): Extension<Arc<BackupPaths>>,
    Query(query): Query<ImportQuery>,
    body: Bytes,
) -> Result<Json<ApiResponse<BackupImportResult>>, AppError> {
    if body.is_empty() {
        return Err(AppError::Business("上传的备份文件为空".to_string()));
    }
    let mode = ImportMode::parse(query.mode.as_deref())?;
    let tz_id = utils::host_timezone().name().to_string();
    let result = BackupService::import_archive(&pool, &paths, &body, mode, &tz_id).await?;
    Ok(Json(ApiResponse::ok(result)))
}

async fn list_rollbacks(
    Extension(paths): Extension<Arc<BackupPaths>>,
) -> Result<Json<ApiResponse<Vec<RollbackItem>>>, AppError> {
    let items = BackupService::list_rollbacks(&paths).await?;
    Ok(Json(ApiResponse::ok(items)))
}

async fn rollback_backup(
    State(pool): State<SqlitePool>,
    Extension(paths): Extension<Arc<BackupPaths>>,
    Json(req): Json<RollbackRequest>,
) -> Result<Json<ApiResponse<BackupImportResult>>, AppError> {
    let tz_id = utils::host_timezone().name().to_string();
    let result = BackupService::rollback_from_file(&pool, &paths, &req.file_name, &tz_id).await?;
    Ok(Json(ApiResponse::ok(result)))
}

async fn test_webdav(
    Json(req): Json<WebDavTestRequest>,
) -> Result<Json<ApiResponse<WebDavTestResult>>, AppError> {
    let res = WebDavService::test_connection(req).await?;
    Ok(Json(ApiResponse::ok(res)))
}

async fn sync_webdav(
    State(pool): State<SqlitePool>,
    Extension(paths): Extension<Arc<BackupPaths>>,
    Extension(config_service): Extension<Arc<ConfigService>>,
    req: Option<Json<WebDavSyncRequest>>,
) -> Result<Json<ApiResponse<WebDavSyncResult>>, AppError> {
    let (merge_override, app_ver) = match req {
        Some(Json(body)) => (
            body.merge_on_sync,
            body.app_version.unwrap_or_else(|| env!("CARGO_PKG_VERSION").to_string()),
        ),
        None => (None, env!("CARGO_PKG_VERSION").to_string()),
    };
    let tz_id = utils::host_timezone().name().to_string();
    let res = WebDavService::sync_now(&pool, &paths, &config_service, &app_ver, &tz_id, merge_override).await?;
    Ok(Json(ApiResponse::ok(res)))
}

async fn list_webdav_backups(
    Extension(config_service): Extension<Arc<ConfigService>>,
) -> Result<Json<ApiResponse<Vec<RemoteBackupItem>>>, AppError> {
    let cfg = config_service.get_or_load().await?;
    let items = WebDavService::list_backups(&cfg.webdav).await?;
    Ok(Json(ApiResponse::ok(items)))
}

async fn restore_webdav_backup(
    State(pool): State<SqlitePool>,
    Extension(paths): Extension<Arc<BackupPaths>>,
    Extension(config_service): Extension<Arc<ConfigService>>,
    Json(req): Json<WebDavRestoreRequest>,
) -> Result<Json<ApiResponse<BackupImportResult>>, AppError> {
    let cfg = config_service.get_or_load().await?;
    let mode = ImportMode::parse(req.mode.as_deref())?;
    let tz_id = utils::host_timezone().name().to_string();
    let res = WebDavService::restore_remote(&pool, &paths, &cfg.webdav, &req.file_name, mode, &tz_id).await?;
    Ok(Json(ApiResponse::ok(res)))
}
