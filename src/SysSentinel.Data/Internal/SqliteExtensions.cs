using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SysSentinel.Data.Internal;

internal static class SqliteExtensions
{
    /// <summary>Thêm parameter (tên dạng "$name"); null -> DBNull.</summary>
    public static SqliteParameter AddParam(this SqliteCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    public static string? GetNullableString(this SqliteDataReader r, int ordinal)
        => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);

    /// <summary>Thời gian lưu dạng ISO-8601 UTC (sắp xếp theo chuỗi vẫn đúng thứ tự thời gian).</summary>
    public static string ToDb(this DateTime value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : fallback;
}
