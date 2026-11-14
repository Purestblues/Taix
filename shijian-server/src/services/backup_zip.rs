use std::collections::HashMap;
use std::io::{Cursor, Read, Write};

use crc32fast::Hasher as Crc32Hasher;
use flate2::read::DeflateDecoder;
use flate2::write::DeflateEncoder;
use flate2::Compression;

const LOCAL_FILE_HEADER_SIG: u32 = 0x0403_4b50;
const CENTRAL_DIR_HEADER_SIG: u32 = 0x0201_4b50;
const EOCD_SIG: u32 = 0x0605_4b50;
const UTF8_FLAG: u16 = 1 << 11;
const METHOD_STORE: u16 = 0;
const METHOD_DEFLATE: u16 = 8;

#[derive(Debug, Clone)]
pub struct ZipEntryData {
    pub name: String,
    pub data: Vec<u8>,
}

struct WrittenEntry {
    name: Vec<u8>,
    method: u16,
    crc32: u32,
    compressed_size: u32,
    uncompressed_size: u32,
    local_header_offset: u32,
}

/// 将若干条目打包为标准 ZIP 字节流（UTF-8 文件名 + CRC-32 + Deflate/Store）。
pub fn build_zip(entries: &[ZipEntryData]) -> Result<Vec<u8>, String> {
    let mut out = Vec::new();
    let mut written = Vec::with_capacity(entries.len());

    for entry in entries {
        let normalized = normalize_archive_path(&entry.name)?;
        let name_bytes = normalized.as_bytes().to_vec();
        if name_bytes.len() > u16::MAX as usize {
            return Err(format!("归档路径过长: {}", normalized));
        }

        let mut hasher = Crc32Hasher::new();
        hasher.update(&entry.data);
        let crc32 = hasher.finalize();

        let uncompressed_size = u32::try_from(entry.data.len())
            .map_err(|_| format!("归档条目过大（超过 4GB 上限）: {}", normalized))?;

        let mut encoder = DeflateEncoder::new(Vec::new(), Compression::default());
        encoder
            .write_all(&entry.data)
            .map_err(|e| format!("压缩条目 {} 失败: {}", normalized, e))?;
        let deflated = encoder
            .finish()
            .map_err(|e| format!("完成压缩条目 {} 失败: {}", normalized, e))?;

        let (method, payload) = if deflated.len() < entry.data.len() {
            (METHOD_DEFLATE, deflated)
        } else {
            (METHOD_STORE, entry.data.clone())
        };

        let compressed_size = u32::try_from(payload.len())
            .map_err(|_| format!("压缩后条目过大: {}", normalized))?;
        let local_header_offset =
            u32::try_from(out.len()).map_err(|_| "归档总大小超过 ZIP32 上限".to_string())?;

        // Local file header (30 bytes + filename)
        push_u32(&mut out, LOCAL_FILE_HEADER_SIG);
        push_u16(&mut out, 20); // version needed: 2.0
        push_u16(&mut out, UTF8_FLAG);
        push_u16(&mut out, method);
        push_u16(&mut out, 0); // mod time
        push_u16(&mut out, 0x5421); // mod date (2022-01-01)
        push_u32(&mut out, crc32);
        push_u32(&mut out, compressed_size);
        push_u32(&mut out, uncompressed_size);
        push_u16(&mut out, name_bytes.len() as u16);
        push_u16(&mut out, 0); // extra len
        out.extend_from_slice(&name_bytes);
        out.extend_from_slice(&payload);

        written.push(WrittenEntry {
            name: name_bytes,
            method,
            crc32,
            compressed_size,
            uncompressed_size,
            local_header_offset,
        });
    }

    let cd_offset =
        u32::try_from(out.len()).map_err(|_| "归档总大小超过 ZIP32 上限".to_string())?;

    for item in &written {
        push_u32(&mut out, CENTRAL_DIR_HEADER_SIG);
        push_u16(&mut out, 20); // version made by
        push_u16(&mut out, 20); // version needed
        push_u16(&mut out, UTF8_FLAG);
        push_u16(&mut out, item.method);
        push_u16(&mut out, 0);
        push_u16(&mut out, 0x5421);
        push_u32(&mut out, item.crc32);
        push_u32(&mut out, item.compressed_size);
        push_u32(&mut out, item.uncompressed_size);
        push_u16(&mut out, item.name.len() as u16);
        push_u16(&mut out, 0); // extra len
        push_u16(&mut out, 0); // comment len
        push_u16(&mut out, 0); // disk number start
        push_u16(&mut out, 0); // internal attrs
        push_u32(&mut out, 0); // external attrs
        push_u32(&mut out, item.local_header_offset);
        out.extend_from_slice(&item.name);
    }

    let cd_size = u32::try_from(out.len() - cd_offset as usize)
        .map_err(|_| "中央目录大小超出上限".to_string())?;
    let entry_count = u16::try_from(written.len())
        .map_err(|_| "归档文件数量超过 65535 上限".to_string())?;

    push_u32(&mut out, EOCD_SIG);
    push_u16(&mut out, 0); // disk
    push_u16(&mut out, 0); // cd start disk
    push_u16(&mut out, entry_count);
    push_u16(&mut out, entry_count);
    push_u32(&mut out, cd_size);
    push_u32(&mut out, cd_offset);
    push_u16(&mut out, 0); // comment len

    Ok(out)
}

/// 解析标准 ZIP 归档并校验 CRC-32 与安全路径，返回规范化路径到解压内容的映射。
pub fn read_zip(bytes: &[u8]) -> Result<HashMap<String, Vec<u8>>, String> {
    if bytes.len() < 22 {
        return Err("无效的 .shijian-backup 文件（文件过小）".to_string());
    }

    let min_offset = bytes.len().saturating_sub(22 + u16::MAX as usize);
    let mut eocd_pos = None;
    for pos in (min_offset..=bytes.len() - 22).rev() {
        if read_u32(bytes, pos) == Some(EOCD_SIG) {
            let comment_len = read_u16(bytes, pos + 20).unwrap_or(0) as usize;
            if pos + 22 + comment_len == bytes.len() {
                eocd_pos = Some(pos);
                break;
            }
        }
    }

    let eocd = eocd_pos.ok_or_else(|| "无效的 .shijian-backup 文件（未找到 ZIP 目录尾）".to_string())?;
    let total_entries = read_u16(bytes, eocd + 10).ok_or("读取 ZIP 条目数失败")? as usize;
    let cd_size = read_u32(bytes, eocd + 12).ok_or("读取中央目录大小失败")? as usize;
    let cd_offset = read_u32(bytes, eocd + 16).ok_or("读取中央目录偏移失败")? as usize;

    if cd_offset.checked_add(cd_size).map_or(true, |end| end > eocd) {
        return Err("无效的 .shijian-backup 文件（中央目录越界）".to_string());
    }

    let mut result = HashMap::new();
    let mut cursor = cd_offset;

    for _ in 0..total_entries {
        if cursor + 46 > cd_offset + cd_size {
            return Err("ZIP 中央目录条目损坏".to_string());
        }
        if read_u32(bytes, cursor) != Some(CENTRAL_DIR_HEADER_SIG) {
            return Err("ZIP 中央目录签名无效".to_string());
        }

        let method = read_u16(bytes, cursor + 10).ok_or("读取压缩方法失败")?;
        let expected_crc = read_u32(bytes, cursor + 16).ok_or("读取 CRC32 失败")?;
        let compressed_size = read_u32(bytes, cursor + 20).ok_or("读取压缩大小失败")? as usize;
        let uncompressed_size = read_u32(bytes, cursor + 24).ok_or("读取解压大小失败")? as usize;
        let name_len = read_u16(bytes, cursor + 28).ok_or("读取文件名长度失败")? as usize;
        let extra_len = read_u16(bytes, cursor + 30).ok_or("读取扩展字段长度失败")? as usize;
        let comment_len = read_u16(bytes, cursor + 32).ok_or("读取注释长度失败")? as usize;
        let local_offset = read_u32(bytes, cursor + 42).ok_or("读取本地头偏移失败")? as usize;

        let name_start = cursor + 46;
        let name_end = name_start + name_len;
        if name_end + extra_len + comment_len > cd_offset + cd_size {
            return Err("ZIP 文件名越界".to_string());
        }
        let raw_name = std::str::from_utf8(&bytes[name_start..name_end])
            .map_err(|_| "ZIP 文件名不是有效 UTF-8".to_string())?;
        cursor = name_end + extra_len + comment_len;

        // 跳过目录条目
        if raw_name.ends_with('/') || raw_name.ends_with('\\') {
            continue;
        }

        let clean_name = normalize_archive_path(raw_name)?;

        if local_offset + 30 > cd_offset || read_u32(bytes, local_offset) != Some(LOCAL_FILE_HEADER_SIG) {
            return Err(format!("ZIP 本地文件头损坏: {}", clean_name));
        }
        let local_name_len = read_u16(bytes, local_offset + 26).ok_or("读取本地文件名长度失败")? as usize;
        let local_extra_len = read_u16(bytes, local_offset + 28).ok_or("读取本地扩展长度失败")? as usize;
        let data_start = local_offset + 30 + local_name_len + local_extra_len;
        let data_end = data_start
            .checked_add(compressed_size)
            .ok_or("ZIP 数据偏移溢出")?;
        if data_end > cd_offset {
            return Err(format!("ZIP 数据区越界: {}", clean_name));
        }

        let compressed_slice = &bytes[data_start..data_end];
        let payload = match method {
            METHOD_STORE => compressed_slice.to_vec(),
            METHOD_DEFLATE => {
                let mut decoder = DeflateDecoder::new(Cursor::new(compressed_slice));
                let mut decoded = Vec::with_capacity(uncompressed_size.min(64 * 1024 * 1024));
                decoder
                    .read_to_end(&mut decoded)
                    .map_err(|e| format!("解压条目 {} 失败: {}", clean_name, e))?;
                decoded
            }
            other => {
                return Err(format!("不支持的 ZIP 压缩算法 {}（条目 {}）", other, clean_name));
            }
        };

        let mut hasher = Crc32Hasher::new();
        hasher.update(&payload);
        if hasher.finalize() != expected_crc {
            return Err(format!("备份文件已损坏（CRC32 校验失败: {}）", clean_name));
        }

        result.insert(clean_name, payload);
    }

    Ok(result)
}

fn normalize_archive_path(raw: &str) -> Result<String, String> {
    let replaced = raw.replace('\\', "/");
    let trimmed = replaced.trim_start_matches("./");
    if trimmed.is_empty() || trimmed.starts_with('/') {
        return Err(format!("非法的归档路径: {}", raw));
    }
    let mut parts = Vec::new();
    for seg in trimmed.split('/') {
        if seg.is_empty() || seg == "." {
            continue;
        }
        if seg == ".." || seg.contains(':') {
            return Err(format!("检测到不安全的归档路径: {}", raw));
        }
        parts.push(seg);
    }
    if parts.is_empty() {
        return Err(format!("非法的归档路径: {}", raw));
    }
    Ok(parts.join("/"))
}

fn push_u16(buf: &mut Vec<u8>, v: u16) {
    buf.extend_from_slice(&v.to_le_bytes());
}

fn push_u32(buf: &mut Vec<u8>, v: u32) {
    buf.extend_from_slice(&v.to_le_bytes());
}

fn read_u16(buf: &[u8], idx: usize) -> Option<u16> {
    buf.get(idx..idx + 2)
        .map(|s| u16::from_le_bytes([s[0], s[1]]))
}

fn read_u32(buf: &[u8], idx: usize) -> Option<u32> {
    buf.get(idx..idx + 4)
        .map(|s| u32::from_le_bytes([s[0], s[1], s[2], s[3]]))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trip_zip_preserves_utf8_and_verifies_crc() {
        let entries = vec![
            ZipEntryData {
                name: "manifest.json".to_string(),
                data: br#"{"formatVersion":1,"appName":"ShiJian"}"#.to_vec(),
            },
            ZipEntryData {
                name: "AppIcons/时见_test.png".to_string(),
                data: vec![42u8; 4096],
            },
        ];

        let archive = build_zip(&entries).expect("build zip");
        let parsed = read_zip(&archive).expect("read zip");

        assert_eq!(parsed.get("manifest.json").unwrap(), &entries[0].data);
        assert_eq!(parsed.get("AppIcons/时见_test.png").unwrap(), &entries[1].data);
    }

    #[test]
    fn read_zip_rejects_path_traversal_and_tampered_bytes() {
        let entries = vec![ZipEntryData {
            name: "data.db".to_string(),
            data: b"sqlite-snapshot-payload-1234567890".to_vec(),
        }];
        let mut archive = build_zip(&entries).unwrap();
        // Flip a byte inside the local file payload (just after header + "data.db")
        archive[45] ^= 0xff;
        assert!(read_zip(&archive).is_err());

        let bad_path = build_zip(&[ZipEntryData {
            name: "../evil.txt".to_string(),
            data: b"x".to_vec(),
        }]);
        assert!(bad_path.is_err());
    }
}
