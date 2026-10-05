// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System.Text.Json.Serialization;

namespace SunShift.Core;

public enum SunPhase { Day, Night }
public enum SolarDayKind { Normal, ContinuousDay, ContinuousNight }
public sealed record Coordinates(double Latitude, double Longitude)
{
    [JsonIgnore] public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude) && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
public sealed record LocationFix(Coordinates Point, double AccuracyMeters, DateTimeOffset Timestamp)
{
    public bool IsUsable(DateTimeOffset now) => Point is { IsValid: true } && double.IsFinite(AccuracyMeters) &&
        AccuracyMeters is >= 0 and <= 50_000 && now - Timestamp <= TimeSpan.FromHours(24) && Timestamp - now <= TimeSpan.FromMinutes(1);
}
public sealed record ImagePair(string? Desktop = null, string? LockScreen = null)
{
    [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Desktop) && string.IsNullOrWhiteSpace(LockScreen);
}
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public bool Automatic { get; set; }
    public ImagePair Day { get; set; } = new();
    public ImagePair Night { get; set; } = new();
    public bool ManualLocation { get; set; }
    public Coordinates? ManualPoint { get; set; }
    public LocationFix? LastLocation { get; set; }
    public ImagePair For(SunPhase phase) => phase == SunPhase.Day ? Day : Night;
}
public sealed record Region(string IanaId, TimeZoneInfo TimeZone)
{
    public static Region Resolve(Coordinates point)
    {
        if (!point.IsValid) throw new ArgumentOutOfRangeException(nameof(point));
        var id = GeoTimeZone.TimeZoneLookup.GetTimeZone(point.Latitude, point.Longitude).Result;
        return new(id, TimeZoneConverter.TZConvert.GetTimeZoneInfo(id));
    }
}
public sealed record SunEvent(DateTimeOffset At, SunPhase PhaseAfter);
public sealed record SolarDay(DateOnly Date, IReadOnlyList<SunEvent> Events, SolarDayKind Kind)
{
    public DateTimeOffset? Sunrise => Events.FirstOrDefault(e => e.PhaseAfter == SunPhase.Day)?.At;
    public DateTimeOffset? Sunset => Events.FirstOrDefault(e => e.PhaseAfter == SunPhase.Night)?.At;
}
public sealed record SolarSnapshot(SunPhase Phase, SolarDay Today, SunEvent? Next, Region Region, DateTimeOffset LocalNow);
