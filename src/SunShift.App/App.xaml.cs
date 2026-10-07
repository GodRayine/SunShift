// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using SunShift.Core;
using Forms = System.Windows.Forms;

namespace SunShift.App;

public partial class App : Application
{
    private Mutex? single;
    private bool ownsSingle;
    private EventWaitHandle? show;
    private RegisteredWaitHandle? wait;
    private Forms.NotifyIcon? tray;
    private SystemTheme? theme;
    private MainWindow? window;
    internal void SetTheme(ThemePreference preference)
    {
        theme?.Dispose();
        theme = new(preference == ThemePreference.System ? null : preference == ThemePreference.Dark);
#pragma warning disable WPF0001
        ThemeMode = preference switch
        {
            ThemePreference.Light => System.Windows.ThemeMode.Light,
            ThemePreference.Dark => System.Windows.ThemeMode.Dark,
            _ => System.Windows.ThemeMode.System
        };
#pragma warning restore WPF0001
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--diagnostics")) { PreviewMode.Diagnostics(e.Args); Shutdown(); return; }
            var preview = e.Args.Contains("--preview") || e.Args.Contains("--smoke-test");
            var integration = e.Args.Contains("--integration-test");
            var isolated = preview || integration;
            if (isolated) ShutdownMode = ShutdownMode.OnMainWindowClose;
            else
            {
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
                single = new(true, @"Local\SunShift-" + user, out ownsSingle);
                show = new(false, EventResetMode.AutoReset, @"Local\SunShift-Show-" + user);
                if (!ownsSingle) { show.Set(); Shutdown(); return; }
            }
            var folder = isolated ? Path.Combine(Path.GetTempPath(), "SunShift-preview-" + Guid.NewGuid().ToString("N")) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SunShift");
            var store = new SettingsStore(folder);
            var settings = isolated ? PreviewMode.Settings(folder, e.Args) : store.Read();
            SetTheme(settings.Theme);
            var checks = integration ? new IntegrationChecks(folder, PreviewMode.Clock(e.Args)) : null;
            window = new(store, settings, folder, preview && !integration, isolated ? PreviewMode.Clock(e.Args) : null, checks, checks, checks == null ? null : () => checks.Clock);
            if (e.Args.Contains("--narrow") && isolated) { window.Width = 680; window.Height = 1000; }
            if (e.Args.Contains("--store-screenshot") && isolated) { window.Width = 1920; window.Height = 1080; }
            if (e.Args.Contains("--large-text") && isolated) window.ContentStack.LayoutTransform = new System.Windows.Media.ScaleTransform(1.25, 1.25);
            MainWindow = window;
            if (!isolated)
            {
                CreateTray();
                wait = ThreadPool.RegisterWaitForSingleObject(show!, (_, _) => Dispatcher.BeginInvoke(window.ShowMain), null, Timeout.Infinite, false);
            }
            window.Show();
            if (!isolated && (e.Args.Contains("--background") || SunShift.App.Startup.LaunchedAtSignIn)) window.Hide();
            if (integration) checks!.Run(window, e.Args);
            else if (e.Args.Contains("--smoke-test")) PreviewMode.Capture(window, e.Args);
        }
        catch (Exception ex)
        {
            if (e.Args.Contains("--smoke-test") || e.Args.Contains("--integration-test"))
            {
                var index = Array.IndexOf(e.Args, "--output");
                var path = index >= 0 && index + 1 < e.Args.Length ? Path.GetFullPath(e.Args[index + 1]) + ".startup-error.txt" : Path.Combine(Path.GetTempPath(), "SunShift-startup-error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, ex.ToString());
            }
            else MessageBox.Show(ex.Message, "SunShift — ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть SunShift", null, (_, _) => Dispatcher.BeginInvoke(() => window!.ShowMain()));
        var automatic = new Forms.ToolStripMenuItem("Автоматическая смена обоев");
        menu.Opening += (_, _) => automatic.Checked = window!.Automatic;
        automatic.Click += (_, _) => Dispatcher.BeginInvoke(async () => await window!.GuardAsync(() => window.SetAutomaticAsync(!window.Automatic)));
        menu.Items.Add(automatic); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("О программе и лицензии", null, (_, _) => Dispatcher.BeginInvoke(() => new AboutWindow().Show()));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(window!.ExitApplication));
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/SunShift.ico")).Stream;
        tray = new() { Icon = new System.Drawing.Icon(stream), Text = "SunShift · дневной и ночной фон", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => window!.ShowMain());
    }
    protected override void OnExit(ExitEventArgs e)
    {
        wait?.Unregister(null); tray?.Dispose(); theme?.Dispose(); show?.Dispose();
        if (ownsSingle) single?.ReleaseMutex(); single?.Dispose();
        base.OnExit(e);
    }
}
