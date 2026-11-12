//! 子母程序合并的判定规则（N6 / N2）。
//!
//! 判定输入：可执行文件的安装目录锚点 + 品牌（进程名里第一个版本号之前的词）+ 发布者（CompanyName）。
//! 输出：若干自动组，每组一个母成员（时长最长者），其余成员折叠进母成员。
//!
//! 规则与客户端 `Librarys/AppMergePlanner` 对齐（`fork.9` 的实现），
//! `fork.10` 起把判定与组表搬到服务端：页面拿到的是「已经合并好的实体行」，
//! 客户端不再自己合并（否则同一段时长会被两套规则各并一次）。
//!
//! 规则顺序：
//! 1. 锚点 = 安装目录（去掉末尾版本号段 / bin / x64 之类打包层级；macOS 取 `.app` 包）；
//! 2. 品牌 = 进程名里第一个版本号之前的词（`PiDeck-0.9.5-setup` → `PiDeck`）；
//! 3. 同锚点 + 同品牌 → 一组；品牌缺失时退回「同锚点 + 同发布者」；
//! 4. 排除浏览器、系统目录、`setup` / `uninstall` 这类无辨识度品牌；
//! 5. 两个成员都带发布者但发布者不同 → 不合并（防止共用目录的不同厂商被并到一起）；
//! 6. 用户的解散（`EntitySuppressions`）优先于自动判定。

use std::collections::{HashMap, HashSet};

/// 参与合并判定的应用（只取判定需要的字段，便于单测构造）
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct MergeCandidate {
    pub name: String,
    pub file: Option<String>,
    pub publisher: Option<String>,
    pub seconds: i64,
}

/// 一个自动组：`key` 是稳定锚点（`目录|品牌`），`members` 第一个是母成员（时长最长）
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AutoGroup {
    pub key: String,
    pub name: String,
    pub members: Vec<String>,
}

/// 不参与自动合并的浏览器（它们的子进程是渲染器，合并了看不出谁在用）
const BROWSER_STEMS: [&str; 14] = [
    "chrome", "msedge", "firefox", "safari", "opera", "brave", "vivaldi", "iexplore",
    "chromium", "thorium", "tor", "arc", "360se", "360chrome",
];

/// 打包产物层级，锚点要往上收一层
const BUILDER_DIRS: [&str; 6] = ["bin", "x64", "x86", "win64", "win32", "app"];

/// 出现在品牌尾部的安装器 / 平台词，判断品牌时直接截断
const PACKAGING_WORDS: [&str; 12] = [
    "win", "win32", "win64", "x64", "x86", "amd64", "user", "setup", "installer", "beta", "rc",
    "cn",
];

/// 完全没有辨识度的品牌：这些词不构成合并依据（否则同一个下载目录会乱并）
const GENERIC_BRANDS: [&str; 13] = [
    "setup", "uninstall", "install", "installer", "update", "updater", "helper", "application",
    "launcher", "service", "main", "index", "run",
];

/// 子进程常见后缀：`FooUpdater` 归到 `Foo`
const ROLE_SUFFIXES: [&str; 12] = [
    "updater", "update", "helper", "service", "tray", "launcher", "broker", "daemon",
    "watchdog", "crashpad", "notifier", "srv",
];

fn contains_ci(list: &[&str], value: &str) -> bool {
    list.iter().any(|item| item.eq_ignore_ascii_case(value))
}

fn is_version_segment(segment: &str) -> bool {
    let trimmed = segment
        .strip_prefix('v')
        .or_else(|| segment.strip_prefix('V'))
        .unwrap_or(segment);
    if trimmed.is_empty() {
        return false;
    }
    if !trimmed.chars().any(|c| c.is_ascii_digit()) {
        return false;
    }
    trimmed
        .chars()
        .all(|c| c.is_ascii_digit() || c == '.' || c == '-' || c == '_')
}

/// 安装目录锚点；拿不到（没有路径 / 相对路径 / 根目录文件）时返回 None
pub fn anchor_of(file: Option<&str>) -> Option<String> {
    let file = file?.trim();
    if file.is_empty() {
        return None;
    }
    let path = file.replace('\\', "/");
    let lower = path.to_lowercase();

    // macOS：以 .app 包为锚点，包内层级（Contents/MacOS）不参与判定
    if let Some(index) = lower.find(".app/") {
        return Some(lower[..index + 4].to_string());
    }

    let slash = lower.rfind('/')?;
    if slash == 0 {
        return None;
    }
    let mut segments: Vec<&str> = lower[..slash]
        .split('/')
        .filter(|s| !s.is_empty())
        .collect();
    if segments.len() <= 1 {
        // c:/x.exe 这种根目录文件不参与
        return None;
    }

    while segments.len() > 1 {
        let last = *segments.last().unwrap();
        if is_version_segment(last) || contains_ci(&BUILDER_DIRS, last) {
            segments.pop();
        } else {
            break;
        }
    }

    if segments.is_empty() {
        None
    } else {
        Some(segments.join("/"))
    }
}

/// 系统目录不参与（界面里它们本来就该各占一行）
pub fn is_system_anchor(anchor: &str) -> bool {
    anchor.starts_with("c:/windows")
        || anchor.contains("/windowsapps/")
        || anchor.starts_with("/system/")
        || anchor.starts_with("/usr/")
        || anchor.starts_with("/bin/")
        || anchor.starts_with("/sbin/")
}

fn stem_of(file: Option<&str>) -> String {
    let file = match file {
        Some(value) if !value.trim().is_empty() => value.trim().replace('\\', "/"),
        _ => return String::new(),
    };
    let name = file.rsplit('/').next().unwrap_or("");
    match name.strip_suffix(".exe").or_else(|| name.strip_suffix(".EXE")) {
        Some(stem) => stem.to_string(),
        None => name.to_string(),
    }
}

/// 品牌：取进程名里第一个版本号之前的词。
/// `PiDeck-0.9.5-setup` → `PiDeck`；`TextFold-win-x64-setup` → `TextFold`；`kimi_3.2.20` → `kimi`。
pub fn brand_of(name: Option<&str>) -> Option<String> {
    let raw = name?.trim();
    if raw.is_empty() {
        return None;
    }
    let stem = match raw.strip_suffix(".exe").or_else(|| raw.strip_suffix(".EXE")) {
        Some(value) => value,
        None => raw,
    };

    let mut kept: Vec<String> = Vec::new();
    for token in stem.split(['_', '-', '+', ' ', '\t']) {
        if token.is_empty() {
            continue;
        }
        if contains_ci(&PACKAGING_WORDS, token) || token.chars().any(|c| c.is_ascii_digit()) {
            break;
        }
        kept.push(token.to_string());
    }

    while let Some(last) = kept.last() {
        if contains_ci(&ROLE_SUFFIXES, last) {
            kept.pop();
        } else {
            break;
        }
    }

    let mut brand = kept.join(" ").trim().to_string();

    // 单 token 情况：`FooUpdater` 没有分隔符，直接按后缀裁掉
    if kept.len() == 1 {
        let lower = brand.to_lowercase();
        for suffix in ROLE_SUFFIXES {
            if brand.len() > suffix.len() + 1 && lower.ends_with(suffix) {
                let cut = brand.len() - suffix.len();
                let prev = brand[..cut].chars().last().unwrap_or(' ');
                if !prev.is_ascii_digit() {
                    brand = brand[..cut].trim().to_string();
                    break;
                }
            }
        }
    }

    if brand.chars().count() < 2 || contains_ci(&GENERIC_BRANDS, &brand) {
        return None;
    }
    Some(brand)
}

/// 从应用列表算出自动组。`suppressed` 里的 key 不再参与。
/// 返回的组按组时长降序，组内第一个成员是母成员。
pub fn build_groups(candidates: &[MergeCandidate], suppressed: &HashSet<String>) -> Vec<AutoGroup> {
    // key → (成员名, 该成员的判定依据)
    let mut buckets: HashMap<String, Vec<&MergeCandidate>> = HashMap::new();
    let mut publishers: HashMap<String, HashSet<Option<String>>> = HashMap::new();

    for candidate in candidates {
        let anchor = match anchor_of(candidate.file.as_deref()) {
            Some(anchor) => anchor,
            None => continue,
        };
        if is_system_anchor(&anchor) {
            continue;
        }
        let stem = stem_of(candidate.file.as_deref());
        if contains_ci(&BROWSER_STEMS, &stem) {
            continue;
        }

        let brand = brand_of(Some(stem.as_str())).or_else(|| brand_of(Some(candidate.name.as_str())));
        let publisher = normalized_publisher(candidate.publisher.as_deref());
        // setup.exe / uninstall.exe 这类没有辨识度的名字，即使同发布者也不参与合并
        let stem_is_generic = contains_ci(&GENERIC_BRANDS, &stem);

        let key = match (&brand, &publisher) {
            (Some(brand), _) => format!("{}|{}", anchor, brand.to_lowercase()),
            (None, Some(publisher)) if !stem_is_generic => {
                format!("{}|publisher:{}", anchor, publisher)
            }
            _ => continue,
        };
        if suppressed.contains(&key) {
            continue;
        }

        buckets.entry(key.clone()).or_default().push(candidate);
        publishers
            .entry(key)
            .or_default()
            .insert(publisher);
    }

    let mut groups: Vec<AutoGroup> = Vec::new();
    for (key, members) in buckets {
        if members.len() < 2 {
            continue;
        }
        // 两个成员都带发布者但发布者不同 → 不合并
        let distinct: HashSet<Option<String>> = publishers
            .get(&key)
            .cloned()
            .unwrap_or_default()
            .into_iter()
            .filter(|p| p.is_some())
            .collect();
        if distinct.len() > 1 {
            continue;
        }

        let mut ordered = members;
        ordered.sort_by(|a, b| {
            b.seconds
                .cmp(&a.seconds)
                .then_with(|| {
                    let al = a.file.as_deref().map(str::len).unwrap_or(usize::MAX);
                    let bl = b.file.as_deref().map(str::len).unwrap_or(usize::MAX);
                    al.cmp(&bl)
                })
                .then_with(|| a.name.cmp(&b.name))
        });

        let primary = ordered[0];
        let primary_stem = stem_of(primary.file.as_deref());
        let name = brand_of(Some(primary_stem.as_str()))
            .or_else(|| brand_of(Some(primary.name.as_str())))
            .or_else(|| normalized_publisher(primary.publisher.as_deref()))
            .unwrap_or_else(|| primary.name.clone());

        groups.push(AutoGroup {
            key,
            name,
            members: ordered.iter().map(|m| m.name.clone()).collect(),
        });
    }

    groups.sort_by(|a, b| a.name.cmp(&b.name));
    groups
}

fn normalized_publisher(raw: Option<&str>) -> Option<String> {
    let value = raw?.trim();
    if value.is_empty() {
        return None;
    }
    Some(value.to_lowercase())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn app(name: &str, file: &str, publisher: Option<&str>, seconds: i64) -> MergeCandidate {
        MergeCandidate {
            name: name.to_string(),
            file: Some(file.to_string()),
            publisher: publisher.map(str::to_string),
            seconds,
        }
    }

    fn suppressed(keys: &[&str]) -> HashSet<String> {
        keys.iter().map(|k| k.to_string()).collect()
    }

    #[test]
    fn anchor_strips_packaging_levels_and_versions() {
        assert_eq!(
            anchor_of(Some(r"C:\Users\me\AppData\Local\Programs\PiDeck\PiDeck-0.9.5-setup.exe")),
            Some("c:/users/me/appdata/local/programs/pideck".to_string())
        );
        assert_eq!(
            anchor_of(Some(r"C:\Program Files\Foo\1.2.3\foo.exe")),
            Some("c:/program files/foo".to_string())
        );
        assert_eq!(
            anchor_of(Some(r"C:\Program Files\Foo\bin\x64\foo.exe")),
            Some("c:/program files/foo".to_string())
        );
        assert_eq!(
            anchor_of(Some("/Applications/Foo.app/Contents/MacOS/Foo")),
            Some("/applications/foo.app".to_string())
        );
        assert_eq!(anchor_of(Some(r"C:\foo.exe")), None);
        assert_eq!(anchor_of(None), None);
    }

    #[test]
    fn brand_stops_at_version_or_packaging_words() {
        assert_eq!(brand_of(Some("PiDeck-0.9.5-setup")), Some("PiDeck".to_string()));
        assert_eq!(brand_of(Some("TextFold-win-x64-setup")), Some("TextFold".to_string()));
        assert_eq!(brand_of(Some("kimi_3.2.20")), Some("kimi".to_string()));
        assert_eq!(brand_of(Some("FooUpdater")), Some("Foo".to_string()));
        assert_eq!(brand_of(Some("setup")), None);
        assert_eq!(brand_of(None), None);
    }

    #[test]
    fn same_anchor_and_brand_becomes_one_group() {
        let apps = vec![
            app("PiDeck-0.9.5-setup", r"C:\Program Files\PiDeck\PiDeck-0.9.5-setup.exe", None, 300),
            app("PiDeck", r"C:\Program Files\PiDeck\PiDeck.exe", None, 900),
        ];
        let groups = build_groups(&apps, &suppressed(&[]));
        assert_eq!(groups.len(), 1);
        assert_eq!(groups[0].name, "PiDeck");
        // 母成员 = 时长最长的那个
        assert_eq!(groups[0].members, vec!["PiDeck", "PiDeck-0.9.5-setup"]);
    }

    #[test]
    fn single_member_is_not_a_group() {
        let apps = vec![app("Foo", r"C:\Program Files\Foo\Foo.exe", None, 10)];
        assert!(build_groups(&apps, &suppressed(&[])).is_empty());
    }

    #[test]
    fn browsers_and_system_dirs_never_merge() {
        let apps = vec![
            app("chrome", r"C:\Program Files\Google\Chrome\chrome.exe", None, 10),
            app("chrome_pwa", r"C:\Program Files\Google\Chrome\chrome_pwa.exe", None, 10),
            app("svchost", r"C:\Windows\System32\svchost.exe", None, 10),
            app("svchost2", r"C:\Windows\System32\svchost2.exe", None, 10),
        ];
        assert!(build_groups(&apps, &suppressed(&[])).is_empty());
    }

    #[test]
    fn different_publishers_do_not_merge() {
        let apps = vec![
            app("Foo", r"C:\Program Files\Foo\Foo.exe", Some("Vendor A"), 10),
            app("FooNext", r"C:\Program Files\Foo\FooNext.exe", Some("Vendor B"), 10),
        ];
        assert!(build_groups(&apps, &suppressed(&[])).is_empty());
    }

    #[test]
    fn empty_publisher_falls_back_to_brand_match() {
        let apps = vec![
            app("Foo", r"C:\Program Files\Foo\Foo.exe", Some("Vendor A"), 10),
            app("FooHelper", r"C:\Program Files\Foo\FooHelper.exe", None, 10),
        ];
        assert_eq!(build_groups(&apps, &suppressed(&[])).len(), 1);
    }

    #[test]
    fn publisher_only_groups_generic_names_in_same_dir() {
        let apps = vec![
            app("setup", r"C:\Program Files\Bar\setup.exe", Some("Bar Inc"), 10),
            app("uninstall", r"C:\Program Files\Bar\uninstall.exe", Some("Bar Inc"), 10),
        ];
        assert!(build_groups(&apps, &suppressed(&[])).is_empty());

        let apps = vec![
            app("wxwork_install", r"C:\Program Files\Bar\wxwork_install.exe", Some("Bar Inc"), 10),
            app("BarChat", r"C:\Program Files\Bar\BarChat.exe", Some("Bar Inc"), 20),
        ];
        // 有品牌的那条按品牌分组；卸载器品牌为 None 时走发布者键，两者键不同 → 不成组
        assert!(build_groups(&apps, &suppressed(&[])).is_empty());
    }

    #[test]
    fn suppression_wins_over_auto_group() {
        let apps = vec![
            app("PiDeck setup", r"C:\Program Files\PiDeck\PiDeck-0.9.5-setup.exe", None, 300),
            app("PiDeck", r"C:\Program Files\PiDeck\PiDeck.exe", None, 900),
        ];
        let groups = build_groups(&apps, &suppressed(&["c:/program files/pideck|pideck"]));
        assert!(groups.is_empty());
    }
}
