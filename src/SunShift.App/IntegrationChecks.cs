// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SunShift.Core;

namespace SunShift.App;

// Runs the production window and persistence code with isolated paths and fake OS services.
// This mode never requests real location permissions or changes system wallpapers.
internal sealed class IntegrationChecks(string folder, DateTimeOffset clock) : ILocationProvider, IWallpaperSink
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    private static int HitTest(MainWindow window, Point point)
    {
        var screen = window.PointToScreen(point);
        var packed = ((int)screen.Y << 16) | ((int)screen.X & 0xffff);
        return (int)SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x84, IntPtr.Zero, new IntPtr(packed));
    }
    internal DateTimeOffset Clock => clock;
    private readonly List<ImagePair> calls = new();
    private int requests, reads;
    private bool denyPermission;
    private bool denyRead;
    public Task RequestPermissionAsync()
    {
        requests++;
        if (denyPermission) throw new UnauthorizedAccessException("Геолокация недоступна: проверка отказа разрешения.");
        return Task.CompletedTask;
    }
    public Task<LocationFix> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); reads++;
        if (denyRead) throw new UnauthorizedAccessException("Геолокация недоступна: системный доступ отозван.");
        return Task.FromResult(new LocationFix(new(40.7667, -73.9), 20, clock));
    }
    public Task<ChangeResult> ChangeAsync(ImagePair pair, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pair.Desktop != null && !File.Exists(pair.Desktop)) throw new FileNotFoundException("Test desktop image missing.");
        if (pair.LockScreen != null && !File.Exists(pair.LockScreen)) throw new FileNotFoundException("Test lock image missing.");
        calls.Add(pair); return Task.FromResult(new ChangeResult(true, true));
    }
    public async void Run(MainWindow window, string[] args)
    {
        var output = PreviewMode.Output(args, "SunShift-integration-checks.json");
        var passed = new List<string>();
        void Verify(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Integration check failed: " + name);
            passed.Add(name);
        }
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await window.InitialCheck;
            var state = (ViewState)window.DataContext;
            var store = new SettingsStore(folder);
            var day = state.Settings.Day;
            Verify(state.PhaseLabel == "Сейчас день", "regional solar state");
            window.AutoToggle.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            window.AutoToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await window.LastUiAction;
            Verify(window.Automatic && calls.Count == 1 && calls[0] == day, "automatic applies actual day profile");
            window.NightTab.SetCurrentValue(RadioButton.IsCheckedProperty, true);
            window.NightTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Verify(state.SelectedNight && state.PhaseLabel == "Сейчас день" && calls.Count == 1, "editing night does not change actual day");
            window.ClearLock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LastUiAction;
            Verify(state.Settings.Night.LockScreen == null && state.Settings.Day == day, "clear only selected lock image");
            Verify(calls.Count == 1, "profile edit does not repeat unchanged active wallpapers");
            Verify(store.Read().Night.LockScreen == null && store.Read().Automatic, "profile edits persist");
            window.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LastUiAction;
            Verify(!window.Automatic && calls.Count == 2 && calls[1] == state.Settings.Night, "selected manual profile applies and pauses automation");
            Verify(!store.Read().Automatic, "pause persists");
            window.ThemePicker.SetCurrentValue(ComboBox.SelectedIndexProperty, 2);
            await window.LastUiAction;
            Verify(store.Read().Theme == ThemePreference.Dark, "theme choice persists");
            await window.SetLocationAsync(false, null);
            Verify(requests == 1 && reads == 1 && state.RegionLabel.Contains("America/New_York"), "Windows location routing");
            Verify(store.Read().Theme == ThemePreference.Dark && store.Read().LastLocation == null && state.Settings.LastLocation != null, "location remains in session and never persists");
            denyRead = true;
            window.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LastUiAction;
            Verify(state.Settings.LastLocation == null && state.LocalClock == "—" && !window.Automatic && calls.Count == 2,
                "revoked Windows access never reuses the former session position");
            denyRead = false;
            denyPermission = true;
            await window.SetLocationAsync(false, null);
            Verify(state.Status.Contains("Геолокация недоступна") && state.LocalClock == "—" && calls.Count == 2, "location denial preserves wallpapers");
            await window.SetLocationAsync(true, new(-33.8688, 151.2093));
            Verify(state.RegionLabel.Contains("Australia/Sydney") && store.Read().ManualLocation, "manual coordinate routing");
            Verify(store.Read().ManualPoint == null && state.Settings.ManualPoint != null, "manual coordinates remain in session only");
            var readsBeforeDisable = reads;
            await window.SetLocationAsync(false, null, true);
            await window.CheckNowAsync();
            window.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.LastUiAction;
            Verify(state.Settings.LastLocation == null && state.Settings.ManualPoint == null && !window.Automatic && state.LocalClock == "—" && reads == readsBeforeDisable,
                "location opt out clears session and stops location access");
            await window.SetLocationAsync(true, new(-33.8688, 151.2093));
            Verify(state.SelectedNight, "editing selection survives solar refresh");
            var dayPool = Path.Combine(folder, "DayPool"); var nightPool = Path.Combine(folder, "NightPool");
            Directory.CreateDirectory(dayPool); Directory.CreateDirectory(nightPool);
            File.Copy(day.Desktop!, Path.Combine(dayPool, "01.png")); File.Copy(state.Settings.Night.Desktop!, Path.Combine(dayPool, "02.png"));
            File.WriteAllText(Path.Combine(nightPool, "00-broken.png"), "broken image");
            File.Copy(state.Settings.Night.Desktop!, Path.Combine(nightPool, "01.png")); File.Copy(day.Desktop!, Path.Combine(nightPool, "02.png"));
            await window.SetRotationAsync(SunPhase.Day, new(dayPool, IntervalMinutes: 5));
            await window.SetRotationAsync(SunPhase.Night, new(nightPool, IntervalMinutes: 1));
            Verify(calls.Count == 2 && store.Read().NightRotation.DesktopFolder == nightPool && store.Read().Theme == ThemePreference.Dark,
                "folder settings persist while paused without OS writes");
            var dialog = new RotationWindow(state.Settings.NightRotation, SunPhase.Night) { Owner = window };
            dialog.Show(); dialog.Interval.Text = "0";
            Verify(!await dialog.ValidateAsync() && dialog.IntervalValidation.Text.Length > 0, "invalid custom interval has inline feedback");
            dialog.Interval.Text = "7";
            Verify(await dialog.ValidateAsync() && dialog.Result.IntervalMinutes == 7, "custom interval accepts valid minutes");
            var hourlyLabels = new[] { "1 ч", "2 ч", "4 ч", "8 ч", "12 ч", "Раз в день" };
            var hourlyMinutes = new[] { 60, 120, 240, 480, 720, 1440 };
            for (var index = 0; index < hourlyLabels.Length; index++)
            {
                dialog.Interval.SelectedItem = hourlyLabels[index];
                Verify(await dialog.ValidateAsync() && dialog.Result.IntervalMinutes == hourlyMinutes[index], "folder preset " + hourlyLabels[index]);
            }
            var legacyDialog = new RotationWindow(new(IntervalMinutes: 180), SunPhase.Day) { Owner = window }; legacyDialog.Show();
            Verify(legacyDialog.Interval.Text == "3 ч" && await legacyDialog.ValidateAsync() && legacyDialog.Result.IntervalMinutes == 180,
                "legacy custom hourly interval preserved");
            legacyDialog.Close();
            dialog.DesktopFolder.Text = Path.Combine(folder, "missing");
            Verify(!await dialog.ValidateAsync() && dialog.DesktopValidation.Text.Length > 0, "unavailable folder has inline feedback");
            dialog.Close();
            await window.SetAutomaticAsync(true);
            Verify(window.Automatic && state.PhaseLabel == "Сейчас ночь" && calls.Count == 3 && File.Exists(calls[^1].Desktop) &&
                calls[^1].Desktop!.Contains("PoolCache") && calls[^1].LockScreen == null, "pool-only automatic mode prepares selected screen");
            Verify(state.Status.Contains("Пропущено изображение") && state.DesktopImage == Path.Combine(nightPool, "01.png"), "corrupt pool image skipped");
            Verify(state.NextLabel == "Следующий кадр" && state.NextTime == "21:01", "next wallpaper change shown in regional time");
            await window.CheckNowAsync();
            Verify(calls.Count == 3, "checks before interval do not rewrite wallpapers");
            clock = clock.AddMinutes(1); await window.CheckNowAsync();
            Verify(calls.Count == 4 && state.DesktopImage == Path.Combine(nightPool, "02.png"), "interval advances actual pool wallpaper");
            await window.SetCollectionAsync(state.Settings.Collection, new(dayPool, IntervalMinutes: 3), state.Settings.NightRotation, state.Settings.ActivePairId);
            Verify(calls.Count == 4 && state.DesktopImage == Path.Combine(nightPool, "02.png"), "editing inactive pool preserves active wallpaper and schedule");
            var original = File.ReadAllBytes(Path.Combine(nightPool, "01.png"));
            for (var i = 0; i < 12; i++) { clock = clock.AddMinutes(1); await window.CheckNowAsync(); }
            Verify(Directory.GetFiles(Path.Combine(folder, "PoolCache"), "*.png").Length <= 4 &&
                System.Linq.Enumerable.SequenceEqual(original, File.ReadAllBytes(Path.Combine(nightPool, "01.png"))), "rotation cache bounded and originals unchanged");
            await window.SetAutomaticAsync(false); var pausedCalls = calls.Count;
            clock = clock.AddMinutes(10); await window.CheckNowAsync();
            Verify(calls.Count == pausedCalls && !store.Read().Automatic, "pause stops interval rotation");
            await window.SetAutomaticAsync(true);
            Verify(calls.Count == pausedCalls + 1, "resume restarts pool schedule");
            Directory.Move(nightPool, nightPool + "-offline"); var offlineCalls = calls.Count;
            await window.CheckNowAsync();
            Verify(calls.Count == offlineCalls && state.Status.Contains("Папка недоступна"), "missing pool preserves current wallpaper");
            Directory.Move(nightPool + "-offline", nightPool); await window.CheckNowAsync();
            Verify(!state.Status.Contains("Папка недоступна"), "reconnected pool recovers automatically");
            var beforeDay = calls.Count;
            clock = clock.AddHours(12); await window.CheckNowAsync();
            window.DayTab.SetCurrentValue(RadioButton.IsCheckedProperty, true);
            Verify(state.PhaseLabel == "Сейчас день" && calls.Count == beforeDay + 1 && calls[^1].LockScreen == null &&
                state.Settings.Day.LockScreen == day.LockScreen && state.DesktopImage == Path.Combine(dayPool, "01.png"),
                "solar transition switches pool without rewriting unchanged single-image screen");
            await window.SetCollectionRotationAsync(false); var heldCalls = calls.Count;
            clock = clock.AddMinutes(15); await window.CheckNowAsync();
            Verify(window.Automatic && !store.Read().Collection.Rotate && calls.Count == heldCalls, "extra folder rotation stops independently of solar automation");
            clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(window.Automatic && state.PhaseLabel == "Сейчас ночь" && calls.Count > heldCalls, "solar transition still applies with extra rotation off");
            var collectionDialog = new CollectionWindow(state.Settings) { Owner = window }; collectionDialog.Show();
            collectionDialog.Mode.SelectedIndex = 1;
            Verify(!collectionDialog.Validate() && collectionDialog.Feedback.Text.Length > 0, "empty linked collection rejected with feedback");
            collectionDialog.Close();
            Verify(store.Read().Collection.Mode == CollectionMode.Folders && !state.Settings.Collection.Rotate, "cancel collection dialog preserves saved settings");
            var collection = new CollectionSettings { Mode = CollectionMode.LinkedPairs, Rotate = false, IntervalMinutes = 5,
                Pairs = [new("one", "Горы", new(day.Desktop), new(state.Settings.Night.Desktop)),
                         new("two", "Море", new(state.Settings.Night.Desktop), new(day.Desktop))] };
            await window.SetCollectionAsync(collection, state.Settings.DayRotation, state.Settings.NightRotation, "one");
            Verify(state.Settings.ActivePairId == "one" && store.Read().Collection.Pairs.Length == 2 &&
                store.Read().ActivePair!.Day.Desktop!.StartsWith(Path.Combine(folder, "Images")) && !state.CanEditSingle,
                "linked assets imported and selected scene persisted");
            var pairsDialog = new CollectionWindow(state.Settings) { Owner = window }; pairsDialog.Show();
            for (var index = 0; index < hourlyLabels.Length; index++)
            {
                pairsDialog.PairInterval.SelectedItem = hourlyLabels[index];
                Verify(pairsDialog.Validate() && pairsDialog.Result.IntervalMinutes == hourlyMinutes[index], "pair preset " + hourlyLabels[index]);
            }
            pairsDialog.Close();
            var pairBeforeDay = state.Settings.ActivePairId;
            clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(state.PhaseLabel == "Сейчас день" && state.Settings.ActivePairId == pairBeforeDay &&
                state.DesktopImage == state.Settings.ActivePair!.Day.Desktop, "sunrise uses day partner of same selected scene");
            await window.SetCollectionRotationAsync(true); clock = clock.AddMinutes(5); await window.CheckNowAsync();
            Verify(state.Settings.ActivePairId == "two" && store.Read().ActivePairId == "two" && state.DesktopImage == state.Settings.ActivePair!.Day.Desktop,
                "pair timer advances whole scene and persists identity");
            await window.SetCollectionRotationAsync(false); clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(window.Automatic && state.PhaseLabel == "Сейчас ночь" && state.Settings.ActivePairId == "two" &&
                !state.Settings.Collection.Rotate, "stopping pair rotation preserves pair across sunset");
            Verify(state.SelectedNight && window.NightTab.IsChecked == true && window.DayTab.IsChecked == false &&
                state.DesktopImage == state.Settings.ActivePair!.Night.Desktop,
                "sunset synchronizes profile tab and preview with actual wallpaper");
            window.DayTab.SetCurrentValue(RadioButton.IsCheckedProperty, true);
            window.DayTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var editingCalls = calls.Count;
            await window.CheckNowAsync();
            Verify(state.SelectedDay && state.PhaseLabel == "Сейчас ночь" && calls.Count == editingCalls,
                "editing other profile survives same-phase refresh without changing wallpaper");
            clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(state.SelectedDay && window.DayTab.IsChecked == true && state.PhaseLabel == "Сейчас день",
                "sunrise synchronizes active profile");
            clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(state.SelectedNight && window.NightTab.IsChecked == true &&
                state.DesktopImage == state.Settings.ActivePair!.Night.Desktop,
                "next sunset resumes following sun after manual tab selection");
            await window.SetAutomaticAsync(false);
            window.DayTab.SetCurrentValue(RadioButton.IsCheckedProperty, true);
            window.DayTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            clock = clock.AddHours(12); await window.CheckNowAsync();
            clock = clock.AddHours(12); await window.CheckNowAsync();
            Verify(state.SelectedDay && state.PhaseLabel == "Сейчас ночь", "paused editing survives solar transitions");
            var startupWindow = new MainWindow(store, new Settings { ManualLocation = true, ManualPoint = new(40.7667, -73.9) },
                folder, false, null, this, this, () => clock);
            startupWindow.Show(); await startupWindow.InitialCheck;
            var startupState = (ViewState)startupWindow.DataContext;
            Verify(startupState.PhaseLabel == "Сейчас ночь" && startupState.SelectedNight && startupWindow.NightTab.IsChecked == true,
                "night startup bindings do not count as manual tab selection");
            startupWindow.Exiting = true; startupWindow.Close(); window.ShowMain();
            var caption = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            Verify(window.WindowStyle == WindowStyle.None && caption?.CaptionHeight == 70 && HitTest(window, new Point(100, 30)) == 2,
                "unified header has native caption hit testing");
            Verify(HitTest(window, new Point(2, window.ActualHeight / 2)) == 10, "native resize border retained");
            window.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Verify(window.WindowState == WindowState.Maximized, "header maximize button works");
            window.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Verify(window.WindowState == WindowState.Normal, "header restore button works");
            window.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Verify(window.WindowState == WindowState.Minimized, "header minimize button works");
            window.ShowMain();
            window.ExitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Verify(window.Exiting && !window.IsVisible, "exit button closes application instead of hiding in tray");
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = passed, FakeWallpaperCalls = calls.Count, FakePermissionRequests = requests, FakeLocationReads = reads, RealOSWrites = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(output + ".error.txt", ex.ToString()); window.Exiting = true; Application.Current.Shutdown(1);
        }
    }
}
