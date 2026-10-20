using System.Globalization;

namespace CarbonFreeze.App.Services;

public static class Formatting
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string HumanSize(long bytes)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return $"{bytes} B";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value >= 100
            ? $"{value:0} {Units[unit]}"
            : $"{value:0.#} {Units[unit]}";
    }

    public static string LocalTime(DateTime? utc) =>
        utc is null
            ? ""
            : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
