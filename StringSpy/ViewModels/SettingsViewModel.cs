using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using StringSpy.Models;
using StringSpy.Settings;

namespace StringSpy.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// Closes the settings window
    /// </summary>
    public event EventHandler? OnRequestClose;

    public static string CurrentVersion =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

    public static Dictionary<string, ThemeVariant> AppThemes => SettingsManager.AppThemes;
    [ObservableProperty] private string _selectedAppTheme = null!;

    private void UpdateGui()
    {
        Application.Current!.RequestedThemeVariant =
            SettingsManager.AppThemes.GetValueOrDefault(SelectedAppTheme, ThemeVariant.Default);
    }

    public List<string> Fonts { get; } = ["<default>"];
    [ObservableProperty] private string _selectedFont = null!;

    public List<string> AddressFormats { get; } = ["Hexadecimal", "Decimal", "Octal"];

    [ObservableProperty] private string _defaultAddressFormat = null!;

    [ObservableProperty] private bool _groupStrings;

    public List<Encoding> AllEncodings { get; }

    [ObservableProperty] private Encoding _selectedDefaultEncoding = Encoding.ASCII;

    public CharSet[] AllCharSets { get; } =
        [CharSet.Ascii, CharSet.Latin1, CharSet.CurrentEncoding];

    [ObservableProperty] private CharSet _selectedCharSet;
    [ObservableProperty] private int _defaultMinimumStringLength;

    [ObservableProperty] private bool _automaticSearch;
    [ObservableProperty] private bool _defaultCaseSensitive;
    [ObservableProperty] private bool _defaultRegex;
    [ObservableProperty] private int _parallelSearchThreshold;

    public SettingsViewModel()
    {
        foreach (FontFamily font in FontManager.Current.SystemFonts.OrderBy(f => f.Name))
        {
            Fonts.Add(font.Name);
        }

        AllEncodings = Encoding.GetEncodings()
            .Select(e => e.GetEncoding())
            .OrderBy(e => e.EncodingName)
            .ToList();

        LoadSettings();
    }

    private void LoadSettings()
    {
        SettingsManager.Instance.LoadSettings();
        AppSettings appSettings = SettingsManager.Instance.Settings;

        SelectedAppTheme = appSettings.AppTheme;
        SelectedFont = appSettings.Font;
        DefaultAddressFormat = appSettings.DefaultAddressFormat;
        GroupStrings = appSettings.GroupDuplicateStrings;

        DefaultMinimumStringLength = appSettings.DefaultMinimumStringLength;
        SelectedCharSet = Character.StringToCharSet(appSettings.CharacterSet);
        SelectedDefaultEncoding = Character.StringToEncoding(appSettings.DefaultEncoding);

        AutomaticSearch = appSettings.AutomaticSearch;
        DefaultCaseSensitive = appSettings.DefaultCaseSensitive;
        DefaultRegex = appSettings.DefaultUseRegex;

        ParallelSearchThreshold = appSettings.ParallelSearchThreshold;

        UpdateGui();
    }

    [RelayCommand]
    public void DiscardSettings()
    {
        LoadSettings();
        OnRequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void SaveSettings()
    {
        AppSettings appSettings = SettingsManager.Instance.Settings;

        appSettings.AppTheme = SelectedAppTheme;
        appSettings.Font = SelectedFont;
        appSettings.DefaultAddressFormat = DefaultAddressFormat;
        appSettings.GroupDuplicateStrings = GroupStrings;

        appSettings.DefaultMinimumStringLength = DefaultMinimumStringLength;
        appSettings.CharacterSet = SelectedCharSet.ToString();
        appSettings.DefaultEncoding = SelectedDefaultEncoding.WebName;

        appSettings.AutomaticSearch = AutomaticSearch;
        appSettings.DefaultCaseSensitive = DefaultCaseSensitive;
        appSettings.DefaultUseRegex = DefaultRegex;

        appSettings.ParallelSearchThreshold = ParallelSearchThreshold;

        SettingsManager.Instance.SaveSettings();

        UpdateGui();

        OnRequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void RestoreDefaultSettings()
    {
        SettingsManager.Instance.Settings = new AppSettings();
        SettingsManager.Instance.SaveSettings();
        LoadSettings();
    }
}