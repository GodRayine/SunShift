// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using SunShift.Core;
using Xunit;

namespace SunShift.Tests;

public sealed class LinkedPairTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
    private static Settings Settings(bool rotate = true) => new()
    {
        Collection = new() { Mode = CollectionMode.LinkedPairs, Rotate = rotate, IntervalMinutes = 5,
            Pairs = [new("one", "Горы", new("day-one.png", "day-lock-one.png"), new("night-one.png", "night-lock-one.png")),
                     new("two", "Море", new("day-two.png"), new("night-two.png"))] }
    };
    [Fact] public void SunsetUsesNightPartnerWithoutChangingSelectedSceneOrDeadline()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        Assert.Equal("day-one.png", rotation.Select(settings, SunPhase.Day, Start).Images.Desktop);
        var night = rotation.Select(settings, SunPhase.Night, Start.AddMinutes(1));
        Assert.Equal(new("night-one.png", "night-lock-one.png"), night.Images);
        Assert.Equal("one", night.PairId); Assert.Equal(Start.AddMinutes(5), night.NextChange);
    }
    [Fact] public void TimerAdvancesWholeSceneAndWraps()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        rotation.Select(settings, SunPhase.Day, Start);
        Assert.Equal("night-two.png", rotation.Select(settings, SunPhase.Night, Start.AddMinutes(5)).Images.Desktop);
        Assert.Equal("day-two.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(6)).Images.Desktop);
        Assert.Equal("one", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(10)).PairId);
    }
    [Fact] public void TurningOffExtraRotationKeepsSceneWhileSolarPhaseStillChanges()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        rotation.Select(settings, SunPhase.Day, Start);
        settings.Collection = settings.Collection with { Rotate = false };
        var held = rotation.Select(settings, SunPhase.Night, Start.AddHours(10));
        Assert.Equal("one", held.PairId); Assert.Equal("night-one.png", held.Images.Desktop); Assert.Null(held.NextChange);
        settings.Collection = settings.Collection with { Rotate = true };
        var resumed = rotation.Select(settings, SunPhase.Night, Start.AddHours(15));
        Assert.Equal("one", resumed.PairId); Assert.Equal(Start.AddHours(15).AddMinutes(5), resumed.NextChange);
    }
    [Fact] public void SavedSceneSurvivesRestartAndAcknowledgingItDoesNotResetTimer()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        rotation.Select(settings, SunPhase.Day, Start);
        var changed = rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5)); settings.ActivePairId = changed.PairId;
        Assert.Equal(Start.AddMinutes(10), rotation.Select(settings, SunPhase.Night, Start.AddMinutes(6)).NextChange);
        var restarted = new LinkedPairRotation().Select(settings, SunPhase.Night, Start.AddHours(2));
        Assert.Equal("night-two.png", restarted.Images.Desktop);
    }
    [Fact] public void ExplicitChoiceAndRemovedPairRecoverCorrectly()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        rotation.Select(settings, SunPhase.Day, Start); settings.ActivePairId = "two";
        Assert.Equal("two", rotation.Select(settings, SunPhase.Day, Start.AddSeconds(1)).PairId);
        settings.Collection = settings.Collection with { Pairs = [settings.Collection.Pairs[0]] };
        var recovered = rotation.Select(settings, SunPhase.Night, Start.AddMinutes(1));
        Assert.Equal("one", recovered.PairId); Assert.Null(recovered.NextChange);
    }
    [Fact] public void RandomScenesNeverMixPartnersOrRepeatPrevious()
    {
        var settings = Settings(); settings.Collection = settings.Collection with { Order = RotationOrder.Random };
        var rotation = new LinkedPairRotation(new Random(7)); string? previous = null;
        for (var i = 0; i < 20; i++)
        {
            var day = rotation.Select(settings, SunPhase.Day, Start.AddMinutes(i * 5));
            var night = rotation.Select(settings, SunPhase.Night, Start.AddMinutes(i * 5).AddSeconds(1));
            Assert.NotEqual(previous, day.PairId); Assert.Equal(day.PairId, night.PairId);
            Assert.Equal("day-" + day.PairId + ".png", day.Images.Desktop);
            Assert.Equal("night-" + day.PairId + ".png", night.Images.Desktop); previous = day.PairId;
        }
    }
    [Fact] public void SleepAndBackwardClockDoNotBurstOrDetachPartners()
    {
        var settings = Settings(); var rotation = new LinkedPairRotation();
        rotation.Select(settings, SunPhase.Day, Start);
        var woke = rotation.Select(settings, SunPhase.Night, Start.AddHours(12));
        Assert.Equal("two", woke.PairId); Assert.Equal(Start.AddHours(12).AddMinutes(5), woke.NextChange);
        var backward = rotation.Select(settings, SunPhase.Day, Start.AddHours(-1));
        Assert.Equal("two", backward.PairId); Assert.Equal(Start.AddHours(-1).AddMinutes(5), backward.NextChange);
    }
    [Fact] public void EmptyCollectionPreservesBothScreens()
    {
        var settings = Settings(); settings.Collection = settings.Collection with { Pairs = [] };
        var selection = new WallpaperRotation(new FileWallpaperCatalog()).Select(settings, SunPhase.Day, Start);
        Assert.True(selection.Images.IsEmpty); Assert.NotNull(selection.Warning); Assert.False(settings.HasSources(SunPhase.Day));
    }
    [Fact] public void DisabledFolderTimerHoldsCurrentImageAndResumeStartsFreshInterval()
    {
        var settings = new Settings { DayRotation = new("folder", IntervalMinutes: 5), Collection = new() { Rotate = true } };
        var rotation = new WallpaperRotation(new Catalog());
        rotation.Select(settings, SunPhase.Day, Start);
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start.AddMinutes(5)).Images.Desktop);
        settings.Collection = settings.Collection with { Rotate = false };
        Assert.Equal("b.png", rotation.Select(settings, SunPhase.Day, Start.AddHours(1)).Images.Desktop);
        settings.Collection = settings.Collection with { Rotate = true };
        var resumed = rotation.Select(settings, SunPhase.Day, Start.AddHours(5));
        Assert.Equal("b.png", resumed.Images.Desktop); Assert.Equal(Start.AddHours(5).AddMinutes(5), resumed.NextChange);
    }
    [Fact] public void SettingsRoundTripAndLegacyFolderMigrationPreserveSources()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SunShift-linked-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder); var settings = Settings(false); settings.ActivePairId = "two"; store.Write(settings);
            var loaded = store.Read(); Assert.Equal("two", loaded.ActivePairId); Assert.Equal("night-two.png", loaded.ActivePair!.Night.Desktop);
            Assert.False(loaded.Collection.Rotate);
            Assert.DoesNotContain("\"ActivePair\":", File.ReadAllText(Path.Combine(folder, "settings.json")));
            File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"Version\":1,\"Day\":{\"Desktop\":\"single.png\"},\"DayRotation\":{\"DesktopFolder\":\"folder\",\"IntervalMinutes\":7}}");
            loaded = store.Read(); Assert.True(loaded.Collection.Rotate); Assert.Equal(CollectionMode.Folders, loaded.Collection.Mode);
            Assert.Equal(7, loaded.DayRotation.IntervalMinutes); Assert.Equal("single.png", loaded.Day.Desktop);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class Catalog : IWallpaperCatalog
    { public WallpaperCatalogResult Read(string folder) => new(["a.png", "b.png", "c.png"]); }
}
