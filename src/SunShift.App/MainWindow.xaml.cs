// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Collections.Generic;
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
    private readonly Func<DateTimeOffset>? clockProvider;
    private readonly ILocationProvider location;
    private readonly WallpaperCoordinator wallpaper;
    private readonly WallpaperRotation rotation = new(new FileWallpaperCatalog());
    private readonly PoolImageCache poolCache;
    private bool resetRotation;
    private readonly HashSet<SunPhase> changedPools = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource operation = new();
    private bool checking;
    private long enableVersion;
    private DateTimeOffset lastGeoAttempt = DateTimeOffset.MinValue;
    internal bool Exiting { get; set; }
    internal Task InitialCheck { get; private set; } = Task.CompletedTask;
    internal Task LastUiAction { get; private set; } = Task.CompletedTask;
    private bool initialized;
    internal bool Automatic => state.Settings.Automatic;
    private DateTimeOffset Now => clockProvider?.Invoke() ?? previewClock ?? DateTimeOffset.UtcNow;
    internal Task CheckNowAsync() => CheckAsync(false, false);

    internal MainWindow(SettingsStore store, Settings settings, string folder, bool preview, DateTimeOffset? previewClock,
        ILocationProvider? locationProvider = null, IWallpaperSink? wallpaperSink = null, Func<DateTimeOffset>? clockProvider = null)
    {
        InitializeComponent();
        this.store = store; this.folder = folder; this.preview = preview; this.previewClock = previewClock;
        this.clockProvider = clockProvider;
        state.Settings = settings; state.Preview = preview;
        location = locationProvider ?? new WindowsLocation();
        wallpaper = new(wallpaperSink ?? new WindowsWallpaper(folder));
        poolCache = new(folder);
        DataContext = state;
        ThemePicker.ItemsSource = new[] { "Как в Windows", "Светлая", "Тёмная" };
        state.StartupEnabled = !preview && Startup.Enabled;
        if (store.Warning != null) state.Status = store.Warning;
        timer.Tick += async (_, _) => await CheckAsync(false, false);
        Loaded += (_, _) => { timer.Start(); InitialCheck = InitializeAsync(); };
        Closing += OnClosing;
        if (!preview) { SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.TimeChanged += TimeChanged; }
        initialized = true;
    }

    private Settings Copy() => new()
    {
        Automatic = state.Settings.Automatic, Day = state.Settings.Day, Night = state.Settings.Night,
        ManualLocation = state.Settings.ManualLocation, ManualPoint = state.Settings.ManualPoint, LastLocation = state.Settings.LastLocation,
        Theme = state.Settings.Theme, DayRotation = state.Settings.DayRotation, NightRotation = state.Settings.NightRotation,
        Collection = state.Settings.Collection, ActivePairId = state.Settings.ActivePairId
    };
    private async Task InitializeAsync()
    {
        await RefreshPoolPreviewAsync(SunPhase.Day);
        await RefreshPoolPreviewAsync(SunPhase.Night);
        await CheckAsync(false, false);
    }
    private async Task RefreshPoolPreviewAsync(SunPhase phase)
    {
        if (state.Settings.Collection.Mode == CollectionMode.LinkedPairs)
        {
            var pair = state.Settings.ActivePair;
            state.SetRotation(phase, new(pair?.For(phase) ?? new(), null, null,
                state.Settings.Collection.Pairs.Length, state.Settings.Collection.Pairs.Length, pair?.Id, pair?.Name));
            return;
        }
        var options = state.Settings.RotationFor(phase);
        var single = state.Settings.For(phase);
        var result = await Task.Run(() =>
        {
            var catalog = new FileWallpaperCatalog();
            var desktop = options.DesktopFolder == null ? null : catalog.Read(options.DesktopFolder);
            var locked = options.LockScreenFolder == null ? null : catalog.Read(options.LockScreenFolder);
            return new RotationSelection(new(desktop == null ? single.Desktop : desktop.Files.Count > 0 ? desktop.Files[0] : null,
                locked == null ? single.LockScreen : locked.Files.Count > 0 ? locked.Files[0] : null), null,
                desktop?.Error ?? locked?.Error, desktop?.Files.Count ?? 0, locked?.Files.Count ?? 0);
        });
        if (options == state.Settings.RotationFor(phase) && single == state.Settings.For(phase)) state.SetRotation(phase, result);
    }
    private async Task<RotationSelection> ResolveImagesAsync(SunPhase phase, CancellationToken token)
    {
        var settings = Copy();
        if (resetRotation) { rotation.Reset(); changedPools.Clear(); resetRotation = false; }
        else if (changedPools.Remove(phase)) rotation.Invalidate(phase);
        var warnings = new List<string>();
        RotationSelection selection = new(new(), null, null, 0, 0);
        ImagePair prepared = new();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var now = Now;
            selection = await Task.Run(() => rotation.Select(settings, phase, now), token);
            token.ThrowIfCancellationRequested();
            if (selection.PairId != null && selection.PairId != state.Settings.ActivePairId)
            { var updated = Copy(); updated.ActivePairId = selection.PairId; Save(updated); }
            var retry = false;
            async Task<string?> Prepare(string? source, bool locked)
            {
                var paired = settings.Collection.Mode == CollectionMode.LinkedPairs;
                if (source == null || (!paired && (locked ? settings.RotationFor(phase).LockScreenFolder : settings.RotationFor(phase).DesktopFolder) == null)) return source;
                try { return await Task.Run(() => poolCache.Prepare(source, locked), token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (!paired) { rotation.Reject(phase, locked, source, now); retry = true; }
                    warnings.Add("Пропущено изображение: " + System.IO.Path.GetFileName(source) + ". " + ex.Message);
                    return null;
                }
            }
            prepared = new(await Prepare(selection.Images.Desktop, false), await Prepare(selection.Images.LockScreen, true));
            token.ThrowIfCancellationRequested();
            if (!retry) break;
        }
        if (selection.Warning != null) warnings.Add(selection.Warning);
        state.SetRotation(phase, selection);
        return selection with { Images = prepared, Warning = warnings.Count == 0 ? null : string.Join("\n", warnings) };
    }
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
                var selection = await ResolveImagesAsync(snapshot.Phase, token);
                var images = selection.Images;
                if (images.IsEmpty) message = $"{phase} · изображения не заданы, текущие фоны сохранены.";
                else
                {
                    token.ThrowIfCancellationRequested();
                    var result = await wallpaper.ApplyAsync(images, token);
                    message = result.Error ?? $"{phase} · фоны актуальны. При смене состояния они переключатся автоматически.";
                }
                if (selection.Warning != null) message += " " + selection.Warning;
                if (selection.NextChange is { } next)
                {
                    var imminentPhase = snapshot.Next != null && snapshot.Next.At <= next;
                    var at = imminentPhase ? snapshot.Next!.At : next;
                    message += $" Следующая смена: {TimeZoneInfo.ConvertTime(at, snapshot.Region.TimeZone):HH:mm}" +
                        (imminentPhase ? " · переход к другому профилю." : ".");
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
            resetRotation = true;
            // Pause in memory immediately even if the settings folder is temporarily unavailable.
            state.Settings = paused; state.NotifyAll();
            store.Write(paused);
            state.Status = "На паузе. Текущие фоны сохранены.";
            return;
        }
        if (!state.Settings.HasSources(SunPhase.Day) && !state.Settings.HasSources(SunPhase.Night)) throw new InvalidOperationException("Сначала выберите изображение или папку с обоями.");
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
        await SetLocationAsync(dialog.IsManual, dialog.Point);
    });
    internal async Task SetLocationAsync(bool manual, Coordinates? point)
    {
        if (manual && point is not { IsValid: true }) throw new ArgumentException("Введите допустимые координаты.");
        var updated = Copy(); updated.ManualLocation = manual; updated.ManualPoint = point;
        // Do not reuse the former manual region when switching back to Windows.
        if (!manual) updated.LastLocation = null;
        Save(updated);
        lastGeoAttempt = DateTimeOffset.MinValue;
        await CheckAsync(!manual, !manual);
    }
    private async void Choose_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        state.SelectedNight = state.SelectedNight;
        var target = (string)((Button)sender).Tag;
        var dialog = new OpenFileDialog { Title = "Выберите фон", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        state.Busy = true;
        try { UpdateImage(target, Images.Import(dialog.FileName, folder)); }
        catch (Exception ex)
        {
            if (target.EndsWith("Desktop", StringComparison.Ordinal)) state.DesktopError = ex.Message; else state.LockError = ex.Message;
            state.NotifyAll(); throw;
        }
        finally { state.Busy = false; }
        await CheckAsync(false, false);
    });
    private async void Clear_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        state.SelectedNight = state.SelectedNight;
        UpdateImage((string)((Button)sender).Tag, null);
        await CheckAsync(false, false);
    });
    private void UpdateImage(string target, string? path)
    {
        state.DesktopError = state.LockError = "";
        var updated = Copy();
        switch (target)
        {
            case "DayDesktop": updated.Day = updated.Day with { Desktop = path }; break;
            case "DayLock": updated.Day = updated.Day with { LockScreen = path }; break;
            case "NightDesktop": updated.Night = updated.Night with { Desktop = path }; break;
            case "NightLock": updated.Night = updated.Night with { LockScreen = path }; break;
            default: throw new ArgumentException("Неизвестное изображение.");
        }
        var phase = target.StartsWith("Night", StringComparison.Ordinal) ? SunPhase.Night : SunPhase.Day;
        var options = updated.RotationFor(phase);
        options = target.EndsWith("Desktop", StringComparison.Ordinal) ? options with { DesktopFolder = null } : options with { LockScreenFolder = null };
        if (phase == SunPhase.Day) updated.DayRotation = options; else updated.NightRotation = options;
        Save(updated);
    }
    private async void Collection_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new CollectionWindow(state.Settings) { Owner = this };
        if (dialog.ShowDialog() == true) await SetCollectionAsync(dialog.Result, dialog.DayFolders, dialog.NightFolders, dialog.ActivePairId);
    });
    private async void RotationToggle_Click(object sender, RoutedEventArgs e) => await GuardAsync(() => SetCollectionRotationAsync(RotationToggle.IsChecked == true));
    internal async Task SetCollectionRotationAsync(bool enabled)
    {
        var updated = Copy(); updated.Collection = updated.Collection with { Rotate = enabled }; Save(updated);
        await CheckAsync(false, false);
    }
    internal async Task SetCollectionAsync(CollectionSettings collection, RotationSettings day, RotationSettings night, string? activePairId)
    {
        state.Busy = true;
        try
        {
            var prepared = await Task.Run(() =>
            {
                var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? Import(string? path)
                {
                    if (path == null) return null;
                    var full = System.IO.Path.GetFullPath(path);
                    var owned = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, "Images")) + System.IO.Path.DirectorySeparatorChar;
                    if (full.StartsWith(owned, StringComparison.OrdinalIgnoreCase)) return full;
                    if (!imported.TryGetValue(full, out var value)) imported[full] = value = Images.Import(full, folder);
                    return value;
                }
                ImagePair ImagesFor(ImagePair pair) => new(Import(pair.Desktop), Import(pair.LockScreen));
                var pairs = new List<WallpaperPair>();
                foreach (var pair in collection.Pairs) pairs.Add(pair with { Day = ImagesFor(pair.Day), Night = ImagesFor(pair.Night) });
                return collection with { Pairs = pairs.ToArray() };
            });
            var updated = Copy(); updated.Collection = prepared.Normalize(); updated.DayRotation = day.Normalize(); updated.NightRotation = night.Normalize(); updated.ActivePairId = activePairId;
            if (updated.Collection.Mode != state.Settings.Collection.Mode) resetRotation = true;
            if (updated.DayRotation != state.Settings.DayRotation) changedPools.Add(SunPhase.Day);
            if (updated.NightRotation != state.Settings.NightRotation) changedPools.Add(SunPhase.Night);
            Save(updated);
            await RefreshPoolPreviewAsync(SunPhase.Day); await RefreshPoolPreviewAsync(SunPhase.Night);
        }
        finally { state.Busy = false; }
        await CheckAsync(false, false);
    }
    internal async Task SetRotationAsync(SunPhase phase, RotationSettings options)
    {
        var updated = Copy();
        if (phase == SunPhase.Day) updated.DayRotation = options.Normalize(); else updated.NightRotation = options.Normalize();
        updated.Collection = updated.Collection with { Mode = CollectionMode.Folders, Rotate = true };
        Save(updated); changedPools.Add(phase);
        await RefreshPoolPreviewAsync(phase);
        await CheckAsync(false, false);
    }
    private async void Apply_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        state.SelectedNight = state.SelectedNight;
        if (preview) { state.Status = "Смена обоев отключена в предпросмотре."; return; }
        var phase = (string)((Button)sender).Tag == "Day" ? SunPhase.Day : SunPhase.Night;
        await SetAutomaticAsync(false);
        state.Busy = true;
        try
        {
            var selection = await ResolveImagesAsync(phase, operation.Token);
            var result = await wallpaper.ApplyAsync(selection.Images, operation.Token, force: true);
            state.Status = (result.Error ?? (selection.Images.IsEmpty ? "Нет доступных изображений. Текущие фоны сохранены." : "Фон применён. Автоматика на паузе.")) +
                (selection.Warning == null ? "" : " " + selection.Warning);
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
    internal Task GuardAsync(Func<Task> action) => LastUiAction = GuardCoreAsync(action);
    private async Task GuardCoreAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            state.Status = ex.Message; AutoToggle.IsChecked = Automatic; state.StartupEnabled = !preview && Startup.Enabled;
            ThemePicker.SetCurrentValue(ComboBox.SelectedIndexProperty, state.ThemeIndex);
        }
    }
    private async void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ThemePicker.SelectedIndex < 0 || ThemePicker.SelectedIndex == state.ThemeIndex) return;
        await GuardAsync(() =>
        {
            var updated = Copy(); updated.Theme = (ThemePreference)ThemePicker.SelectedIndex;
            Save(updated); ((App)Application.Current).SetTheme(updated.Theme);
            return Task.CompletedTask;
        });
    }
    private void License_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.Show();
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    internal void ExitApplication()
    {
        Exiting = true;
        Close();
        Application.Current.Shutdown();
    }
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton == null) return;
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Восстановить окно" : "Развернуть окно");
        OuterFrame.Padding = maximized ? new Thickness(6) : new Thickness(0);
    }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidePanel == null) return;
        var scale = ContentStack.LayoutTransform is System.Windows.Media.ScaleTransform transform ? transform.ScaleX : 1;
        var narrow = (ActualWidth > 0 ? ActualWidth : Width) / scale < 980;
        Grid.SetColumn(SidePanel, narrow ? 0 : 2); Grid.SetRow(SidePanel, narrow ? 1 : 0);
        SidePanel.Margin = narrow ? new Thickness(0, 24, 0, 0) : new Thickness(0);
        GapColumn.Width = new GridLength(narrow ? 0 : 24); SideColumn.Width = new GridLength(narrow ? 0 : 300);
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
