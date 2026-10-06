// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SunShift.Core;

namespace SunShift.App;

internal sealed class ViewState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Settings Settings { get; set; } = new();
    public bool Automatic => Settings.Automatic;
    public string AutomationLabel => Automatic ? "Включена" : "На паузе";
    public bool RotateCollection => Settings.Collection.Rotate;
    public bool SingleSourceMode => Settings.Collection.Mode != CollectionMode.LinkedPairs;
    public bool CanEditSingle => Ready && SingleSourceMode;
    public string CollectionSummary => Settings.Collection.Mode == CollectionMode.LinkedPairs ?
        $"Связанные пары · {Settings.Collection.Pairs.Length}\nТекущая: {Settings.ActivePair?.Name ?? "не выбрана"}\n" + (RotateCollection ? $"Интервал: {IntervalOptions.Format(Settings.Collection.IntervalMinutes)}" : "Одна пара; день и ночь переключаются по солнцу") :
        "Папки для дня и ночи\n" + (RotateCollection ? "Смена по интервалам профилей" : "По одному кадру из папки; переходы по солнцу сохраняются");
    public int ThemeIndex => (int)Settings.Theme;
    private bool selectedNight, selectedByUser, solarAvailable;
    public bool SelectedNight { get => selectedNight; set { selectedByUser = true; if (selectedNight == value) return; selectedNight = value; NotifyAll(); } }
    public string RefreshLabel => Settings.ManualLocation ? "Обновить расчёт" : "Обновить геопозицию";
    public bool SelectedDay { get => !selectedNight; set { if (value) SelectedNight = false; } }
    public string ProfileTitle => selectedNight ? "Ночной профиль" : "Дневной профиль";
    public string ProfileSubtitle => selectedNight ? "После заката и до следующего рассвета" : "От рассвета до заката";
    private readonly Dictionary<SunPhase, RotationSelection> selections = new();
    private SunPhase SelectedPhase => selectedNight ? SunPhase.Night : SunPhase.Day;
    public void SetRotation(SunPhase phase, RotationSelection selection) { selections[phase] = selection; NotifyAll(); }
    public string? DesktopImage => !SingleSourceMode ? Settings.ActivePair?.For(SelectedPhase).Desktop : Settings.RotationFor(SelectedPhase).DesktopFolder == null ? Settings.For(SelectedPhase).Desktop : selections.GetValueOrDefault(SelectedPhase)?.Images.Desktop;
    public string? LockImage => !SingleSourceMode ? Settings.ActivePair?.For(SelectedPhase).LockScreen : Settings.RotationFor(SelectedPhase).LockScreenFolder == null ? Settings.For(SelectedPhase).LockScreen : selections.GetValueOrDefault(SelectedPhase)?.Images.LockScreen;
    public bool EmptyDesktop => Images.Preview(DesktopImage) == null;
    public bool EmptyLock => Images.Preview(LockImage) == null;
    public string DesktopSourceLabel => !SingleSourceMode ? "Из пары «" + Settings.ActivePair?.Name + "»" : Settings.RotationFor(SelectedPhase).DesktopFolder is { } path ? $"Папка · {selections.GetValueOrDefault(SelectedPhase)?.DesktopCount ?? 0} изображений\n{path}" : "Одно изображение";
    public string LockSourceLabel => !SingleSourceMode ? (LockImage == null ? "Текущий фон сохраняется" : "Из той же пары") : Settings.RotationFor(SelectedPhase).LockScreenFolder is { } path ? $"Папка · {selections.GetValueOrDefault(SelectedPhase)?.LockScreenCount ?? 0} изображений\n{path}" : "Одно изображение";
    public string RotationSummary => !SingleSourceMode ? "Пара «" + Settings.ActivePair?.Name + "» · настройка в коллекции обоев" : Settings.RotationFor(SelectedPhase) is { Enabled: true } options ?
        (RotateCollection ? $"Интервал: {IntervalOptions.Format(options.IntervalMinutes)} · {(options.Order == RotationOrder.Random ? "случайный порядок" : "по имени файла")}" : "Пул выбран · смена по интервалу выключена") : "Один фон на весь день или ночь";
    public string DesktopTarget => selectedNight ? "NightDesktop" : "DayDesktop";
    public string LockTarget => selectedNight ? "NightLock" : "DayLock";
    public string SelectedPhaseTag => selectedNight ? "Night" : "Day";
    public bool CanApplySelected => selectedNight ? CanApplyNight : CanApplyDay;
    public bool ShowLockClock => !EmptyLock && solarAvailable;
    public bool Preview { get; set; }
    public string FooterHint => Preview ? "Предпросмотр · системные фоны, геолокация и автозапуск отключены" :
        "Ручное применение ставит автоматику на паузу. Пустое изображение сохраняет текущий фон экрана.";
    public string? DayDesktop => Settings.Day.Desktop;
    public string? DayLock => Settings.Day.LockScreen;
    public string? NightDesktop => Settings.Night.Desktop;
    public string? NightLock => Settings.Night.LockScreen;
    public bool EmptyDayDesktop => Images.Preview(DayDesktop) == null;
    public bool EmptyDayLock => Images.Preview(DayLock) == null;
    public bool EmptyNightDesktop => Images.Preview(NightDesktop) == null;
    public bool EmptyNightLock => Images.Preview(NightLock) == null;
    public bool CanApplyDay => Settings.HasSources(SunPhase.Day) && !busy;
    public bool CanApplyNight => Settings.HasSources(SunPhase.Night) && !busy;
    private bool busy, startup;
    private string status = "Выберите дневные и ночные изображения, затем определите местоположение.";
    public bool Busy { get => busy; set { busy = value; NotifyAll(); } }
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
    public string LocalClock { get; private set; } = "—";
    public string LocalDate { get; private set; } = "Местное время региона";
    private SolarSnapshot? solarSnapshot;
    private string solarNextLabel = "Следующее переключение", solarNextTime = "—";
    private DateTimeOffset? RotationDue => Automatic && RotateCollection && solarSnapshot is { } snapshot &&
        selections.GetValueOrDefault(snapshot.Phase)?.NextChange is { } due && due > snapshot.LocalNow &&
        (snapshot.Next == null || due < snapshot.Next.At) ? due : null;
    public string NextLabel => RotationDue != null ? "Следующий кадр" : solarNextLabel;
    public string NextTime => RotationDue is { } due && solarSnapshot is { } snapshot ? TimeZoneInfo.ConvertTime(due, snapshot.Region.TimeZone).ToString("HH:mm") : solarNextTime;
    public string DesktopError { get; set; } = "";
    public string LockError { get; set; } = "";
    public string LocationSource => Settings.ManualLocation ? "Координаты вручную" : "Геолокация Windows";

    public void SetSolar(SolarSnapshot snapshot, LocationFix fix, bool manual, DateTimeOffset now)
    {
        solarSnapshot = snapshot;
        PhaseLabel = snapshot.Phase == SunPhase.Day ? "Сейчас день" : "Сейчас ночь";
        PhaseGlyph = snapshot.Phase == SunPhase.Day ? "\uE706" : "\uE708";
        solarAvailable = true;
        if (!selectedByUser) selectedNight = snapshot.Phase == SunPhase.Night;
        LocalClock = snapshot.LocalNow.ToString("HH:mm");
        LocalDate = snapshot.LocalNow.ToString("dd MMMM yyyy");
        var offset = snapshot.LocalNow.Offset;
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        RegionLabel = $"{snapshot.Region.IanaId} · UTC{sign}{offset.Duration():hh\\:mm}";
        var age = Math.Max(0, (int)(now - fix.Timestamp).TotalMinutes);
        CoordinatesLabel = $"{fix.Point.Latitude:0.00000}, {fix.Point.Longitude:0.00000} · " +
            (manual ? "задано вручную" : $"±{fix.AccuracyMeters:0} м · {age} мин назад");
        Sunrise = Format(snapshot.Today.Sunrise, snapshot.Region.TimeZone);
        Sunset = Format(snapshot.Today.Sunset, snapshot.Region.TimeZone);
        Next = snapshot.Next == null ? "В ближайшие дни переключений нет" :
            $"{(snapshot.Next.PhaseAfter == SunPhase.Day ? "Дневной" : "Ночной")} фон · {TimeZoneInfo.ConvertTime(snapshot.Next.At, snapshot.Region.TimeZone):dd MMM, HH:mm}";
        var nextLocal = snapshot.Next == null ? (DateTimeOffset?)null : TimeZoneInfo.ConvertTime(snapshot.Next.At, snapshot.Region.TimeZone);
        solarNextLabel = snapshot.Next == null ? "Переключений пока нет" : (snapshot.Next.PhaseAfter == SunPhase.Day ? "Дневной профиль" : "Ночной профиль") +
            (nextLocal!.Value.Date != snapshot.LocalNow.Date ? $" · {nextLocal:dd MMM}" : "");
        solarNextTime = nextLocal?.ToString("HH:mm") ?? "—";
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
        solarAvailable = false; LocalClock = "—"; LocalDate = "Местное время региона";
        solarSnapshot = null; solarNextLabel = "Следующее переключение"; solarNextTime = "—";
        NotifySolar();
    }
    private void NotifySolar()
    {
        NotifyAll();
    }
    private static string Format(DateTimeOffset? at, TimeZoneInfo zone) => at.HasValue ? TimeZoneInfo.ConvertTime(at.Value, zone).ToString("HH:mm") : "—";
    public void NotifyAll() => PropertyChanged?.Invoke(this, new(null));
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
