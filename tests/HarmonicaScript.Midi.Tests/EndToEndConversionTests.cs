using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Profiles;

namespace HarmonicaScript.Midi.Tests;

/// <summary>
/// The end-to-end gate: every fixture, both timing profiles, both lowerings.
/// </summary>
public sealed class EndToEndConversionTests
{
    private static readonly LoadedInstrument Reference = new ProfileStore().LoadValidated("df.harmonica.v1", "df.reference");
    private static readonly LoadedInstrument Conservative = new ProfileStore().LoadValidated("df.harmonica.v1", "df.conservative");

    public static TheoryData<string, string> AllFixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var (category, name) in Corpus.Everything())
        {
            data.Add(category, name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void ConvertsAndValidatesUnderTheReferenceProfile(string category, string name)
    {
        var song = Corpus.Read(category, name);
        var result = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());

        // ValidateOrThrow already ran inside Convert; re-asserting makes the intent explicit.
        var model = Reference.ToProfileSet().Model;
        Assert.Empty(TimelineValidator.Validate(model, result.Timeline.Events, ModifierBehavior.Hold));
        Assert.NotEmpty(result.Score.Notes);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void ConvertsAndValidatesUnderTheConservativeProfile(string category, string name)
    {
        var song = Corpus.Read(category, name);
        var result = Converter.Convert(song, Conservative.ToProfileSet(), new ConversionSettings());

        var model = Conservative.ToProfileSet().Model;
        Assert.Empty(TimelineValidator.Validate(model, result.Timeline.Events, ModifierBehavior.Hold));
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void SimulatorReproducesTheScoreExactly(string category, string name)
    {
        // M7's central assertion, in three parts.
        var song = Corpus.Read(category, name);
        var profiles = Reference.ToProfileSet();
        var result = Converter.Convert(song, profiles, new ConversionSettings());

        var simulation = InstrumentSimulator.Simulate(
            profiles.Emission.Profile, result.Timeline.Events, ModifierBehavior.Hold);

        // (a) the sounded pitch sequence equals the score's EffectiveMidiNote sequence
        var expected = result.Scheduled.Select(s => (int)s.Note.EffectiveMidiNote).ToList();
        var actual = simulation.Notes.Select(s => s.Pitch).ToList();
        Assert.Equal(expected, actual);

        // (b) sounded pitch is CONSTANT across each note's whole [down, up) span. A modifier
        //     armed one note early would bend the previous note and show up here, and nowhere else.
        Assert.Empty(simulation.Repitches);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EffectiveNoteEqualsSourcePlusTransposeWhereverNothingWasAltered(string category, string name)
    {
        // (c) the NON-CIRCULAR assertion. Everything above compares the engine against itself;
        //     this compares it against the input the user actually supplied.
        var song = Corpus.Read(category, name);
        var profiles = Reference.ToProfileSet();
        var result = Converter.Convert(song, profiles, new ConversionSettings());

        var unaltered = result.Score.Notes.Where(n =>
            n.IsAudible && (n.Alteration & (NoteAlteration.OctaveFolded | NoteAlteration.Dropped | NoteAlteration.ChordSibling)) == 0);

        Assert.All(unaltered, n =>
            Assert.Equal(n.SourceMidiNote + result.Score.Transpose, n.EffectiveMidiNote));
    }

    [Fact]
    public void ADeliberatelyBrokenPlanFailsTheRoundTrip()
    {
        // The negative test that gives the positive ones their meaning: if a corrupted timeline
        // still passed, the whole gate would be decorative.
        //
        // Note WHICH invariant catches it. Moving a modifier press earlier does not necessarily
        // bend a sounding note - it can instead land before that modifier's own release, leaving
        // the logical state unchanged there and simply arriving in the WRONG STATE at the strike.
        // So the assertion is over the gate as a whole, not over one of its two halves.
        var song = Corpus.Read("generated", "exclusive-swap-storm");
        var profiles = Reference.ToProfileSet();
        var result = Converter.Convert(song, profiles, new ConversionSettings());

        var events = result.Timeline.Events.ToList();
        var midSongPress = events
            .Select((e, i) => (e.Phase, e.TimeMs, Index: i))
            .Where(x => x.Phase == InputPhase.ModifierDown && x.TimeMs > 2000)
            .Select(x => x.Index)
            .FirstOrDefault(-1);
        Assert.True(midSongPress >= 0, "fixture must contain a modifier press after the first two seconds");

        events[midSongPress] = events[midSongPress] with { TimeMs = events[midSongPress].TimeMs - 400 };
        events.Sort();

        var simulation = InstrumentSimulator.Simulate(profiles.Emission.Profile, events, ModifierBehavior.Hold);
        var expected = result.Scheduled.Select(s => (int)s.Note.EffectiveMidiNote).ToList();
        var actual = simulation.Notes.Select(s => s.Pitch).ToList();

        Assert.True(
            !expected.SequenceEqual(actual) || simulation.Repitches.Count > 0,
            "the round-trip gate failed to notice a modifier armed 400ms early");
    }

    [Fact]
    public void ASoundingNoteBentMidWayIsCaughtByTheConstantPitchInvariant()
    {
        // The surgical version: inject a modifier press squarely inside a note that is sounding
        // with that modifier OFF. This is the failure the step-function model exists to catch and
        // that sampling pitch at onset would miss entirely.
        var song = Corpus.Read("pd", "twinkle");
        var profiles = Reference.ToProfileSet();
        var result = Converter.Convert(song, profiles, new ConversionSettings());

        var sharp = profiles.Emission.Profile.Modifiers.Single(m => m.Id == "semitone");
        var note = result.Scheduled[4];
        var midPointMs = (int)((note.DownUs + note.UpUs) / 2000);

        var events = result.Timeline.Events.ToList();
        events.Add(new InputEvent(midPointMs, InputPhase.ModifierDown, sharp.Binding.Device, sharp.Binding.Code, true, -1));
        events.Add(new InputEvent(midPointMs + 5, InputPhase.ModifierUp, sharp.Binding.Device, sharp.Binding.Code, false, -1));
        events.Sort();

        var simulation = InstrumentSimulator.Simulate(profiles.Emission.Profile, events, ModifierBehavior.Hold);

        var bend = Assert.Single(simulation.Repitches, r => r.AtMs == midPointMs);
        Assert.Equal(bend.FromPitch + 1, bend.ToPitch);
    }

    [Fact]
    public void ToggleLoweringNeverLeavesAModifierLatched()
    {
        // The failure that hold-shaped invariants cannot see: every event paired, nothing
        // physically held at the end, and 半音 still switched ON inside the game.
        var song = Corpus.Read("generated", "chromatic-full-range");
        var toggleProfile = Reference.Instrument with
        {
            Modifiers = [.. Reference.Instrument.Modifiers.Select(m => m with { Behavior = ModifierBehavior.Toggle })],
        };

        var emission = EmissionTable.Build(toggleProfile);
        var model = Core.Planning.ArticulationModel.Build(emission, Reference.Timing);
        var result = Converter.Convert(song, new LoadedProfileSet(emission, Reference.Timing, model), new ConversionSettings());

        Assert.Empty(TimelineValidator.Validate(model, result.Timeline.Events, ModifierBehavior.Toggle));

        var simulation = InstrumentSimulator.Simulate(toggleProfile, result.Timeline.Events, ModifierBehavior.Toggle);
        Assert.Equal(
            result.Scheduled.Select(s => (int)s.Note.EffectiveMidiNote),
            simulation.Notes.Select(s => s.Pitch));
    }

    [Fact]
    public void ImpossibleDensityIsReportedRatherThanSilentlyEmitted()
    {
        var song = Corpus.Read("generated", "impossible-density");
        var result = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());

        // 30 ms inter-onsets with 25 ms notes, against a derived 40 ms survival floor.
        // NOTE what actually happens here, because it is a structural property worth stating:
        // minSurvivingDurationMs is DERIVED as noteHoldMinMs + keyChangeGapMs, which is exactly
        // the minimum spacing the articulation ladder could ever satisfy. So for monophonic input
        // the reduction's dropShort pass catches impossible density BEFORE the ladder sees it,
        // and the ladder's merge/drop rungs fire only when modifier setup pushes the requirement
        // ABOVE that floor. Either way the loss is reported rather than silently emitted - which
        // is the property that matters.
        Assert.True(result.Score.Report.DroppedCount > 0, "impossible density must be reported as loss");
        Assert.True(result.Score.Report.PlayabilityScore < 100);
        Assert.Contains(result.Score.Report.PenaltyBreakdown(), p => p.Penalty > 0);

        // A handful of notes at chord-collapse boundaries inherit a longer span and survive.
        // The requirement is that the vast majority did not, and that the loss is visible.
        Assert.True(
            result.Score.Report.AudibleNoteCount * 10 < result.Score.Report.SourceNoteCount,
            $"expected almost everything to be reported as lost, kept {result.Score.Report.AudibleNoteCount} "
            + $"of {result.Score.Report.SourceNoteCount}");
    }

    [Fact]
    public void FiveMinutePieceDoesNotAccumulateTimingDrift()
    {
        var song = Corpus.Read("generated", "five-minute-drift");
        var result = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());

        var audible = result.Score.Notes.Where(n => n.IsAudible).ToList();
        Assert.True(audible.Count > 1000);

        // Every emitted onset must stay within the drift budget of where the music put it.
        var maxDriftMs = Reference.Timing.MaxDriftMs.V;
        foreach (var note in audible)
        {
            var driftMs = Math.Abs(note.ScheduledDownUs - note.OnsetUs) / 1000;
            Assert.True(driftMs <= maxDriftMs, $"note {note.Id} drifted {driftMs}ms, budget is {maxDriftMs}ms");
        }
    }

    [Fact]
    public void LongSustainsAreSplitRatherThanHeldPastTheCap()
    {
        var song = Corpus.Read("generated", "long-sustains");
        var result = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());

        // No key may be down longer than the cap - a stuck key in an FPS is the worst outcome.
        var downAt = new Dictionary<ushort, int>();
        foreach (var e in result.Timeline.Events.Where(e => !e.IsModifier))
        {
            if (e.IsDown)
            {
                downAt[e.Code] = e.TimeMs;
            }
            else if (downAt.Remove(e.Code, out var down))
            {
                Assert.True(e.TimeMs - down <= Reference.Timing.MaxKeyHoldMs.V);
            }
        }
    }

    [Fact]
    public void ConversionIsDeterministic()
    {
        var song = Corpus.Read("pd", "bach-minuet-g");
        var a = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());
        var b = Converter.Convert(song, Reference.ToProfileSet(), new ConversionSettings());

        Assert.Equal(a.Timeline.ScoreSha256, b.Timeline.ScoreSha256);
        Assert.Equal(a.Timeline.SettingsSha256, b.Timeline.SettingsSha256);
        Assert.Equal(a.Timeline.Events, b.Timeline.Events);
    }
}
