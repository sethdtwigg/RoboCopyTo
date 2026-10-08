using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoboCopyTo.Core.Presets;

/// <summary>Human-readable, indented JSON for presets.json and settings.json.</summary>
internal static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads a JSON file. A missing file gives null with no warning. An unreadable or invalid file gives null
    /// and a warning; an invalid file is first renamed to "&lt;name&gt;.bad-&lt;timestamp&gt;" so a later save
    /// cannot overwrite the user's data.
    /// </summary>
    public static T? Read<T>(string path, out string? warning) where T : class
    {
        warning = null;
        if (!File.Exists(path))
            return null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"Could not read {path}: {ex.Message}";
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (JsonException ex)
        {
            var backup = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try
            {
                File.Move(path, backup, overwrite: true);
                warning = $"{Path.GetFileName(path)} could not be read ({ex.Message}). It was kept as {Path.GetFileName(backup)}, and defaults are used.";
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                warning = $"{Path.GetFileName(path)} could not be read ({ex.Message}).";
            }
            return null;
        }
    }

    /// <summary>Writes via a uniquely named temp file so a crash, or a second dialog saving at once, never leaves a half-written file.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoboCopyTo");
}
