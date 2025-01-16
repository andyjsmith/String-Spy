using System;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace StringSpy.ViewModels;

public class ViewModelBase : ObservableObject
{
    protected static void ShowErrorDialog(Exception e)
    {
        Debug.WriteLine(e);

        Dispatcher.UIThread.Invoke(() =>
        {
            var dialog = new ContentDialog
            {
                Content = e.InnerException != null ? e.InnerException.Message : e.Message,
                Title = "Error",
                PrimaryButtonText = "OK",
            };
            return dialog.ShowAsync();
        });
    }
}