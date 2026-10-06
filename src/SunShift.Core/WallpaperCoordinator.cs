// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
namespace SunShift.Core;

public sealed record ChangeResult(bool DesktopOk, bool LockScreenOk, string? Error = null);
public interface IWallpaperSink { Task<ChangeResult> ChangeAsync(ImagePair pair, CancellationToken token); }

public sealed class WallpaperCoordinator(IWallpaperSink sink)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private string? desktop;
    private string? lockScreen;
    private (long Size, long Modified) desktopVersion, lockVersion;
    private static (long, long) FileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return default;
        try { var file = new FileInfo(path); return file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : default; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return default; }
    }
    public async Task<ChangeResult> ApplyAsync(ImagePair pair, CancellationToken token, bool force = false)
    {
        await mutex.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var desktopStamp = FileVersion(pair.Desktop);
            var lockStamp = FileVersion(pair.LockScreen);
            var pending = new ImagePair(
                string.IsNullOrWhiteSpace(pair.Desktop) || (!force && desktop == pair.Desktop && desktopVersion == desktopStamp) ? null : pair.Desktop,
                string.IsNullOrWhiteSpace(pair.LockScreen) || (!force && lockScreen == pair.LockScreen && lockVersion == lockStamp) ? null : pair.LockScreen);
            if (pending.IsEmpty) return new(true, true);
            var result = await sink.ChangeAsync(pending, token);
            if (pending.Desktop != null && result.DesktopOk) { desktop = pending.Desktop; desktopVersion = desktopStamp; }
            if (pending.LockScreen != null && result.LockScreenOk) { lockScreen = pending.LockScreen; lockVersion = lockStamp; }
            if (force && pending.Desktop != null && !result.DesktopOk) desktop = null;
            if (force && pending.LockScreen != null && !result.LockScreenOk) lockScreen = null;
            return result;
        }
        finally { mutex.Release(); }
    }
}
