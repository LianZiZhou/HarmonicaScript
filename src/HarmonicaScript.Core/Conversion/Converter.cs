using System.Security.Cryptography;
using System.Text;
using HarmonicaScript.Core.Conversion.Passes;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Planning;
using HarmonicaScript.Core.Source;
using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Core.Conversion;

/// <summary>Everything one conversion produces.</summary>
public sealed record ConversionResult(
    HarmonicaScore Score,
    InputTimeline Timeline,
    IReadOnlyList<ScheduledNote> Scheduled,
    TranspositionResult Search);

/// <summary>
/// The whole pipeline, assembled.
///
/// The order is not arbitrary. Reduction is a bounded fixpoint because its passes feed each
/// other. Speed comes after reduction so the chord window stays a notational tolerance. Range
/// legalisation runs INSIDE the candidate loop, before planning, because folding changes which
/// band a note lands in. Hold caps are applied to the scheduled notes, before lowering, so they
/// become a property of the serialised timeline rather than a runtime behaviour. And quantisation
/// is the single rounding point, immediately before validation.
/// </summary>
public static class Converter
{
    public static ConversionResult Convert(
        SourceSong song,
        LoadedProfileSet profiles,
        ConversionSettings settings,
        ObjectiveWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(settings);

        var model = profiles.Model;
        var objective = weights ?? ObjectiveWeights.Default;

        var context = new PipelineContext(song, settings, model, WorkNoteFactory.Create(song));

        var pipeline = new Pipeline(
        [
            new TrackSelectPass(),
            new TrimSilencePass(),
            new FixpointGroup(
                "reduce",
                [new ChordCollapsePass(), new MonophonyPass(), new DropShortPass(), new SmoothMelodyPass()],
                settings.MaxReductionIterations),
            new SpeedPass(),
        ]);

        var run = pipeline.Run(context);
        var counters = run.Counters.ToList();
        var diagnostics = run.Diagnostics.ToList();

        // ---- fit.search: joint transposition + modifier plan --------------------------------
        var audible = context.Audible();
        var search = TranspositionSearch.Search(
            model,
            [.. audible.Select(n => n.SourceMidiNote)],
            [.. audible.Select(n => n.OnsetUs)],
            [.. audible.Select(n => n.DurationUs)],
            settings,
            objective);

        context.Transpose = search.Best.Transpose;
        counters.Add(new PassCounter("fit.search.candidates", search.CandidatesEvaluated));
        counters.Add(new PassCounter("fit.search.transpose", search.Best.Transpose));
        counters.Add(new PassCounter("fit.search.musicalLossPpm", search.Best.MusicalLossPpm));
        counters.Add(new PassCounter("fit.search.hardInfeasible", search.Best.HardInfeasible));

        // ---- fit.commit: materialise the surviving notes -------------------------------------
        var scheduled = new List<ScheduledNote>(audible.Count);
        var planIndex = 0;

        for (var i = 0; i < audible.Count; i++)
        {
            var note = audible[i];
            var offset = search.Offsets.Length > i ? search.Offsets[i] : int.MinValue;

            if (offset == int.MinValue)
            {
                var tooHigh = note.SourceMidiNote + search.Best.Transpose - model.Emission.Profile.BaseMidiNote > model.Emission.HiOffset;
                note.Drop(NoteAlteration.None);
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.NoteDroppedOutOfRange, note.SourceIndex, note.OnsetUs, note.SourceMidiNote, tooHigh ? 1 : 0));
                continue;
            }

            var state = search.States.Length > planIndex ? search.States[planIndex] : (byte)0;
            var degree = model.Emission.DegreeOf(state, offset);
            planIndex++;

            var effective = (byte)Math.Clamp(model.Emission.Profile.BaseMidiNote + offset, 0, 127);
            if (effective != note.SourceMidiNote + search.Best.Transpose)
            {
                note.Alteration |= NoteAlteration.OctaveFolded;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCode.NoteOctaveFolded, note.SourceIndex, note.OnsetUs,
                    note.SourceMidiNote, effective));
            }

            note.EffectiveMidiNote = effective;
            note.StateIndex = state;
            note.DegreeIndex = degree;

            scheduled.Add(new ScheduledNote
            {
                Note = note,
                StateIndex = state,
                DegreeIndex = degree,
                Offset = offset,
            });
        }

        // ---- sched.articulate + sched.breath -------------------------------------------------
        var schedule = ArticulationScheduler.Schedule(model, scheduled, settings);
        counters.AddRange(schedule.Counters);
        diagnostics.AddRange(schedule.Diagnostics);

        var useBreath = settings.BreathRests ?? model.Timing.BreathRest.Enabled;
        var (breathCount, breathDiagnostics) = ArticulationScheduler.InsertBreathRests(
            schedule.Notes, model.Timing.BreathRest with { Enabled = useBreath });
        counters.Add(new PassCounter("sched.breath.inserted", breathCount));
        diagnostics.AddRange(breathDiagnostics);

        // ---- emit.holdCaps -> emit.lower -> emit.quantise -> emit.validate --------------------
        var (capped, capCounters, capDiagnostics) = HoldCapPass.Apply(model, schedule.Notes);
        counters.AddRange(capCounters);
        diagnostics.AddRange(capDiagnostics);

        var behavior = model.Emission.Profile.Modifiers.Count == 0
            ? ModifierBehavior.Hold
            : model.Emission.Profile.Modifiers[0].Behavior;

        var raw = behavior == ModifierBehavior.Hold
            ? TimelineLowering.LowerHoldSpans(model, capped)
            : TimelineLowering.LowerToggleTransitions(model, capped);

        var events = TimelineLowering.Quantise(raw, model.Timing.GlobalLeadMs.V * 1000L);
        TimelineValidator.ValidateOrThrow(model, events, behavior);

        // ---- report ---------------------------------------------------------------------------
        var settingsHash = HashSettings(settings, objective);
        var report = BuildReport(model, counters, diagnostics, search, song, capped, events);

        var scoreNotes = BuildScoreNotes(context, capped);
        var score = new HarmonicaScore(
            HarmonicaScore.CurrentFormatVersion,
            new ProfileRef(model.Emission.Profile.Id, model.Emission.Profile.ProfileVersion),
            new ProfileRef(model.Timing.Id, model.Timing.SchemaVersion),
            settingsHash,
            new ScoreMetadata(
                StripExtension(song.FileName),
                song.FileName,
                song.Sha256,
                song.TempoMap.Count > 0 ? song.TempoMap[0].Bpm : 120),
            search.Best.Transpose,
            settings,
            scoreNotes,
            report);

        var timeline = new InputTimeline(
            InputTimeline.CurrentFormatVersion,
            model.Emission.Profile.Ref,
            model.Timing.Ref,
            HashScore(scoreNotes),
            settingsHash,
            events,
            events.Count == 0 ? 0 : events[^1].TimeMs,
            []);

        return new ConversionResult(score, timeline, capped, search);
    }

    private static List<ScoreNote> BuildScoreNotes(PipelineContext context, IReadOnlyList<ScheduledNote> scheduled)
    {
        var scheduleBySource = new Dictionary<int, ScheduledNote>();
        foreach (var s in scheduled)
        {
            scheduleBySource.TryAdd(s.Note.SourceIndex, s);
        }

        var notes = new List<ScoreNote>(context.Notes.Count);
        var id = 0;

        foreach (var note in context.Notes.OrderBy(n => n.OnsetUs).ThenBy(n => n.SourceIndex))
        {
            scheduleBySource.TryGetValue(note.SourceIndex, out var s);

            notes.Add(new ScoreNote
            {
                Id = id++,
                SourceIndex = note.SourceIndex,
                OnsetTicks = note.OnsetTicks,
                DurationTicks = note.DurationTicks,
                OnsetUs = note.OnsetUs,
                DurationUs = note.DurationUs,
                ScheduledDownUs = s?.DownUs ?? note.OnsetUs,
                ScheduledUpUs = s?.UpUs ?? note.EndUs,
                Fingering = s is null ? null : new Fingering((byte)s.DegreeIndex, (byte)s.StateIndex),
                SourceMidiNote = note.SourceMidiNote,
                EffectiveMidiNote = note.EffectiveMidiNote,
                Alteration = note.Alteration,
                Muted = note.Muted,
                ChordSiblingSourceIndices = note.ChordSiblingSourceIndices,
            });
        }

        return notes;
    }

    private static ConversionReport BuildReport(
        ArticulationModel model,
        List<PassCounter> counters,
        List<Diagnostic> diagnostics,
        TranspositionResult search,
        SourceSong song,
        IReadOnlyList<ScheduledNote> scheduled,
        IReadOnlyList<InputEvent> events)
    {
        var (peak, p95, p50) = NotesPerSecond(scheduled);
        var modifierEvents = events.Count(e => e.IsModifier);
        var swaps = CountExclusiveSwaps(model, scheduled);
        var longestRun = LongestModifierRunMs(model, scheduled);

        // The ceiling that actually binds, computed from THIS score's transition mix rather than
        // from the loosest theoretical case.
        var effective = EffectiveCeiling(model, scheduled);

        return new ConversionReport(
            counters,
            diagnostics,
            search.TopCandidates,
            song.Notes.Count,
            scheduled.Count,
            model.SameKeyCeilingNps,
            model.KeyChangeCeilingNps,
            model.ModifierSwapCeilingNps,
            effective,
            peak,
            p95,
            p50,
            modifierEvents,
            swaps,
            longestRun);
    }

    private static double EffectiveCeiling(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        if (notes.Count < 2)
        {
            return model.SameKeyCeilingNps;
        }

        long worstMs = 0;
        for (var i = 1; i < notes.Count; i++)
        {
            var sameKey = notes[i - 1].DegreeIndex == notes[i].DegreeIndex;
            worstMs = Math.Max(worstMs, model.RequiredSilenceMs(notes[i - 1].StateIndex, notes[i].StateIndex, sameKey));
        }

        return 1000.0 / (model.Timing.NoteHoldMinMs.V + worstMs);
    }

    private static (double Peak, double P95, double P50) NotesPerSecond(IReadOnlyList<ScheduledNote> notes)
    {
        if (notes.Count < 2)
        {
            return (0, 0, 0);
        }

        var rates = new List<double>(notes.Count);
        for (var i = 1; i < notes.Count; i++)
        {
            var gapUs = notes[i].DownUs - notes[i - 1].DownUs;
            rates.Add(gapUs <= 0 ? 0 : 1_000_000.0 / gapUs);
        }

        rates.Sort();

        // P95 rather than peak drives the grade: one 32nd-note flourish should not tank an
        // otherwise comfortable arrangement.
        return (rates[^1], rates[(int)(rates.Count * 0.95)], rates[rates.Count / 2]);
    }

    private static int CountExclusiveSwaps(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        var count = 0;
        for (var i = 1; i < notes.Count; i++)
        {
            if (model.IsExclusiveSwap(notes[i - 1].StateIndex, notes[i].StateIndex))
            {
                count++;
            }
        }

        return count;
    }

    private static int LongestModifierRunMs(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        long longest = 0;
        long runStart = 0;
        var inRun = false;

        foreach (var note in notes)
        {
            var active = model.Emission.States[note.StateIndex].Mask != 0;
            if (active && !inRun)
            {
                runStart = note.DownUs;
                inRun = true;
            }
            else if (!active && inRun)
            {
                longest = Math.Max(longest, note.DownUs - runStart);
                inRun = false;
            }
        }

        if (inRun && notes.Count > 0)
        {
            longest = Math.Max(longest, notes[^1].UpUs - runStart);
        }

        return (int)(longest / 1000);
    }

    /// <summary>
    /// Core is banned from System.IO (Policy.Tests enforces it), so the title is derived with
    /// string arithmetic rather than Path.GetFileNameWithoutExtension.
    /// </summary>
    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static string HashSettings(ConversionSettings settings, ObjectiveWeights weights) =>
        System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{settings.Reduction}|{settings.ChordWindowMs}|{settings.TrimLeadingSilence}|{settings.Speed}|"
            + $"{settings.TranspositionMode}|{settings.ManualTranspose}|{settings.OutOfRange}|{settings.MaxFoldOctaves}|"
            + $"{settings.FoldMinDurationMs}|{settings.ContourGuardSemitones}|{settings.PhraseGapMs}|"
            + $"{settings.ExcludePercussion}|{string.Join(',', settings.SelectedTracks)}|{settings.Humanise}|"
            + $"{settings.HumaniseSigmaMs}|{settings.HumaniseSeed}|{settings.BreathRests}|"
            + $"{weights.Event}|{weights.ExclusiveSwap}|{weights.SoftDeficitPerMs}|{weights.DutyPerSecond}|{weights.HardPenalty}")))[..16];

    private static string HashScore(IReadOnlyList<ScoreNote> notes)
    {
        var builder = new StringBuilder();
        foreach (var note in notes.Where(n => n.IsAudible))
        {
            builder.Append(note.ScheduledDownUs).Append(':').Append(note.EffectiveMidiNote).Append(';');
        }

        return System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }
}

/// <summary>Instrument, timing, key table, emission table and articulation model, loaded together.</summary>
public sealed record LoadedProfileSet(EmissionTable Emission, TimingProfile Timing, ArticulationModel Model);
