using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Core.Models;

namespace JobTracker.Core;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to a JSON file in the user's
/// application data directory (%APPDATA%\JobTracker\appsettings.json).
/// The settings file is never committed to source control.
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JobTracker");

    /// <summary>Full path to the settings file on disk.</summary>
    public static readonly string SettingsPath =
        Path.Combine(SettingsDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true,
        Converters               = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads settings from disk. Returns a default <see cref="AppSettings"/>
    /// instance if the file does not exist or cannot be read.
    /// </summary>
    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings();
        }
        catch (Exception)
        {
            // File is corrupt or unreadable — return safe defaults.
            // The UI should surface a warning in this case.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves <paramref name="settings"/> to disk, creating the settings
    /// directory if it does not already exist.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>
    /// Returns <c>true</c> if the minimum required settings are present to
    /// attempt a fetch run: a service account key path and a spreadsheet ID.
    /// </summary>
    public static bool IsConfigured(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ServiceAccountKeyPath) &&
        File.Exists(settings.ServiceAccountKeyPath)               &&
        !string.IsNullOrWhiteSpace(settings.SpreadsheetId);
}
