// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SunShift.Core;
using Windows.Devices.Geolocation;
using Windows.Storage;
using Windows.System.UserProfile;

namespace SunShift.App;

internal sealed class WindowsLocation
{
    private Geolocator? locator;
    public async Task RequestPermissionAsync()
    {
        if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            throw new InvalidOperationException("Разрешите геолокацию и доступ для классических приложений в параметрах Windows или укажите координаты вручную.");
    }
    public async Task<LocationFix> ReadAsync(CancellationToken token)
    {
        locator ??= new() { DesiredAccuracy = PositionAccuracy.High };
        var position = await locator.GetGeopositionAsync(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(25)).AsTask(token);
        var c = position.Coordinate;
        var fix = new LocationFix(new(c.Point.Position.Latitude, c.Point.Position.Longitude), c.Accuracy, c.Timestamp);
        if (!fix.IsUsable(DateTimeOffset.UtcNow)) throw new InvalidOperationException("Windows вернула устаревшую или слишком неточную геопозицию.");
        return fix;
    }
}

internal sealed class WindowsWallpaper(string folder) : IWallpaperSink
{
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    public async Task<ChangeResult> ChangeAsync(ImagePair pair, CancellationToken token)
    {
        bool desktop = true, locked = true;
        var errors = new List<string>();
        if (pair.Desktop != null)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(pair.Desktop)) throw new FileNotFoundException("Изображение не найдено.");
                if (!SystemParametersInfo(0x14, 0, pair.Desktop, 3)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { desktop = false; errors.Add("Рабочий стол: " + ex.Message); }
        }
        if (pair.LockScreen != null)
        {
            token.ThrowIfCancellationRequested();
            string? copy = null;
            try
            {
                if (!UserProfilePersonalizationSettings.IsSupported()) throw new InvalidOperationException("Windows не разрешает смену экрана блокировки для приложения.");
                var cache = Path.Combine(folder, "LockScreen");
                Directory.CreateDirectory(cache);
                copy = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".png");
                File.Copy(pair.LockScreen, copy);
                var file = await StorageFile.GetFileFromPathAsync(copy).AsTask(token);
                token.ThrowIfCancellationRequested();
                if (!await UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(file).AsTask(token))
                    throw new InvalidOperationException("Windows отклонила изображение. Проверьте режим персонализации и политики устройства.");
                foreach (var old in Directory.EnumerateFiles(cache, "*.png")) if (old != copy) TryDelete(old);
            }
            catch (OperationCanceledException) { if (copy != null) TryDelete(copy); throw; }
            catch (Exception ex) { if (copy != null) TryDelete(copy); locked = false; errors.Add("Экран блокировки: " + ex.Message); }
        }
        return new(desktop, locked, errors.Count == 0 ? null : string.Join("\n", errors));
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

internal static class Images
{
    public static string Import(string source, string folder)
    {
        if (new FileInfo(source).Length > 40 * 1024 * 1024) throw new InvalidOperationException("Максимальный размер файла — 40 МБ.");
        using var stream = File.OpenRead(source);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 60_000_000) throw new InvalidOperationException("Максимум — 60 мегапикселей.");
        var images = Path.Combine(folder, "Images");
        Directory.CreateDirectory(images);
        var path = Path.Combine(images, Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(frame);
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }
    public static BitmapImage? Preview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 800;
            image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return null; }
    }
}
public sealed class PreviewConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Images.Preview(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

internal static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("SunShift") is string; } }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (!enabled) { key.DeleteValue("SunShift", false); return; }
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден EXE приложения.");
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Для автозапуска запустите SunShift.exe.");
        key.SetValue("SunShift", $"\"{path}\" --background");
    }
    public static void OpenPrivacy() => Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
}
