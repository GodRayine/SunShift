// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SunShift.Core;

public sealed class SettingsStore(string folder)
{
    private readonly string path = Path.Combine(folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string? Warning { get; private set; }
    public Settings Read()
    {
        Warning = null;
        SanitizeRecoveryFiles();
        if (File.Exists(path + ".tmp")) SanitizeFile(path + ".tmp");
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<Settings>(json, Options);
            if (settings is not { Version: 1, Day: not null, Night: not null }) throw new JsonException("Invalid settings.");
            if (!Enum.IsDefined(settings.Theme)) settings.Theme = ThemePreference.System;
            settings.DayRotation = (settings.DayRotation ?? new()).Normalize();
            settings.NightRotation = (settings.NightRotation ?? new()).Normalize();
            using var document = JsonDocument.Parse(json);
            settings.Collection = !document.RootElement.TryGetProperty(nameof(Settings.Collection), out _) ?
                new() { Rotate = settings.DayRotation.Enabled || settings.NightRotation.Enabled } : (settings.Collection ?? new()).Normalize();
            if (ContainsLocation(document.RootElement)) Write(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = path + ".recovery-" + Guid.NewGuid().ToString("N");
            // Never copy unparseable text: a truncated JSON can still contain a position.
            File.WriteAllText(backup, Redact(File.ReadAllText(path)));
            Write(new());
            Warning = "Настройки не удалось прочитать. Копия без геопозиции сохранена: " + backup;
            return new();
        }
    }
    private static bool ContainsLocation(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.EnumerateObject().Any(p => IsLocationKey(p.Name));
    private static bool IsLocationKey(string name) => name.Equals(nameof(Settings.ManualPoint), StringComparison.OrdinalIgnoreCase) ||
        name.Equals(nameof(Settings.LastLocation), StringComparison.OrdinalIgnoreCase);
    private static string Redact(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return "{}";
            foreach (var key in root.Select(p => p.Key).Where(IsLocationKey).ToArray()) root.Remove(key);
            return root.ToJsonString(Options);
        }
        catch (JsonException) { return "{\"Recovery\":\"Unparseable settings omitted to avoid retaining location data.\"}"; }
    }
    private static void SanitizeFile(string file)
    {
        var original = File.ReadAllText(file);
        var redacted = Redact(original);
        if (original != redacted) File.WriteAllText(file, redacted);
    }
    private void SanitizeRecoveryFiles()
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "settings.json.recovery-*")) SanitizeFile(file);
    }
    public void Write(Settings settings)
    {
        Directory.CreateDirectory(folder);
        var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
