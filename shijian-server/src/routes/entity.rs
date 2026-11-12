use axum::{
    extract::{Extension, Query, State},
    routing::{get, post},
    Json, Router,
};
use chrono::NaiveDateTime;
use serde::Deserialize;
use sqlx::SqlitePool;
use std::sync::Arc;

use crate::models::entity::{BrowserOptionModel, EntityListModel, MergeGroupListModel};
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
