namespace VaultGuard.Endpoint.Server.Services;

/// <summary>
/// 审计事件的界面展示辅助。集中定义事件类型的取值与对应的样式类，
/// 避免各处散落"新建/修改/删除/重命名"字符串。
/// </summary>
public static class EventDisplay
{
    /// <summary>新建事件。</summary>
    public const string Created = "新建";

    /// <summary>修改事件。</summary>
    public const string Modified = "修改";

    /// <summary>删除事件。</summary>
    public const string Deleted = "删除";

    /// <summary>重命名事件。</summary>
    public const string Renamed = "重命名";

    /// <summary>需要在界面上支持筛选的事件类型全集。</summary>
    public static readonly string[] AllTypes = { Created, Modified, Deleted, Renamed };

    /// <summary>把事件类型映射为标签的样式类后缀。</summary>
    public static string CssClass(string? eventType) => eventType switch
    {
        Created => "type-create",
        Modified => "type-modify",
        Deleted => "type-delete",
        Renamed => "type-rename",
        _ => ""
    };
}
