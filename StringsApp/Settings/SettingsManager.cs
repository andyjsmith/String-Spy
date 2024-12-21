using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StringsApp.Settings;

public class AppSettings
{
    // The values here are the defaults
    public string AppTheme { get; set; } = "System";
    public string Font { get; set; } = "<default>";
    public string DefaultAddressFormat { get; set; } = "Hexadecimal";
    public bool GroupDuplicateStrings { get; set; } = false;

    // Strings
    public int DefaultMinimumStringLength { get; set; } = 4;
    public string CharacterSet { get; set; } = "Ascii";
    public string DefaultEncoding { get; set; } = "us-ascii";

    // Search
    public bool AutomaticSearch { get; set; } = true;
    public bool DefaultCaseSensitive { get; set; } = false;
    public bool DefaultUseRegex { get; set; } = false;

    // Advanced
    public int ParallelSearchThreshold { get; set; } = 100_000;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

public class SettingsManager
{
    private SettingsManager()
    {
        LoadSettings();
    }

    private static readonly object Lock = new();
    private static SettingsManager? _instance;
    private AppSettings? _settings;

    private string SettingsDirectory
    {
        get
        {
            string baseDir = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            string appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "StringsApp";
            return Path.Combine(baseDir, appName);
        }
    }

    private string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings AppSettings
    {
        get
        {
            if (_instance?._settings == null) LoadSettings();
            return _settings!;
        }
        set => _settings = value;
    }

    public static SettingsManager Instance
    {
        get
        {
            lock (Lock)
            {
                return _instance ??= new SettingsManager();
            }
        }
    }

    public void LoadSettings()
    {
        try
        {
            using FileStream stream = new(SettingsPath, FileMode.Open);
            object? loadedSettings =
                JsonSerializer.Deserialize(stream, typeof(AppSettings), SourceGenerationContext.Default);
            if (loadedSettings is AppSettings s)
            {
                _settings = s;
                return;
            }
        }
        catch (FileNotFoundException)
        {
            Debug.WriteLine("Could not load settings from file, file does not exist");
        }
        catch (DirectoryNotFoundException)
        {
            Debug.WriteLine("Could not load settings from file, directory does not exist");
        }
        catch (JsonException)
        {
            Debug.WriteLine("Could not load settings from file, JSON is invalid");
        }
        catch
        {
            Debug.WriteLine("Could not load settings from file, unknown exception");
        }

        Debug.WriteLine("Using default settings");
        AppSettings = new AppSettings();
    }

    public void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDirectory);
        using FileStream stream = new(SettingsPath, FileMode.Create);
        JsonSerializer.Serialize(stream, AppSettings, SourceGenerationContext.Default.AppSettings);
    }

    // private static void SetString(string key, string value)
    // {
    //     Settings[key] = value;
    // }
    //
    // private static string GetString(string key, string? defaultValue = null)
    // {
    //     return Settings[key] ?? defaultValue ?? "";
    // }
    //
    // private static void SetInt(string key, int value)
    // {
    //     Settings[key] = value.ToString();
    // }
    //
    // private static int GetInt(string key, int? defaultValue = null)
    // {
    //     string? value = Settings[key];
    //     if (value == null) return defaultValue ?? 0;
    //     try
    //     {
    //         return int.Parse(value);
    //     }
    //     catch (FormatException)
    //     {
    //         Debug.WriteLine("Unable to parse int, key = {0}, value = {1}", key, value);
    //         return defaultValue ?? 0;
    //     }
    // }
    //
    // private static void SetBool(string key, bool value)
    // {
    //     Settings[key] = value.ToString();
    // }
    //
    // private static bool GetBool(string key, bool? defaultValue = null)
    // {
    //     string? value = Settings[key];
    //     if (value == null) return defaultValue ?? false;
    //     try
    //     {
    //         return bool.Parse(value);
    //     }
    //     catch (FormatException)
    //     {
    //         Debug.WriteLine("Unable to parse bool, key = {0}, value = {1}", key, value);
    //         return defaultValue ?? false;
    //     }
    // }
    //
    // public static string AppTheme
    // {
    //     get => GetString("AppTheme", "system");
    //     set => SetString("AppTheme", value);
    // }
    //
    // public static string Font
    // {
    //     get => GetString("Font", "_system_");
    //     set => SetString("Font", value);
    // }
    //
    // public static string AddressFormat
    // {
    //     get => GetString("AddressFormat", "hexadecimal");
    //     set => SetString("AddressFormat", value);
    // }
    //
    // public static bool GroupDuplicateStrings
    // {
    //     get => GetBool("GroupDuplicateStrings", false);
    //     set => SetBool("GroupDuplicateStrings", value);
    // }
    //
    // // Strings
    // public static string CharacterSet
    // {
    //     get => GetString("CharacterSet", "ascii");
    //     set => SetString("CharacterSet", value);
    // }
    //
    // public static string DefaultEncoding
    // {
    //     get => GetString("DefaultEncoding", "ascii");
    //     set => SetString("DefaultEncoding", value);
    // }
    //
    // // Search
    // public static bool AutomaticSearch
    // {
    //     get => GetBool("AutomaticSearch", true);
    //     set => SetBool("AutomaticSearch", value);
    // }
    //
    // public static bool DefaultCaseSensitive
    // {
    //     get => GetBool("DefaultCaseSensitive", false);
    //     set => SetBool("DefaultCaseSensitive", value);
    // }
    //
    // public static bool DefaultUseRegex
    // {
    //     get => GetBool("DefaultUseRegex", false);
    //     set => SetBool("DefaultUseRegex", value);
    // }
    //
    // // Advanced
    // public static int ParallelSearchThreshold
    // {
    //     get => GetInt("ParallelSearchThreshold", 100_000);
    //     set => SetInt("ParallelSearchThreshold", value);
    // }
}