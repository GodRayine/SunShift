// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 GodRayine
namespace SunShift.Core;

// One cursor for both phases and both screens: sunset selects the night member
// of the current scene, never a separately indexed or randomly chosen partner.
public sealed class LinkedPairRotation(Random? random = null)
{
    private readonly Random generator = random ?? Random.Shared;
    private CollectionSettings? previous;
    private string? observedId, currentId;
    private DateTimeOffset due, selectedAt;
    public void Reset() { previous = null; observedId = currentId = null; }
    public RotationSelection Select(Settings settings, SunPhase phase, DateTimeOffset now)
    {
        var options = settings.Collection;
        var pairs = options.Pairs;
        if (pairs.Length == 0) return new(new(), null, "Добавьте связанную пару дневных и ночных обоев.", 0, 0);
        var index = Array.FindIndex(pairs, pair => pair.Id == currentId);
        var changed = previous == null || previous.IntervalMinutes != options.IntervalMinutes || previous.Order != options.Order ||
            !previous.Pairs.SequenceEqual(pairs);
        var explicitChoice = observedId != settings.ActivePairId && settings.ActivePairId != currentId;
        if (changed || explicitChoice || index < 0)
        {
            index = Array.FindIndex(pairs, pair => pair.Id == settings.ActivePairId);
            if (index < 0) index = 0;
            due = now.AddMinutes(options.IntervalMinutes); selectedAt = now;
        }
        else if (options.Rotate && previous?.Rotate == true && now >= due)
        {
            if (options.Order == RotationOrder.Random && pairs.Length > 1)
            { var next = generator.Next(pairs.Length - 1); index = next >= index ? next + 1 : next; }
            else index = (index + 1) % pairs.Length;
            due = now.AddMinutes(options.IntervalMinutes); selectedAt = now;
        }
        if (now < selectedAt || !options.Rotate || previous?.Rotate != options.Rotate)
        { due = now.AddMinutes(options.IntervalMinutes); selectedAt = now; }
        var pair = pairs[index]; currentId = pair.Id; observedId = settings.ActivePairId; previous = options;
        return new(pair.For(phase), options.Rotate && pairs.Length > 1 ? due : null, null,
            pairs.Length, pairs.Length, pair.Id, pair.Name);
    }
}
