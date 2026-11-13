/** 宿主传给 mount(el, api) 的主题信息 */
export interface PluginUiTheme {
  choice: 'auto' | 'light' | 'dark';
  resolved: 'light' | 'dark';
}

/** 界面语言，auto 在宿主侧就解析成 zh 或 en */
export type PluginUiLocale = 'zh' | 'en';

/** 清单 configSchema 的字段描述，本 UI 只做类型参考 */
export interface ConfigField {
  key: string;
  label: string;
  type: 'string' | 'text' | 'number' | 'bool' | 'select' | 'json';
  min?: number;
  max?: number;
  options?: string[];
  default?: unknown;
}

/** 宿主 API，配置页在挂载时将实例传入 */
export interface ShiJianPluginUiApi {
  id: string;
  version: string;
  schema: ConfigField[];
  /** 挂载时的配置快照，非响应式；写回一律走 patch */
  values: Record<string, unknown>;
  /** 合并改动进宿主草稿，脏检测与保存栏基于它 */
  patch(partial: Record<string, unknown>): void;
  theme: PluginUiTheme;
  onTheme(cb: (t: PluginUiTheme) => void): () => void;
  locale: PluginUiLocale;
  onLocale(cb: (l: PluginUiLocale) => void): () => void;
  /** 非致命错误：显示在宿主错误条，不触发回退 */
  reportError(msg: string | null): void;
  /** 同源 REST 便捷封装：自动解 {code,message,data}，失败抛 Error */
  getJson(path: string): Promise<unknown>;
  postJson(path: string, body?: unknown): Promise<unknown>;
}

/** 应用选择项，来自 GET /api/appdata */
export interface AppOption {
  name: string;
  alias: string | null;
  totalTime: number;
}

export interface MergeGroupPayload {
  id: number;
  name: string;
  kind: string;
  primaryRef: string;
  members: string[];
}

function parseAppMemberName(raw: string): string | null {
  const trimmed = raw.trim();
  if (!trimmed || trimmed.startsWith('site:')) return null;
  return trimmed.startsWith('app:') ? trimmed.slice(4).trim() : trimmed;
}

/** 按服务端合并组折叠应用选择项（保留母成员名作为匹配词，累加同组应用时长） */
export function foldAppOptions(
  apps: AppOption[],
  groups: MergeGroupPayload[],
): AppOption[] {
  if (!groups.length || !apps.length) return apps;

  const byName = new Map<string, AppOption>();
  for (const app of apps) byName.set(app.name, app);

  const memberToGroup = new Map<string, MergeGroupPayload>();
  for (const group of groups) {
    if (group.kind !== 'app' && group.kind !== 'mixed') continue;
    for (const raw of group.members) {
      const appName = parseAppMemberName(raw);
      if (appName) memberToGroup.set(appName, group);
    }
  }

  const emittedGroups = new Set<number>();
  const result: AppOption[] = [];

  for (const app of apps) {
    const group = memberToGroup.get(app.name);
    if (!group) {
      result.push(app);
      continue;
    }
    if (emittedGroups.has(group.id)) continue;
    emittedGroups.add(group.id);

    const groupApps = group.members
      .map(parseAppMemberName)
      .filter((n): n is string => Boolean(n))
      .map((n) => byName.get(n))
      .filter((a): a is AppOption => Boolean(a));

    if (!groupApps.length) continue;

    const primaryName = parseAppMemberName(group.primaryRef);
    const primary = (primaryName ? byName.get(primaryName) : undefined)
      ?? groupApps.slice().sort((a, b) => b.totalTime - a.totalTime)[0];

    result.push({
      name: primary.name,
      alias: group.name || primary.alias,
      totalTime: groupApps.reduce((sum, item) => sum + item.totalTime, 0),
    });
  }

  return result;
}

export function postCommand(api: ShiJianPluginUiApi, cmd: string, data?: unknown): Promise<unknown> {
  return api.postJson(`/api/plugins/${api.id}/commands`, { cmd, data });
}

/** 读插件记录：返回该 key 最新一条的 value，无记录返回 null */
export async function getRecord<T>(api: ShiJianPluginUiApi, key: string): Promise<T | null> {
  const rows = (await api.getJson(
    `/api/plugins/${api.id}/records?key=${encodeURIComponent(key)}`,
  )) as { value: T }[] | null;
  return Array.isArray(rows) && rows.length ? rows[0].value : null;
}

/** 分类名列表，来自 GET /api/category */
export async function listCategories(api: ShiJianPluginUiApi): Promise<string[]> {
  const rows = (await api.getJson('/api/category')) as { name: string | null }[];
  return rows.map((c) => c.name ?? '').filter(Boolean);
}

/** 应用列表，用于匹配词选择器 */
export async function listApps(api: ShiJianPluginUiApi): Promise<AppOption[]> {
  const [rows, mergeResp] = await Promise.all([
    api.getJson('/api/appdata') as Promise<{
      name: string | null;
      alias: string | null;
      totalTime?: number;
    }[]>,
    api.getJson('/api/entity/merge-groups')
      .then((res) => (res as { groups?: MergeGroupPayload[] } | null)?.groups ?? [])
      .catch(() => [] as MergeGroupPayload[]),
  ]);
  const apps = rows.flatMap((a) =>
    a.name ? [{ name: a.name, alias: a.alias, totalTime: a.totalTime ?? 0 }] : [],
  );
  return foldAppOptions(apps, mergeResp);
}
