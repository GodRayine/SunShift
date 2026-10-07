// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Globalization;
using System.Windows;
using SunShift.Core;

namespace SunShift.App;

public partial class LocationWindow : Window
{
    public bool IsManual { get; private set; }
    public bool Disabled { get; private set; }
    public Coordinates? Point { get; private set; }
    internal LocationWindow(Settings settings)
    {
        InitializeComponent();
        ManualMode.IsChecked = settings.ManualLocation;
        WindowsMode.IsChecked = !settings.ManualLocation && !settings.LocationDisabled;
        DisabledMode.IsChecked = settings.LocationDisabled;
        var point = settings.ManualPoint ?? settings.LastLocation?.Point;
        Latitude.Text = point?.Latitude.ToString("0.######", CultureInfo.InvariantCulture) ?? "";
        Longitude.Text = point?.Longitude.ToString("0.######", CultureInfo.InvariantCulture) ?? "";
    }
    private static bool Parse(string text, out double value) => double.TryParse(text.Trim().Replace(',', '.'),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Disabled = DisabledMode.IsChecked == true;
        IsManual = !Disabled && ManualMode.IsChecked == true;
        if (IsManual)
        {
            if (!Parse(Latitude.Text, out var lat) || !Parse(Longitude.Text, out var lon) || !(Point = new Coordinates(lat, lon)).IsValid)
            { Error.Text = "Широта: от −90 до 90. Долгота: от −180 до 180."; return; }
        }
        DialogResult = true;
    }
    private void Privacy_Click(object sender, RoutedEventArgs e) => Startup.OpenPrivacy();
    private void Policy_Click(object sender, RoutedEventArgs e) => new PrivacyWindow { Owner = this }.Show();
}
