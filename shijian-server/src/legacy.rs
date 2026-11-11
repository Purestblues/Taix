//! 旧品牌（Taix）遗留目录的一次性迁移。
//!
//! 改名后数据目录换了名字，不搬就会表现为「历史统计消失」。规则：
//! - **只补不覆盖**：目标里已有的文件一律不动，所以重复执行安全；
//! - 旧目录保留在原处（用户数据不删，出问题还能捞回来）；
//! - 旧自启动项（LaunchAgent）顺手摘掉，避免新旧两份同时采集。

/// 在打开数据库之前调用。
pub fn migrate_legacy_brand_dirs() {
    #[cfg(target_os = "macos")]
    {
        macos::migrate();
    }
}

#[cfg(target_os = "macos")]
mod macos {
    use std::path::{Path, PathBuf};

    pub fn migrate() {
        let Some(home) = dirs::home_dir() else {
            return;
        };
        let support = home.join("Library").join("Application Support");

        let pairs: [(PathBuf, PathBuf); 4] = [
            (support.join("Taix"), support.join("ShiJian")),
            (
                home.join("Library").join("Logs").join("Taix"),
                home.join("Library").join("Logs").join("ShiJian"),
            ),
            (
                home.join("Library").join("Caches").join("Taix"),
                home.join("Library").join("Caches").join("ShiJian"),
            ),
            (
                PathBuf::from("/Applications/TaixTools"),
                PathBuf::from("/Applications/ShiJianTools"),
            ),
        ];

        for (old, new) in &pairs {
            match merge_dir_if_missing(old, new) {
                Ok(0) => {}
                Ok(n) => tracing::info!("migrated {n} entries: {} -> {}", old.display(), new.display()),
                Err(e) => tracing::warn!("legacy migration failed for {}: {e}", old.display()),
            }
        }

        stop_legacy_agents(&home);
    }

    /// 停掉旧进程、摘掉旧 LaunchAgent。
    fn stop_legacy_agents(home: &Path) {
        for proc in ["taix-shell", "taix-server", "taix-monitor-macos"] {
            let _ = std::process::Command::new("pkill").args(["-f", proc]).output();
        }

        let plist = home.join("Library").join("LaunchAgents").join("com.taix.shell.plist");
        if !plist.exists() {
            return;
        }

        if let Ok(uid) = std::process::Command::new("id").arg("-u").output() {
            let uid = String::from_utf8_lossy(&uid.stdout).trim().to_string();
            let _ = std::process::Command::new("launchctl")
                .args(["bootout", &format!("gui/{uid}/com.taix.shell")])
                .output();
        }

        let _ = std::fs::remove_file(&plist);
        tracing::info!("removed legacy LaunchAgent: {}", plist.display());
    }

    /// 把 `src` 里目标不存在的条目补进 `dst`，返回复制的条目数。
    fn merge_dir_if_missing(src: &Path, dst: &Path) -> std::io::Result<usize> {
        if !src.is_dir() {
            return Ok(0);
        }
        std::fs::create_dir_all(dst)?;

        let mut copied = 0usize;
        for entry in std::fs::read_dir(src)? {
            let entry = entry?;
            let from = entry.path();
            let to = dst.join(entry.file_name());

            if from.is_dir() {
                copied += merge_dir_if_missing(&from, &to)?;
            } else if !to.exists() && std::fs::copy(&from, &to).is_ok() {
                copied += 1;
            }
        }
        Ok(copied)
    }
}
