using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Export;

/// <summary>One event, expressed as a wait followed by an action - the shape every macro format wants.</summary>
public readonly record struct LoweredEvent(int DelayBeforeMs, InputEvent Event);

public sealed record LoweredPart(
    IReadOnlyList<LoweredEvent> Events,
    int PartIndex,
    int PartCount,
    long CumulativeDriftMs);

public sealed record LoweredTimeline(IReadOnlyList<LoweredPart> Parts, IReadOnlyList<ExportWarning> Warnings)
{
    public int TotalEvents => Parts.Sum(p => p.Events.Count);
}

/// <summary>
/// The shared lowering chain, run ONCE in one place so every writer is 80-150 lines of pure
/// serialisation and every writer behaves identically at the edges.
///
/// Deltas are computed from ABSOLUTE times with the rounding residual carried forward and
/// reabsorbed at the next long rest. Recomputing each delta independently from a rounded
/// predecessor is how a five-minute piece ends up seconds late.
/// </summary>
public static class MacroLowering
{
    public static LoweredTimeline Lower(InputTimeline timeline, ExporterCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(capabilities);

        var warnings = new List<ExportWarning>();
        var events = timeline.Events;

        // 1. Deltaise, carrying the residual.
        var lowered = new List<LoweredEvent>(events.Count);
        long emittedMs = 0;
        long residualMs = 0;

        foreach (var e in events)
        {
            var wantedDelta = e.TimeMs - emittedMs + residualMs;
            var delta = wantedDelta;

            // 2. Clamp to the target's expressible range.
            if (delta < capabilities.MinDelayMs && lowered.Count > 0)
            {
                delta = capabilities.MinDelayMs;
            }

            if (delta < 0)
            {
                delta = 0;
            }

            // 3. Quantise to the target's granularity, keeping the remainder.
            if (capabilities.DelayGranularityMs > 1)
            {
                var granular = delta / capabilities.DelayGranularityMs * capabilities.DelayGranularityMs;
                residualMs = delta - granular;
                delta = granular;
            }
            else
            {
                residualMs = wantedDelta - delta;
            }

            // A residual only survives until a rest long enough to absorb it silently.
            if (residualMs != 0 && delta > 200)
            {
                delta += residualMs;
                residualMs = 0;
            }

            lowered.Add(new LoweredEvent((int)delta, e));
            emittedMs += delta;
        }

        var driftMs = events.Count == 0 ? 0 : Math.Abs(emittedMs - events[^1].TimeMs);
        if (driftMs > 0)
        {
            warnings.Add(new ExportWarning("drift", $"cumulative timing drift {driftMs}ms"));
        }

        // 4. Split long waits the target cannot express.
        if (capabilities.MaxDelayMs is { } maxDelay)
        {
            lowered = SplitLongWaits(lowered, maxDelay);
        }

        // 5. Budget check and, if needed, split into chained parts at the longest rests.
        var parts = capabilities.MaxEvents is { } maxEvents && lowered.Count > maxEvents
            ? SplitIntoParts(lowered, maxEvents, warnings)
            : [new LoweredPart(lowered, 0, 1, driftMs)];

        return new LoweredTimeline(parts, warnings);
    }

    private static List<LoweredEvent> SplitLongWaits(List<LoweredEvent> events, int maxDelayMs)
    {
        var result = new List<LoweredEvent>(events.Count);
        foreach (var e in events)
        {
            var remaining = e.DelayBeforeMs;
            while (remaining > maxDelayMs)
            {
                // A no-op wait carrying the overflow. Every target that has a delay cap also
                // accepts a bare wait, so this is safe rather than clever.
                result.Add(new LoweredEvent(maxDelayMs, e.Event with { NoteId = -2 }));
                remaining -= maxDelayMs;
            }

            result.Add(e with { DelayBeforeMs = remaining });
        }

        return result;
    }

    /// <summary>
    /// Splits at the LONGEST rests, so a seam falls between phrases rather than mid-run, and
    /// never between a modifier press and the strike that depends on it.
    /// </summary>
    private static List<LoweredPart> SplitIntoParts(
        List<LoweredEvent> events,
        int maxEvents,
        List<ExportWarning> warnings)
    {
        var partCount = (events.Count + maxEvents - 1) / maxEvents;
        warnings.Add(new ExportWarning(
            "split",
            $"{events.Count} events exceed the target's {maxEvents}-event limit; split into {partCount} chained parts"));

        var parts = new List<LoweredPart>(partCount);
        var cursor = 0;

        for (var index = 0; index < partCount; index++)
        {
            var wanted = Math.Min(cursor + maxEvents, events.Count);
            var boundary = wanted;

            if (wanted < events.Count)
            {
                // Walk back to the widest gap in the last quarter of this part.
                var searchFrom = Math.Max(cursor + 1, wanted - (maxEvents / 4));
                var bestDelay = -1;
                for (var i = searchFrom; i < wanted; i++)
                {
                    if (events[i].DelayBeforeMs > bestDelay && events[i].Event.Phase == InputPhase.NoteDown)
                    {
                        bestDelay = events[i].DelayBeforeMs;
                        boundary = i;
                    }
                }
            }

            parts.Add(new LoweredPart(events.GetRange(cursor, boundary - cursor), index, partCount, 0));
            cursor = boundary;

            if (cursor >= events.Count)
            {
                break;
            }
        }

        return parts;
    }
}
