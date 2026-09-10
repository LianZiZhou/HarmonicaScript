using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Timeline;

/// <summary>A note the scheduler has placed: exact key-down and key-up, plus its modifier state.</summary>
public sealed class ScheduledNote
{
    public required WorkNote Note { get; init; }

    public required int StateIndex { get; init; }

    public required int DegreeIndex { get; init; }

    public required int Offset { get; init; }

    public long DownUs { get; set; }

    public long UpUs { get; set; }

    /// <summary>
    /// Modifiers that must be released and re-pressed immediately before this note, even though
    /// the plan keeps them active. Set by the hold-cap pass so a control is never held longer
    /// than the profile allows.
    /// </summary>
    public ushort ForceRestartMask { get; set; }

    public long DurationUs => UpUs - DownUs;
}

public sealed record ScheduleResult(
    IReadOnlyList<ScheduledNote> Notes,
    IReadOnlyList<PassCounter> Counters,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Places the planned notes in real time and resolves collisions.
///
/// Because the planner already guaranteed its plan is realisable by truncating a predecessor
/// alone, the later rungs of this ladder fire only on genuine note-density infeasibility - not
/// on anything the modifier plan caused.
/// </summary>
public static class ArticulationScheduler
{
    /// <summary>
    /// The ladder, ordered by how imperceptible each repair is:
    ///   (a) truncate the previous note   - staccato is less noticeable than rubato
    ///   (b) shift this note later        - within a per-note and a cumulative budget
    ///   (c) merge into the previous note - a merged repeat reads as a tie
    ///   (d) drop it                      - with a diagnostic carrying the exact shortfall
    /// </summary>
    public static ScheduleResult Schedule(
        ArticulationModel model,
        IReadOnlyList<ScheduledNote> planned,
        ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(planned);

        var timing = model.Timing;
        var holdMinUs = timing.NoteHoldMinMs.V * 1000L;
        var heldMinUs = timing.KeyHeldMinMs.V * 1000L;
        var maxShiftUs = timing.MaxOnsetShiftMs.V * 1000L;
        var maxDriftUs = timing.MaxDriftMs.V * 1000L;

        var diagnostics = new List<Diagnostic>();
        var kept = new List<ScheduledNote>(planned.Count);
        var clamped = 0;
        var shifted = 0;
        var merged = 0;
        var dropped = 0;
        long carriedShiftUs = 0;

        foreach (var note in planned)
        {
            note.DownUs = note.Note.OnsetUs + carriedShiftUs;
            note.UpUs = note.DownUs + Math.Max(Math.Max(note.Note.DurationUs, holdMinUs), heldMinUs);

            if (kept.Count == 0)
            {
                kept.Add(note);
                continue;
            }

            var previous = kept[^1];
            var sameKey = previous.DegreeIndex == note.DegreeIndex;
            var requiredUs = model.RequiredSilenceMs(previous.StateIndex, note.StateIndex, sameKey) * 1000L;

            if (previous.UpUs + requiredUs <= note.DownUs)
            {
                kept.Add(note);
                continue;
            }

            // (a) truncate the predecessor, never below its own floor
            var earliestPreviousUp = previous.DownUs + Math.Max(holdMinUs, heldMinUs);
            var wantedPreviousUp = note.DownUs - requiredUs;
            if (wantedPreviousUp >= earliestPreviousUp)
            {
                if (wantedPreviousUp < previous.UpUs)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCode.DurationClamped, previous.Note.SourceIndex, previous.DownUs,
                        (previous.UpUs - wantedPreviousUp) / 1000));
                    previous.UpUs = wantedPreviousUp;
                    previous.Note.Alteration |= NoteAlteration.DurationClamped;
                    clamped++;
                }

                kept.Add(note);
                continue;
            }

            previous.UpUs = earliestPreviousUp;
            previous.Note.Alteration |= NoteAlteration.DurationClamped;
            clamped++;

            // (b) shift this note later, within both budgets
            var shortfallUs = previous.UpUs + requiredUs - note.DownUs;
            if (shortfallUs <= maxShiftUs && carriedShiftUs + shortfallUs <= maxDriftUs)
            {
                note.DownUs += shortfallUs;
                note.UpUs += shortfallUs;
                carriedShiftUs += shortfallUs;
                note.Note.Alteration |= NoteAlteration.OnsetShifted;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.OnsetShifted, note.Note.SourceIndex, note.DownUs, shortfallUs / 1000));
                shifted++;
                kept.Add(note);
                continue;
            }

            // (c) merge a repeated key into its predecessor - reads as a tie, not a hole
            if (sameKey && previous.StateIndex == note.StateIndex)
            {
                previous.UpUs = Math.Max(previous.UpUs, note.UpUs);
                note.Note.Alteration |= NoteAlteration.MergedWithPrev;
                note.Note.Drop(NoteAlteration.MergedWithPrev);
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.MergedWithPrevious, note.Note.SourceIndex, note.DownUs, shortfallUs / 1000));
                merged++;
                continue;
            }

            // (d) drop, always with the exact shortfall so the UI can say 「缺 38 毫秒」
            note.Note.Drop(NoteAlteration.None);
            diagnostics.Add(new Diagnostic(
                DiagnosticCode.DroppedInsufficientGap, note.Note.SourceIndex, note.DownUs,
                shortfallUs / 1000, requiredUs / 1000));
            dropped++;
        }

        var counters = new List<PassCounter>
        {
            new("sched.articulate.clamped", clamped),
            new("sched.articulate.shifted", shifted),
            new("sched.articulate.merged", merged),
            new("sched.articulate.dropped", dropped),
            new("sched.articulate.carriedDriftMs", carriedShiftUs / 1000),
        };

        return new ScheduleResult(kept, counters, diagnostics);
    }

    /// <summary>
    /// Inserts a rest after a stretch of unbroken sound.
    ///
    /// The reference tool added this against an ACTUALLY OBSERVED in-game failure - the game's
    /// judgement "sticking" (判定粘连) on long continuous play. It is the only guard in the whole
    /// timing profile with a real observation behind it, which is why it defaults on, and its
    /// 8s / 90ms figures are that author's guess, which is why they are configurable.
    /// </summary>
    public static (int Inserted, IReadOnlyList<Diagnostic> Diagnostics) InsertBreathRests(
        IReadOnlyList<ScheduledNote> notes,
        Instrument.BreathRestSpec spec)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!spec.Enabled || notes.Count == 0)
        {
            return (0, []);
        }

        var diagnostics = new List<Diagnostic>();
        var afterUs = spec.AfterMs * 1000L;
        var restUs = spec.RestMs * 1000L;
        var phraseGapUs = spec.PhraseGapMs * 1000L;

        long continuousStartUs = notes[0].DownUs;
        var inserted = 0;

        for (var i = 1; i < notes.Count; i++)
        {
            // A natural gap already gives the instrument a break; no need to manufacture one.
            if (notes[i].DownUs - notes[i - 1].UpUs >= phraseGapUs)
            {
                continuousStartUs = notes[i].DownUs;
                continue;
            }

            if (notes[i].UpUs - continuousStartUs < afterUs)
            {
                continue;
            }

            // Take the rest out of the preceding note rather than delaying everything after it:
            // a 90 ms shortening is far less audible than a 90 ms rhythmic displacement.
            var newUp = Math.Max(notes[i - 1].DownUs + restUs, notes[i].DownUs - restUs);
            if (newUp < notes[i - 1].UpUs)
            {
                notes[i - 1].UpUs = newUp;
                notes[i - 1].Note.Alteration |= NoteAlteration.BreathRest;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.BreathRestInserted, notes[i - 1].Note.SourceIndex, newUp, spec.RestMs));
                inserted++;
            }

            continuousStartUs = notes[i].DownUs;
        }

        return (inserted, diagnostics);
    }
}
