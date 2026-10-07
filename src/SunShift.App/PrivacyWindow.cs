// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SunShift.App;

internal sealed class PrivacyWindow : Window
{
    internal PrivacyWindow()
    {
        Title = "SunShift — конфиденциальность";
        Width = 660; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/PRIVACY.md")).Stream;
        using var reader = new StreamReader(stream);
        Content = new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(24), Padding = new Thickness(12),
            FontSize = 14 };
    }
}
