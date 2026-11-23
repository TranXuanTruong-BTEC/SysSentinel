using Microsoft.Extensions.DependencyInjection;
using SysSentinel.Core.Interfaces;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Initialization;
using SysSentinel.Data.Repositories;

namespace SysSentinel.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSysSentinelData(
        this IServiceCollection services, Action<DatabaseOptions>? configure = null)
    {
        var options = new DatabaseOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        // Repository stateless (mỗi lần gọi tự mở/đóng connection) nên singleton là an toàn.
        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IAutomationRuleRepository, AutomationRuleRepository>();
        services.AddSingleton<IFirewallRuleRepository, FirewallRuleRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
        return services;
    }
}
