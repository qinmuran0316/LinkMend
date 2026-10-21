using System.Text.Json;
using LinkMend.Core.Paths;

namespace LinkMend.App.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = LanguageManager.Chinese;

    public bool ScanDesktop { get; set; } = true;

    public bool ScanStartMenu { get; set; } = true;

    public List<string> CustomFolders { get; set; } = new();

    public List<string> SearchRoots { get; set; } = new();
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AppSettingsStore(string rootDirectory)
    {
        SettingsFilePath = Path.Combine(rootDirectory, "settings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var bytes = File.ReadAllBytes(SettingsFilePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bytes = bytes[3..];
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions);
            if (settings is null)
            {
                return new AppSettings();
            }

            settings.CustomFolders = Normalize(settings.CustomFolders);
            settings.SearchRoots = Normalize(settings.SearchRoots);
            settings.Language = string.Equals(settings.Language, LanguageManager.English, StringComparison.OrdinalIgnoreCase)
                ? LanguageManager.English
                : LanguageManager.Chinese;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        AtomicFile.WriteAllBytes(SettingsFilePath, json);
    }

    private static List<string> Normalize(List<string>? values)
    {
        if (values is null)
        {
            return new List<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
