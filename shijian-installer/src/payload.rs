//! SFX 尾部标记：打包器（`bin/pack-payload`）写、安装器（`sfx`）读，**两处必须共用这一份**。
//!
//! 格式（永远在文件末尾）：`<压缩数据> <MARKER> <payload 长度 u64 小端>`
//!
//! ⚠️ 标记长度参与偏移计算。历史上这里踩过一次：把标记从 16 字节换成 18 字节，
//! 但读取端仍按 16 字节读，结果所有 Setup 都报「安装程序无效」。
//! 所以：**固定 16 字节**，日后要换标记就换整套格式（并保留旧标记的兼容读取）。

/// 16 字节。
pub const PAYLOAD_MARKER: &[u8; 16] = b"SHIJIAN_PAYLOAD!";

/// 尾部元数据总长 = 标记长度 + 8 字节长度字段。
#[allow(dead_code)] // 打包器只写标记，不需要算总长
pub const TRAILER_SIZE: usize = PAYLOAD_MARKER.len() + 8;
