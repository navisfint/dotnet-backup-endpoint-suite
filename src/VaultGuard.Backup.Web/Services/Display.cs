using VaultGuard.Common.Models;

namespace VaultGuard.Backup.Web.Services;

/// <summary>
/// 界面展示辅助工具。集中处理备份模式名称、字节数格式化、状态对应的样式类、
/// 以及文件通配符排除匹配等纯展示/纯逻辑转换，页面层只调用不重复实现。
/// </summary>
public static class Display
{
    /// <summary>把备份模式枚举转换为中文名称。</summary>
    public static string ModeName(BackupMode mode) => mode switch
    {
        BackupMode.Full => "全量",
        BackupMode.Differential => "差异",
        BackupMode.Incremental => "增量",
        _ => "未知"
    };

    /// <summary>把备份模式整数（数据库字段）转换为中文名称。</summary>
    public static string ModeName(int mode) => ModeName((BackupMode)mode);

    /// <summary>把备份模式映射为标签样式类（全量 / 差异 / 增量各自配色）。</summary>
    public static string ModeCssClass(BackupMode mode) => mode switch
    {
        BackupMode.Full => "mode-full",
        BackupMode.Differential => "mode-differential",
        BackupMode.Incremental => "mode-incremental",
        _ => ""
    };

    /// <summary>把备份模式整数映射为标签样式类。</summary>
    public static string ModeCssClass(int mode) => ModeCssClass((BackupMode)mode);

    /// <summary>把备份点/还原记录的状态映射为状态样式类：online=正常，offline=失败，其余为进行中。</summary>
    public static string StatusClass(string? status) => status switch
    {
        "成功" => "online",
        "失败" => "offline",
        _ => "pending"
    };

    /// <summary>把主机连通性检测结果映射为状态样式类。</summary>
    public static string HostStatusClass(string? status) => status switch
    {
        "在线" => "online",
        "离线" => "offline",
        _ => "pending"
    };

    /// <summary>把字节数格式化为人性化文本（B / KB / MB / GB / TB）。</summary>
    public static string Size(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // 字节数本身不保留小数，其余保留两位
        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }

    /// <summary>把耗时秒数格式化为易读文本。</summary>
    public static string Elapsed(double seconds)
    {
        if (seconds <= 0)
        {
            return "—";
        }

        if (seconds < 60)
        {
            return $"{seconds:0.##} 秒";
        }

        var minutes = (int)(seconds / 60);
        var rest = seconds - minutes * 60;
        return $"{minutes} 分 {rest:0.#} 秒";
    }

    /// <summary>
    /// 按分号/换行拆分多值文本（来源目录、排除规则等），自动去除空白项。
    /// </summary>
    public static List<string> SplitList(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => p.Trim())
                  .Where(p => p.Length > 0)
                  .ToList();

    /// <summary>
    /// 通配符匹配（区分大小写的文件系统无关：Windows 下按不区分大小写处理）。
    /// 支持 <c>*</c>（任意多个字符）与 <c>?</c>（单个字符），用于排除规则如 <c>*.tmp</c>。
    /// </summary>
    /// <param name="pattern">通配符模式。</param>
    /// <param name="text">待匹配文本（通常是文件名或相对路径）。</param>
    public static bool WildcardMatch(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        // 把通配符翻译为正则：先转义正则元字符，再还原 * 与 ?
        var regex = System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");
        return System.Text.RegularExpressions.Regex.IsMatch(
            text ?? "",
            "^" + regex + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
