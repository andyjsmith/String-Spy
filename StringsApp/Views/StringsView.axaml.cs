using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StringsApp.ViewModels;

namespace StringsApp;

public partial class StringsView : UserControl
{

    public StringsView()
    {
        InitializeComponent();
    }

    private async void Open_Clicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StringsViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a file",
            AllowMultiple = false,
        });

        if (files.Count == 0) return;

        vm.OpenFile(files[0].Path.LocalPath);
    }
}