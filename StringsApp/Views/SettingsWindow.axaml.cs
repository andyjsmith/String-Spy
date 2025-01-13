using Avalonia.Controls;
using StringsApp.ViewModels;

namespace StringsApp.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var vm = new SettingsViewModel();
        DataContext = vm;

        vm.OnRequestClose += (_, _) => Close();
    }
}