use crate::services::web_domain::{host_without_port, is_loopback_site_domain};

/// 9 个预设内置分类名（与 migrations::PRESET_CATEGORIES 保持一致）
pub const CAT_DEV: &str = "开发";
pub const CAT_OFFICE: &str = "办公";
pub const CAT_AI: &str = "AI";
pub const CAT_ENTERTAINMENT: &str = "娱乐";
pub const CAT_SOCIAL: &str = "社交";
pub const CAT_STUDY: &str = "学习";
pub const CAT_DESIGN: &str = "设计";
pub const CAT_MEDIA: &str = "影音";
pub const CAT_TOOLS: &str = "工具";

/// 判断 domain 是否精确匹配 pattern 或为其子域名（零分配）
fn domain_matches(host: &str, pattern: &str) -> bool {
    if host == pattern {
        return true;
    }
    let h_len = host.len();
    let p_len = pattern.len();
    h_len > p_len
        && host.as_bytes()[h_len - p_len - 1] == b'.'
        && host.ends_with(pattern)
}

/// 特定子域名规则表（必须在根域名规则之前检查）
const SPECIFIC_DOMAIN_RULES: &[(&str, &str)] = &[
    // AI / 大模型
    ("chat.deepseek.com", CAT_AI),
    ("platform.deepseek.com", CAT_AI),
    ("gemini.google.com", CAT_AI),
    ("aistudio.google.com", CAT_AI),
    ("notebooklm.google.com", CAT_AI),
    ("copilot.microsoft.com", CAT_AI),
    ("tongyi.aliyun.com", CAT_AI),
    ("qianwen.aliyun.com", CAT_AI),
    ("yiyan.baidu.com", CAT_AI),
    ("kimi.moonshot.cn", CAT_AI),
    ("chatglm.cn", CAT_AI),
    ("yuanbao.tencent.com", CAT_AI),
    ("grok.com", CAT_AI),
    ("x.ai", CAT_AI),
    // 开发（Google / 腾讯 / 微软 / 阿里 等旗下开发平台子域名）
    ("code.visualstudio.com", CAT_DEV),
    ("vscode.dev", CAT_DEV),
    ("github.dev", CAT_DEV),
    ("gist.github.com", CAT_DEV),
    ("colab.research.google.com", CAT_DEV),
    ("console.cloud.google.com", CAT_DEV),
    ("developer.android.com", CAT_DEV),
    ("developer.apple.com", CAT_DEV),
    ("developer.mozilla.org", CAT_DEV),
    ("learn.microsoft.com", CAT_DEV),
    ("portal.azure.com", CAT_DEV),
    ("console.aws.amazon.com", CAT_DEV),
    ("console.cloud.tencent.com", CAT_DEV),
    ("cnb.cool", CAT_DEV),
    // 办公（腾讯文档/邮件、QQ邮箱、网易邮箱、Google Docs/Mail/Drive 等）
    ("docs.qq.com", CAT_OFFICE),
    ("mail.qq.com", CAT_OFFICE),
    ("exmail.qq.com", CAT_OFFICE),
    ("meeting.tencent.com", CAT_OFFICE),
    ("work.weixin.qq.com", CAT_OFFICE),
    ("mail.163.com", CAT_OFFICE),
    ("mail.126.com", CAT_OFFICE),
    ("docs.google.com", CAT_OFFICE),
    ("sheets.google.com", CAT_OFFICE),
    ("slides.google.com", CAT_OFFICE),
    ("drive.google.com", CAT_OFFICE),
    ("mail.google.com", CAT_OFFICE),
    ("calendar.google.com", CAT_OFFICE),
    ("meet.google.com", CAT_OFFICE),
    ("outlook.live.com", CAT_OFFICE),
    ("outlook.office.com", CAT_OFFICE),
    ("office.com", CAT_OFFICE),
    ("onedrive.live.com", CAT_OFFICE),
    ("teams.microsoft.com", CAT_OFFICE),
    ("pan.baidu.com", CAT_TOOLS),
    ("www.aliyundrive.com", CAT_TOOLS),
    ("alipan.com", CAT_TOOLS),
    // 社交
    ("wx.qq.com", CAT_SOCIAL),
    ("web.whatsapp.com", CAT_SOCIAL),
    ("web.telegram.org", CAT_SOCIAL),
    // 影音
    ("music.163.com", CAT_MEDIA),
    ("y.qq.com", CAT_MEDIA),
    ("v.qq.com", CAT_MEDIA),
    ("tv.sohu.com", CAT_MEDIA),
    ("music.youtube.com", CAT_MEDIA),
    ("music.apple.com", CAT_MEDIA),
    ("tv.apple.com", CAT_MEDIA),
    // 学习
    ("scholar.google.com", CAT_STUDY),
    ("translate.google.com", CAT_TOOLS),
];

/// 根域名规则表（支持精确域名与 *.domain 子域名匹配）
const ROOT_DOMAIN_RULES: &[(&str, &str)] = &[
    // AI
    ("chatgpt.com", CAT_AI),
    ("openai.com", CAT_AI),
    ("claude.ai", CAT_AI),
    ("anthropic.com", CAT_AI),
    ("deepseek.com", CAT_AI),
    ("perplexity.ai", CAT_AI),
    ("poe.com", CAT_AI),
    ("huggingface.co", CAT_AI),
    ("kimi.ai", CAT_AI),
    ("moonshot.cn", CAT_AI),
    ("doubao.com", CAT_AI),
    ("coze.cn", CAT_AI),
    ("coze.com", CAT_AI),
    ("midjourney.com", CAT_AI),
    ("civitai.com", CAT_AI),
    ("cursor.com", CAT_AI),
    ("cursor.sh", CAT_AI),
    ("v0.dev", CAT_AI),
    ("bolt.new", CAT_AI),
    ("lovable.dev", CAT_AI),
    ("dify.ai", CAT_AI),
    ("siliconflow.cn", CAT_AI),
    ("openrouter.ai", CAT_AI),
    ("ollama.com", CAT_AI),
    // 开发
    ("github.com", CAT_DEV),
    ("githubusercontent.com", CAT_DEV),
    ("gitlab.com", CAT_DEV),
    ("gitee.com", CAT_DEV),
    ("bitbucket.org", CAT_DEV),
    ("stackoverflow.com", CAT_DEV),
    ("stackexchange.com", CAT_DEV),
    ("juejin.cn", CAT_DEV),
    ("csdn.net", CAT_DEV),
    ("cnblogs.com", CAT_DEV),
    ("segmentfault.com", CAT_DEV),
    ("v2ex.com", CAT_DEV),
    ("oschina.net", CAT_DEV),
    ("npmjs.com", CAT_DEV),
    ("crates.io", CAT_DEV),
    ("docs.rs", CAT_DEV),
    ("pypi.org", CAT_DEV),
    ("nuget.org", CAT_DEV),
    ("pkg.go.dev", CAT_DEV),
    ("hub.docker.com", CAT_DEV),
    ("docker.com", CAT_DEV),
    ("rust-lang.org", CAT_DEV),
    ("python.org", CAT_DEV),
    ("typescriptlang.org", CAT_DEV),
    ("react.dev", CAT_DEV),
    ("vuejs.org", CAT_DEV),
    ("nextjs.org", CAT_DEV),
    ("tailwindcss.com", CAT_DEV),
    ("vercel.com", CAT_DEV),
    ("netlify.com", CAT_DEV),
    ("cloudflare.com", CAT_DEV),
    ("supabase.com", CAT_DEV),
    ("leetcode.com", CAT_DEV),
    ("leetcode.cn", CAT_DEV),
    ("codeforces.com", CAT_DEV),
    ("kaggle.com", CAT_DEV),
    ("jetbrains.com", CAT_DEV),
    ("visualstudio.com", CAT_DEV),
    // 办公
    ("feishu.cn", CAT_OFFICE),
    ("larksuite.com", CAT_OFFICE),
    ("dingtalk.com", CAT_OFFICE),
    ("notion.so", CAT_OFFICE),
    ("notion.site", CAT_OFFICE),
    ("yuque.com", CAT_OFFICE),
    ("wolai.com", CAT_OFFICE),
    ("flowus.cn", CAT_OFFICE),
    ("kdocs.cn", CAT_OFFICE),
    ("wps.cn", CAT_OFFICE),
    ("shimo.im", CAT_OFFICE),
    ("modao.cc", CAT_OFFICE),
    ("processon.com", CAT_OFFICE),
    ("miro.com", CAT_OFFICE),
    ("trello.com", CAT_OFFICE),
    ("asana.com", CAT_OFFICE),
    ("linear.app", CAT_OFFICE),
    ("atlassian.net", CAT_OFFICE),
    ("atlassian.com", CAT_OFFICE),
    ("slack.com", CAT_OFFICE),
    ("zoom.us", CAT_OFFICE),
    ("microsoft365.com", CAT_OFFICE),
    ("sharepoint.com", CAT_OFFICE),
    ("airtable.com", CAT_OFFICE),
    ("monday.com", CAT_OFFICE),
    // 设计
    ("figma.com", CAT_DESIGN),
    ("canva.com", CAT_DESIGN),
    ("canva.cn", CAT_DESIGN),
    ("dribbble.com", CAT_DESIGN),
    ("behance.net", CAT_DESIGN),
    ("pinterest.com", CAT_DESIGN),
    ("huaban.com", CAT_DESIGN),
    ("zcool.com.cn", CAT_DESIGN),
    ("ui.cn", CAT_DESIGN),
    ("iconfont.cn", CAT_DESIGN),
    ("mastergo.com", CAT_DESIGN),
    ("js.design", CAT_DESIGN),
    ("pixso.cn", CAT_DESIGN),
    ("gaoding.com", CAT_DESIGN),
    ("adobe.com", CAT_DESIGN),
    ("unsplash.com", CAT_DESIGN),
    ("pexels.com", CAT_DESIGN),
    ("sketch.com", CAT_DESIGN),
    ("framer.com", CAT_DESIGN),
    ("lucide.dev", CAT_DESIGN),
    // 学习
    ("wikipedia.org", CAT_STUDY),
    ("zhihu.com", CAT_STUDY),
    ("coursera.org", CAT_STUDY),
    ("edx.org", CAT_STUDY),
    ("udemy.com", CAT_STUDY),
    ("khanacademy.org", CAT_STUDY),
    ("icourse163.org", CAT_STUDY),
    ("xuetangx.com", CAT_STUDY),
    ("mooc.org", CAT_STUDY),
    ("arxiv.org", CAT_STUDY),
    ("cnki.net", CAT_STUDY),
    ("wanfangdata.com.cn", CAT_STUDY),
    ("semanticscholar.org", CAT_STUDY),
    ("researchgate.net", CAT_STUDY),
    ("ieee.org", CAT_STUDY),
    ("springer.com", CAT_STUDY),
    ("sciencedirect.com", CAT_STUDY),
    ("duolingo.com", CAT_STUDY),
    ("ankiweb.net", CAT_STUDY),
    ("weread.qq.com", CAT_STUDY),
    ("read.douban.com", CAT_STUDY),
    ("zotero.org", CAT_STUDY),
    ("brilliant.org", CAT_STUDY),
    // 影音
    ("youtube.com", CAT_MEDIA),
    ("youtu.be", CAT_MEDIA),
    ("netflix.com", CAT_MEDIA),
    ("iqiyi.com", CAT_MEDIA),
    ("youku.com", CAT_MEDIA),
    ("mgtv.com", CAT_MEDIA),
    ("acfun.cn", CAT_MEDIA),
    ("spotify.com", CAT_MEDIA),
    ("soundcloud.com", CAT_MEDIA),
    ("kugou.com", CAT_MEDIA),
    ("kuwo.cn", CAT_MEDIA),
    ("ximalaya.com", CAT_MEDIA),
    ("xiaoyuzhoufm.com", CAT_MEDIA),
    ("disneyplus.com", CAT_MEDIA),
    ("primevideo.com", CAT_MEDIA),
    ("hbo.com", CAT_MEDIA),
    ("imdb.com", CAT_MEDIA),
    // 娱乐
    ("bilibili.com", CAT_ENTERTAINMENT),
    ("b23.tv", CAT_ENTERTAINMENT),
    ("douyin.com", CAT_ENTERTAINMENT),
    ("tiktok.com", CAT_ENTERTAINMENT),
    ("kuaishou.com", CAT_ENTERTAINMENT),
    ("twitch.tv", CAT_ENTERTAINMENT),
    ("douyu.com", CAT_ENTERTAINMENT),
    ("huya.com", CAT_ENTERTAINMENT),
    ("steampowered.com", CAT_ENTERTAINMENT),
    ("steamcommunity.com", CAT_ENTERTAINMENT),
    ("epicgames.com", CAT_ENTERTAINMENT),
    ("epicgames.dev", CAT_ENTERTAINMENT),
    ("mihoyo.com", CAT_ENTERTAINMENT),
    ("hoyolab.com", CAT_ENTERTAINMENT),
    ("tap.io", CAT_ENTERTAINMENT),
    ("taptap.cn", CAT_ENTERTAINMENT),
    ("nga.cn", CAT_ENTERTAINMENT),
    ("ngabbs.com", CAT_ENTERTAINMENT),
    ("ign.com", CAT_ENTERTAINMENT),
    ("nexusmods.com", CAT_ENTERTAINMENT),
    ("roblox.com", CAT_ENTERTAINMENT),
    ("minecraft.net", CAT_ENTERTAINMENT),
    // 社交
    ("weibo.com", CAT_SOCIAL),
    ("weibo.cn", CAT_SOCIAL),
    ("xiaohongshu.com", CAT_SOCIAL),
    ("x.com", CAT_SOCIAL),
    ("twitter.com", CAT_SOCIAL),
    ("reddit.com", CAT_SOCIAL),
    ("discord.com", CAT_SOCIAL),
    ("discord.gg", CAT_SOCIAL),
    ("instagram.com", CAT_SOCIAL),
    ("facebook.com", CAT_SOCIAL),
    ("threads.net", CAT_SOCIAL),
    ("linkedin.com", CAT_SOCIAL),
    ("douban.com", CAT_SOCIAL),
    ("tieba.baidu.com", CAT_SOCIAL),
    ("bsky.app", CAT_SOCIAL),
    ("mastodon.social", CAT_SOCIAL),
    // 工具
    ("google.com", CAT_TOOLS),
    ("google.com.hk", CAT_TOOLS),
    ("bing.com", CAT_TOOLS),
    ("baidu.com", CAT_TOOLS),
    ("duckduckgo.com", CAT_TOOLS),
    ("sogou.com", CAT_TOOLS),
    ("deepl.com", CAT_TOOLS),
    ("fanyi.baidu.com", CAT_TOOLS),
    ("dict.youdao.com", CAT_TOOLS),
    ("1password.com", CAT_TOOLS),
    ("bitwarden.com", CAT_TOOLS),
    ("speedtest.net", CAT_TOOLS),
    ("taobao.com", CAT_TOOLS),
    ("jd.com", CAT_TOOLS),
    ("amazon.com", CAT_TOOLS),
    ("12306.cn", CAT_TOOLS),
    ("amap.com", CAT_TOOLS),
    ("map.baidu.com", CAT_TOOLS),
];

/// 按内置规则匹配网站域名，返回预设分类名；未命中则返回 None
pub fn match_builtin_domain(domain: &str) -> Option<&'static str> {
    let trimmed = domain.trim();
    if trimmed.is_empty() || is_loopback_site_domain(trimmed) {
        return None;
    }

    let host = host_without_port(trimmed).to_ascii_lowercase();
    if host.is_empty() {
        return None;
    }

    for (pattern, category) in SPECIFIC_DOMAIN_RULES {
        if domain_matches(&host, pattern) {
            return Some(category);
        }
    }

    for (pattern, category) in ROOT_DOMAIN_RULES {
        if domain_matches(&host, pattern) {
            return Some(category);
        }
    }

    None
}

/// 常见桌面应用精确进程名映射（小写且已剥离 .exe / .app 后缀）
const APP_PROCESS_RULES: &[(&str, &str)] = &[
    // AI 客户端
    ("chatgpt", CAT_AI),
    ("claude", CAT_AI),
    ("cursor", CAT_AI),
    ("windsurf", CAT_AI),
    ("trae", CAT_AI),
    ("antigravity", CAT_AI),
    ("ollama", CAT_AI),
    ("lm studio", CAT_AI),
    ("lmstudio", CAT_AI),
    ("cherrystudio", CAT_AI),
    ("cherry studio", CAT_AI),
    ("chatbox", CAT_AI),
    ("jan", CAT_AI),
    ("poe", CAT_AI),
    ("doubao", CAT_AI),
    // 开发工具
    ("code", CAT_DEV),
    ("code - insiders", CAT_DEV),
    ("vscodium", CAT_DEV),
    ("devenv", CAT_DEV),
    ("idea64", CAT_DEV),
    ("idea", CAT_DEV),
    ("pycharm64", CAT_DEV),
    ("pycharm", CAT_DEV),
    ("webstorm64", CAT_DEV),
    ("webstorm", CAT_DEV),
    ("goland64", CAT_DEV),
    ("goland", CAT_DEV),
    ("clion64", CAT_DEV),
    ("clion", CAT_DEV),
    ("rustrover64", CAT_DEV),
    ("rustrover", CAT_DEV),
    ("rider64", CAT_DEV),
    ("rider", CAT_DEV),
    ("datagrip64", CAT_DEV),
    ("datagrip", CAT_DEV),
    ("androidstudio64", CAT_DEV),
    ("studio64", CAT_DEV),
    ("xcode", CAT_DEV),
    ("zed", CAT_DEV),
    ("sublime_text", CAT_DEV),
    ("notepad++", CAT_DEV),
    ("windowsterminal", CAT_DEV),
    ("wezterm-gui", CAT_DEV),
    ("wezterm", CAT_DEV),
    ("alacritty", CAT_DEV),
    ("kitty", CAT_DEV),
    ("iterm2", CAT_DEV),
    ("powershell", CAT_DEV),
    ("pwsh", CAT_DEV),
    ("cmd", CAT_DEV),
    ("mintty", CAT_DEV),
    ("git-bash", CAT_DEV),
    ("githubdesktop", CAT_DEV),
    ("sourcetree", CAT_DEV),
    ("fork", CAT_DEV),
    ("postman", CAT_DEV),
    ("apifox", CAT_DEV),
    ("insomnia", CAT_DEV),
    ("bruno", CAT_DEV),
    ("docker desktop", CAT_DEV),
    ("docker", CAT_DEV),
    ("dbeaver", CAT_DEV),
    ("navicat", CAT_DEV),
    ("dataspell", CAT_DEV),
    ("wireshark", CAT_DEV),
    ("fiddler", CAT_DEV),
    ("charles", CAT_DEV),
    // 办公协作
    ("winword", CAT_OFFICE),
    ("excel", CAT_OFFICE),
    ("powerpnt", CAT_OFFICE),
    ("onenote", CAT_OFFICE),
    ("outlook", CAT_OFFICE),
    ("olk", CAT_OFFICE),
    ("ms-teams", CAT_OFFICE),
    ("teams", CAT_OFFICE),
    ("wps", CAT_OFFICE),
    ("et", CAT_OFFICE),
    ("wpp", CAT_OFFICE),
    ("wpsoffice", CAT_OFFICE),
    ("feishu", CAT_OFFICE),
    ("lark", CAT_OFFICE),
    ("dingtalk", CAT_OFFICE),
    ("notion", CAT_OFFICE),
    ("obsidian", CAT_OFFICE),
    ("typora", CAT_OFFICE),
    ("logseq", CAT_OFFICE),
    ("siyuan", CAT_OFFICE),
    ("yuque", CAT_OFFICE),
    ("foxmail", CAT_OFFICE),
    ("thunderbird", CAT_OFFICE),
    ("zoom", CAT_OFFICE),
    ("wemeetapp", CAT_OFFICE),
    ("wemeet", CAT_OFFICE),
    ("wecom", CAT_OFFICE),
    ("wxwork", CAT_OFFICE),
    ("xmind", CAT_OFFICE),
    ("libreoffice", CAT_OFFICE),
    ("soffice", CAT_OFFICE),
    ("acrobat", CAT_OFFICE),
    ("acrord32", CAT_OFFICE),
    ("foxitpdfreader", CAT_OFFICE),
    // 社交沟通
    ("wechat", CAT_SOCIAL),
    ("weixin", CAT_SOCIAL),
    ("qq", CAT_SOCIAL),
    ("qqnt", CAT_SOCIAL),
    ("tim", CAT_SOCIAL),
    ("discord", CAT_SOCIAL),
    ("telegram", CAT_SOCIAL),
    ("slack", CAT_SOCIAL),
    ("whatsapp", CAT_SOCIAL),
    ("line", CAT_SOCIAL),
    ("signal", CAT_SOCIAL),
    ("kook", CAT_SOCIAL),
    // 设计创作
    ("figma", CAT_DESIGN),
    ("photoshop", CAT_DESIGN),
    ("illustrator", CAT_DESIGN),
    ("indesign", CAT_DESIGN),
    ("afterfx", CAT_DESIGN),
    ("premiere pro", CAT_DESIGN),
    ("adobe premiere pro", CAT_DESIGN),
    ("lightroom", CAT_DESIGN),
    ("blender", CAT_DESIGN),
    ("sketch", CAT_DESIGN),
    ("affinity", CAT_DESIGN),
    ("affinity designer 2", CAT_DESIGN),
    ("affinity photo 2", CAT_DESIGN),
    ("pixso", CAT_DESIGN),
    ("mastergo", CAT_DESIGN),
    ("jianyingpro", CAT_DESIGN),
    ("capcut", CAT_DESIGN),
    ("davinciresolve", CAT_DESIGN),
    ("resolve", CAT_DESIGN),
    ("krita", CAT_DESIGN),
    ("gimp", CAT_DESIGN),
    ("inkscape", CAT_DESIGN),
    ("c4d", CAT_DESIGN),
    ("cinema 4d", CAT_DESIGN),
    ("axure", CAT_DESIGN),
    ("snipaste", CAT_DESIGN),
    ("pixpin", CAT_DESIGN),
    // 学习阅读
    ("anki", CAT_STUDY),
    ("zotero", CAT_STUDY),
    ("calibre", CAT_STUDY),
    ("kindle", CAT_STUDY),
    ("sumatrapdf", CAT_STUDY),
    ("cajviewer", CAT_STUDY),
    ("mendeley", CAT_STUDY),
    ("marginnote", CAT_STUDY),
    ("eudic", CAT_STUDY),
    ("youdaodict", CAT_STUDY),
    // 影音媒体
    ("cloudmusic", CAT_MEDIA),
    ("qqmusic", CAT_MEDIA),
    ("kugou", CAT_MEDIA),
    ("kwmusic", CAT_MEDIA),
    ("spotify", CAT_MEDIA),
    ("apple music", CAT_MEDIA),
    ("itunes", CAT_MEDIA),
    ("foobar2000", CAT_MEDIA),
    ("potplayermini64", CAT_MEDIA),
    ("potplayermini", CAT_MEDIA),
    ("potplayer", CAT_MEDIA),
    ("vlc", CAT_MEDIA),
    ("mpv", CAT_MEDIA),
    ("iina", CAT_MEDIA),
    ("obs64", CAT_MEDIA),
    ("obs", CAT_MEDIA),
    ("iqiyi", CAT_MEDIA),
    ("qqlive", CAT_MEDIA),
    ("youku", CAT_MEDIA),
    ("bilibili", CAT_MEDIA),
    // 娱乐游戏
    ("steam", CAT_ENTERTAINMENT),
    ("steamwebhelper", CAT_ENTERTAINMENT),
    ("epicgameslauncher", CAT_ENTERTAINMENT),
    ("battle.net", CAT_ENTERTAINMENT),
    ("riotclientservices", CAT_ENTERTAINMENT),
    ("league of legends", CAT_ENTERTAINMENT),
    ("leagueclient", CAT_ENTERTAINMENT),
    ("valorant", CAT_ENTERTAINMENT),
    ("genshinimpact", CAT_ENTERTAINMENT),
    ("yuanshen", CAT_ENTERTAINMENT),
    ("starrail", CAT_ENTERTAINMENT),
    ("zenlesszonezero", CAT_ENTERTAINMENT),
    ("wegame", CAT_ENTERTAINMENT),
    ("minecraft", CAT_ENTERTAINMENT),
    ("hmcl", CAT_ENTERTAINMENT),
    ("pcl2", CAT_ENTERTAINMENT),
    // 常用工具 / 浏览器
    ("chrome", CAT_TOOLS),
    ("msedge", CAT_TOOLS),
    ("firefox", CAT_TOOLS),
    ("brave", CAT_TOOLS),
    ("arc", CAT_TOOLS),
    ("opera", CAT_TOOLS),
    ("vivaldi", CAT_TOOLS),
    ("safari", CAT_TOOLS),
    ("explorer", CAT_TOOLS),
    ("finder", CAT_TOOLS),
    ("everything", CAT_TOOLS),
    ("listary", CAT_TOOLS),
    ("utools", CAT_TOOLS),
    ("powertoys", CAT_TOOLS),
    ("raycast", CAT_TOOLS),
    ("alfred", CAT_TOOLS),
    ("clash-verge", CAT_TOOLS),
    ("clash verge", CAT_TOOLS),
    ("mihomo", CAT_TOOLS),
    ("v2rayn", CAT_TOOLS),
    ("baidunetdisk", CAT_TOOLS),
    ("aliyundrive", CAT_TOOLS),
    ("qbittorrent", CAT_TOOLS),
    ("motrix", CAT_TOOLS),
    ("7zfm", CAT_TOOLS),
    ("winrar", CAT_TOOLS),
    ("bandizip", CAT_TOOLS),
    ("1password", CAT_TOOLS),
    ("bitwarden", CAT_TOOLS),
    ("keepassxc", CAT_TOOLS),
    ("notepad", CAT_TOOLS),
    ("calc", CAT_TOOLS),
    ("calculatorapp", CAT_TOOLS),
    ("taskmgr", CAT_TOOLS),
    ("systemsettings", CAT_TOOLS),
];

/// Windows 底层系统宿主进程（不应当作为常用工具被频繁归纳，保持未分类或由用户处理）
fn is_os_background_host(proc_lower: &str) -> bool {
    matches!(
        proc_lower,
        "svchost"
            | "conhost"
            | "dwm"
            | "csrss"
            | "lsass"
            | "services"
            | "smss"
            | "winlogon"
            | "wininit"
            | "fontdrvhost"
            | "sihost"
            | "taskhostw"
            | "dllhost"
            | "runtimebroker"
            | "searchhost"
            | "startmenuexperiencehost"
            | "shellexperiencehost"
            | "lockapp"
            | "applicationframehost"
            | "textinputhost"
    )
}

fn normalize_process_name(name: &str) -> String {
    let trimmed = name.trim().to_ascii_lowercase();
    trimmed
        .strip_suffix(".exe")
        .or_else(|| trimmed.strip_suffix(".app"))
        .unwrap_or(&trimmed)
        .to_string()
}

/// 按内置规则匹配应用元数据，返回预设分类名；未命中则返回 None
pub fn match_builtin_app(
    name: &str,
    description: Option<&str>,
    file: Option<&str>,
    publisher: Option<&str>,
) -> Option<&'static str> {
    let proc_lower = normalize_process_name(name);
    if proc_lower.is_empty()
        || proc_lower.starts_with("shijian")
        || is_os_background_host(&proc_lower)
    {
        return None;
    }

    for (rule_proc, category) in APP_PROCESS_RULES {
        if proc_lower == *rule_proc {
            return Some(category);
        }
    }

    // 结合描述 / 发布者 / 安装路径关键字做次级匹配
    let desc_lower = description.unwrap_or("").trim().to_ascii_lowercase();
    let pub_lower = publisher.unwrap_or("").trim().to_ascii_lowercase();
    let path_lower = file.unwrap_or("").trim().replace('\\', "/").to_ascii_lowercase();

    if desc_lower.contains("visual studio code")
        || desc_lower.contains("jetbrains")
        || pub_lower.contains("jetbrains")
        || path_lower.contains("/jetbrains/")
        || path_lower.contains("/microsoft vs code/")
    {
        return Some(CAT_DEV);
    }

    if path_lower.contains("/steam/steamapps/")
        || path_lower.contains("/epic games/")
        || path_lower.contains("/riot games/")
        || path_lower.contains("/mihoyo/")
        || path_lower.contains("/hoyoverse/")
    {
        return Some(CAT_ENTERTAINMENT);
    }

    if pub_lower.contains("adobe") || desc_lower.contains("adobe ") {
        return Some(CAT_DESIGN);
    }

    if desc_lower.contains("chatgpt") || desc_lower.contains("claude") || desc_lower.contains("copilot") {
        return Some(CAT_AI);
    }

    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn matches_common_websites_and_subdomains() {
        assert_eq!(match_builtin_domain("github.com"), Some(CAT_DEV));
        assert_eq!(match_builtin_domain("gist.github.com"), Some(CAT_DEV));
        assert_eq!(match_builtin_domain("bilibili.com"), Some(CAT_ENTERTAINMENT));
        assert_eq!(match_builtin_domain("www.bilibili.com"), Some(CAT_ENTERTAINMENT));
        assert_eq!(match_builtin_domain("b23.tv"), Some(CAT_ENTERTAINMENT));
        assert_eq!(match_builtin_domain("chatgpt.com"), Some(CAT_AI));
        assert_eq!(match_builtin_domain("chat.deepseek.com"), Some(CAT_AI));
        assert_eq!(match_builtin_domain("docs.qq.com"), Some(CAT_OFFICE));
        assert_eq!(match_builtin_domain("music.163.com"), Some(CAT_MEDIA));
        assert_eq!(match_builtin_domain("Figma.com:443"), Some(CAT_DESIGN));
        assert_eq!(match_builtin_domain("localhost:3000"), None);
        assert_eq!(match_builtin_domain("unmatched.example.invalid"), None);
    }

    #[test]
    fn matches_common_apps_and_metadata_fallbacks() {
        assert_eq!(match_builtin_app("Code.exe", None, None, None), Some(CAT_DEV));
        assert_eq!(match_builtin_app("WINWORD", None, None, None), Some(CAT_OFFICE));
        assert_eq!(match_builtin_app("Cursor.exe", None, None, None), Some(CAT_AI));
        assert_eq!(match_builtin_app("WeChat.exe", None, None, None), Some(CAT_SOCIAL));
        assert_eq!(match_builtin_app("cloudmusic", None, None, None), Some(CAT_MEDIA));
        assert_eq!(match_builtin_app("explorer.exe", None, None, None), Some(CAT_TOOLS));
        assert_eq!(match_builtin_app("ShiJian.Client", None, None, None), None);
        assert_eq!(
            match_builtin_app(
                "SomeGame",
                None,
                Some("D:\\Steam\\steamapps\\common\\SomeGame\\game.exe"),
                None
            ),
            Some(CAT_ENTERTAINMENT)
        );
        assert_eq!(
            match_builtin_app("MyCustomTool", None, None, Some("JetBrains s.r.o.")),
            Some(CAT_DEV)
        );
        assert_eq!(match_builtin_app("unknown_binary", None, None, None), None);
    }
}
