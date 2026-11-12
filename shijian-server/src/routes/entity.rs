use axum::{
    extract::{Extension, Query, State},
    routing::{get, post},
    Json, Router,
};
use chrono::NaiveDateTime;
use serde::Deserialize;
use sqlx::SqlitePool;
use std::sync::Arc;

use crate::models::entity::{
    BrowserOptionModel, EntityHoursModel, EntityListModel, MergeGroupListModel,
};
use crate::response::ApiResponse;
use crate::services::config::ConfigService;
use crate::services::entity::EntityService;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct EntityListQuery {
    start: NaiveDateTime,
    end: NaiveDateTime,
    #[serde(default)]
    browser_merged: bool,
    timezone: String,
    /// 只要前 N 行，其余归并成「其他」；缺省返回全量
    take: Option<usize>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct EntityHoursQuery {
    start: NaiveDateTime,
    end: NaiveDateTime,
    /// hour（单日 24 桶）/ day（区间每天）/ month（整年 12 桶）
    #[serde(default = "default_buckets")]
    buckets: String,
    #[serde(default)]
    browser_merged: bool,
    timezone: String,
}

fn default_buckets() -> String {
    "day".to_string()
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct GroupIdRequest {
    group_id: i64,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RenameGroupRequest {
    group_id: i64,
    name: String,
}

pub fn router() -> Router<SqlitePool> {
    Router::new()
        .route("/api/entity/list", get(get_entity_list))
        .route("/api/entity/hours", get(get_entity_hours))
        .route("/api/entity/browsers", get(get_browsers))
        .route("/api/entity/merge-groups", get(get_merge_groups))
        .route("/api/entity/merge-groups/dissolve", post(dissolve_group))
        .route("/api/entity/merge-groups/rename", post(rename_group))
        .route("/api/entity/merge-groups/restore", post(restore_groups))
}

async fn get_entity_list(
    State(pool): State<SqlitePool>,
    Extension(config_service): Extension<Arc<ConfigService>>,
    Query(q): Query<EntityListQuery>,
) -> Json<ApiResponse<EntityListModel>> {
    match EntityService::list(
        &pool,
        &config_service,
        q.start.date(),
        q.end.date(),
        &q.timezone,
        q.browser_merged,
        q.take,
    )
    .await
    {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn get_entity_hours(
    State(pool): State<SqlitePool>,
    Extension(config_service): Extension<Arc<ConfigService>>,
    Query(q): Query<EntityHoursQuery>,
) -> Json<ApiResponse<EntityHoursModel>> {
    match EntityService::hours(
        &pool,
        &config_service,
        q.start.date(),
        q.end.date(),
        &q.timezone,
        &q.buckets,
        q.browser_merged,
    )
    .await
    {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn get_browsers(
    State(pool): State<SqlitePool>,
    Extension(config_service): Extension<Arc<ConfigService>>,
) -> Json<ApiResponse<Vec<BrowserOptionModel>>> {
    match EntityService::browsers(&pool, &config_service).await {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn get_merge_groups(State(pool): State<SqlitePool>) -> Json<ApiResponse<MergeGroupListModel>> {
    match EntityService::merge_groups(&pool).await {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn dissolve_group(
    State(pool): State<SqlitePool>,
    Json(req): Json<GroupIdRequest>,
) -> Json<ApiResponse<()>> {
    match EntityService::dissolve_group(&pool, req.group_id).await {
        Ok(()) => Json(ApiResponse::ok_empty()),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn rename_group(
    State(pool): State<SqlitePool>,
    Json(req): Json<RenameGroupRequest>,
) -> Json<ApiResponse<()>> {
    match EntityService::rename_group(&pool, req.group_id, &req.name).await {
        Ok(()) => Json(ApiResponse::ok_empty()),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn restore_groups(State(pool): State<SqlitePool>) -> Json<ApiResponse<()>> {
    match EntityService::restore_suppressed(&pool).await {
        Ok(()) => Json(ApiResponse::ok_empty()),
        Err(e) => Json(ApiResponse {
            code: 500,
            message: e.to_string(),
            data: None,
        }),
    }
}
