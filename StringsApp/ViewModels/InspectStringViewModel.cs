using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StringsApp.Settings;
using StringsApp.Strings;

namespace StringsApp.ViewModels;

public partial class InspectStringViewModel : ViewModelBase
{
    [ObservableProperty] private StringResult _stringResult;
    [ObservableProperty] private FontFamily _font = SettingsManager.Instance.AppSettings.Font;

    public InspectStringViewModel(StringResult stringResult)
    {
        StringResult = stringResult;
    }
}