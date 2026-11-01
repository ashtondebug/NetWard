using Microsoft.UI.Xaml.Media;

namespace NetWard.Modern;

public sealed class StatCard
{
    public string Caption { get; set; }
    public string Value { get; set; }
    public string Hint { get; set; }
}

public sealed class EventRow
{
    public string Time { get; set; }
    public string Domain { get; set; }
    public string Method { get; set; }
    public string ResultText { get; set; }
    public Brush ResultBrush { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class RuleRow
{
    public string Domain { get; set; }
    public string Scope { get; set; }
    public string State { get; set; }
    public Brush StateBrush { get; set; }
}

public sealed class CategoryRow
{
    public string Name { get; set; }
    public string RuleCount { get; set; }
    public string State { get; set; }
    public string Source { get; set; }
    public Brush StateBrush { get; set; }
}

public sealed class PolicySummary
{
    public bool HasPolicy { get; set; }
    public long Version { get; set; }
    public string Mode { get; set; }
    public int Rules { get; set; }
    public int Categories { get; set; }
    public int CategoryRules { get; set; }
    public long Events { get; set; }
    public System.Collections.Generic.List<RuleRow> RuleRows { get; } = new();
    public System.Collections.Generic.List<CategoryRow> CategoryRows { get; } = new();
}