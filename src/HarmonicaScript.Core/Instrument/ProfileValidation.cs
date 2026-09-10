namespace HarmonicaScript.Core.Instrument;

/// <summary>A single reason a profile was rejected, phrased so a user editing JSON can act on it.</summary>
public sealed record ProfileProblem(string Field, string Message);

public sealed class ProfileValidationException(string profileId, IReadOnlyList<ProfileProblem> problems)
    : Exception($"Profile '{profileId}' is invalid:{Environment.NewLine}"
        + string.Join(Environment.NewLine, problems.Select(p => $"  - {p.Field}: {p.Message}")))
{
    public string ProfileId { get; } = profileId;

    public IReadOnlyList<ProfileProblem> Problems { get; } = problems;
}

/// <summary>
/// Validates only DERIVED invariants - the things that must hold for any instrument at all.
/// Instrument-specific numbers (6 states, 38 offsets, 48 renderings) live in a pinned fixture
/// test against df.harmonica.v1, not here: baking them in would make the schema a lie about
/// being generic.
/// </summary>
public static class ProfileValidator
{
    public static void ValidateOrThrow(InstrumentProfile profile, TimingProfile? timing = null)
    {
        var problems = Validate(profile, timing);
        if (problems.Count > 0)
        {
            throw new ProfileValidationException(profile.Id, problems);
        }
    }

    public static IReadOnlyList<ProfileProblem> Validate(InstrumentProfile profile, TimingProfile? timing = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var problems = new List<ProfileProblem>();

        ValidateDegrees(profile, problems);
        ValidateModifiers(profile, problems);

        if (profile.MaxSimultaneousModifiers < 1)
        {
            problems.Add(new ProfileProblem("maxSimultaneousModifiers", "must be at least 1."));
        }

        if (profile.BaseMidiNote is < 0 or > 127)
        {
            problems.Add(new ProfileProblem("baseMidiNote", $"must be a MIDI note 0-127, was {profile.BaseMidiNote}."));
        }

        // Coverage invariants need the derived table, which only builds if the above held.
        if (problems.Count == 0)
        {
            ValidateCoverage(profile, problems);
        }

        if (timing is not null)
        {
            ValidateTiming(profile, timing, problems);
        }

        return problems;
    }

    private static void ValidateDegrees(InstrumentProfile profile, List<ProfileProblem> problems)
    {
        if (profile.Degrees.Count == 0)
        {
            problems.Add(new ProfileProblem("degrees", "at least one degree is required."));
            return;
        }

        if (profile.Degrees.Count > 127)
        {
            problems.Add(new ProfileProblem("degrees", "at most 127 degrees are supported (the degree index is an sbyte)."));
        }

        // THE load-bearing invariant. Pairwise-distinct semitones is what makes a modifier
        // state uniquely determine the physical key, which is what collapses the fingering
        // search into a plain Viterbi over states.
        var bySemitone = profile.Degrees.GroupBy(d => d.Semitone).Where(g => g.Count() > 1).ToList();
        foreach (var clash in bySemitone)
        {
            problems.Add(new ProfileProblem(
                "degrees",
                $"semitone {clash.Key} is produced by degrees {string.Join(", ", clash.Select(d => d.Index))}. "
                + "Degrees must be pairwise distinct: a modifier state has to determine the key uniquely."));
        }

        foreach (var dup in profile.Degrees.GroupBy(d => d.Binding).Where(g => g.Count() > 1))
        {
            problems.Add(new ProfileProblem("degrees", $"binding {dup.Key} is bound to more than one degree."));
        }
    }

    private static void ValidateModifiers(InstrumentProfile profile, List<ProfileProblem> problems)
    {
        foreach (var dup in profile.Modifiers.GroupBy(m => m.Id).Where(g => g.Count() > 1))
        {
            problems.Add(new ProfileProblem("modifiers", $"duplicate modifier id '{dup.Key}'."));
        }

        foreach (var dup in profile.Modifiers.GroupBy(m => m.Binding).Where(g => g.Count() > 1))
        {
            problems.Add(new ProfileProblem("modifiers", $"binding {dup.Key} is bound to more than one modifier."));
        }

        foreach (var group in profile.Modifiers.Where(m => m.ExclusionGroup != 0).GroupBy(m => m.ExclusionGroup))
        {
            if (group.Count() < 2)
            {
                problems.Add(new ProfileProblem(
                    "modifiers",
                    $"exclusion group {group.Key} has one member ('{group.First().Id}'); a group of one excludes nothing."));
            }
        }

        foreach (var m in profile.Modifiers)
        {
            if (m.PressLeadMs < 0 || m.ReleaseLeadMs < 0)
            {
                problems.Add(new ProfileProblem($"modifiers.{m.Id}", "lead times must not be negative."));
            }

            if (m.DutyWeightMilli < 0)
            {
                problems.Add(new ProfileProblem($"modifiers.{m.Id}", "dutyWeightMilli must not be negative."));
            }
        }

        var keyBindings = profile.Degrees.Select(d => d.Binding).ToHashSet();
        foreach (var m in profile.Modifiers.Where(m => keyBindings.Contains(m.Binding)))
        {
            problems.Add(new ProfileProblem($"modifiers.{m.Id}", $"binding {m.Binding} collides with a note degree."));
        }
    }

    private static void ValidateCoverage(InstrumentProfile profile, List<ProfileProblem> problems)
    {
        EmissionTable table;
        try
        {
            table = EmissionTable.Build(profile);
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(new ProfileProblem("emissionTable", ex.Message));
            return;
        }

        // An interior gap would mean a pitch inside the instrument's own span is unplayable,
        // which every downstream range check assumes cannot happen.
        var gaps = table.Offsets().Where(o => !table.IsPlayable(o)).ToList();
        if (gaps.Count > 0)
        {
            problems.Add(new ProfileProblem(
                "emissionTable",
                $"coverage has {gaps.Count} interior gap(s) at offsets {string.Join(", ", gaps.Take(8))}"
                + (gaps.Count > 8 ? ", ..." : string.Empty)));
        }

        var totalRenderings = table.Offsets().Sum(table.Multiplicity);
        if (totalRenderings != table.RenderingCount)
        {
            problems.Add(new ProfileProblem(
                "emissionTable",
                $"popcount sum {totalRenderings} != stateCount*degreeCount {table.RenderingCount}; "
                + "a rendering was lost, which means two degrees collided within one state."));
        }
    }

    private static void ValidateTiming(InstrumentProfile profile, TimingProfile timing, List<ProfileProblem> problems)
    {
        // Cross-constant relation. If a user edits one number and breaks the ordering, the
        // planner's feasibility model quietly stops matching the scheduler's, so refuse loudly.
        var maxPressLead = profile.Modifiers.Count == 0 ? 0 : profile.Modifiers.Max(m => m.PressLeadMs);
        if (timing.KeyChangeGapMs.V < timing.ExclusiveSwapMs.V)
        {
            problems.Add(new ProfileProblem(
                "timing.keyChangeGapMs",
                $"must be >= exclusiveSwapMs ({timing.ExclusiveSwapMs.V}), was {timing.KeyChangeGapMs.V}."));
        }

        if (timing.ExclusiveSwapMs.V < maxPressLead)
        {
            problems.Add(new ProfileProblem(
                "timing.exclusiveSwapMs",
                $"must be >= the largest modifier pressLeadMs ({maxPressLead}), was {timing.ExclusiveSwapMs.V}."));
        }

        if (timing.NoteHoldMinMs.V < timing.KeyHeldMinMs.V)
        {
            problems.Add(new ProfileProblem(
                "timing.noteHoldMinMs",
                $"must be >= keyHeldMinMs ({timing.KeyHeldMinMs.V}), was {timing.NoteHoldMinMs.V}."));
        }

        if (timing.MaxKeyHoldMs.V <= timing.NoteHoldMinMs.V)
        {
            problems.Add(new ProfileProblem("timing.maxKeyHoldMs", "must be greater than noteHoldMinMs."));
        }

        if (timing.MaxDriftMs.V < timing.MaxOnsetShiftMs.V)
        {
            problems.Add(new ProfileProblem("timing.maxDriftMs", "must be >= maxOnsetShiftMs."));
        }

        if (timing.BreathRest.Enabled && timing.BreathRest.RestMs <= 0)
        {
            problems.Add(new ProfileProblem("timing.breathRest.restMs", "must be positive when breath rests are enabled."));
        }
    }
}
