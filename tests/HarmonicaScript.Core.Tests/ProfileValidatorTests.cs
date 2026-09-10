using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Tests;

/// <summary>
/// The validator asserts only DERIVED invariants. Its job is to make a bad hand-edit fail
/// loudly at load with an actionable message, rather than silently producing an instrument
/// whose optimiser output is nonsense.
/// </summary>
public sealed class ProfileValidatorTests
{
    private static InstrumentProfile Reference => DeltaForceProfile.Reference.Instrument;

    [Fact]
    public void ShippedProfileIsValid()
    {
        Assert.Empty(ProfileValidator.Validate(Reference, DeltaForceProfile.Reference.Timing));
    }

    [Fact]
    public void RejectsDegreesThatCollideWithinAState()
    {
        // Two degrees on the same semitone would break the uniqueness the DP depends on.
        var broken = Reference with
        {
            Degrees = [.. Reference.Degrees.Select((d, i) => i == 1 ? d with { Semitone = 0 } : d)],
        };

        var problems = ProfileValidator.Validate(broken);
        Assert.Contains(problems, p => p.Message.Contains("pairwise distinct", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnExclusionGroupWithOneMember()
    {
        var broken = Reference with
        {
            Modifiers = [.. Reference.Modifiers.Select(m => m.Id == "octaveUp" ? m with { ExclusionGroup = 7 } : m)],
        };

        var problems = ProfileValidator.Validate(broken);
        Assert.Contains(problems, p => p.Message.Contains("excludes nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsInteriorCoverageGaps()
    {
        // Widen the semitone modifier to +3: the middle band then skips offsets and the union
        // is no longer contiguous, so the "every pitch in range is playable" assumption fails.
        var broken = Reference with
        {
            Modifiers = [.. Reference.Modifiers.Select(m => m.Id == "semitone" ? m with { SemitoneDelta = 3 } : m)],
        };

        var problems = ProfileValidator.Validate(broken);
        Assert.Contains(problems, p => p.Message.Contains("interior gap", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAModifierBoundToANoteKey()
    {
        var broken = Reference with
        {
            Modifiers = [.. Reference.Modifiers.Select(m =>
                m.Id == "semitone" ? m with { Binding = Reference.Degrees[0].Binding } : m)],
        };

        Assert.Contains(ProfileValidator.Validate(broken), p => p.Message.Contains("collides", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsADeliberatelyInvertedTimingProfile()
    {
        // keyChangeGapMs >= exclusiveSwapMs >= max(pressLeadMs) >= sharpLeadMs.
        // Invert the first relation and the planner's feasibility model silently stops matching
        // the scheduler's, so this must be refused with the exact reason.
        var timing = DeltaForceProfile.Reference.Timing;
        var inverted = timing with { KeyChangeGapMs = timing.KeyChangeGapMs with { V = 4 } };

        var problems = ProfileValidator.Validate(Reference, inverted);
        var problem = Assert.Single(problems, p => p.Field == "timing.keyChangeGapMs");
        Assert.Contains("exclusiveSwapMs", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivesMinSurvivingDurationRatherThanTakingItAsAConstant()
    {
        // Non-vacuous across the two shipped profiles: an earlier max(45, hold+5) form
        // evaluated to a constant 45 under both, which hid the dependency entirely.
        Assert.Equal(40, DeltaForceProfile.Reference.Timing.MinSurvivingDurationMs);
        Assert.Equal(70, DeltaForceProfile.Conservative.Timing.MinSurvivingDurationMs);
    }

    [Fact]
    public void ReportsThreeSeparateCeilingsWithTheModifierSwapAccountingForKeyChange()
    {
        var model = ArticulationModel.Build(DeltaForceProfile.Reference.Emission, DeltaForceProfile.Reference.Timing);

        // Same key is loosest: 1000 / (20 hold + 12 retrigger).
        Assert.Equal(31.25, model.SameKeyCeilingNps, 2);

        // Key change: 1000 / (20 hold + 20 gap).
        Assert.Equal(25.0, model.KeyChangeCeilingNps, 2);

        // A modifier swap does NOT excuse the note keys from their own separation requirement,
        // so the swap ceiling is bounded by max(keyChangeGap, setup) - not by setup alone.
        // The WORST exclusive swap is not the obvious two-event one ({down} -> {up}); it is the
        // THREE-event {down,sharp} -> {up}, which must release two controls and press a third:
        //   lead   = max(down.release 16, sharp.release 8, up.press 12) = 16
        //   swap   = max(16, exclusiveSwapMs 16)                        = 16
        //   stagger= + interModifierStaggerMs 4 * (3 events - 1)        = 24
        //   reqSil = max(keyChangeGapMs 20, 24)                         = 24
        // 1000 / (20 hold + 24) = 22.73 nps. Reporting 25 here would overstate the ceiling by 10%.
        Assert.Equal(1000.0 / (20 + 24), model.ModifierSwapCeilingNps, 3);
        Assert.True(model.SameKeyCeilingNps >= model.KeyChangeCeilingNps);
        Assert.True(model.KeyChangeCeilingNps >= model.ModifierSwapCeilingNps);
    }

    [Fact]
    public void ConservativeProfileMakesModifierSwapsStrictlyMoreExpensive()
    {
        // Under df.conservative the setup (28 swap + 6 stagger = 34) exceeds the key-change gap
        // (30), so the swap ceiling separates from the key-change ceiling. This is the case that
        // proves the max() in the model is load-bearing rather than decorative.
        var model = ArticulationModel.Build(DeltaForceProfile.Conservative.Emission, DeltaForceProfile.Conservative.Timing);

        // Worst swap again three-event: max(16 leads, 28 exclusiveSwap) + 6 stagger * 2 = 40,
        // and max(keyChangeGapMs 30, 40) = 40.
        Assert.True(model.ModifierSwapCeilingNps < model.KeyChangeCeilingNps);
        Assert.Equal(1000.0 / (40 + 40), model.ModifierSwapCeilingNps, 3);
    }

}
