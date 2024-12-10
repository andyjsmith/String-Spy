using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.Styling;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace StringsApp.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private string _currentAppTheme = "System";

    private readonly FluentAvaloniaTheme _faTheme;

    public Dictionary<string, ThemeVariant> AppThemes { get; } =
        new()
        {
            { "System", ThemeVariant.Default },
            { "Light", ThemeVariant.Light },
            { "Dark", ThemeVariant.Dark },
            { "High Contrast", FluentAvaloniaTheme.HighContrastTheme }
        };

    public string CurrentAppTheme
    {
        get => _currentAppTheme;
        set
        {
            _currentAppTheme = value;
            var variant = AppThemes.GetValueOrDefault(_currentAppTheme, ThemeVariant.Default);
            Application.Current.RequestedThemeVariant = variant;
            _faTheme.PreferSystemTheme = variant == ThemeVariant.Default;
        }
    }

    [ObservableProperty] private ObservableCollection<FontFamily> _fonts = new();
    [ObservableProperty] private FontFamily _currentFont;

    [ObservableProperty]
    private ObservableCollection<string> _addressFormats = ["Binary", "Octal", "Decimal", "Hexadecimal"];

    [ObservableProperty] private string _addressFormat;

    [ObservableProperty] private bool _groupStrings;

    public SettingsViewModel()
    {
        _faTheme = Application.Current.Styles[Application.Current.Styles.Count - 1] as FluentAvaloniaTheme;

        foreach (var font in FontManager.Current.SystemFonts.OrderBy(f => f.Name))
        {
            _fonts.Add(font);
        }

        _currentFont = _fonts.First();

        _addressFormat = "Hexadecimal";
    }
}