// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
namespace SunShift.Core;

public sealed record WallpaperCatalogResult(IReadOnlyList<string> Files, string? Error = null);
public interface IWallpaperCatalog { WallpaperCatalogResult Read(string folder); }

public sealed class FileWallpaperCatalog : IWallpaperCatalog
{
    public WallpaperCatalogResult Read(string folder)
    {
        try
        {
            if (!Path.IsPathFullyQualified(folder)) return new([], "Укажите полный путь к папке.");
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp")
                .Take(10_001).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length > 10_000) return new([], "В папке более 10 000 изображений. Выберите папку меньшего размера.");
            return files.Length == 0 ? new([], "В папке нет JPG, PNG или BMP.") : new(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new([], "Папка недоступна. " + ex.Message); }
    }
}

public sealed record RotationSelection(ImagePair Images, DateTimeOffset? NextChange, string? Warning,
    int DesktopCount, int LockScreenCount, string? PairId = null, string? PairName = null);

// Called by the window's serialized check loop. Deadlines start from the actual
// selection, so sleep or a clock jump advances at most once, never in a burst.
public sealed class WallpaperRotation(IWallpaperCatalog catalog, Random? random = null)
{
    private sealed class Cursor
    {
        public string? Current;
        public DateTimeOffset Due;
        public DateTimeOffset SelectedAt;
        public string? Folder;
        public int Interval;
        public RotationOrder Order;
        public bool? Rotate;
        public readonly Dictionary<string, DateTimeOffset> Rejected = new(StringComparer.OrdinalIgnoreCase);
    }
    private readonly Dictionary<(SunPhase, bool), Cursor> cursors = new();
    private readonly Random generator = random ?? Random.Shared;
    private readonly LinkedPairRotation linked = new(random);
    public void Reset() { cursors.Clear(); linked.Reset(); }
    public void Invalidate(SunPhase phase) { cursors.Remove((phase, false)); cursors.Remove((phase, true)); }
    public void Reject(SunPhase phase, bool locked, string path, DateTimeOffset now)
    {
        if (cursors.TryGetValue((phase, locked), out var cursor))
            cursor.Rejected[path] = now.AddMinutes(5);
    }

    public RotationSelection Select(Settings settings, SunPhase phase, DateTimeOffset now)
    {
        if (settings.Collection.Mode == CollectionMode.LinkedPairs) return linked.Select(settings, phase, now);
        var options = settings.RotationFor(phase).Normalize();
        var single = settings.For(phase);
        var warnings = new List<string>();
        var inventories = new Dictionary<string, WallpaperCatalogResult>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? next = null;
        string? Choose(bool locked, string? folder, string? fallback, out int count)
        {
            count = 0;
            var key = (phase, locked);
            if (string.IsNullOrWhiteSpace(folder)) { cursors.Remove(key); return fallback; }
            if (!inventories.TryGetValue(folder, out var inventory)) inventories[folder] = inventory = catalog.Read(folder);
            count = inventory.Files.Count;
            if (inventory.Error != null) warnings.Add((locked ? "Экран блокировки: " : "Рабочий стол: ") + inventory.Error);
            if (count == 0) return null; // Preserve that screen; never substitute a different source silently.
            if (!cursors.TryGetValue(key, out var cursor) || cursor.Folder != folder || cursor.Interval != options.IntervalMinutes || cursor.Order != options.Order)
                cursors[key] = cursor = new() { Folder = folder, Interval = options.IntervalMinutes, Order = options.Order };
            foreach (var expired in cursor.Rejected.Where(entry => now >= entry.Value).Select(entry => entry.Key).ToArray()) cursor.Rejected.Remove(expired);
            var files = inventory.Files.Where(path => !cursor.Rejected.TryGetValue(path, out var until) || now >= until).ToArray();
            if (files.Length == 0) { warnings.Add((locked ? "Экран блокировки: " : "Рабочий стол: ") + "Изображения в папке не удалось прочитать; повтор через 5 минут."); return null; }
            count = files.Length;
            if (cursor.Rotate != settings.Collection.Rotate)
            { cursor.Rotate = settings.Collection.Rotate; cursor.SelectedAt = now; cursor.Due = now.AddMinutes(options.IntervalMinutes); }
            var currentIndex = -1;
            for (var i = 0; i < count; i++) if (StringComparer.OrdinalIgnoreCase.Equals(files[i], cursor.Current)) { currentIndex = i; break; }
            if (currentIndex < 0 || (settings.Collection.Rotate && now >= cursor.Due))
            {
                int index;
                if (options.Order == RotationOrder.Random && count > 1)
                {
                    index = generator.Next(currentIndex < 0 ? count : count - 1);
                    if (currentIndex >= 0 && index >= currentIndex) index++;
                }
                else if (currentIndex >= 0) index = (currentIndex + 1) % count;
                else index = 0;
                cursor.Current = files[index];
                cursor.SelectedAt = now;
                cursor.Due = now.AddMinutes(options.IntervalMinutes);
            }
            if (now < cursor.SelectedAt) { cursor.SelectedAt = now; cursor.Due = now.AddMinutes(options.IntervalMinutes); }
            if (!settings.Collection.Rotate) { cursor.SelectedAt = now; cursor.Due = now.AddMinutes(options.IntervalMinutes); }
            if (settings.Collection.Rotate && count > 1 && (next == null || cursor.Due < next)) next = cursor.Due;
            return cursor.Current;
        }
        var desktop = Choose(false, options.DesktopFolder, single.Desktop, out var desktopCount);
        var locked = Choose(true, options.LockScreenFolder, single.LockScreen, out var lockCount);
        return new(new(desktop, locked), next, warnings.Count == 0 ? null : string.Join("\n", warnings), desktopCount, lockCount);
    }
}
