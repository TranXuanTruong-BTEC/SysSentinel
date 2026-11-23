namespace SysSentinel.Core.Models;

public sealed class AutomationRule
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public RuleConditionType ConditionType { get; set; }
    public string ConditionValue { get; set; } = string.Empty;
    public RuleActionType ActionType { get; set; }

    /// <summary>JSON tham số cho action (null nếu không cần).</summary>
    public string? ActionPayload { get; set; }

    /// <summary>Số nhỏ = ưu tiên cao.</summary>
    public int Priority { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
}
