use serde::{Deserialize, Serialize};

/// 统一实体表的一行（N2）。
///
/// `kind`：
/// - `app`：普通应用（若在合并组里，则是组行）
/// - `site`：网站实体（`browserMerged=0` 时独立成行）
/// - `other`：浏览器内其他页面（差额行，`browserMerged=0` 时替代浏览器行）
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityModel {
    /// 稳定标识：`app:12` / `site:5` / `other:3`
    pub key: String,
    pub kind: String,
    pub name: String,
    pub icon: Option<String>,
    pub seconds: i64,
    /// 分类命名空间（应用分类 / 网站分类是两套自增 ID）
    pub category_id: i64,
    pub category_kind: String,
    pub category_name: Option<String>,
    pub category_color: Option<String>,
    /// 这一行是不是浏览器（差额行也带，用于说明它属于哪个浏览器）
    pub is_browser: bool,
    /// 合并组成员 / 浏览器内被并入的站点；母成员不在里面
    pub members: Vec<EntityMemberModel>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityMemberModel {
    pub key: String,
    pub name: String,
    pub seconds: i64,
    pub icon: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityListModel {
    pub rows: Vec<EntityModel>,
    /// 行时长之和（= `app_seconds` + `unmatched_seconds`）
    pub total_seconds: i64,
    /// 应用侧总时长（排除名单已扣除）
    pub app_seconds: i64,
    /// 网站侧总时长（已按互斥归属裁剪，含并入浏览器的部分）
    pub site_seconds: i64,
    /// 差额行合计（浏览器内其他页面）
    pub balance_seconds: i64,
    /// 站点时长超出浏览器会话覆盖的部分（数据异常计数，正常应接近 0）
    pub overflow_seconds: i64,
    /// 找不到归属浏览器的站点时长（默认浏览器也没跑），此时不退化成差额行
    pub unmatched_seconds: i64,
    pub browser_merged: bool,
    pub default_browser: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BrowserOptionModel {
    /// 浏览器标识（chrome / firefox / safari / …）
    pub reference: String,
    /// 对应的应用实体 key（应用侧没识别到时为 null）
    pub app_key: Option<String>,
    pub app_name: Option<String>,
    pub seconds: i64,
    pub is_default: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MergeGroupModel {
    pub id: i64,
    pub name: String,
    pub kind: String,
    pub is_auto: bool,
    pub primary_ref: String,
    pub members: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MergeGroupListModel {
    pub groups: Vec<MergeGroupModel>,
    pub suppressed: Vec<String>,
}

/// `/api/entity/hours` 的一行：一个实体在时间轴上的时长分布
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityHoursRow {
    pub key: String,
    pub kind: String,
    pub name: String,
    pub icon: Option<String>,
    pub category_id: i64,
    pub category_kind: String,
    pub category_name: Option<String>,
    pub category_color: Option<String>,
    pub is_browser: bool,
    /// 与 `labels` 一对一，单位秒
    pub values: Vec<i64>,
    pub members: Vec<EntityMemberModel>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EntityHoursModel {
    /// 桶边界（本地时间 ISO），客户端切换周期时用它对齐坐标轴
    pub labels: Vec<String>,
    pub rows: Vec<EntityHoursRow>,
    pub browser_merged: bool,
    pub bucket_count: i32,
}
