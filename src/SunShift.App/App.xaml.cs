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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--diagnostics")) { PreviewMode.Diagnostics(e.Args); Shutdown(); return; }
            var preview = e.Args.Contains("--preview") || e.Args.Contains("--smoke-test");
            if (preview) ShutdownMode = ShutdownMode.OnMainWindowClose;
            else
            {
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
                single = new(true, @"Local\SunShift-" + user, out ownsSingle);
                show = new(false, EventResetMode.AutoReset, @"Local\SunShift-Show-" + user);
                if (!ownsSingle) { show.Set(); Shutdown(); return; }
            }
            theme = new(preview && e.Args.Contains("--dark") ? true : preview && e.Args.Contains("--light") ? false : null);
#pragma warning disable WPF0001
            if (preview && e.Args.Contains("--dark")) ThemeMode = System.Windows.ThemeMode.Dark;
            if (preview && e.Args.Contains("--light")) ThemeMode = System.Windows.ThemeMode.Light;
#pragma warning restore WPF0001
            var folder = preview ? Path.Combine(Path.GetTempPath(), "SunShift-preview-" + Guid.NewGuid().ToString("N")) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SunShift");
            var store = new SettingsStore(folder);
            window = new(store, preview ? PreviewMode.Settings(folder, e.Args) : store.Read(), folder, preview, preview ? PreviewMode.Clock(e.Args) : null);
            MainWindow = window;
            if (!preview)
            {
                CreateTray();
                wait = ThreadPool.RegisterWaitForSingleObject(show!, (_, _) => Dispatcher.BeginInvoke(window.ShowMain), null, Timeout.Infinite, false);
            }
            window.Show();
            if (!preview && e.Args.Contains("--background")) window.Hide();
            if (e.Args.Contains("--smoke-test")) PreviewMode.Capture(window, e.Args);
        }
        catch (Exception ex)
        {
            if (e.Args.Contains("--smoke-test"))
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
        var automatic = new Forms.ToolStripMenuItem("Автоматическая смена");
        menu.Opening += (_, _) => automatic.Checked = window!.Automatic;
        automatic.Click += (_, _) => Dispatcher.BeginInvoke(async () => await window!.GuardAsync(() => window.SetAutomaticAsync(!window.Automatic)));
        menu.Items.Add(automatic); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("О программе и лицензии", null, (_, _) => Dispatcher.BeginInvoke(() => new AboutWindow().Show()));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(() => { window!.Exiting = true; window.Close(); Shutdown(); }));
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
