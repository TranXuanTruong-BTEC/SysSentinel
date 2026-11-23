using SysSentinel.Core.Common;
using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IFirewallController
{
    Task<OperationResult> CreateRuleAsync(FirewallRule rule, CancellationToken ct = default);
    Task<OperationResult> RemoveRuleAsync(string ruleName, CancellationToken ct = default);
    Task<OperationResult> SetRuleEnabledAsync(string ruleName, bool enabled, CancellationToken ct = default);
    Task<bool> RuleExistsAsync(string ruleName, CancellationToken ct = default);
}
