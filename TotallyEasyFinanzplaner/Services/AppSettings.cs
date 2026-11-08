using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>Programmeinstellungen (einstellungen.json im Programmdatenordner).</summary>
public sealed class AppSettings
{
    private const int MaxRecent = 8;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Zuletzt geöffneter Finanzplan – wird beim Start wieder geöffnet.</summary>
    public string? LastFile { get; set; }

    /// <summary>Zuletzt verwendete Finanzpläne, neueste zuerst.</summary>
    public List<string> RecentFiles { get; set; } = [];

    private static string FilePath => Path.Combine(DataStore.FolderPath, "einstellungen.json");

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                s.RecentFiles ??= [];
                return s;
            }
        }
        catch (Exception) { /* defekte Einstellungen sind kein Grund abzubrechen */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataStore.FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* nicht speicherbar – gilt dann nur für diese Sitzung */ }
    }

    /// <summary>Merkt sich einen geöffneten Plan als zuletzt verwendet.</summary>
    public void RememberFile(string path)
    {
        path = Path.GetFullPath(path);
        LastFile = path;
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
        Save();
    }

    public void ForgetFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(LastFile, path, StringComparison.OrdinalIgnoreCase)) LastFile = null;
        Save();
    }
}
