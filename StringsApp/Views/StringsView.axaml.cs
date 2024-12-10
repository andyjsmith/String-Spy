using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
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

        TaskDialog td = new()
        {
            Header = "Loading strings",
            ShowProgressBar = true,
            Buttons = { TaskDialogButton.CancelButton },
            XamlRoot = VisualRoot as Visual,
            Content = files[0].Path,
        };

        Debug.WriteLine($"Loading file: {files[0].Name}");
        CancellationTokenSource cts = new();

        td.Opened += async (s, _) =>
        {
            td.SetProgressBarState(0, TaskDialogProgressState.Normal);
            await using Stream file = await files[0].OpenReadAsync();
            Stopwatch sw = Stopwatch.StartNew();
            await Task.Run(() =>
            {
                vm.RunParallel(files[0].Path.LocalPath, Environment.ProcessorCount, cts.Token,
                    (double progress) => { td.SetProgressBarState(progress * 100.0, TaskDialogProgressState.Normal); });
            }, cts.Token);
            sw.Stop();
            Debug.WriteLine($"Finished loading file in {sw.Elapsed}");
            td.SetProgressBarState(100, TaskDialogProgressState.Normal);
            td.Hide();
        };

        td.Closing += async (s, e) =>
        {
            if (e.Result is TaskDialogStandardResult.Cancel)
            {
                await cts.CancelAsync();
            }
        };

        await td.ShowAsync(true);
    }
}