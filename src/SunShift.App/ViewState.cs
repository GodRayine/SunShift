// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SunShift.Core;

namespace SunShift.App;

internal sealed class ViewState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Settings Settings { get; set; } = new();
    public bool Automatic => Settings.Automatic;
    public string AutomationLabel => Automatic ? "Автоматически" : "На паузе";
    public string? DayDesktop => Settings.Day.Desktop;
    public string? DayLock => Settings.Day.LockScreen;
    public string? NightDesktop => Settings.Night.Desktop;
    public string? NightLock => Settings.Night.LockScreen;
    public bool EmptyDayDesktop => Images.Preview(DayDesktop) == null;
    public bool EmptyDayLock => Images.Preview(DayLock) == null;
    public bool EmptyNightDesktop => Images.Preview(NightDesktop) == null;
    public bool EmptyNightLock => Images.Preview(NightLock) == null;
    public bool CanApplyDay => !Settings.Day.IsEmpty && !busy;
    public bool CanApplyNight => !Settings.Night.IsEmpty && !busy;
    private bool busy, startup;
    private string status = "Выберите дневные и ночные изображения, затем определите местоположение.";
    public bool Busy { get => busy; set { busy = value; Notify(); Notify(nameof(Ready)); Notify(nameof(CanApplyDay)); Notify(nameof(CanApplyNight)); } }
    public bool Ready => !busy;
    public bool StartupEnabled { get => startup; set { startup = value; Notify(); } }
    public string Status { get => status; set { status = value; Notify(); } }
    public string PhaseLabel { get; private set; } = "Где сейчас солнце?";
    public string PhaseGlyph { get; private set; } = "\uE707";
    public string RegionLabel { get; private set; } = "Местоположение ещё не определено";
    public string CoordinatesLabel { get; private set; } = "Геопозиция Windows или координаты вручную";
    public string Sunrise { get; private set; } = "—";
    public string Sunset { get; private set; } = "—";
    public string Next { get; private set; } = "Определите место для расчёта";
    public string AstronomyNote { get; private set; } = "День начинается на рассвете, ночь — на закате.";

    public void SetSolar(SolarSnapshot snapshot, LocationFix fix, bool manual, DateTimeOffset now)
    {
        PhaseLabel = snapshot.Phase == SunPhase.Day ? "Сейчас день" : "Сейчас ночь";
        PhaseGlyph = snapshot.Phase == SunPhase.Day ? "\uE706" : "\uE708";
        var offset = snapshot.LocalNow.Offset;
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        RegionLabel = $"{snapshot.Region.IanaId} · {snapshot.LocalNow:dd MMM, HH:mm} · UTC{sign}{offset.Duration():hh\\:mm}";
        var age = Math.Max(0, (int)(now - fix.Timestamp).TotalMinutes);
        CoordinatesLabel = $"{fix.Point.Latitude:0.00000}, {fix.Point.Longitude:0.00000} · " +
            (manual ? "задано вручную" : $"±{fix.AccuracyMeters:0} м · {age} мин назад");
        Sunrise = Format(snapshot.Today.Sunrise, snapshot.Region.TimeZone);
        Sunset = Format(snapshot.Today.Sunset, snapshot.Region.TimeZone);
        Next = snapshot.Next == null ? "В ближайшие дни переключений нет" :
            $"{(snapshot.Next.PhaseAfter == SunPhase.Day ? "Дневной" : "Ночной")} фон · {TimeZoneInfo.ConvertTime(snapshot.Next.At, snapshot.Region.TimeZone):dd MMM, HH:mm}";
        AstronomyNote = snapshot.Today.Kind switch
        {
            SolarDayKind.ContinuousDay => "Солнце сегодня не заходит — полярный день.",
            SolarDayKind.ContinuousNight => "Солнце сегодня не восходит — полярная ночь.",
            _ => "Время показано для найденного региона. Расчёт не зависит от часового пояса Windows."
        };
        NotifySolar();
    }
    public void ClearSolar()
    {
        PhaseLabel = "Нужна геопозиция"; PhaseGlyph = "\uE707";
        RegionLabel = "Свежие координаты недоступны";
        CoordinatesLabel = "Определите место через Windows или укажите координаты вручную";
        Sunrise = "—"; Sunset = "—"; Next = "Местоположение требуется для расчёта";
        AstronomyNote = "Без достоверной позиции текущие фоны сохраняются.";
        NotifySolar();
    }
    private void NotifySolar()
    {
        foreach (var name in new[] { nameof(PhaseLabel), nameof(PhaseGlyph), nameof(RegionLabel), nameof(CoordinatesLabel),
            nameof(Sunrise), nameof(Sunset), nameof(Next), nameof(AstronomyNote) }) Notify(name);
    }
    private static string Format(DateTimeOffset? at, TimeZoneInfo zone) => at.HasValue ? TimeZoneInfo.ConvertTime(at.Value, zone).ToString("HH:mm") : "—";
    public void NotifyAll() => PropertyChanged?.Invoke(this, new(null));
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
