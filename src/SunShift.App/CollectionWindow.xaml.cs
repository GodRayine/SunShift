// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SunShift.Core;

namespace SunShift.App;

internal sealed class PairDraft(WallpaperPair pair) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; } = pair.Id;
    private string name = pair.Name;
    public string Name { get => name; set { name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    private ImagePair day = pair.Day, night = pair.Night;
    public ImagePair Day { get => day; set { day = value; PropertyChanged?.Invoke(this, new(nameof(Day))); } }
    public ImagePair Night { get => night; set { night = value; PropertyChanged?.Invoke(this, new(nameof(Night))); } }
    public WallpaperPair ToPair() => new(Id, Name.Trim(), Day, Night);
}

public partial class CollectionWindow : Window
{
    private readonly ObservableCollection<PairDraft> pairs = new();
    private readonly string staging = Path.Combine(Path.GetTempPath(), "SunShift-pair-editor-" + Guid.NewGuid().ToString("N"));
    internal CollectionSettings Result { get; private set; }
    internal RotationSettings DayFolders { get; private set; }
    internal RotationSettings NightFolders { get; private set; }
    internal string? ActivePairId { get; private set; }
    internal CollectionWindow(Settings settings)
    {
        InitializeComponent(); Height = Math.Min(Height, SystemParameters.WorkArea.Height - 60);
        Result = settings.Collection; DayFolders = settings.DayRotation; NightFolders = settings.NightRotation;
        Rotate.IsChecked = settings.Collection.Rotate;
        Mode.ItemsSource = new[] { "Папки для дня и ночи", "Связанные пары день + ночь" }; Mode.SelectedIndex = (int)settings.Collection.Mode;
        PairInterval.ItemsSource = IntervalOptions.Labels; PairInterval.Text = IntervalOptions.Format(settings.Collection.IntervalMinutes);
        PairOrder.ItemsSource = new[] { "По порядку списка", "Случайный" }; PairOrder.SelectedIndex = (int)settings.Collection.Order;
        foreach (var pair in settings.Collection.Pairs) pairs.Add(new(pair));
        PairList.ItemsSource = pairs; PairList.SelectedItem = pairs.FirstOrDefault(pair => pair.Id == settings.ActivePairId) ?? pairs.FirstOrDefault();
        RefreshFolders();
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FoldersPanel == null) return;
        FoldersPanel.Visibility = Mode.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        PairsPanel.Visibility = Mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PairEditor == null) return;
        var scale = ((FrameworkElement)Content).LayoutTransform is System.Windows.Media.ScaleTransform transform ? transform.ScaleX : 1;
        var narrow = ActualWidth / scale < 760;
        Grid.SetColumnSpan(PairListPanel, narrow ? 3 : 1);
        Grid.SetColumn(PairEditor, narrow ? 0 : 2);
        Grid.SetRow(PairEditor, narrow ? 1 : 0);
        Grid.SetColumnSpan(PairEditor, narrow ? 3 : 1);
        PairEditor.Margin = narrow ? new Thickness(0, 20, 0, 0) : new Thickness(0);
        PairList.Height = narrow ? 120 : 250;
        ListColumn.Width = new GridLength(narrow ? 0 : 180);
        ListGap.Width = new GridLength(narrow ? 0 : 20);
    }
    private void RefreshFolders()
    {
        static string Describe(RotationSettings options) => "Рабочий стол: " + (options.DesktopFolder ?? "одиночное изображение") +
            "\nБлокировка: " + (options.LockScreenFolder ?? "одиночное изображение") +
            $"\nИнтервал: {IntervalOptions.Format(options.IntervalMinutes)} · {(options.Order == RotationOrder.Random ? "случайный" : "по имени файла")}";
        DayFoldersSummary.Text = Describe(DayFolders); NightFoldersSummary.Text = Describe(NightFolders);
    }
    private void Folders_Click(object sender, RoutedEventArgs e)
    {
        var day = (string)((Button)sender).Tag == "Day";
        var dialog = new RotationWindow(day ? DayFolders : NightFolders, day ? SunPhase.Day : SunPhase.Night) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (day) DayFolders = dialog.Result; else NightFolders = dialog.Result;
        RefreshFolders();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (pairs.Count >= 500) { Feedback.Text = "Максимум — 500 пар."; return; }
        var pair = new PairDraft(new(Guid.NewGuid().ToString("N"), $"Пара {pairs.Count + 1}", new(), new()));
        pairs.Add(pair); PairList.SelectedItem = pair; PairName.Focus();
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PairList.SelectedItem is PairDraft pair) pairs.Remove(pair);
        PairList.SelectedItem = pairs.FirstOrDefault();
    }
    private void Image_Click(object sender, RoutedEventArgs e)
    {
        if (PairList.SelectedItem is not PairDraft pair) { Feedback.Text = "Сначала добавьте или выберите пару."; return; }
        var target = (string)((Button)sender).Tag;
        var dialog = new OpenFileDialog { Title = "Выберите изображение пары", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = Images.Import(dialog.FileName, staging);
            switch (target)
            {
                case "DayDesktop": pair.Day = pair.Day with { Desktop = path }; break;
                case "DayLock": pair.Day = pair.Day with { LockScreen = path }; break;
                case "NightDesktop": pair.Night = pair.Night with { Desktop = path }; break;
                case "NightLock": pair.Night = pair.Night with { LockScreen = path }; break;
            }
            Feedback.Text = "Изображение выбрано. Изменения вступят в силу после сохранения.";
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
    private void ClearLock_Click(object sender, RoutedEventArgs e)
    {
        if (PairList.SelectedItem is not PairDraft pair) return;
        if ((string)((Button)sender).Tag == "DayLock") pair.Day = pair.Day with { LockScreen = null };
        else pair.Night = pair.Night with { LockScreen = null };
    }
    internal bool Validate()
    {
        Feedback.Text = "";
        var minutes = Result.IntervalMinutes;
        if (Mode.SelectedIndex == 1)
        {
            if (!IntervalOptions.TryParse(PairInterval.Text, out minutes))
            { Feedback.Text = "Выберите интервал или введите 1–1440 минут."; PairInterval.Focus(); return false; }
            if (pairs.Count == 0) { Feedback.Text = "Добавьте хотя бы одну пару."; return false; }
            foreach (var pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair.Name) || !File.Exists(pair.Day.Desktop) || !File.Exists(pair.Night.Desktop) ||
                    (pair.Day.LockScreen != null && !File.Exists(pair.Day.LockScreen)) || (pair.Night.LockScreen != null && !File.Exists(pair.Night.LockScreen)))
                {
                    PairList.SelectedItem = pair; PairName.Focus();
                    Feedback.Text = "Для каждой пары нужны название и доступные дневной/ночной фоны рабочего стола. Блокировка — по желанию.";
                    return false;
                }
            }
        }
        Result = Result with { Mode = (CollectionMode)Math.Max(0, Mode.SelectedIndex), Rotate = Rotate.IsChecked == true,
            IntervalMinutes = minutes, Order = (RotationOrder)Math.Max(0, PairOrder.SelectedIndex), Pairs = pairs.Select(pair => pair.ToPair()).ToArray() };
        ActivePairId = (PairList.SelectedItem as PairDraft)?.Id;
        return true;
    }
    private void Save_Click(object sender, RoutedEventArgs e) { if (Validate()) DialogResult = true; }
}
