using Avalonia.Controls;
using StringSpy.ViewModels;

namespace StringSpy.Views;

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