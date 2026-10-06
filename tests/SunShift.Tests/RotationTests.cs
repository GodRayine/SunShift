// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using SunShift.Core;
using Xunit;

namespace SunShift.Tests;

public sealed class RotationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
    private sealed class Catalog : IWallpaperCatalog
    {
        public readonly Dictionary<string, WallpaperCatalogResult> Folders = new()
        { ["day"] = new(["a.png", "b.png", "c.png"]), ["night"] = new(["n1.png", "n2.png"]) };
        public WallpaperCatalogResult Read(string folder) => Folders.GetValueOrDefault(folder) ?? new([], "Папка недоступна.");
    }
    private static Settings Settings() => new()
    {
        Day = new("single.png", "lock.png"), Night = new("night.png"),
        DayRotation = new("day", IntervalMinutes: 5), NightRotation = new("night", IntervalMinutes: 1),
        Collection = new() { Rotate = true }
    };
    [Fact] public void AdvancesAtDeadlineAndWrapsWithoutRepeatedChecksAdvancing()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        var first = rotation.Select(settings, SunPhase.Day, Start);
        Assert.Equal(new("a.png", "lock.png"), first.Images); Assert.Equal(Start.AddMinutes(5), first.NextChange);
        Assert.Equal("a.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5).AddTicks(-1)).Images.Desktop);
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5)).Images.Desktop);
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5)).Images.Desktop);
        Assert.Equal("c.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(10)).Images.Desktop);
        Assert.Equal("a.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(15)).Images.Desktop);
    }
    [Fact] public void SolarPhaseSwitchUsesOtherPoolImmediately()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start);
        Assert.Equal("n1.png", rotation.Select(settings, SunPhase.Night, Start.AddSeconds(1)).Images.Desktop);
        Assert.Equal("n2.png", rotation.Select(settings, SunPhase.Night, Start.AddMinutes(1).AddSeconds(1)).Images.Desktop);
        Assert.Equal("a.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(2)).Images.Desktop);
    }
    [Fact] public void SleepAdvancesOnceAndStartsFreshDeadline()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start);
        var woke = rotation.Select(settings, SunPhase.Day, Start.AddHours(10));
        Assert.Equal("b.png", woke.Images.Desktop); Assert.Equal(Start.AddHours(10).AddMinutes(5), woke.NextChange);
    }
    [Fact] public void BackwardClockJumpRecoversSchedule()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start);
        var moved = rotation.Select(settings, SunPhase.Day, Start.AddHours(-1));
        Assert.Equal(Start.AddHours(-1).AddMinutes(5), moved.NextChange);
    }
    [Fact] public void RandomDoesNotRepeatPreviousAndCoversAllFiles()
    {
        var settings = Settings(); settings.DayRotation = settings.DayRotation with { Order = RotationOrder.Random };
        var rotation = new WallpaperRotation(new Catalog(), new Random(42));
        string? previous = null; var seen = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var current = rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5 * i)).Images.Desktop;
            Assert.NotEqual(previous, current); seen.Add(current!); previous = current;
        }
        Assert.Equal(3, seen.Count);
    }
    [Fact] public void SingleFileHasNoRedundantNextChange()
    {
        var catalog = new Catalog(); catalog.Folders["day"] = new(["a.png"]);
        var rotation = new WallpaperRotation(catalog);
        Assert.Null(rotation.Select(Settings(), SunPhase.Day, Start).NextChange);
        Assert.Equal("a.png", rotation.Select(Settings(), SunPhase.Day, Start.AddDays(1)).Images.Desktop);
    }
    [Fact] public void EmptyOrUnavailableFolderPreservesThatScreenAndRecovers()
    {
        var catalog = new Catalog(); catalog.Folders["day"] = new([], "Папка недоступна.");
        var rotation = new WallpaperRotation(catalog);
        var missing = rotation.Select(Settings(), SunPhase.Day, Start);
        Assert.Null(missing.Images.Desktop); Assert.Equal("lock.png", missing.Images.LockScreen); Assert.NotNull(missing.Warning);
        catalog.Folders["day"] = new(["new.png"]);
        Assert.Equal("new.png", rotation.Select(Settings(), SunPhase.Day, Start.AddSeconds(30)).Images.Desktop);
    }
    [Fact] public void DeletedCurrentAndNewFilesAreDetectedOnCheck()
    {
        var catalog = new Catalog(); var rotation = new WallpaperRotation(catalog); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start);
        catalog.Folders["day"] = new(["b.png", "new.png"]);
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start.AddSeconds(30)).Images.Desktop);
        Assert.Equal("new.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5).AddSeconds(30)).Images.Desktop);
    }
    [Fact] public void RejectedFileIsSkippedAndRetriedLater()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start); rotation.Reject(SunPhase.Day, false, "a.png", Start);
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start).Images.Desktop);
        rotation.Reject(SunPhase.Day, false, "b.png", Start); rotation.Reject(SunPhase.Day, false, "c.png", Start);
        Assert.Null(rotation.Select(settings, SunPhase.Day, Start).Images.Desktop);
        Assert.Equal("c.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5)).Images.Desktop);
    }
    [Fact] public void SettingsChangeAndResumeResetSchedule()
    {
        var rotation = new WallpaperRotation(new Catalog()); var settings = Settings();
        rotation.Select(settings, SunPhase.Day, Start);
        settings.DayRotation = settings.DayRotation with { IntervalMinutes = 15 };
        Assert.Equal(Start.AddMinutes(16), rotation.Select(settings, SunPhase.Day, Start.AddMinutes(1)).NextChange);
        rotation.Select(settings, SunPhase.Day, Start.AddMinutes(16)); rotation.Reset();
        Assert.Equal("a.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(17)).Images.Desktop);
    }
    [Fact] public void CatalogSortsSupportedFilesWithoutSubfolders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        try
        {
            foreach (var name in new[] { "z.JPG", "A.png", "b.bmp", "ignored.txt", "nested/hidden.png" }) File.WriteAllText(Path.Combine(folder, name), "test");
            var catalog = new FileWallpaperCatalog(); var result = catalog.Read(folder);
            Assert.Equal(new[] { "A.png", "b.bmp", "z.JPG" }, result.Files.Select(Path.GetFileName)); Assert.Null(result.Error);
            Assert.NotNull(catalog.Read(Path.Combine(folder, "missing")).Error);
            Assert.NotNull(catalog.Read("relative").Error);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void RotationSettingsPersistAndInvalidValuesRecoverWithoutLosingProfiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-rotation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder); var settings = Settings(); settings.Automatic = true;
            store.Write(settings); Assert.Equal(settings.DayRotation, store.Read().DayRotation);
            settings.DayRotation = new("day", IntervalMinutes: 0, Order: (RotationOrder)99); store.Write(settings);
            var loaded = store.Read(); Assert.Equal(30, loaded.DayRotation.IntervalMinutes); Assert.Equal(RotationOrder.Sequential, loaded.DayRotation.Order);
            Assert.Equal("single.png", loaded.Day.Desktop); Assert.True(loaded.Automatic);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public async Task ChangedPreparedFileIsAppliedEvenWhenPathMatchesPreviousSuccess()
    {
        var path = Path.Combine(Path.GetTempPath(), "SunShift-version-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllText(path, "first"); var sink = new Sink(); var coordinator = new WallpaperCoordinator(sink);
            await coordinator.ApplyAsync(new(path), CancellationToken.None);
            await coordinator.ApplyAsync(new(path), CancellationToken.None); Assert.Equal(1, sink.Count);
            File.WriteAllText(path, "changed content");
            await coordinator.ApplyAsync(new(path), CancellationToken.None); Assert.Equal(2, sink.Count);
        }
        finally { File.Delete(path); }
    }
    private sealed class Sink : IWallpaperSink
    {
        public int Count;
        public Task<ChangeResult> ChangeAsync(ImagePair pair, CancellationToken token) { Count++; return Task.FromResult(new ChangeResult(true, true)); }
    }
}
