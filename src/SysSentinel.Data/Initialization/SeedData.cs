using SysSentinel.Core.Models;

namespace SysSentinel.Data.Initialization;

internal static class SeedData
{
    public static IReadOnlyList<Profile> DefaultProfiles() => new[]
    {
        new Profile
        {
            Name = "Default",
            Description = "Cấu hình cân bằng hằng ngày, không can thiệp tiến trình.",
            PowerPlanGuid = PowerPlans.Balanced,
            IsFocusModeEnabled = false,
            AutoTriggerProcesses = new List<string>(),
            IsActive = true
        },
        new Profile
        {
            Name = "Gaming Mode",
            Description = "Ưu tiên hiệu năng tối đa, giảm tiến trình nền khi chơi game.",
            PowerPlanGuid = PowerPlans.HighPerformance,
            IsFocusModeEnabled = true,
            AutoTriggerProcesses = new List<string> { "steam.exe", "EpicGamesLauncher.exe", "RiotClientServices.exe", "GenshinImpact.exe" },
            IsActive = false
        },
        new Profile
        {
            Name = "Work / Focus Mode",
            Description = "Tập trung làm việc: hạn chế ứng dụng gây xao nhãng, giữ máy cân bằng.",
            PowerPlanGuid = PowerPlans.Balanced,
            IsFocusModeEnabled = true,
            AutoTriggerProcesses = new List<string> { "Code.exe", "devenv.exe", "WINWORD.EXE", "EXCEL.EXE" },
            IsActive = false
        }
    };
}
