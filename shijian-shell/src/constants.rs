#[cfg(target_os = "windows")]
pub const CLIENT_PIPE_NAME: &str = r"\\.\pipe\ShiJianClient";

#[cfg(target_os = "windows")]
pub const CLIENT_EXE_NAME: &str = "ShiJian.exe";

#[cfg(target_os = "windows")]
pub const MONITOR_EXE_NAME: &str = "shijian-monitor-windows.exe";

#[cfg(target_os = "macos")]
pub const MONITOR_EXE_NAME: &str = "shijian-monitor-macos";

#[cfg(target_os = "windows")]
pub const SERVER_EXE_NAME: &str = "shijian-server.exe";

#[cfg(target_os = "macos")]
pub const SERVER_EXE_NAME: &str = "shijian-server";

#[cfg(target_os = "windows")]
pub const SHELL_MUTEX_NAME: &str = r"Global\ShiJianShellSingleInstance";

#[cfg(target_os = "macos")]
pub const SHELL_MUTEX_NAME: &str = "shijian-shell";

pub const TASK_NAME: &str = "ShiJianShell";

// 插件监督
pub const PLUGINS_DIR_NAME: &str = "plugins";
pub const PLUGIN_REGISTRY_POLL_SECS: u64 = 5;

#[cfg(target_os = "macos")]
pub fn default_data_dir() -> std::path::PathBuf {
    dirs::data_local_dir()
        .unwrap_or_else(|| std::path::PathBuf::from("/tmp"))
        .join("ShiJian")
}

#[cfg(target_os = "windows")]
pub fn default_data_dir() -> std::path::PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.to_path_buf().join("Data")))
        .unwrap_or_else(|| std::path::PathBuf::from("Data"))
}
