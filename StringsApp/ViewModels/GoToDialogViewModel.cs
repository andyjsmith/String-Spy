using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using StringsApp.Models;
using StringsApp.Views;

namespace StringsApp.ViewModels;

public partial class GoToDialogViewModel : ViewModelBase
{
    [ObservableProperty] private OffsetFormat _format = OffsetFormat.Hexadecimal;
    partial void OnFormatChanged(OffsetFormat value) => Validate();

    public OffsetFormat[] Formats { get; } =
    [
        OffsetFormat.Hexadecimal,
        OffsetFormat.Decimal,
        OffsetFormat.Octal,
        OffsetFormat.Binary
    ];

    [ObservableProperty] private string _value = string.Empty;
    partial void OnValueChanged(string value) => Validate();

    [ObservableProperty] private bool _isValid = true;


    private void Validate()
    {
        IsValid = true;
        try
        {
            long? num = Format switch
            {
                OffsetFormat.Hexadecimal => Convert.ToInt64(Value, 16),
                OffsetFormat.Decimal => Convert.ToInt64(Value, 10),
                OffsetFormat.Octal => Convert.ToInt64(Value, 8),
                OffsetFormat.Binary => Convert.ToInt64(Value, 2),
                _ => null
            };
            if (num == null) IsValid = false;
        }
        catch (ArgumentOutOfRangeException)
        {
            IsValid = false;
        }
        catch (FormatException)
        {
            IsValid = false;
        }
        catch (ArgumentException)
        {
            IsValid = false;
        }
    }

    public async Task<long?> ShowAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Go to address",
            PrimaryButtonText = "Go",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            Content = new GoToDialog { DataContext = this }
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                return Format switch
                {
                    OffsetFormat.Hexadecimal => Convert.ToInt64(Value, 16),
                    OffsetFormat.Decimal => Convert.ToInt64(Value, 10),
                    OffsetFormat.Octal => Convert.ToInt64(Value, 8),
                    OffsetFormat.Binary => Convert.ToInt64(Value, 2),
                    _ => null
                };
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            catch (FormatException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}