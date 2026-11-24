using System.Collections.ObjectModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Data;

namespace SysSentinel.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IProfileRepository _profiles;
    private readonly DatabaseOptions _dbOptions;

    [ObservableProperty]
    private string _statusText = "Đang tải...";

    public ObservableCollection<Profile> Profiles { get; } = new();

    public MainViewModel(IProfileRepository profiles, DatabaseOptions dbOptions)
    {
        _profiles = profiles;
        _dbOptions = dbOptions;
    }
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var all = await _profiles.GetAllAsync();
            Profiles.Clear();
            foreach (var p in all) Profiles.Add(p);

            var active = all.FirstOrDefault(p => p.IsActive);
            using var identity = WindowsIdentity.GetCurrent();
            var isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            StatusText = $"Admin: {(isAdmin ? "Có" : "KHÔNG")}  |  Profile đang bật: {active?.Name ?? "(không có)"}  |  DB: {_dbOptions.DatabasePath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi đọc dữ liệu: {ex.Message}";
        }
    }
}
