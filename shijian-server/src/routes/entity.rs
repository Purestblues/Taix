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
    BrowserOptionModel, EntityHoursModel, EntityListModel, EntitySummaryModel, MergeGroupListModel,
    MergeGroupModel,
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

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct EntitySummaryQuery {
    start: NaiveDateTime,
    end: NaiveDateTime,
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

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct CreateGroupRequest {
    #[serde(default)]
    name: String,
    #[serde(default)]
    primary_ref: String,
    members: Vec<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct UpdateGroupRequest {
    group_id: i64,
    name: Option<String>,
    primary_ref: Option<String>,
    members: Option<Vec<String>>,
}

pub fn router() -> Router<SqlitePool> {
    Router::new()
        .route("/api/entity/list", get(get_entity_list))
        .route("/api/entity/hours", get(get_entity_hours))
        .route("/api/entity/summary", get(get_entity_summary))
        .route("/api/entity/browsers", get(get_browsers))
        .route("/api/entity/merge-groups", get(get_merge_groups))
        .route("/api/entity/merge-groups/create", post(create_group))
        .route("/api/entity/merge-groups/update", post(update_group))
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

async fn get_entity_summary(
    State(pool): State<SqlitePool>,
    Extension(config_service): Extension<Arc<ConfigService>>,
    Query(q): Query<EntitySummaryQuery>,
) -> Json<ApiResponse<EntitySummaryModel>> {
    match EntityService::summary(
        &pool,
        &config_service,
        q.start.date(),
        q.end.date(),
        &q.timezone,
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

async fn create_group(
    State(pool): State<SqlitePool>,
    Json(req): Json<CreateGroupRequest>,
) -> Json<ApiResponse<MergeGroupModel>> {
    match EntityService::create_group(&pool, &req.name, &req.primary_ref, &req.members).await {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 400,
            message: e.to_string(),
            data: None,
        }),
    }
}

async fn update_group(
    State(pool): State<SqlitePool>,
    Json(req): Json<UpdateGroupRequest>,
) -> Json<ApiResponse<MergeGroupModel>> {
    match EntityService::update_group(
        &pool,
        req.group_id,
        req.name.as_deref(),
        req.primary_ref.as_deref(),
        req.members.as_deref(),
    )
    .await
    {
        Ok(data) => Json(ApiResponse::ok(data)),
        Err(e) => Json(ApiResponse {
            code: 400,
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
