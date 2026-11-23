namespace SysSentinel.Data;

public sealed class DatabaseOptions
{
    public const string DefaultFileName = "syssentinel.db";

    /// <summary>Mặc định: %LocalAppData%\SysSentinel\syssentinel.db</summary>
    public string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SysSentinel",
        DefaultFileName);
}
