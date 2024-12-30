using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using StringsApp.ViewModels;

namespace StringsApp;

public partial class GoToDialog : UserControl
{
    public GoToDialog()
    {
        InitializeComponent();
        
        // Autofocus the text box on load
        Loaded += (sender, args) => OffsetTextBox.Focus();
    }
}