using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Timeline;

/// <summary>
/// Bounds how long any physical control stays down.
///
/// Decision D9: this is a COMPILE-TIME property of the timeline, not a scheduler behaviour. That
/// matters for three reasons. It can be validated, golden-filed and simulated on a Mac. It holds
/// identically for an exported macro whose playback we do not control at runtime. And a
/// scheduler-level cap mutating the stream below the validator would be untested by construction.
///
/// A stuck held MOUSE button in an online FPS is continuous fire, and is the worst thing this
/// project can produce.
/// </summary>
public static class HoldCapPass
{
    public static (List<ScheduledNote> Notes, IReadOnlyList<PassCounter> Counters, IReadOnlyList<Diagnostic> Diagnostics)
        Apply(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(notes);

        var timing = model.Timing;
        var maxKeyHoldUs = timing.MaxKeyHoldMs.V * 1000L;
        var maxModifierHoldUs = timing.MaxModifierHoldMs.V * 1000L;
        var gapUs = timing.SameKeyRetriggerMs.V * 1000L;
        var breakGapUs = timing.ExclusiveSwapMs.V * 1000L;
        var holdMinUs = timing.NoteHoldMinMs.V * 1000L;

        var diagnostics = new List<Diagnostic>();
        var keySplits = 0;
        var modifierBreaks = 0;

        // 1. Split any note held longer than the cap into a chain of re-presses. The pitch is
        //    unchanged by construction, so the simulator's constant-pitch assertion still holds.
        var result = new List<ScheduledNote>(notes.Count);
        foreach (var note in notes)
        {
            if (note.DurationUs <= maxKeyHoldUs)
            {
                result.Add(note);
                continue;
            }

            var cursor = note.DownUs;
            var end = note.UpUs;
            var first = true;

            while (cursor < end)
            {
                var chunkEnd = Math.Min(end, cursor + maxKeyHoldUs);
                var isLast = chunkEnd >= end;
                var upUs = isLast ? chunkEnd : chunkEnd - gapUs;

                if (upUs - cursor < holdMinUs)
                {
                    break;
                }

                var piece = first
                    ? note
                    : new ScheduledNote
                    {
                        Note = note.Note,
                        StateIndex = note.StateIndex,
                        DegreeIndex = note.DegreeIndex,
                        Offset = note.Offset,
                    };

                piece.DownUs = cursor;
                piece.UpUs = upUs;
                if (!first)
                {
                    piece.Note.Alteration |= NoteAlteration.HoldSplit;
                }

                result.Add(piece);
                if (!first)
                {
                    keySplits++;
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCode.HoldSplit, note.Note.SourceIndex, cursor, (upUs - cursor) / 1000));
                }

                first = false;
                cursor = chunkEnd;
            }
        }

        // 2. Break modifier runs that exceed the cap, preferring a real rest so the break is inaudible.
        var modifierCount = model.Emission.Profile.Modifiers.Count;
        for (var m = 0; m < modifierCount; m++)
        {
            var bit = 1 << m;
            var runStartUs = long.MinValue;

            for (var i = 0; i < result.Count; i++)
            {
                var active = (model.Emission.States[result[i].StateIndex].Mask & bit) != 0;
                if (!active)
                {
                    runStartUs = long.MinValue;
                    continue;
                }

                if (runStartUs == long.MinValue)
                {
                    runStartUs = result[i].DownUs;
                    continue;
                }

                if (result[i].UpUs - runStartUs <= maxModifierHoldUs)
                {
                    continue;
                }

                // Prefer breaking at a genuine rest; failing that, break here anyway - an
                // audible seam beats a control held past its cap.
                var restIndex = FindRest(result, i, runStartUs, maxModifierHoldUs, breakGapUs, bit, model);
                var breakAt = restIndex >= 0 ? restIndex : i;

                result[breakAt].ForceRestartMask |= (ushort)bit;
                runStartUs = result[breakAt].DownUs;
                modifierBreaks++;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.HoldSplit, result[breakAt].Note.SourceIndex, result[breakAt].DownUs, m));
            }
        }

        return (
            result,
            [new PassCounter("emit.holdCaps.keySplits", keySplits), new PassCounter("emit.holdCaps.modifierBreaks", modifierBreaks)],
            diagnostics);
    }

    /// <summary>Latest note within the cap window whose preceding gap is wide enough to hide a release and re-press.</summary>
    private static int FindRest(
        List<ScheduledNote> notes,
        int upTo,
        long runStartUs,
        long capUs,
        long neededGapUs,
        int bit,
        ArticulationModel model)
    {
        for (var i = upTo; i > 0; i--)
        {
            if (notes[i].DownUs - runStartUs > capUs)
            {
                continue;
            }

            if ((model.Emission.States[notes[i].StateIndex].Mask & bit) == 0)
            {
                continue;
            }

            if (notes[i].DownUs - notes[i - 1].UpUs >= neededGapUs)
            {
                return i;
            }
        }

        return -1;
    }
}
