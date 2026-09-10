using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Tests;

/// <summary>
/// Generates planning instances that are actually hard.
///
/// A uniform generator over the 38 offsets is nearly useless here: 30 of them have exactly one
/// feasible state, so a uniform instance is mostly forced and the DP has no choice to get wrong.
/// This deliberately over-samples the eight multi-way offsets and the adjacent pairs that force
/// an exclusion-group swap, which is where every interesting bug lives.
/// </summary>
public static class PlanGenerator
{
    /// <summary>The two three-way and six two-way offsets, where the planner actually has a choice.</summary>
    public static readonly int[] MultiWayOffsets = [0, 12, -7, 1, 5, 13, 17, 24];

    /// <summary>Extremes that can only be reached from opposite ends of the exclusion group.</summary>
    public static readonly int[] SwapForcingOffsets = [-12, -11, -10, 23, 24, 25];

    public static PlanNote[] Random(Random random, int count, int minGapMs = 20, int maxGapMs = 400)
    {
        ArgumentNullException.ThrowIfNull(random);

        var notes = new PlanNote[count];
        long onsetUs = 0;

        for (var i = 0; i < count; i++)
        {
            var offset = random.Next(100) switch
            {
                < 55 => MultiWayOffsets[random.Next(MultiWayOffsets.Length)],
                < 80 => SwapForcingOffsets[random.Next(SwapForcingOffsets.Length)],
                _ => random.Next(-12, 26),
            };

            var gapUs = random.Next(minGapMs, maxGapMs) * 1000L;
            notes[i] = new PlanNote(offset, onsetUs, Math.Max(1000, gapUs - 10_000));
            onsetUs += gapUs;
        }

        return notes;
    }

    /// <summary>Exhaustive optimum over every legal state sequence. Only tractable for tiny N.</summary>
    public static (long Cost, byte[] States) BruteForce(
        ArticulationModel model,
        PlanNote[] notes,
        ObjectiveWeights weights)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(notes);

        var k = model.StateCount;
        var states = new byte[notes.Length];
        var best = long.MaxValue;
        byte[]? bestStates = null;

        void Recurse(int index)
        {
            if (index == notes.Length)
            {
                var cost = ModifierPlanner.ScorePlan(model, notes, states, weights);
                if (cost < best)
                {
                    best = cost;
                    bestStates = (byte[])states.Clone();
                }

                return;
            }

            var mask = model.Emission.StateMask(notes[index].Offset);
            for (var s = 0; s < k; s++)
            {
                if ((mask & (1 << s)) == 0)
                {
                    continue;
                }

                states[index] = (byte)s;
                Recurse(index + 1);
            }
        }

        Recurse(0);
        return (best, bestStates ?? []);
    }
}
