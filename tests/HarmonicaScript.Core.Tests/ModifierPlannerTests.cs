using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Tests;

public sealed class ModifierPlannerTests
{
    private static ArticulationModel Model =>
        ArticulationModel.Build(DeltaForceProfile.Reference.Emission, DeltaForceProfile.Reference.Timing);

    private static readonly ObjectiveWeights Weights = ObjectiveWeights.Default;

    [Fact]
    public void MatchesBruteForceExactlyOnTwoThousandInstances()
    {
        // THE central correctness gate for M4. Exact int64 equality, not an epsilon: the whole
        // point of an integer optimiser is that "as good as" and "equal to" are the same claim.
        var model = Model;
        var random = new Random(20260910);
        var checkedCount = 0;

        for (var trial = 0; trial < 2000; trial++)
        {
            var notes = PlanGenerator.Random(random, random.Next(2, 7));
            var expected = PlanGenerator.BruteForce(model, notes, Weights);
            var actual = ModifierPlanner.Plan(model, notes, Weights);

            Assert.Equal(expected.Cost, actual.Cost);
            checkedCount++;
        }

        Assert.Equal(2000, checkedCount);
    }

    [Fact]
    public void ReportedCostEqualsAnIndependentRescoreOfItsOwnPlan()
    {
        // Self-consistency: the forward pass and ScorePlan are separate implementations of the
        // same objective. If they drift, the report starts stating fiction.
        var model = Model;
        var random = new Random(4242);

        for (var trial = 0; trial < 500; trial++)
        {
            var notes = PlanGenerator.Random(random, random.Next(2, 40));
            var result = ModifierPlanner.Plan(model, notes, Weights);

            Assert.Equal(result.Cost, ModifierPlanner.ScorePlan(model, notes, result.States, Weights));
        }
    }

    [Fact]
    public void RaisingTheEventWeightNeverRaisesTheEventCount()
    {
        // Monotonicity. If making events more expensive produced MORE events, the objective and
        // the search would be disagreeing about what they are optimising.
        var model = Model;
        var random = new Random(7);

        for (var trial = 0; trial < 200; trial++)
        {
            var notes = PlanGenerator.Random(random, random.Next(4, 25));

            var cheap = ModifierPlanner.Plan(model, notes, Weights);
            var dear = ModifierPlanner.Plan(model, notes, Weights with { Event = Weights.Event * 8 });

            Assert.True(
                dear.TotalEvents <= cheap.TotalEvents,
                $"raising the event weight raised the event count: {cheap.TotalEvents} -> {dear.TotalEvents}");
        }
    }

    [Fact]
    public void EveryEdgeItCallsFeasibleIsRealisableByTruncatingThePreviousNoteAlone()
    {
        // Soundness of the pessimistic availSil. This is what buys the single-pass design: if a
        // transition the DP accepted needed the scheduler's shift budget, the planner and the
        // scheduler would be modelling different instruments.
        var model = Model;
        var random = new Random(99);
        var holdMinUs = model.Timing.NoteHoldMinMs.V * 1000L;

        for (var trial = 0; trial < 300; trial++)
        {
            var notes = PlanGenerator.Random(random, random.Next(3, 30), minGapMs: 25, maxGapMs: 300);
            var plan = ModifierPlanner.Plan(model, notes, Weights);

            for (var i = 1; i < notes.Length; i++)
            {
                var sameKey = model.Emission.DegreeOf(plan.States[i - 1], notes[i - 1].Offset)
                    == model.Emission.DegreeOf(plan.States[i], notes[i].Offset);
                var requiredUs = model.RequiredSilenceMs(plan.States[i - 1], plan.States[i], sameKey) * 1000L;
                var spanUs = notes[i].OnsetUs - notes[i - 1].OnsetUs;

                // Truncating the predecessor to its minimum hold is a purely local operation.
                var achievableUs = Math.Max(0, spanUs - holdMinUs);
                var deficitUs = requiredUs - achievableUs;

                if (deficitUs > 0)
                {
                    // Only allowed when NO state pair could have done better - i.e. the deficit
                    // is the note density's fault, not the modifier plan's.
                    var baseline = model.BaselineSilenceMs(
                        model.Emission.StateMask(notes[i - 1].Offset),
                        model.Emission.StateMask(notes[i].Offset),
                        (a, b) => model.Emission.DegreeOf(a, notes[i - 1].Offset) == model.Emission.DegreeOf(b, notes[i].Offset));

                    Assert.True(
                        baseline * 1000L > achievableUs,
                        $"note {i}: the plan needs {requiredUs / 1000}ms but only {achievableUs / 1000}ms is available, "
                        + $"and a cheaper state pair existed ({baseline}ms)");
                }
            }
        }
    }

    [Fact]
    public void PrefersNaturalSpellingsWhenNothingElseSeparatesThem()
    {
        // Offset 5 is renderable as degree 4 in the neutral state, or degree 3 with 半音 held.
        // The duty term makes the natural spelling win without a hand-written rule.
        var model = Model;
        PlanNote[] notes =
        [
            new(5, 0, 400_000),
            new(5, 500_000, 400_000),
            new(5, 1_000_000, 400_000),
        ];

        var plan = ModifierPlanner.Plan(model, notes, Weights);
        Assert.All(plan.States, s => Assert.Equal(0, model.Emission.States[s].Mask));
    }

    [Fact]
    public void HoldsAModifierAcrossARunRatherThanRetogglingIt()
    {
        // Eight notes that are ALL renderable in the high band: offsets 12 + {0,2,4,5,7,9,11,12}.
        // One press at the start, one release at the end, nothing in between.
        var model = Model;
        int[] highBand = [12, 14, 16, 17, 19, 21, 23, 24];
        var notes = highBand.Select((off, i) => new PlanNote(off, i * 400_000L, 300_000)).ToArray();

        var plan = ModifierPlanner.Plan(model, notes, Weights);

        Assert.Equal(2, plan.TotalEvents);          // one press, one release, for eight notes
        Assert.Equal(0, plan.ExclusiveSwaps);
        Assert.Equal(PlanGenerator.BruteForce(model, notes, Weights).Cost, plan.Cost);

        // It uses TWO states, not one, and that is the better answer: offset 12 is three-way, so
        // the first note is played neutrally (degree 8) and 升调 is not pressed until the second
        // note needs it. Same event count, strictly less time holding a mouse button down.
        Assert.Equal([0, 4, 4, 4, 4, 4, 4, 4], plan.States.Select(s => (int)s));
    }

    [Fact]
    public void TogglesTheSharpWhenTheMelodyGenuinelyRequiresIt()
    {
        // Offset 15 is renderable ONLY with 半音 held (13 + 2); offsets 14 and 16 only without it
        // (12 + 2 and 12 + 4). So an alternating line is genuinely two events per note and no
        // planner can do better - the emission table simply offers no alternative. This is the
        // counterpart to the run test: it proves the low event count above is a real optimisation
        // and not the planner refusing to press anything.
        var model = Model;
        int[] alternating = [14, 15, 16, 15, 14];
        var notes = alternating.Select((off, i) => new PlanNote(off, i * 400_000L, 300_000)).ToArray();

        var plan = ModifierPlanner.Plan(model, notes, Weights);

        Assert.Equal(1, model.Emission.Multiplicity(15));
        Assert.Equal(PlanGenerator.BruteForce(model, notes, Weights).Cost, plan.Cost);

        // Press 升调, then toggle 半音 on/off/on/off, then release 升调 = 6 events. There is no
        // cheaper plan, because 15 has exactly one rendering and 14/16 have exactly one each.
        Assert.Equal(6, plan.TotalEvents);
    }

    [Fact]
    public void SolvesTwoThousandNotesAcrossTheWholeCandidateRangeQuickly()
    {
        // Budget: the GUI re-runs the whole search on every settings change, so the interactive
        // feel depends on this staying in the tens of milliseconds.
        var model = Model;
        var random = new Random(2024);
        var notes = PlanGenerator.Random(random, 2000, minGapMs: 60, maxGapMs: 300);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var candidate = 0; candidate < 120; candidate++)
        {
            ModifierPlanner.Plan(model, notes, Weights);
        }

        stopwatch.Stop();
        Assert.True(
            stopwatch.ElapsedMilliseconds < 2000,
            $"120 candidates x 2000 notes took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void ThrowsRatherThanSilentlyCollapsingWhenGivenAnUnrenderableNote()
    {
        // If an out-of-range note reached the planner, every candidate would come back infeasible
        // and the transposition search would pick arbitrarily. Fail loudly instead.
        var model = Model;
        PlanNote[] notes = [new(0, 0, 100_000), new(99, 200_000, 100_000)];

        var ex = Assert.Throws<InvalidOperationException>(() => ModifierPlanner.Plan(model, notes, Weights));
        Assert.Contains("legalisation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
