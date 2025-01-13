﻿using Avalonia.Controls;

namespace StringsApp.Views;

public partial class GoToDialog : UserControl
{
    public GoToDialog()
    {
        InitializeComponent();

        // Autofocus the text box on load
        Loaded += (_, _) => OffsetTextBox.Focus();
    }
}