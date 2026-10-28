using System.Globalization;

namespace VaultGuard.Endpoint.Server.Services;

/// <summary>
/// 数据行读取辅助工具。<see cref="Database.Query"/> 返回的行是
/// <c>Dictionary&lt;string, object?&gt;</c>（键名大小写不敏感），
/// 本类把其中的值安全地转换为强类型，避免页面层到处写强制转换。
/// </summary>
public static class Row
{
    /// <summary>数据库中的时间统一以该格式存储为 TEXT。</summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>读取字符串，字段不存在或为 NULL 时返回空串。</summary>
    public static string Str(IReadOnlyDictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return "";
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>读取 64 位整数，字段不存在或无法转换时返回 0。</summary>
    public static long Long(IReadOnlyDictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>读取 32 位整数，字段不存在或无法转换时返回 0。</summary>
    public static int Int(IReadOnlyDictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>读取布尔值（SQLite 中以 0/1 存储），字段不存在时返回 false。</summary>
    public static bool Bool(IReadOnlyDictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
    }

    /// <summary>读取时间字段，字段缺失或格式非法时返回 null。</summary>
    public static DateTime? Time(IReadOnlyDictionary<string, object?>? row, string key)
    {
        var text = Str(row, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>把时间转换为数据库存储格式的文本。</summary>
    public static string ToDbText(DateTime value) => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>把当前时间转换为数据库存储格式的文本。</summary>
    public static string NowText() => DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
