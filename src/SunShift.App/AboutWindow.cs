// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SunShift.App;

internal sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "О SunShift";
        Width = 540; Height = 370; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.2.0";
        panel.Children.Add(new TextBlock { Text = "SunShift " + version, FontSize = 26, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Copyright (C) 2026 GodRayine\nGNU GPL, версия 3 или любая более поздняя.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "Свободная программа: вы можете изменять и распространять её на условиях GNU GPL. Поставляется без каких-либо гарантий, включая гарантии пригодности для продажи или конкретной цели.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 20)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var license = new Button { Content = "Текст лицензии", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 12, 0) };
        license.Click += (_, _) => ShowLicense();
        var source = new Button { Content = "Исходный код", Padding = new Thickness(12, 8, 12, 8) };
        source.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/GodRayine/SunShift") { UseShellExecute = true });
        buttons.Children.Add(license); buttons.Children.Add(source); panel.Children.Add(buttons);
        Content = panel;
    }

    private void ShowLicense()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/LICENSE")).Stream;
        using var reader = new StreamReader(stream);
        var text = new TextBox
        {
            Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18)
        };
        new Window { Title = "GNU General Public License v3", Width = 780, Height = 640, Owner = this, Content = text }.Show();
    }
}
