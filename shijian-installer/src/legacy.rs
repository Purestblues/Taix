//! 旧品牌（Taix）遗留物的识别与一次性迁移。
//!
//! 只在安装 / 更新开头调用。原则：
//! - 先停旧进程、摘掉旧自启动项，避免新旧两份同时采集；
//! - 数据只**补齐**，不覆盖：目标目录里已有的文件一律不动；
//! - 旧目录本身不删（用户数据与历史安装留在原地，需要时自己清理）。

use std::path::{Path, PathBuf};

use crate::platform::Platform;

/// 旧版本进程名（不含 `.exe`）
pub const LEGACY_PROCESSES: &[&str] = &["taix-shell", "taix-server", "taix-monitor-windows", "Taix"];

/// 旧版本计划任务名
pub const LEGACY_TASKS: &[&str] = &["TaixShell", "TaixMonitor", "TaixServer"];

/// 旧版本 HKCU\...\Run 键名
pub const LEGACY_RUN_KEYS: &[&str] = &["Taix", "TaixServer", "TaixMonitor", "TaixShell"];

/// 数据子目录名（Windows 下数据跟着安装目录走）
const DATA_SUBDIR: &str = "Data";

/// 旧版本安装路径记录文件
fn legacy_install_location_file() -> PathBuf {
    dirs::data_local_dir()
        .unwrap_or_else(|| PathBuf::from(r"C:\Users\Default\AppData\Local"))
        .join("Taix")
        .join("install-location.txt")
}

/// 从记录文件读出旧安装目录（校验过存在性）
fn read_legacy_install_location() -> Option<PathBuf> {
    let content = std::fs::read_to_string(legacy_install_location_file()).ok()?;
    let path = PathBuf::from(content.trim());
    if path.join(DATA_SUBDIR).is_dir() || path.join("taix-shell.exe").exists() {
        Some(path)
    } else {
        None
    }
}

/// 找旧版本安装目录：记录文件优先，其次常见路径，最后看当前安装器是否就在旧目录里。
pub fn find_legacy_install_dir() -> Option<PathBuf> {
    if let Some(path) = read_legacy_install_location() {
        return Some(path);
    }

    let program_files = std::env::var("ProgramFiles").unwrap_or_else(|_| r"C:\Program Files".to_string());
    let program_files_x86 =
        std::env::var("ProgramFiles(x86)").unwrap_or_else(|_| r"C:\Program Files (x86)".to_string());
    let local_app_data = std::env::var("LOCALAPPDATA").ok();

    let mut candidates = vec![
        PathBuf::from(&program_files).join("Taix"),
        PathBuf::from(&program_files_x86).join("Taix"),
        PathBuf::from(r"C:\Taix"),
    ];
    if let Some(local) = local_app_data {
        candidates.push(PathBuf::from(&local).join("Programs").join("Taix"));
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            candidates.push(dir.to_path_buf());
        }
    }

    candidates
        .into_iter()
        .find(|p| p.join(DATA_SUBDIR).is_dir())
}

/// 摘掉旧版本的自启动与运行中的进程。失败一律忽略：迁移是尽力而为，不能挡住安装。
pub fn stop_legacy() {
    for name in LEGACY_PROCESSES {
        let _ = <() as Platform>::stop_process(name);
    }

    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;

        for task in LEGACY_TASKS {
            let _ = std::process::Command::new("schtasks")
                .args(["/DELETE", "/F", "/TN", task])
                .creation_flags(CREATE_NO_WINDOW)
                .output();
        }

        let reg_path = r"Software\Microsoft\Windows\CurrentVersion\Run";
        for key in LEGACY_RUN_KEYS {
            let _ = std::process::Command::new("reg")
                .args(["delete", &format!("HKCU\\{}", reg_path), "/v", key, "/f"])
                .creation_flags(CREATE_NO_WINDOW)
                .output();
        }
    }
}

/// 把旧安装目录里的 `Data/` 补进新目录。已有同名文件的不覆盖。
/// 返回是否真的搬了东西。
pub fn migrate_data(legacy: &Path, dest: &Path) -> anyhow::Result<bool> {
    let src = legacy.join(DATA_SUBDIR);
    if !src.is_dir() {
        return Ok(false);
    }

    let dst = dest.join(DATA_SUBDIR);
    std::fs::create_dir_all(&dst)?;

    let mut copied = 0usize;
    merge_dir(&src, &dst, &mut copied)?;

    if copied > 0 {
        // 迁移完成后摘掉旧记录，避免下次安装又去找一个已经空的旧目录
        let _ = std::fs::remove_file(legacy_install_location_file());
    }

    Ok(copied > 0)
}

fn merge_dir(src: &Path, dst: &Path, copied: &mut usize) -> anyhow::Result<()> {
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let from = entry.path();
        let to = dst.join(entry.file_name());

        if from.is_dir() {
            std::fs::create_dir_all(&to)?;
            merge_dir(&from, &to, copied)?;
        } else if !to.exists() {
            std::fs::copy(&from, &to)?;
            *copied += 1;
        }
    }
    Ok(())
}
