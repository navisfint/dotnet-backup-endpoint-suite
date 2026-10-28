namespace VaultGuard.Common;

/// <summary>
/// 公司品牌与版权信息。两套系统的登录页、关于页统一引用此处，
/// 保证"登录界面展示品牌名称与版权"这一展示要求只有唯一来源。
/// </summary>
public static class Branding
{
    /// <summary>展示用公司名称（占位，可由使用方替换）。</summary>
    public const string CompanyName = "示例科技有限公司";

    /// <summary>版权声明全文（登录页/关于页原样展示）。</summary>
    public const string Copyright = "Copyright © 2026 示例科技有限公司. All rights reserved.";

    /// <summary>系统一产品名与版本。</summary>
    public const string BackupProductName = "虚拟化数据备份还原系统";
    public const string BackupProductVersion = "V1.0";

    /// <summary>系统二产品名与版本。</summary>
    public const string EndpointProductName = "终端安全准入与行为审计系统";
    public const string EndpointProductVersion = "V1.0";
}
