using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;

namespace NetWard.Modern.Administrator;

public sealed class AdminPolicy
{
    public bool HasPolicy { get; set; }
    public long Version { get; set; }
    public string Mode { get; set; } = "AllowList";
    public List<AdminRule> Rules { get; } = new();
    public List<AdminTimedCategory> TimedCategories { get; } = new();
    public List<AdminTimedRule> TimedRules { get; } = new();
    public List<AdminCategory> Categories { get; } = new();
    public List<AdminCategoryRule> CategoryRules { get; } = new();
    public long Events { get; set; }
}

public sealed class AdminRule
{
    public string Domain { get; set; }
    public bool IncludeSubdomains { get; set; }
    public bool Enabled { get; set; }
    public string ScopeText => IncludeSubdomains ? "含子域" : "精确";
    public string StateText => Enabled ? "已启用" : "已停用";
    public Brush StateBrush => Enabled
        ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 15, 123, 15))
        : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 157, 93, 0));
}

public sealed class AdminTimedCategory
{
    public string Name { get; set; }
    public int LimitMinutes { get; set; }
    public string Detail => Name + " · 每日 " + LimitMinutes + " 分钟";
}

public sealed class AdminTimedRule
{
    public string Domain { get; set; }
    public bool IncludeSubdomains { get; set; }
    public string Category { get; set; }
    public bool Enabled { get; set; }
    public string Detail => Domain + " · " + Category + (IncludeSubdomains ? " · 含子域" : string.Empty);
}

public sealed class AdminCategory
{
    public string Name { get; set; }
    public string SourceName { get; set; }
    public bool Enabled { get; set; }
    public int RuleCount { get; set; }
    public string Detail => RuleCount + " 条域名 · " + (Enabled ? "已启用" : "未启用");
    public string StateText => Enabled ? "已启用" : "未启用";
    public Brush StateBrush => Enabled
        ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 15, 123, 15))
        : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 157, 93, 0));
}

public sealed class AdminCategoryRule
{
    public string Domain { get; set; }
    public bool IncludeSubdomains { get; set; }
    public string Category { get; set; }
    public bool Enabled { get; set; }
}

public sealed class AdminEventRow
{
    public string Time { get; set; }
    public string Domain { get; set; }
    public string Method { get; set; }
    public string ResultText { get; set; }
    public Brush ResultBrush { get; set; }
}

public sealed class AdminHistoryRow
{
    public long Version { get; set; }
    public string Mode { get; set; }
    public int RuleCount { get; set; }
    public string UpdatedAt { get; set; }
    public string Detail => "版本 " + Version + " · " + Mode + " · " + RuleCount + " 条规则 · " + UpdatedAt;
}

public sealed class CategoryParseResult
{
    public List<AdminCategory> Categories { get; } = new();
    public List<AdminCategoryRule> Rules { get; } = new();
    public List<string> Errors { get; } = new();
    public int Duplicates { get; set; }
}
public sealed class AdminAccessRequest
{
    public string Id { get; set; }
    public string Url { get; set; }
    public string Host { get; set; }
    public string Reason { get; set; }
    public string TimeText { get; set; }
    public string StatusText { get; set; }
}