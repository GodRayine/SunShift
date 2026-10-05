// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SunShift.App;

internal sealed class SystemTheme : IDisposable
{
    private readonly bool? previewDark;
    public SystemTheme(bool? previewDark = null) { this.previewDark = previewDark; Refresh(); SystemEvents.UserPreferenceChanged += Changed; }
    private void Changed(object sender, UserPreferenceChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(Refresh);
    private void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = previewDark ?? (key?.GetValue("AppsUseLightTheme") is int value && value == 0);
        var contrast = SystemParameters.HighContrast;
        Set("Page", contrast ? SystemColors.WindowColor : Color(dark ? "#191919" : "#F5F5F5"));
        Set("Surface", contrast ? SystemColors.WindowColor : Color(dark ? "#242424" : "#FFFFFF"));
        Set("Inset", contrast ? SystemColors.WindowColor : Color(dark ? "#1B1B1B" : "#EFEFEF"));
        Set("Outline", contrast ? SystemColors.WindowTextColor : Color(dark ? "#3A3A3A" : "#E0E0E0"));
        Set("Text", contrast ? SystemColors.WindowTextColor : Color(dark ? "#F6F6F6" : "#222222"));
        Set("SecondaryText", contrast ? SystemColors.WindowTextColor : Color(dark ? "#BDBDBD" : "#646464"));
        var accent = SystemColors.AccentColor;
        var lightness = .2126 * Linear(accent.R) + .7152 * Linear(accent.G) + .0722 * Linear(accent.B);
        Set("AccentText", lightness > .179 ? Colors.Black : Colors.White);
    }
    private static double Linear(byte b) => b / 255.0 <= .04045 ? b / 255.0 / 12.92 : Math.Pow((b / 255.0 + .055) / 1.055, 2.4);
    private static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static void Set(string name, Color color) => Application.Current.Resources[name] = new SolidColorBrush(color);
    public void Dispose() => SystemEvents.UserPreferenceChanged -= Changed;
}
