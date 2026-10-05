// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using SunShift.Core;
using Xunit;

namespace SunShift.Tests;

public sealed class SolarTests
{
    // Reference: US Naval Observatory API /api/rstt/oneday, queried 2026-10-05.
    [Theory]
    [InlineData(55.751244, 37.618423, 2026, 10, 5, 6, 40, 17, 55)]
    [InlineData(40.7667, -73.9, 2026, 6, 21, 5, 24, 20, 30)]
    [InlineData(-33.8688, 151.2093, 2026, 12, 21, 5, 41, 20, 5)]
    public void MatchesIndependentUsnoReference(double lat, double lon, int year, int month, int day, int riseHour, int riseMinute, int setHour, int setMinute)
    {
        var point = new Coordinates(lat, lon);
        var region = Region.Resolve(point);
        var date = new DateOnly(year, month, day);
        var solar = SolarCalculator.ForDate(point, date, region.TimeZone);
        Assert.NotNull(solar.Sunrise); Assert.NotNull(solar.Sunset);
        var rise = TimeZoneInfo.ConvertTime(solar.Sunrise!.Value, region.TimeZone);
        var set = TimeZoneInfo.ConvertTime(solar.Sunset!.Value, region.TimeZone);
        Assert.Equal(date, DateOnly.FromDateTime(rise.DateTime));
        Assert.Equal(date, DateOnly.FromDateTime(set.DateTime));
        Assert.InRange(Math.Abs((rise.TimeOfDay - new TimeSpan(riseHour, riseMinute, 0)).TotalMinutes), 0, 2);
        Assert.InRange(Math.Abs((set.TimeOfDay - new TimeSpan(setHour, setMinute, 0)).TotalMinutes), 0, 2);
    }
    [Theory]
    [InlineData(55.75, 37.61, "Europe/Moscow")]
    [InlineData(40.77, -73.9, "America/New_York")]
    [InlineData(-33.87, 151.21, "Australia/Sydney")]
    [InlineData(-36.85, 174.76, "Pacific/Auckland")]
    [InlineData(69.6492, 18.9553, "Europe/Oslo")]
    public void TimeZoneComesFromCoordinates(double lat, double lon, string expected) => Assert.Equal(expected, Region.Resolve(new(lat, lon)).IanaId);

    [Fact] public void SunriseAndSunsetSwitchAtTheirActualBoundaries()
    {
        var point = new Coordinates(55.75, 37.61);
        var day = SolarCalculator.ForDate(point, new(2026, 10, 5), Region.Resolve(point).TimeZone);
        var rise = day.Sunrise!.Value; var set = day.Sunset!.Value;
        Assert.Equal(SunPhase.Night, SolarCalculator.PhaseAt(point, rise.AddSeconds(-1)));
        Assert.Equal(SunPhase.Day, SolarCalculator.PhaseAt(point, rise));
        Assert.Equal(SunPhase.Day, SolarCalculator.PhaseAt(point, set.AddSeconds(-1)));
        Assert.Equal(SunPhase.Night, SolarCalculator.PhaseAt(point, set));
    }
    [Theory]
    [InlineData(6, SolarDayKind.ContinuousDay, SunPhase.Day)]
    [InlineData(12, SolarDayKind.ContinuousNight, SunPhase.Night)]
    public void PolarConditionsHaveNoFakeRiseOrSet(int month, SolarDayKind kind, SunPhase phase)
    {
        var point = new Coordinates(69.6492, 18.9553);
        var snapshot = SolarCalculator.Calculate(point, new(2026, month, 21, 10, 0, 0, TimeSpan.Zero));
        Assert.Equal(kind, snapshot.Today.Kind); Assert.Equal(phase, snapshot.Phase);
        Assert.Null(snapshot.Today.Sunrise); Assert.Null(snapshot.Today.Sunset); Assert.Null(snapshot.Next);
    }
    [Fact] public void StateIsIndependentOfSystemOrInputClockOffset()
    {
        var point = new Coordinates(-33.87, 151.21);
        var utc = new DateTimeOffset(2026, 12, 20, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(SolarCalculator.Calculate(point, utc).Phase, SolarCalculator.Calculate(point, utc.ToOffset(TimeSpan.FromHours(-10))).Phase);
        Assert.Equal(new DateOnly(2026, 12, 21), SolarCalculator.Calculate(point, utc).Today.Date);
    }
    [Theory]
    [InlineData(3, 8, -4)]
    [InlineData(11, 1, -5)]
    public void DaylightSavingTransitionDaysHaveOrderedEvents(int month, int day, int noonOffset)
    {
        var point = new Coordinates(40.7667, -73.9);
        var snapshot = SolarCalculator.Calculate(point, new(2026, month, day, 17, 0, 0, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromHours(noonOffset), snapshot.LocalNow.Offset);
        Assert.Equal(2, snapshot.Today.Events.Count);
        Assert.True(snapshot.Today.Sunrise < snapshot.Today.Sunset);
        Assert.Equal(new DateOnly(2026, month, day), snapshot.Today.Date);
    }
    [Fact] public void NewYorkSunsetCanFallOnFollowingUtcDate()
    {
        var point = new Coordinates(40.7667, -73.9);
        var day = SolarCalculator.ForDate(point, new(2026, 6, 21), Region.Resolve(point).TimeZone);
        Assert.Equal(22, day.Sunset!.Value.UtcDateTime.Day);
    }
    [Fact] public void NightScheduleFindsNextMorning()
    {
        var snapshot = SolarCalculator.Calculate(new(55.75, 37.61), new(2026, 10, 5, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(SunPhase.Night, snapshot.Phase);
        Assert.Equal(SunPhase.Day, snapshot.Next!.PhaseAfter);
        Assert.Equal(new DateOnly(2026, 10, 6), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.Next.At, snapshot.Region.TimeZone).DateTime));
    }
    [Fact] public void DateLineAndSouthernSummerUseRegionalDate()
    {
        var snapshot = SolarCalculator.Calculate(new(-36.85, 174.76), new(2026, 12, 20, 21, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 12, 21), snapshot.Today.Date);
        Assert.Equal(SunPhase.Day, snapshot.Phase);
        Assert.Equal(TimeSpan.FromHours(13), snapshot.LocalNow.Offset);
    }
    [Fact] public void PoleDoesNotProduceNaN()
    {
        Assert.True(double.IsFinite(SolarCalculator.Elevation(new(90, 0), new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero))));
        Assert.Equal(SunPhase.Night, SolarCalculator.PhaseAt(new(-90, 0), new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero)));
    }
    [Fact] public void InvalidCoordinatesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SolarCalculator.PhaseAt(new(double.NaN, 0), DateTimeOffset.UtcNow));
        Assert.False(new Coordinates(91, 0).IsValid); Assert.False(new Coordinates(0, 181).IsValid);
    }
    [Fact] public void CachedPositionExpiresAndFutureOrVeryCoarseFixesAreRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var fix = new LocationFix(new(55, 37), 1000, now.AddHours(-23));
        Assert.True(fix.IsUsable(now)); Assert.False((fix with { Timestamp = now.AddHours(-25) }).IsUsable(now));
        Assert.False((fix with { Timestamp = now.AddMinutes(2) }).IsUsable(now));
        Assert.False((fix with { AccuracyMeters = 100_000 }).IsUsable(now));
    }
}
