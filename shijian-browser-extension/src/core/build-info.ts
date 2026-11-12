import { BrowseData } from '../types';

// 构建期由 scripts/build.js 通过 esbuild `define` 注入（chrome / firefox / safari）。
// 一份产物只对应一个浏览器，所以浏览器标识是「零猜测」的常量，不做运行期探测：
// WebExtension 拿不到可靠的「我在哪个浏览器里跑」的 API，探测反而会误判。
declare const __SHIJIAN_BROWSER__: string;

export const BROWSER_NAME: string =
  typeof __SHIJIAN_BROWSER__ === 'string' && __SHIJIAN_BROWSER__ ? __SHIJIAN_BROWSER__ : 'unknown';

/** 统一构造上报体，浏览器标识只在这里贴一次，避免三处漏写 */
export function createBrowseData(
  url: string,
  title: string,
  icon: string,
  duration: number,
  activeTime: number,
): BrowseData {
  return {
    Url: url,
    Title: title,
    Icon: icon,
    Duration: duration,
    ActiveTime: activeTime,
    Browser: BROWSER_NAME,
  };
}
