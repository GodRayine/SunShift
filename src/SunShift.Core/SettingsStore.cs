// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System.Text.Json;

namespace SunShift.Core;

public sealed class SettingsStore(string folder)
{
    private readonly string path = Path.Combine(folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string? Warning { get; private set; }
    public Settings Read()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options);
            if (settings is not { Version: 1, Day: not null, Night: not null } ||
                (settings.ManualLocation && settings.ManualPoint is not { IsValid: true })) throw new JsonException("Invalid settings.");
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = path + ".recovery-" + Guid.NewGuid().ToString("N");
            File.Copy(path, backup);
            Warning = "Настройки не удалось прочитать. Копия сохранена: " + backup;
            return new();
        }
    }
    public void Write(Settings settings)
    {
        Directory.CreateDirectory(folder);
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
