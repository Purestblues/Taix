export interface ActivePage {
  url: string;
  title: string;
  icon: string;
  startTime: number;
}

export interface BrowseData {
  Url: string;
  Title: string;
  Icon: string;
  Duration: number;
  ActiveTime: number;
  /** 构建期注入的浏览器标识（chrome / firefox / safari），服务端据此把站点归属到浏览器 */
  Browser: string;
}

export interface SavedState {
  activePage?: ActivePage;
  isSleep?: boolean;
  reconnectFail?: number;
  notifyFailList?: BrowseData[];
}

export interface TabInfo {
  id: number;
  windowId: number;
  url?: string;
  title?: string;
  favIconUrl?: string;
  active: boolean;
}

export interface EventListener<T> {
  addListener: (callback: (arg: T, ...args: unknown[]) => void) => void;
  removeListener?: (callback: (arg: T, ...args: unknown[]) => void) => void;
}

export interface IconDetails {
  path: { [key: string]: string };
}

export interface AlarmInfo {
  periodInMinutes?: number;
  delayInMinutes?: number;
}

export interface Alarm {
  name: string;
}

export interface TabActiveInfo {
  tabId: number;
  windowId: number;
}

export interface TabChangeInfo {
  status?: string;
  url?: string;
}

export interface NavigationDetails {
  tabId: number;
  frameId: number;
  url: string;
}

export type BrowserType = 'chrome' | 'firefox' | 'safari';

export interface VisibilityMessage {
  type: 'visibility';
  state: 'visible' | 'hidden';
  url?: string;
}