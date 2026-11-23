namespace SysSentinel.Data.Initialization;

internal sealed record Migration(int Version, string Description, string Sql);

internal static class Migrations
{
    public static int LatestVersion => All[^1].Version;

    public static readonly IReadOnlyList<Migration> All = new[]
    {
        new Migration(1, "Initial schema: Profiles, AutomationRules, FirewallRules, AuditLogs", """
            CREATE TABLE Profiles (
                Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                Name                 TEXT    NOT NULL COLLATE NOCASE CHECK (length(trim(Name)) > 0),
                Description          TEXT    NOT NULL DEFAULT '',
                PowerPlanGuid        TEXT    NULL,
                IsFocusModeEnabled   INTEGER NOT NULL DEFAULT 0 CHECK (IsFocusModeEnabled IN (0, 1)),
                AutoTriggerProcesses TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(AutoTriggerProcesses)),
                IsActive             INTEGER NOT NULL DEFAULT 0 CHECK (IsActive IN (0, 1)),
                CreatedAt            TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            );
            CREATE UNIQUE INDEX UX_Profiles_Name ON Profiles (Name);
            -- Tối đa 1 profile active tại một thời điểm (ràng buộc ở tầng DB)
            CREATE UNIQUE INDEX UX_Profiles_SingleActive ON Profiles (IsActive) WHERE IsActive = 1;

            CREATE TABLE AutomationRules (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId      INTEGER NOT NULL REFERENCES Profiles (Id) ON DELETE CASCADE,
                ConditionType  TEXT    NOT NULL,
                ConditionValue TEXT    NOT NULL DEFAULT '',
                ActionType     TEXT    NOT NULL,
                ActionPayload  TEXT    NULL CHECK (ActionPayload IS NULL OR json_valid(ActionPayload)),
                Priority       INTEGER NOT NULL DEFAULT 100,
                IsEnabled      INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1))
            );
            CREATE INDEX IX_AutomationRules_Profile_Priority ON AutomationRules (ProfileId, Priority);
            CREATE INDEX IX_AutomationRules_Enabled ON AutomationRules (IsEnabled) WHERE IsEnabled = 1;

            CREATE TABLE FirewallRules (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleName   TEXT    NOT NULL COLLATE NOCASE CHECK (length(trim(RuleName)) > 0),
                TargetPath TEXT    NULL,
                RemoteIp   TEXT    NULL,
                Direction  TEXT    NOT NULL CHECK (Direction IN ('Inbound', 'Outbound')),
                Action     TEXT    NOT NULL CHECK (Action IN ('Allow', 'Block')),
                IsEnabled  INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1)),
                CreatedAt  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
                CHECK (TargetPath IS NOT NULL OR RemoteIp IS NOT NULL)
            );
            CREATE UNIQUE INDEX UX_FirewallRules_RuleName ON FirewallRules (RuleName);
            CREATE INDEX IX_FirewallRules_TargetPath ON FirewallRules (TargetPath);

            CREATE TABLE AuditLogs (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                Category          TEXT    NOT NULL,
                ActionName        TEXT    NOT NULL,
                Target            TEXT    NOT NULL DEFAULT '',
                Status            TEXT    NOT NULL,
                RollbackType      TEXT    NOT NULL DEFAULT 'None',
                OriginalStateJson TEXT    NULL CHECK (OriginalStateJson IS NULL OR json_valid(OriginalStateJson)),
                IsRolledBack      INTEGER NOT NULL DEFAULT 0 CHECK (IsRolledBack IN (0, 1)),
                Timestamp         TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            );
            CREATE INDEX IX_AuditLogs_Timestamp ON AuditLogs (Timestamp DESC);
            CREATE INDEX IX_AuditLogs_Category_Timestamp ON AuditLogs (Category, Timestamp DESC);
            -- Truy vấn "việc có thể khôi phục" phải dùng đúng điều kiện WHERE này để index được áp dụng
            CREATE INDEX IX_AuditLogs_PendingRollback ON AuditLogs (Timestamp DESC)
                WHERE IsRolledBack = 0 AND RollbackType <> 'None';
            """)
    };
}
