use std::path::{Path, PathBuf};
use std::process::Command;

const LAUNCH_AGENT_ID: &str = "com.shijian.shell";

fn get_plist_path() -> PathBuf {
    dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("/tmp"))
        .join("Library")
        .join("LaunchAgents")
        .join(format!("{}.plist", LAUNCH_AGENT_ID))
}

fn current_uid() -> Result<String, String> {
    let output = Command::new("id")
        .arg("-u")
        .output()
        .map_err(|e| format!("Failed to execute id: {}", e))?;
    let uid = String::from_utf8(output.stdout).map_err(|e| format!("Failed to parse uid: {}", e))?;
    Ok(uid.trim().to_string())
}

fn build_plist_content(exe_path: &Path, data_dir: Option<&PathBuf>) -> String {
    let exe_str = exe_path.to_string_lossy();
    let data_dir_args = if let Some(dir) = data_dir {
        format!(
            "\n\t\t<string>--data-dir</string>\n\t\t<string>{}</string>",
            dir.to_string_lossy()
        )
    } else {
        String::new()
    };

    let log_dir = dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("/tmp"))
        .join("Library")
        .join("Logs")
        .join("ShiJian");

    format!(
        r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>{}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{}</string>
        <string>run</string>{}
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardErrorPath</key>
    <string>{}/shell.err</string>
</dict>
</plist>"#,
        LAUNCH_AGENT_ID, exe_str, data_dir_args, log_dir.display()
    )
}

/// 摘掉旧品牌（Taix）的 LaunchAgent，避免升级后新旧两份同时采集。
fn remove_legacy_agent() {
    let Some(home) = dirs::home_dir() else {
        return;
    };
    let plist = home
        .join("Library")
        .join("LaunchAgents")
        .join("com.taix.shell.plist");
    if !plist.exists() {
        return;
    }

    if let Ok(uid) = current_uid() {
        let _ = Command::new("launchctl")
            .args(["bootout", &format!("gui/{}/com.taix.shell", uid)])
            .output();
    }
    let _ = std::fs::remove_file(&plist);
}

pub fn install(exe_path: &Path, data_dir: Option<&PathBuf>, _task_name: &str) -> Result<(), String> {
    // 旧品牌（Taix）残留：先卸掉旧 LaunchAgent，避免新旧两份同时采集
    remove_legacy_agent();

    let plist_content = build_plist_content(exe_path, data_dir);
    let plist_path = get_plist_path();

    if let Some(parent) = plist_path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|e| format!("Failed to create LaunchAgents directory: {}", e))?;
    }

    let log_dir = dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("/tmp"))
        .join("Library")
        .join("Logs")
        .join("ShiJian");
    std::fs::create_dir_all(&log_dir)
        .map_err(|e| format!("Failed to create Logs directory: {}", e))?;

    std::fs::write(&plist_path, &plist_content)
        .map_err(|e| format!("Failed to write plist: {}", e))?;

    let uid = current_uid()?;
    let domain = format!("gui/{}", uid);
    let label = format!("{}/{}", domain, LAUNCH_AGENT_ID);
    let _ = Command::new("launchctl")
        .args(["bootout", &label])
        .output();

    let _ = Command::new("launchctl")
        .args(["enable", &label])
        .output();

    let output = Command::new("launchctl")
        .args(["bootstrap", &domain, plist_path.to_str().ok_or("Invalid plist path")?])
        .output()
        .map_err(|e| format!("Failed to execute launchctl: {}", e))?;

    if output.status.success() {
        return Ok(());
    }

    let bootstrap_stderr = String::from_utf8_lossy(&output.stderr).trim().to_string();
    let fallback = Command::new("launchctl")
        .args(["load", "-w", plist_path.to_str().ok_or("Invalid plist path")?])
        .output()
        .map_err(|e| format!("Failed to execute launchctl: {}", e))?;

    if fallback.status.success() {
        Ok(())
    } else {
        let stderr = String::from_utf8_lossy(&fallback.stderr);
        if stderr.contains("service already loaded") || stderr.contains("already loaded") {
            return Err(format!(
                "'{}' is already running. Run 'uninstall' first if you want to reconfigure.",
                LAUNCH_AGENT_ID
            ));
        }
        Err(format!(
            "launchctl bootstrap failed: {}; load failed: {}",
            bootstrap_stderr,
            stderr.trim()
        ))
    }
}

pub fn uninstall(_task_name: &str) -> Result<(), String> {
    let plist_path = get_plist_path();

    if let Ok(uid) = current_uid() {
        let _ = Command::new("launchctl")
            .args(["bootout", &format!("gui/{}/{}", uid, LAUNCH_AGENT_ID)])
            .output();
    }

    if plist_path.exists() {
        std::fs::remove_file(&plist_path)
            .map_err(|e| format!("Failed to remove plist: {}", e))?;
    }

    Ok(())
}
