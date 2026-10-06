// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System;
using System.Globalization;
using System.Linq;

namespace SunShift.App;

internal static class IntervalOptions
{
    public static string[] Labels { get; } = new[] { 1, 5, 15, 30, 60, 120, 240, 480, 720, 1440 }.Select(Format).ToArray();

    public static string Format(int minutes) => minutes == 1440 ? "Раз в день" :
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} ч" : $"{minutes} мин";

    public static bool TryParse(string text, out int minutes)
    {
        minutes = 0;
        var value = text.Trim();
        if (value.Equals("Раз в день", StringComparison.OrdinalIgnoreCase)) { minutes = 1440; return true; }
        var multiplier = 1;
        if (value.EndsWith("мин", StringComparison.OrdinalIgnoreCase)) value = value[..^3].TrimEnd();
        else if (value.EndsWith("ч", StringComparison.OrdinalIgnoreCase)) { value = value[..^1].TrimEnd(); multiplier = 60; }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 1 || amount > 1440 / multiplier) return false;
        minutes = amount * multiplier;
        return true;
    }
}
