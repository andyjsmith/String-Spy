using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace StringSpy.Settings;

public class AppSettings
{
    // The values here are the defaults
    public string AppTheme { get; set; } = "System";
    public string Font { get; set; } = "<default>";
    public string FontValue => Font == "<default>" ? "Consolas, San Francisco, Monaco, Courier New" : Font;
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

            string appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "StringSpy";
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
                UpdateTheme(s.AppTheme);
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
        _settings = new AppSettings();
        UpdateTheme(_settings.AppTheme);
    }

    public static Dictionary<string, ThemeVariant> AppThemes { get; } =
        new()
        {
            { "System", ThemeVariant.Default },
            { "Light", ThemeVariant.Light },
            { "Dark", ThemeVariant.Dark },
            { "High Contrast", FluentAvaloniaTheme.HighContrastTheme }
        };

    private void UpdateTheme(string theme)
    {
        Application.Current!.RequestedThemeVariant = AppThemes.GetValueOrDefault(theme, ThemeVariant.Default);
    }

    public void SaveSettings()
    {
        Directory.CreateDirectory(SettingsDirectory);
        using FileStream stream = new(SettingsPath, FileMode.Create);
        JsonSerializer.Serialize(stream, AppSettings, SourceGenerationContext.Default.AppSettings);
    }
}