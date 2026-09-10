namespace HarmonicaScript.Core.Conversion.Passes;

/// <summary>
/// Collapses near-simultaneous notes into one, keeping the survivor chosen by the policy and
/// FLAGGING the losers as siblings rather than deleting them.
/// </summary>
public sealed class ChordCollapsePass : IScorePass
{
    public string Id => "reduce.chords";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Musical time: the window is applied BEFORE the speed multiplier, so a tempo change
        // never turns a fast passage into a chord or lets a real chord escape.
        var windowUs = context.Settings.ChordWindowMs * 1000L;
        var candidates = context.Notes.Where(n => n.IsAudible).OrderBy(n => n.OnsetUs).ThenBy(n => n.SourceIndex).ToList();

        var collapsed = 0;
        var rolled = 0;
        var diagnostics = new List<Diagnostic>();

        var i = 0;
        while (i < candidates.Count)
        {
            var group = new List<WorkNote> { candidates[i] };
            var anchor = candidates[i].OnsetUs;

            var j = i + 1;
            while (j < candidates.Count && candidates[j].OnsetUs - anchor <= windowUs)
            {
                group.Add(candidates[j]);
                j++;
            }

            if (group.Count > 1)
            {
                var spreadUs = group[^1].OnsetUs - group[0].OnsetUs;
                if (spreadUs > 0)
                {
                    // Under-collapse is visible rather than silent: a genuinely rolled chord
                    // that we flattened is worth knowing about.
                    rolled++;
                }

                var winner = Pick(group, context.Settings.Reduction);

                // The group takes the EARLIEST onset. A rolled chord must not land late - the
                // listener hears the first attack, not the last.
                winner.OnsetUs = group[0].OnsetUs;
                winner.DurationUs = Math.Max(winner.DurationUs, group.Max(n => n.EndUs) - winner.OnsetUs);
                winner.Alteration |= NoteAlteration.ChordReduced;
                winner.ChordSiblingSourceIndices =
                    [.. group.Where(n => !ReferenceEquals(n, winner)).Select(n => n.SourceIndex)];

                foreach (var loser in group.Where(n => !ReferenceEquals(n, winner)))
                {
                    loser.Alteration |= NoteAlteration.ChordSibling;
                    collapsed++;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.ChordCollapsed, winner.SourceIndex, winner.OnsetUs, group.Count, spreadUs / 1000));
            }

            i = j;
        }

        return new PassResult(
            collapsed > 0,
            [new PassCounter("reduce.chords.collapsed", collapsed), new PassCounter("reduce.chords.rolled", rolled)],
            diagnostics);
    }

    private static WorkNote Pick(List<WorkNote> group, ReductionPolicy policy) => policy switch
    {
        // Tie-break velocity then length: between two notes of the same pitch the louder and
        // longer one is the one the arranger meant.
        ReductionPolicy.Highest => group
            .OrderByDescending(n => n.SourceMidiNote).ThenByDescending(n => n.Velocity)
            .ThenByDescending(n => n.DurationUs).ThenBy(n => n.SourceIndex).First(),
        ReductionPolicy.Lowest => group
            .OrderBy(n => n.SourceMidiNote).ThenByDescending(n => n.Velocity)
            .ThenByDescending(n => n.DurationUs).ThenBy(n => n.SourceIndex).First(),
        ReductionPolicy.Loudest => group
            .OrderByDescending(n => n.Velocity).ThenByDescending(n => n.SourceMidiNote)
            .ThenByDescending(n => n.DurationUs).ThenBy(n => n.SourceIndex).First(),
        ReductionPolicy.TrackPriority => group
            .OrderBy(n => n.TrackIndex).ThenByDescending(n => n.SourceMidiNote)
            .ThenByDescending(n => n.Velocity).ThenBy(n => n.SourceIndex).First(),
        _ => group[0],
    };
}

/// <summary>
/// Forward sweep that truncates every note at the next note's onset. The instrument is strictly
/// monophonic, so overlap is not a stylistic choice - it is an impossible state.
/// </summary>
public sealed class MonophonyPass : IScorePass
{
    public string Id => "reduce.monophony";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var audible = context.Audible();
        var truncated = 0;
        var diagnostics = new List<Diagnostic>();

        for (var i = 0; i < audible.Count - 1; i++)
        {
            var current = audible[i];
            var next = audible[i + 1];
            if (current.EndUs <= next.OnsetUs)
            {
                continue;
            }

            var newDuration = Math.Max(0, next.OnsetUs - current.OnsetUs);
            if (newDuration != current.DurationUs)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.NoteTruncatedByNextOnset,
                    current.SourceIndex,
                    current.OnsetUs,
                    (current.DurationUs - newDuration) / 1000));

                current.DurationUs = newDuration;
                truncated++;
            }
        }

        return new PassResult(truncated > 0, [new PassCounter("reduce.monophony.truncated", truncated)], diagnostics);
    }
}

/// <summary>
/// Drops notes too short to be played at all. The floor is DERIVED from the timing profile
/// (hold minimum plus key-change gap), so it actually tracks the profile instead of being a
/// constant that happens to look plausible.
/// </summary>
public sealed class DropShortPass : IScorePass
{
    public string Id => "reduce.dropShort";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var floorUs = context.Timing.MinSurvivingDurationMs * 1000L;
        var dropped = 0;
        var diagnostics = new List<Diagnostic>();

        foreach (var note in context.Notes.Where(n => n.IsAudible && n.DurationUs < floorUs))
        {
            note.Drop(NoteAlteration.None);
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.NoteDroppedTooShort, note.SourceIndex, note.OnsetUs,
                note.DurationUs / 1000, context.Timing.MinSurvivingDurationMs));
            dropped++;
        }

        return new PassResult(dropped > 0, [new PassCounter("reduce.dropShort.dropped", dropped)], diagnostics);
    }
}

/// <summary>
/// Removes isolated melodic spikes - a short note leaping far above both neighbours, which is
/// almost always an accompaniment note that the skyline reduction grabbed by accident.
///
/// Crucially it PROMOTES the next-best chord sibling before dropping. The correct repair for
/// "the skyline grabbed the wrong voice" is to take the other voice, not to leave a hole.
/// </summary>
public sealed class SmoothMelodyPass : IScorePass
{
    public string Id => "reduce.smoothMelody";

    private const int LeapSemitones = 12;
    private const int ShortMs = 90;

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var audible = context.Audible();
        var promoted = 0;
        var smoothed = 0;
        var diagnostics = new List<Diagnostic>();
        var bySourceIndex = context.Notes.ToDictionary(n => n.SourceIndex);

        for (var i = 1; i < audible.Count - 1; i++)
        {
            var note = audible[i];
            if (note.DurationUs >= ShortMs * 1000L)
            {
                continue;
            }

            var above = note.SourceMidiNote - audible[i - 1].SourceMidiNote;
            var below = note.SourceMidiNote - audible[i + 1].SourceMidiNote;
            if (above < LeapSemitones || below < LeapSemitones)
            {
                continue;
            }

            var replacement = BestSibling(note, bySourceIndex, audible[i - 1].SourceMidiNote);
            if (replacement is not null)
            {
                replacement.Alteration &= ~NoteAlteration.ChordSibling;
                replacement.Alteration |= NoteAlteration.ChordReduced;
                replacement.OnsetUs = note.OnsetUs;
                replacement.DurationUs = note.DurationUs;
                replacement.ChordSiblingSourceIndices = note.ChordSiblingSourceIndices;

                note.Alteration |= NoteAlteration.ChordSibling | NoteAlteration.SmoothedOut;
                note.ChordSiblingSourceIndices = null;

                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.MelodySpikePromotedSibling, note.SourceIndex, note.OnsetUs,
                    note.SourceMidiNote, replacement.SourceMidiNote));
                promoted++;
            }
            else
            {
                note.Drop(NoteAlteration.SmoothedOut);
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.MelodySpikeSmoothed, note.SourceIndex, note.OnsetUs, Math.Min(above, below)));
                smoothed++;
            }
        }

        return new PassResult(
            promoted + smoothed > 0,
            [
                new PassCounter("reduce.smoothMelody.promoted", promoted),
                new PassCounter("reduce.smoothMelody.dropped", smoothed),
            ],
            diagnostics);
    }

    private static WorkNote? BestSibling(
        WorkNote spike,
        Dictionary<int, WorkNote> bySourceIndex,
        byte previousPitch)
    {
        if (spike.ChordSiblingSourceIndices is not { Count: > 0 } siblings)
        {
            return null;
        }

        // The next-highest sibling that is not itself a spike against the previous note.
        return siblings
            .Select(index => bySourceIndex.TryGetValue(index, out var s) ? s : null)
            .Where(s => s is not null && (s.Alteration & NoteAlteration.Dropped) == 0)
            .Where(s => Math.Abs(s!.SourceMidiNote - previousPitch) < LeapSemitones)
            .OrderByDescending(s => s!.SourceMidiNote)
            .FirstOrDefault();
    }
}
