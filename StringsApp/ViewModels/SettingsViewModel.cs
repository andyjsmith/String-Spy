using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.Styling;
using System.Collections.ObjectModel;
using System.Linq;

namespace StringsApp.ViewModels;
public partial class SettingsViewModel : ViewModelBase {
    private const string _system = "System";
    private const string _dark = "Dark";
    private const string _light = "Light";
    private string _currentAppTheme = _system;

    private readonly FluentAvaloniaTheme _faTheme;
    public string[] AppThemes { get; } =
        new[] { _system, _light, _dark /*, FluentAvaloniaTheme.HighContrastTheme*/ };

    public string CurrentAppTheme {
        get => _currentAppTheme;
        set {
            if (RaiseAndSetIfChanged(ref _currentAppTheme, value)) {
                var newTheme = GetThemeVariant(value);
                if (newTheme != null) {
                    Application.Current.RequestedThemeVariant = newTheme;
                }
                if (value != _system) {
                    _faTheme.PreferSystemTheme = false;
                } else {
                    _faTheme.PreferSystemTheme = true;
                }
            }
        }
    }

    private ThemeVariant GetThemeVariant(string value) {
        switch (value) {
            case _light:
                return ThemeVariant.Light;
            case _dark:
                return ThemeVariant.Dark;
            case _system:
            default:
                return null;
        }
    }

    [ObservableProperty]
    private ObservableCollection<FontFamily> fonts = new();
    [ObservableProperty]
    private FontFamily currentFont;
    
    [ObservableProperty]
    private ObservableCollection<string> addressFormats = ["Binary", "Octal", "Decimal", "Hexadecimal"];
    
    [ObservableProperty]
    private string addressFormat;

    [ObservableProperty] private bool groupStrings;

    public SettingsViewModel() {
        _faTheme = App.Current.Styles[App.Current.Styles.Count - 1] as FluentAvaloniaTheme;
        
        foreach (var font in FontManager.Current.SystemFonts.OrderBy(f => f.Name)) {
            fonts.Add(font);
        }
        currentFont = fonts.First();
        
        addressFormat = "Hexadecimal";
    }
}
