// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SunShift.Core;

namespace SunShift.App;

public partial class MainWindow : Window
{
    private readonly ViewState state = new();
    private readonly SettingsStore store;
    private readonly string folder;
    private readonly bool preview;
    private readonly DateTimeOffset? previewClock;
    private readonly WindowsLocation location = new();
    private readonly WallpaperCoordinator wallpaper;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource operation = new();
    private bool checking;
    private long enableVersion;
    private DateTimeOffset lastGeoAttempt = DateTimeOffset.MinValue;
    internal bool Exiting { get; set; }
    internal Task InitialCheck { get; private set; } = Task.CompletedTask;
    internal bool Automatic => state.Settings.Automatic;
    private DateTimeOffset Now => previewClock ?? DateTimeOffset.UtcNow;

    internal MainWindow(SettingsStore store, Settings settings, string folder, bool preview, DateTimeOffset? previewClock)
    {
        InitializeComponent();
        this.store = store; this.folder = folder; this.preview = preview; this.previewClock = previewClock;
        state.Settings = settings;
        wallpaper = new(new WindowsWallpaper(folder));
        DataContext = state;
        state.StartupEnabled = !preview && Startup.Enabled;
        if (store.Warning != null) state.Status = store.Warning;
        timer.Tick += async (_, _) => await CheckAsync(false, false);
        Loaded += (_, _) => { timer.Start(); InitialCheck = CheckAsync(false, false); };
        Closing += OnClosing;
        if (!preview) { SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.TimeChanged += TimeChanged; }
    }

    private Settings Copy() => new()
    {
        Automatic = state.Settings.Automatic, Day = state.Settings.Day, Night = state.Settings.Night,
        ManualLocation = state.Settings.ManualLocation, ManualPoint = state.Settings.ManualPoint, LastLocation = state.Settings.LastLocation
    };
    private void Save(Settings settings) { if (!preview) store.Write(settings); state.Settings = settings; state.NotifyAll(); }
    private LocationFix? CurrentFix(DateTimeOffset now) => state.Settings.ManualLocation && state.Settings.ManualPoint is { IsValid: true } point
        ? new(point, 0, now) : state.Settings.LastLocation is { } fix && fix.IsUsable(now) ? fix : null;

    private async Task CheckAsync(bool prompt, bool forceLocation)
    {
        if (checking) return;
        checking = true; state.Busy = true;
        var token = operation.Token;
        try
        {
            var now = Now;
            var fix = CurrentFix(now);
            string? locationError = null;
            var retryInterval = fix == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
            if (!preview && !state.Settings.ManualLocation &&
                (forceLocation || (Automatic && now - lastGeoAttempt >= retryInterval)))
            {
                lastGeoAttempt = now;
                state.Status = "Определяю местоположение…";
                try
                {
                    if (prompt) { ShowMain(); await location.RequestPermissionAsync(); }
                    token.ThrowIfCancellationRequested();
                    fix = await location.ReadAsync(token);
                    token.ThrowIfCancellationRequested();
                    var updated = Copy(); updated.LastLocation = fix; Save(updated);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { locationError = ex.Message; }
            }
            token.ThrowIfCancellationRequested();
            if (fix == null || !fix.IsUsable(Now))
            {
                state.ClearSolar();
                state.Status = locationError ?? "Определите местоположение через Windows или укажите координаты вручную.";
                return;
            }
            now = Now;
            var snapshot = await Task.Run(() => SolarCalculator.Calculate(fix.Point, now), token);
            token.ThrowIfCancellationRequested();
            state.SetSolar(snapshot, fix, state.Settings.ManualLocation, now);
            var phase = snapshot.Phase == SunPhase.Day ? "День" : "Ночь";
            string message;
            if (preview) message = "Предпросмотр · геолокация, автозапуск и смена обоев отключены.";
            else if (!Automatic) message = $"{phase} · автоматика на паузе. Расчёт обновляется каждые 30 секунд.";
            else
            {
                var images = state.Settings.For(snapshot.Phase);
                if (images.IsEmpty) message = $"{phase} · изображения не заданы, текущие фоны сохранены.";
                else
                {
                    token.ThrowIfCancellationRequested();
                    var result = await wallpaper.ApplyAsync(images, token);
                    message = result.Error ?? $"{phase} · фоны актуальны. При смене состояния они переключатся автоматически.";
                }
            }
            if (locationError != null) message += " Геолокация недоступна; расчёт по последней известной позиции. " + locationError;
            if (store.Warning != null) message += " " + store.Warning;
            state.Status = message;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { state.Status = "Не удалось выполнить расчёт. " + ex.Message; }
        finally { checking = false; state.Busy = false; }
    }

    internal async Task SetAutomaticAsync(bool enabled)
    {
        var version = ++enableVersion;
        if (preview) { AutoToggle.IsChecked = false; state.Status = "Автоматика отключена в предпросмотре."; return; }
        if (!enabled)
        {
            operation.Cancel(); operation.Dispose(); operation = new();
            var paused = Copy(); paused.Automatic = false;
            // Pause in memory immediately even if the settings folder is temporarily unavailable.
            state.Settings = paused; state.NotifyAll();
            store.Write(paused);
            state.Status = "На паузе. Текущие фоны сохранены.";
            return;
        }
        if (state.Settings.Day.IsEmpty && state.Settings.Night.IsEmpty) throw new InvalidOperationException("Сначала выберите дневное или ночное изображение.");
        if (!state.Settings.ManualLocation)
        {
            ShowMain(); state.Busy = true;
            var token = operation.Token;
            try
            {
                await location.RequestPermissionAsync();
                token.ThrowIfCancellationRequested();
                if (version != enableVersion || Exiting) return;
                var fix = await location.ReadAsync(token);
                if (version != enableVersion || Exiting) return;
                var updated = Copy(); updated.LastLocation = fix; Save(updated);
                lastGeoAttempt = Now;
            }
            finally { state.Busy = checking; }
        }
        if (version != enableVersion || Exiting) return;
        var active = Copy(); active.Automatic = true; Save(active);
        await CheckAsync(false, false);
    }
    private async void Automatic_Click(object sender, RoutedEventArgs e) => await GuardAsync(() => SetAutomaticAsync(AutoToggle.IsChecked == true));
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await GuardAsync(() => CheckAsync(true, true));

    private async void Location_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new LocationWindow(state.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var updated = Copy(); updated.ManualLocation = dialog.IsManual; updated.ManualPoint = dialog.Point;
        // Do not reuse the former manual region when switching back to Windows.
        if (!dialog.IsManual) updated.LastLocation = null;
        Save(updated);
        lastGeoAttempt = DateTimeOffset.MinValue;
        await CheckAsync(!dialog.IsManual, !dialog.IsManual);
    });
    private async void Choose_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Title = "Выберите фон", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        state.Busy = true;
        try { UpdateImage((string)((Button)sender).Tag, Images.Import(dialog.FileName, folder)); }
        finally { state.Busy = false; }
        await CheckAsync(false, false);
    });
    private async void Clear_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        UpdateImage((string)((Button)sender).Tag, null);
        await CheckAsync(false, false);
    });
    private void UpdateImage(string target, string? path)
    {
        var updated = Copy();
        switch (target)
        {
            case "DayDesktop": updated.Day = updated.Day with { Desktop = path }; break;
            case "DayLock": updated.Day = updated.Day with { LockScreen = path }; break;
            case "NightDesktop": updated.Night = updated.Night with { Desktop = path }; break;
            case "NightLock": updated.Night = updated.Night with { LockScreen = path }; break;
            default: throw new ArgumentException("Неизвестное изображение.");
        }
        Save(updated);
    }
    private async void Apply_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (preview) { state.Status = "Смена обоев отключена в предпросмотре."; return; }
        var phase = (string)((Button)sender).Tag == "Day" ? SunPhase.Day : SunPhase.Night;
        var pair = state.Settings.For(phase);
        await SetAutomaticAsync(false);
        state.Busy = true;
        try
        {
            var result = await wallpaper.ApplyAsync(pair, operation.Token, force: true);
            state.Status = result.Error ?? "Фон применён. Автоматика на паузе.";
        }
        finally { state.Busy = false; }
    });
    private async void Startup_Click(object sender, RoutedEventArgs e) => await GuardAsync(() =>
    {
        if (preview) throw new InvalidOperationException("Автозапуск отключён в предпросмотре.");
        var enabled = ((CheckBox)sender).IsChecked == true;
        Startup.Set(enabled); state.StartupEnabled = enabled;
        state.Status = enabled ? "Автозапуск включён. Окно будет скрыто в трее." : "Автозапуск выключен.";
        return Task.CompletedTask;
    });
    private async void Privacy_Click(object sender, RoutedEventArgs e) => await GuardAsync(() => { Startup.OpenPrivacy(); return Task.CompletedTask; });
    internal async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { state.Status = ex.Message; AutoToggle.IsChecked = Automatic; state.StartupEnabled = !preview && Startup.Enabled; }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(async () => await CheckAsync(false, Automatic));
    }
    private void TimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(async () => { TimeZoneInfo.ClearCachedData(); await CheckAsync(false, false); });
    internal void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!Exiting && !preview) { e.Cancel = true; Hide(); return; }
        timer.Stop(); operation.Cancel();
        if (!preview) { SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.TimeChanged -= TimeChanged; }
    }
}
