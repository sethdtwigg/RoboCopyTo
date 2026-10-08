using System.Globalization;

namespace RoboCopyTo.Core;

public static class Format
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
            return bytes.ToString("N0", CultureInfo.CurrentCulture) + (bytes == 1 ? " byte" : " bytes");
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string Elapsed(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
