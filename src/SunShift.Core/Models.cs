// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System.Text.Json.Serialization;

namespace SunShift.Core;

public enum SunPhase { Day, Night }
public enum ThemePreference { System, Light, Dark }
public enum RotationOrder { Sequential, Random }
public enum CollectionMode { Folders, LinkedPairs }
public sealed record WallpaperPair(string Id, string Name, ImagePair Day, ImagePair Night)
{
    public ImagePair For(SunPhase phase) => phase == SunPhase.Day ? Day : Night;
}
public sealed record CollectionSettings
{
    public CollectionMode Mode { get; init; }
    public bool Rotate { get; init; }
    public int IntervalMinutes { get; init; } = 30;
    public RotationOrder Order { get; init; }
    public WallpaperPair[] Pairs { get; init; } = [];
    public CollectionSettings Normalize() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : CollectionMode.Folders,
        IntervalMinutes = IntervalMinutes is >= 1 and <= 1440 ? IntervalMinutes : 30,
        Order = Enum.IsDefined(Order) ? Order : RotationOrder.Sequential,
        Pairs = (Pairs ?? []).Where(pair => pair is { Day: not null, Night: not null } && !string.IsNullOrWhiteSpace(pair.Id))
            .DistinctBy(pair => pair.Id).Take(500).Select(pair => pair with { Name = string.IsNullOrWhiteSpace(pair.Name) ? "Без названия" : pair.Name }).ToArray()
    };
}
public sealed record RotationSettings(string? DesktopFolder = null, string? LockScreenFolder = null,
    int IntervalMinutes = 30, RotationOrder Order = RotationOrder.Sequential)
{
    [JsonIgnore] public bool Enabled => !string.IsNullOrWhiteSpace(DesktopFolder) || !string.IsNullOrWhiteSpace(LockScreenFolder);
    public RotationSettings Normalize() => this with
    {
        DesktopFolder = string.IsNullOrWhiteSpace(DesktopFolder) ? null : DesktopFolder,
        LockScreenFolder = string.IsNullOrWhiteSpace(LockScreenFolder) ? null : LockScreenFolder,
        IntervalMinutes = IntervalMinutes is >= 1 and <= 1440 ? IntervalMinutes : 30,
        Order = Enum.IsDefined(Order) ? Order : RotationOrder.Sequential
    };
}
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
    public ThemePreference Theme { get; set; }
    public ImagePair Day { get; set; } = new();
    public ImagePair Night { get; set; } = new();
    public RotationSettings DayRotation { get; set; } = new();
    public RotationSettings NightRotation { get; set; } = new();
    public CollectionSettings Collection { get; set; } = new();
    public string? ActivePairId { get; set; }
    public bool ManualLocation { get; set; }
    public bool LocationDisabled { get; set; }
    // Location belongs to the current session only, including manually entered values.
    [JsonIgnore] public Coordinates? ManualPoint { get; set; }
    [JsonIgnore] public LocationFix? LastLocation { get; set; }
    public ImagePair For(SunPhase phase) => phase == SunPhase.Day ? Day : Night;
    public RotationSettings RotationFor(SunPhase phase) => phase == SunPhase.Day ? DayRotation : NightRotation;
    [JsonIgnore] public WallpaperPair? ActivePair => Collection.Pairs.FirstOrDefault(pair => pair.Id == ActivePairId) ?? Collection.Pairs.FirstOrDefault();
    public bool HasSources(SunPhase phase) => Collection.Mode == CollectionMode.LinkedPairs
        ? Collection.Pairs.Any(pair => !pair.For(phase).IsEmpty) : !For(phase).IsEmpty || RotationFor(phase).Enabled;
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
