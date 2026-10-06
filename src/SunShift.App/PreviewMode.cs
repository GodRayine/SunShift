// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SunShift.Core;

namespace SunShift.App;

internal static class PreviewMode
{
    public static DateTimeOffset Clock(string[] args) => args.Contains("--polar-day") ? new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero) :
        args.Contains("--polar-night") ? new(2026, 12, 21, 10, 0, 0, TimeSpan.Zero) :
        new(2026, 10, 5, args.Contains("--night") ? 21 : 10, 0, 0, TimeSpan.Zero);
    public static Settings Settings(string folder, string[] args)
    {
        var theme = args.Contains("--dark") ? ThemePreference.Dark : args.Contains("--light") ? ThemePreference.Light : ThemePreference.System;
        if (args.Contains("--empty")) return new() { Theme = theme };
        Directory.CreateDirectory(folder);
        var day = Draw(folder, "day.png", Color.FromRgb(111, 158, 174), Color.FromRgb(242, 212, 163), false);
        var night = Draw(folder, "night.png", Color.FromRgb(18, 25, 56), Color.FromRgb(58, 92, 110), true);
        var settings = new Settings { Theme = theme, ManualLocation = true, ManualPoint = args.Any(a => a.StartsWith("--polar")) ? new(69.6492, 18.9553) : new(55.751244, 37.618423), Day = new(day, day), Night = new(night, night) };
        if (args.Contains("--pool"))
        {
            var dayPool = Path.Combine(folder, "DayPool"); var nightPool = Path.Combine(folder, "NightPool");
            Directory.CreateDirectory(dayPool); Directory.CreateDirectory(nightPool);
            File.Copy(day, Path.Combine(dayPool, "01.png")); File.Copy(night, Path.Combine(dayPool, "02.png"));
            File.Copy(night, Path.Combine(nightPool, "01.png")); File.Copy(day, Path.Combine(nightPool, "02.png"));
            settings.DayRotation = new(dayPool, IntervalMinutes: 30);
            settings.NightRotation = new(nightPool, nightPool, 15, RotationOrder.Random);
            settings.Collection = new() { Rotate = true };
        }
        if (args.Contains("--pairs"))
        {
            var seaDay = Draw(folder, "sea-day.png", Color.FromRgb(85, 164, 181), Color.FromRgb(238, 225, 186), false);
            var seaNight = Draw(folder, "sea-night.png", Color.FromRgb(12, 29, 51), Color.FromRgb(37, 73, 96), true);
            settings.Collection = new() { Mode = CollectionMode.LinkedPairs, Rotate = true, IntervalMinutes = 30,
                Pairs = [new("mountains", "Горы", new(day, day), new(night, night)), new("sea", "Побережье", new(seaDay), new(seaNight))] };
            settings.ActivePairId = "mountains";
        }
        return settings;
    }
    private static string Draw(string folder, string name, Color top, Color bottom, bool night)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(top, bottom, 90), null, new Rect(0, 0, 800, 480));
            dc.DrawEllipse(new SolidColorBrush(night ? Colors.LightGray : Color.FromRgb(255, 237, 197)), null, new Point(620, 100), 35, 35);
            if (night)
            {
                dc.DrawEllipse(new SolidColorBrush(top), null, new Point(634, 89), 33, 33);
                for (var i = 0; i < 28; i++) dc.DrawEllipse(Brushes.White, null, new Point(20 + i * 67 % 750, 20 + i * 41 % 220), 1, 1);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(160, 40, 65, 70)), null, Geometry.Parse("M0,480 L0,330 Q150,200 370,330 Q650,430 800,260 L800,480 Z"));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(190, 19, 34, 48)), null, Geometry.Parse("M0,480 L0,430 Q180,350 430,420 Q650,510 800,360 L800,480 Z"));
        }
        var bitmap = new RenderTargetBitmap(800, 480, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var path = Path.Combine(folder, name); Save(bitmap, path); return path;
    }
    public static async void Capture(MainWindow main, string[] args)
    {
        try
        {
            await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await main.InitialCheck;
            await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Window target = main;
            if (args.Contains("--location")) { target = new LocationWindow(((ViewState)main.DataContext).Settings) { Owner = main }; target.Show(); }
            if (args.Contains("--about")) { target = new AboutWindow { Owner = main }; target.Show(); }
            if (args.Contains("--rotation"))
            {
                var state = (ViewState)main.DataContext;
                target = new RotationWindow(state.Settings.RotationFor(state.SelectedNight ? SunPhase.Night : SunPhase.Day), state.SelectedNight ? SunPhase.Night : SunPhase.Day) { Owner = main };
                if (args.Contains("--narrow")) target.Width = 480;
                if (args.Contains("--large-text")) ((FrameworkElement)target.Content).LayoutTransform = new ScaleTransform(1.25, 1.25);
                target.Show();
            }
            if (args.Contains("--collection"))
            {
                target = new CollectionWindow(((ViewState)main.DataContext).Settings) { Owner = main };
                if (args.Contains("--narrow")) target.Width = 660;
                if (args.Contains("--large-text")) ((FrameworkElement)target.Content).LayoutTransform = new ScaleTransform(1.25, 1.25);
                target.Show();
            }
            target.UpdateLayout();
            var content = (FrameworkElement)target.Content;
            var bounds = content.LayoutTransform.TransformBounds(new Rect(0, 0,
                content.ActualWidth + content.Margin.Left + content.Margin.Right,
                content.ActualHeight + content.Margin.Top + content.Margin.Bottom));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(target); Save(bitmap, Output(args, "SunShift-preview.png"));
            if (target != main) target.Close();
            main.Exiting = true; main.Close(); Application.Current.Shutdown();
        }
        catch (Exception ex) { File.WriteAllText(Output(args, "SunShift-preview-error.txt") + ".error.txt", ex.ToString()); main.Exiting = true; main.Close(); Application.Current.Shutdown(1); }
    }
    public static void Diagnostics(string[] args)
    {
        var supported = Windows.System.UserProfile.UserProfilePersonalizationSettings.IsSupported();
        File.WriteAllText(Output(args, "SunShift-diagnostics.json"), System.Text.Json.JsonSerializer.Serialize(new
        { OS = Environment.OSVersion.VersionString, LockScreenApiSupported = supported, Note = "Read-only check; no wallpaper change or location request." }));
    }
    internal static string Output(string[] args, string defaultName)
    {
        var index = Array.IndexOf(args, "--output");
        var path = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(Path.GetTempPath(), defaultName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    private static void Save(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
