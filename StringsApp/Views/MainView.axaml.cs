using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using StringsApp.ViewModels;
using System;

namespace StringsApp.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    public void NavigationViewSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem navItem && navItem.Tag is not null && DataContext != null)
        {
            switch (navItem.Tag.ToString())
            {
                case "Settings":
                    PageFrame.Navigate(typeof(Settings));
                    PageFrame.DataContext = ((MainViewModel)DataContext).SettingsDataContext;
                    break;
                case "HomePage":
                    PageFrame.Navigate(typeof(StringsView));
                    PageFrame.DataContext = ((MainViewModel)DataContext).StringsDataContext;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}