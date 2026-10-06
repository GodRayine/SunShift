// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using SunShift.Core;
using Xunit;

namespace SunShift.Tests;

public sealed class PersistenceAndWallpaperTests
{
    [Fact] public async Task DayNightDayOnlyWritesChangedScreens()
    {
        var sink = new FakeSink(); var coordinator = new WallpaperCoordinator(sink);
        var day = new ImagePair("day.png", "day-lock.png"); var night = new ImagePair("night.png", "night-lock.png");
        await coordinator.ApplyAsync(day, CancellationToken.None);
        await coordinator.ApplyAsync(day, CancellationToken.None);
        await coordinator.ApplyAsync(night, CancellationToken.None);
        await coordinator.ApplyAsync(day, CancellationToken.None);
        Assert.Equal(new[] { day, night, day }, sink.Calls);
    }
    [Fact] public async Task LockFailureDoesNotRepeatSuccessfulDesktop()
    {
        var sink = new FakeSink { Result = new(true, false, "Denied") }; var coordinator = new WallpaperCoordinator(sink);
        var pair = new ImagePair("desktop.png", "lock.png");
        await coordinator.ApplyAsync(pair, CancellationToken.None); sink.Result = new(true, true);
        await coordinator.ApplyAsync(pair, CancellationToken.None);
        Assert.Null(sink.Calls[1].Desktop); Assert.Equal("lock.png", sink.Calls[1].LockScreen);
    }
    [Fact] public async Task ManualApplyBypassesCacheAndLeavesFailedScreenRetryable()
    {
        var sink = new FakeSink(); var coordinator = new WallpaperCoordinator(sink); var pair = new ImagePair("day.png", "lock.png");
        await coordinator.ApplyAsync(pair, CancellationToken.None); sink.Result = new(false, true, "Failed");
        await coordinator.ApplyAsync(pair, CancellationToken.None, true); Assert.Equal(pair, sink.Calls[1]);
        sink.Result = new(true, true); await coordinator.ApplyAsync(pair, CancellationToken.None);
        Assert.Equal(new ImagePair("day.png"), sink.Calls[2]);
    }
    [Fact] public async Task ConcurrentChecksDoNotApplyTwice()
    {
        var sink = new FakeSink(); var coordinator = new WallpaperCoordinator(sink);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => coordinator.ApplyAsync(new("day.png"), CancellationToken.None)));
        Assert.Single(sink.Calls);
    }
    [Fact] public async Task PauseCancellationPreventsChange()
    {
        var sink = new FakeSink(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WallpaperCoordinator(sink).ApplyAsync(new("day.png"), cancellation.Token));
        Assert.Empty(sink.Calls);
    }
    [Fact] public async Task EmptyProfilePreservesBothScreens()
    {
        var sink = new FakeSink(); await new WallpaperCoordinator(sink).ApplyAsync(new(), CancellationToken.None); Assert.Empty(sink.Calls);
    }
    [Fact] public void SettingsRoundTripAndRecoveryDisableAutomaticMode()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder); var point = new Coordinates(55.75, 37.61);
            store.Write(new() { Automatic = true, ManualLocation = true, ManualPoint = point, Day = new("day.png"), Night = new("night.png") });
            var loaded = store.Read(); Assert.True(loaded.Automatic); Assert.Equal(point, loaded.ManualPoint);
            Assert.Equal("night.png", loaded.Night.Desktop); Assert.False(File.Exists(Path.Combine(folder, "settings.json.tmp")));
            File.WriteAllText(Path.Combine(folder, "settings.json"), "invalid json");
            Assert.False(store.Read().Automatic); Assert.NotNull(store.Warning);
            Assert.Equal("invalid json", File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.recovery-*"))));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact] public void LegacySettingsKeepProfilesAndDefaultToWindowsTheme()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"Version\":1,\"Automatic\":true,\"Day\":{\"Desktop\":\"day.png\"},\"Night\":{\"LockScreen\":\"night-lock.png\"}}");
            var settings = new SettingsStore(folder).Read();
            Assert.True(settings.Automatic); Assert.Equal(ThemePreference.System, settings.Theme);
            Assert.Equal("day.png", settings.Day.Desktop); Assert.Equal("night-lock.png", settings.Night.LockScreen);
            Assert.False(settings.DayRotation.Enabled); Assert.False(settings.NightRotation.Enabled);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public void ThemeRoundTripAndUnknownThemePreserveOtherSettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder);
            store.Write(new() { Theme = ThemePreference.Dark, Automatic = true, Day = new("day.png") });
            Assert.Equal(ThemePreference.Dark, store.Read().Theme);
            store.Write(new() { Theme = (ThemePreference)999, Automatic = true, Day = new("day.png") });
            var loaded = store.Read(); Assert.Equal(ThemePreference.System, loaded.Theme);
            Assert.True(loaded.Automatic); Assert.Equal("day.png", loaded.Day.Desktop); Assert.Null(store.Warning);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class FakeSink : IWallpaperSink
    {
        public List<ImagePair> Calls { get; } = [];
        public ChangeResult Result { get; set; } = new(true, true);
        public async Task<ChangeResult> ChangeAsync(ImagePair pair, CancellationToken token) { await Task.Delay(5, token); Calls.Add(pair); return Result; }
    }
}
