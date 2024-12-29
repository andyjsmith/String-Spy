using System;
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

    private void CopyRow_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (sender is not MenuItem menuItem) return;
        if (DataContext is not StringsViewModel vm) return;
        if (vm.StringsSource.RowSelection?.SelectedItem is not { } selectedItem) return;
        if (menuItem.Tag is not string tag) throw new NotSupportedException("Tag is not a supported type");
        switch (tag)
        {
            case "Start":
                clipboard.SetTextAsync(vm.SelectedOffsetFormatter.Format(selectedItem.Position));
                break;
            case "End":
                clipboard.SetTextAsync(vm.SelectedOffsetFormatter.Format(selectedItem.EndPosition));
                break;
            case "String":
                clipboard.SetTextAsync(selectedItem.Content);
                break;
            default:
                throw new NotSupportedException("Tag is not a supported option");
        }
    }
}