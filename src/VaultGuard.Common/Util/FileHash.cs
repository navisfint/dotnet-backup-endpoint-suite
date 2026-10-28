using System.Security.Cryptography;

namespace VaultGuard.Common.Util;

/// <summary>
/// 文件摘要工具。增量备份依靠"大小 + 最后写入时间"快速判重，
/// 只在时间戳发生变化时再用 CRC32 复核内容，避免大文件反复全量读盘。
/// </summary>
public static class FileHash
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        const uint polynomial = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ polynomial : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>计算字节数组的 CRC32。</summary>
    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>计算文件的 CRC32（流式，内存占用恒定）。</summary>
    public static uint ComputeFileCrc32(string path)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[64 * 1024];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>计算文件 SHA-256（十六进制小写），用于交付包的完整性核对。</summary>
    public static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
