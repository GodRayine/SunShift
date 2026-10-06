// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SunShift.Core;

namespace SunShift.App;

public partial class RotationWindow : Window
{
    internal RotationSettings Result { get; private set; }
    internal RotationWindow(RotationSettings settings, SunPhase phase)
    {
        InitializeComponent(); Result = settings;
        Heading.Text = phase == SunPhase.Day ? "Дневной пул обоев" : "Ночной пул обоев";
        DesktopFolder.Text = settings.DesktopFolder ?? ""; LockFolder.Text = settings.LockScreenFolder ?? "";
        Interval.ItemsSource = IntervalOptions.Labels;
        Interval.Text = IntervalOptions.Format(settings.IntervalMinutes);
        Order.ItemsSource = new[] { "По имени файла", "Случайный" }; Order.SelectedIndex = (int)settings.Order;
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var field = (string)((Button)sender).Tag == "Desktop" ? DesktopFolder : LockFolder;
        var dialog = new OpenFolderDialog { Title = "Выберите папку с обоями", Multiselect = false };
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName;
    }
    private void Clear_Click(object sender, RoutedEventArgs e) =>
        ((string)((Button)sender).Tag == "Desktop" ? DesktopFolder : LockFolder).Clear();
    internal Task<bool> ValidateAsync() => ValidateCoreAsync();
    private async Task<bool> ValidateCoreAsync()
    {
        DesktopValidation.Text = LockValidation.Text = IntervalValidation.Text = "";
        if (!IntervalOptions.TryParse(Interval.Text, out var minutes))
        { IntervalValidation.Text = "Выберите интервал или введите 1–1440 минут."; Interval.Focus(); return false; }
        var desktop = string.IsNullOrWhiteSpace(DesktopFolder.Text) ? null : DesktopFolder.Text.Trim();
        var locked = string.IsNullOrWhiteSpace(LockFolder.Text) ? null : LockFolder.Text.Trim();
        Fields.IsEnabled = SaveButton.IsEnabled = false; Feedback.Text = "Проверяю папки…";
        try
        {
            var catalogs = await Task.Run(() =>
            {
                var catalog = new FileWallpaperCatalog();
                return (Desktop: desktop == null ? null : catalog.Read(desktop), Lock: locked == null ? null : catalog.Read(locked));
            });
            DesktopValidation.Text = catalogs.Desktop?.Error ?? ""; LockValidation.Text = catalogs.Lock?.Error ?? "";
            if (catalogs.Desktop?.Error != null || catalogs.Lock?.Error != null)
            { (catalogs.Desktop?.Error != null ? DesktopFolder : LockFolder).Focus(); return false; }
            Result = new(desktop, locked, minutes, (RotationOrder)Math.Max(0, Order.SelectedIndex));
            return true;
        }
        finally
        {
            Fields.IsEnabled = SaveButton.IsEnabled = true; Feedback.Text = "";
            if (DesktopValidation.Text.Length > 0) DesktopFolder.Focus();
            else if (LockValidation.Text.Length > 0) LockFolder.Focus();
        }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try { if (await ValidateAsync() && IsVisible) DialogResult = true; }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
}
