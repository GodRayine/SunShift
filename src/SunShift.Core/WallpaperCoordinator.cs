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
    public async Task<ChangeResult> ApplyAsync(ImagePair pair, CancellationToken token, bool force = false)
    {
        await mutex.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var pending = new ImagePair(
                string.IsNullOrWhiteSpace(pair.Desktop) || (!force && desktop == pair.Desktop) ? null : pair.Desktop,
                string.IsNullOrWhiteSpace(pair.LockScreen) || (!force && lockScreen == pair.LockScreen) ? null : pair.LockScreen);
            if (pending.IsEmpty) return new(true, true);
            var result = await sink.ChangeAsync(pending, token);
            if (pending.Desktop != null && result.DesktopOk) desktop = pending.Desktop;
            if (pending.LockScreen != null && result.LockScreenOk) lockScreen = pending.LockScreen;
            if (force && pending.Desktop != null && !result.DesktopOk) desktop = null;
            if (force && pending.LockScreen != null && !result.LockScreenOk) lockScreen = null;
            return result;
        }
        finally { mutex.Release(); }
    }
}
