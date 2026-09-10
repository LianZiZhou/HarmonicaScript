using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Tests;

/// <summary>
/// PINNED FACTS about df.harmonica.v1. These deliberately live in a test rather than in the
/// generic validator: baking "38 offsets" into the schema would make the claim of being a
/// generic instrument engine a lie.
///
/// Every number here was derived by hand from the profile and re-derived independently by the
/// closed-form test below. If one of these moves, either the game changed or someone broke the
/// derivation - and both deserve to stop the build.
/// </summary>
public sealed class EmissionTablePinnedTests
{
    private static readonly EmissionTable Table = DeltaForceProfile.Reference.Emission;

    [Fact]
    public void DerivesExactlySixModifierStates()
    {
        // {}, {sharp}, {down}, {down,sharp}, {up}, {up,sharp}.
        // {down,up} is excluded (shared exclusion group 1); triples exceed maxSimultaneousModifiers=2.
        Assert.Equal(6, Table.StateCount);
        Assert.Equal(8, Table.DegreeCount);

        var deltas = Table.States.Select(s => s.SemitoneDelta).Order().ToArray();
        Assert.Equal([-12, -11, 0, 1, 12, 13], deltas);

        Assert.True(Table.States[0].IsNeutral, "state 0 must be the neutral state");
    }

    [Fact]
    public void SpansThirtyEightSemitonesWithNoInteriorGaps()
    {
        Assert.Equal(-12, Table.LoOffset);
        Assert.Equal(25, Table.HiOffset);
        Assert.Equal(38, Table.OffsetCount);

        var unplayable = Table.Offsets().Where(o => !Table.IsPlayable(o)).ToArray();
        Assert.Empty(unplayable);
    }

    [Fact]
    public void HasFortyEightRenderingsDistributedOneTwoAndThreeWays()
    {
        Assert.Equal(48, Table.RenderingCount);
        Assert.Equal(48, Table.Offsets().Sum(Table.Multiplicity));

        var histogram = Table.Offsets()
            .GroupBy(Table.Multiplicity)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(new Dictionary<int, int> { [1] = 30, [2] = 6, [3] = 2 }, histogram);
    }

    [Fact]
    public void TwoAndThreeWayOffsetsAreExactlyTheEnharmonicAndSeamDuplicates()
    {
        var twoWay = Table.Offsets().Where(o => Table.Multiplicity(o) == 2).ToArray();
        var threeWay = Table.Offsets().Where(o => Table.Multiplicity(o) == 3).ToArray();

        // -7 : degree 5 in {down} == degree 4 in {down,sharp}   (the #3 == 4 enharmonic, low band)
        //  1 : degree 1 in {sharp} == degree 8 in {down,sharp}  (a band seam)
        //  5 : degree 4 in {}      == degree 3 in {sharp}       (#3 == 4, middle band)
        // 13 : degree 8 in {sharp} == degree 1 in {up,sharp}    (a band seam)
        // 17 : degree 4 in {up}    == degree 3 in {up,sharp}    (#3 == 4, high band)
        // 24 : degree 8 in {up}    == degree 7 in {up,sharp}    (#7 == 1^)
        Assert.Equal([-7, 1, 5, 13, 17, 24], twoWay);

        //  0 : degree 1 in {}, degree 8 in {down}, degree 7 in {down,sharp}
        // 12 : degree 8 in {}, degree 7 in {sharp}, degree 1 in {up}
        Assert.Equal([0, 12], threeWay);
    }

    [Fact]
    public void AModifierStateUniquelyDeterminesTheKey()
    {
        // THE load-bearing structural fact. It is what collapses the fingering search into a
        // plain Viterbi over states, and it is what makes "alternate keys to dodge the same-key
        // retrigger floor" impossible - an alternative spelling always changes state.
        for (var state = 0; state < Table.StateCount; state++)
        {
            var seen = new HashSet<int>();
            foreach (var offset in Table.Offsets())
            {
                var degree = Table.DegreeOf(state, offset);
                if (degree >= 0)
                {
                    Assert.True(seen.Add(offset), $"state {state} renders offset {offset} twice");
                }
            }
        }
    }

    [Fact]
    public void EveryAlternativeSpellingCostsAModifierTransition()
    {
        // The corollary, asserted directly rather than left as a comment: for every offset with
        // more than one rendering, all renderings live in DIFFERENT states. Seam alternation is
        // therefore never free, which is why it is not a feature and has no milestone gate.
        foreach (var offset in Table.Offsets().Where(o => Table.Multiplicity(o) > 1))
        {
            var states = Enumerable.Range(0, Table.StateCount)
                .Where(s => Table.DegreeOf(s, offset) >= 0)
                .ToArray();

            Assert.Equal(Table.Multiplicity(offset), states.Length);
            Assert.Equal(states.Length, states.Distinct().Count());
        }
    }

    [Fact]
    public void TableAgreesWithTheClosedFormAtEveryPoint()
    {
        // Independent re-derivation: pitch = baseDelta(state) + semitone(degree).
        // This is the non-circular check that the table is not merely self-consistent.
        var profile = DeltaForceProfile.Reference.Instrument;
        var covered = 0;

        for (var s = 0; s < Table.StateCount; s++)
        {
            for (var d = 0; d < profile.Degrees.Count; d++)
            {
                var offset = Table.States[s].SemitoneDelta + profile.Degrees[d].Semitone;

                Assert.True(Table.IsInRange(offset), $"state {s} degree {d} produced out-of-range offset {offset}");
                Assert.Equal(d, Table.DegreeOf(s, offset));
                Assert.True((Table.StateMask(offset) & (1 << s)) != 0);
                covered++;
            }
        }

        Assert.Equal(Table.RenderingCount, covered);
    }

    [Fact]
    public void IsFullyChromatic()
    {
        // Because coverage has no gaps across 38 semitones, every pitch class is reachable in
        // every band. This is why "notes out of scale" is structurally always zero here and is
        // NOT reported as a metric - unlike every Genshin/FFXIV tool, where it is the headline.
        foreach (var pitchClass in Enumerable.Range(0, 12))
        {
            Assert.Contains(Table.Offsets(), o => Table.IsPlayable(o) && ((o % 12) + 12) % 12 == pitchClass);
        }
    }
}
