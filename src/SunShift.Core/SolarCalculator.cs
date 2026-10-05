// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
namespace SunShift.Core;

/// <summary>NOAA/Meeus solar geometry; calculations use UTC and the actual geographic point.</summary>
public static class SolarCalculator
{
    public const double HorizonDegrees = -.833;
    private const double Radians = Math.PI / 180;
    private static double Sin(double degrees) => Math.Sin(degrees * Radians);
    private static double Cos(double degrees) => Math.Cos(degrees * Radians);
    private static double Wrap(double value, double period) => (value % period + period) % period;

    public static double Elevation(Coordinates point, DateTimeOffset at)
    {
        if (!point.IsValid) throw new ArgumentOutOfRangeException(nameof(point));
        var utc = at.ToUniversalTime();
        var jd = 2440587.5 + (utc - DateTimeOffset.UnixEpoch).TotalDays;
        var t = (jd - 2451545) / 36525;
        var meanLongitude = Wrap(280.46646 + t * (36000.76983 + .0003032 * t), 360);
        var anomaly = 357.52911 + t * (35999.05029 - .0001537 * t);
        var eccentricity = .016708634 - t * (.000042037 + .0000001267 * t);
        var center = Sin(anomaly) * (1.914602 - t * (.004817 + .000014 * t)) +
            Sin(2 * anomaly) * (.019993 - .000101 * t) + Sin(3 * anomaly) * .000289;
        var omega = 125.04 - 1934.136 * t;
        var longitude = meanLongitude + center - .00569 - .00478 * Sin(omega);
        var obliquity = 23 + (26 + (21.448 - t * (46.815 + t * (.00059 - t * .001813))) / 60) / 60 + .00256 * Cos(omega);
        var declination = Math.Asin(Sin(obliquity) * Sin(longitude)) / Radians;
        var y = Math.Pow(Math.Tan(obliquity * Radians / 2), 2);
        var equationMinutes = 4 / Radians * (y * Sin(2 * meanLongitude) - 2 * eccentricity * Sin(anomaly) +
            4 * eccentricity * y * Sin(anomaly) * Cos(2 * meanLongitude) - .5 * y * y * Sin(4 * meanLongitude) -
            1.25 * eccentricity * eccentricity * Sin(2 * anomaly));
        var solarMinutes = Wrap(utc.TimeOfDay.TotalMinutes + equationMinutes + 4 * point.Longitude, 1440);
        var hourAngle = solarMinutes / 4 - 180;
        var cosineZenith = Sin(point.Latitude) * Sin(declination) + Cos(point.Latitude) * Cos(declination) * Cos(hourAngle);
        return 90 - Math.Acos(Math.Clamp(cosineZenith, -1, 1)) / Radians;
    }

    public static SunPhase PhaseAt(Coordinates point, DateTimeOffset at) => Elevation(point, at) >= HorizonDegrees ? SunPhase.Day : SunPhase.Night;

    public static SolarDay ForDate(Coordinates point, DateOnly date, TimeZoneInfo zone)
    {
        var start = StartOfDay(date, zone);
        var end = StartOfDay(date.AddDays(1), zone);
        var events = new List<SunEvent>();
        var previousTime = start;
        var previousPhase = PhaseAt(point, start);
        var firstPhase = previousPhase;
        // Search the geographic civil day, including 23/25-hour DST days and UTC date crossings.
        for (var sample = start.AddMinutes(1); previousTime < end; sample = sample.AddMinutes(1))
        {
            var time = sample < end ? sample : end;
            var phase = PhaseAt(point, time);
            if (phase != previousPhase)
            {
                var low = previousTime;
                var high = time;
                while ((high - low).TotalMilliseconds > 20)
                {
                    var mid = low.AddTicks((high - low).Ticks / 2);
                    if (PhaseAt(point, mid) == previousPhase) low = mid; else high = mid;
                }
                if (high < end) events.Add(new(high, phase));
            }
            previousTime = time;
            previousPhase = phase;
        }
        return new(date, events, events.Count > 0 ? SolarDayKind.Normal :
            firstPhase == SunPhase.Day ? SolarDayKind.ContinuousDay : SolarDayKind.ContinuousNight);
    }

    public static SolarSnapshot Calculate(Coordinates point, DateTimeOffset now, Region? region = null)
    {
        region ??= Region.Resolve(point);
        var local = TimeZoneInfo.ConvertTime(now, region.TimeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var today = ForDate(point, date, region.TimeZone);
        var next = today.Events.FirstOrDefault(e => e.At > now);
        if (next == null)
        {
            for (var day = 1; day <= 2 && next == null; day++)
                next = ForDate(point, date.AddDays(day), region.TimeZone).Events.FirstOrDefault(e => e.At > now);
        }
        return new(PhaseAt(point, now), today, next, region, local);
    }

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some regions advance their clocks at midnight.
        for (var i = 0; zone.IsInvalidTime(local) && i < 1440; i++) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
