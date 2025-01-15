using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StringsApp.Strings;
using StringsApp.ViewModels;

namespace StringsApp.Views;

public partial class StringsView : UserControl
{
    public StringsView()
    {
        InitializeComponent();

        DataContextChanged += DataContextChangedHandler;
    }

    private void DataContextChangedHandler(object? sender, EventArgs? e)
    {
        if (DataContext is not StringsViewModel vm) return;

        vm.SelectionChanged += index =>
        {
            if (Tree.RowsPresenter == null) return;
            Tree.RowsPresenter.BringIntoView(index);
        };

        vm.FocusSearchBoxEvent += () => SearchTextBox.Focus();

        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not StringsViewModel vm) return;
        IStorageItem? file = e.Data.GetFiles()?.FirstOrDefault();
        if (file is not IStorageFile) return;

        vm.OpenFile(file.Path.LocalPath);
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

    private async void Export_Clicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StringsViewModel vm) return;
        if (sender is not Control control) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        bool exportAll = control.Tag is "all";
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save as",
            FileTypeChoices = [FilePickerFileTypes.TextPlain, FilePickerFileTypes.All]
        });

        if (file == null) return;

        await vm.Export(file.Path.LocalPath, exportAll);
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

    private void Settings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        new SettingsWindow().ShowDialog(window);
    }

    private void Tree_OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not TreeDataGrid tree) return;
        if (tree.RowsPresenter != null) tree.RowsPresenter.DoubleTapped += TreeRowsPresenter_OnDoubleTapped;
    }

    /// <summary>
    /// Handler for opening the string inspection dialog
    /// </summary>
    private void TreeRowsPresenter_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        if (Tree.RowSelection?.SelectedItem is not StringResult selectedItem) return;
        new InspectStringDialog
        {
            DataContext = new InspectStringViewModel(selectedItem)
        }.ShowDialog(window);
    }
}