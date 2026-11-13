import type {
  ApiResponse, AppItem, CategoryItem, MergeGroupItem, PluginDetail, PluginListItem,
} from './types';
import { t } from './i18n.svelte';

// 按状态码区分失败原因：空响应体的 404 是最常见的一种——
// 后台服务在跑，但二进制比 webui 旧、不含该路由（编译产物未更新）
function httpError(status: number): Error {
  if (status === 404) return new Error(t('err.api404', { at: location.host }));
  if (status === 502 || status === 503) return new Error(t('err.busy', { status }));
  return new Error(t('err.http', { status, at: location.host }));
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let resp: Response;
  try {
    resp = await fetch(path, {
      method,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new Error(t('err.connect', { host: location.host }));
  }
  let api: ApiResponse<T>;
  try {
    api = (await resp.json()) as ApiResponse<T>;
  } catch {
    throw httpError(resp.status);
  }
  if (api.code !== 0) throw new Error(api.message || `HTTP ${resp.status}`);
  return api.data as T;
}

export const api = {
  listPlugins: () => request<PluginListItem[]>('GET', '/api/plugins'),
  getPlugin: (id: string) => request<PluginDetail>('GET', `/api/plugins/${id}`),
  saveConfig: (id: string, values: Record<string, unknown>) =>
    request<{ configVersion: number }>('PUT', `/api/plugins/${id}/config`, { values }),
  setEnabled: (id: string, enabled: boolean) =>
    request('POST', `/api/plugins/${id}/enabled`, { enabled }),
  postCommand: (id: string, cmd: string, data?: unknown) =>
    request('POST', `/api/plugins/${id}/commands`, { cmd, data }),
  // 读插件记录：返回该 key 最新一条的 value，无记录返回 null
  getRecord: (id: string, key: string) =>
    request<{ key: string; value: unknown }[]>(
      'GET',
      `/api/plugins/${id}/records?key=${encodeURIComponent(key)}`,
    ).then((rows) => (Array.isArray(rows) && rows.length ? rows[0].value : null)),
  listCategories: () => request<CategoryItem[]>('GET', '/api/category'),
  listMergeGroups: () =>
    request<{ groups: MergeGroupItem[] }>('GET', '/api/entity/merge-groups').then((r) => r.groups ?? []),
  listApps: async () => {
    const [apps, groups] = await Promise.all([
      request<AppItem[]>('GET', '/api/appdata'),
      request<{ groups: MergeGroupItem[] }>('GET', '/api/entity/merge-groups')
        .then((r) => r.groups ?? [])
        .catch(() => [] as MergeGroupItem[]),
    ]);
    if (!groups.length || !apps.length) return apps;
    const byName = new Map(apps.filter((a) => a.name).map((a) => [a.name!, a]));
    const parseApp = (raw: string) => {
      const t = raw.trim();
      if (!t || t.startsWith('site:')) return null;
      return t.startsWith('app:') ? t.slice(4).trim() : t;
    };
    const memberToGroup = new Map<string, MergeGroupItem>();
    for (const g of groups) {
      if (g.kind !== 'app' && g.kind !== 'mixed') continue;
      for (const m of g.members) {
        const n = parseApp(m);
        if (n) memberToGroup.set(n, g);
      }
    }
    const emitted = new Set<number>();
    const out: AppItem[] = [];
    for (const app of apps) {
      const g = app.name ? memberToGroup.get(app.name) : undefined;
      if (!g) {
        out.push(app);
        continue;
      }
      if (emitted.has(g.id)) continue;
      emitted.add(g.id);
      const members = g.members
        .map(parseApp)
        .filter((n): n is string => Boolean(n))
        .map((n) => byName.get(n))
        .filter((a): a is AppItem => Boolean(a));
      if (!members.length) continue;
      const pName = parseApp(g.primaryRef);
      const primary = (pName ? byName.get(pName) : undefined)
        ?? members.slice().sort((a, b) => b.totalTime - a.totalTime)[0];
      out.push({
        id: primary.id,
        name: primary.name,
        alias: g.name || primary.alias,
        totalTime: members.reduce((sum, item) => sum + item.totalTime, 0),
      });
    }
    return out;
  },
};

/** 插件自带配置 UI 的通用请求入口，getJson/postJson 转发到它 */
export { request as rawRequest };
