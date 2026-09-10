using HarmonicaScript.Core.Conversion;

namespace HarmonicaScript.Core.Planning;

/// <summary>
/// One transposition candidate, scored on four tiers.
///
/// The tiers are compared LEXICOGRAPHICALLY, not summed. A weighted sum lets a mechanical
/// saving buy a musical loss, and on this instrument that is catastrophic rather than merely
/// suboptimal: "transpose -1 and hold 半音 for the whole piece" is pitch-identical to
/// "transpose 0", so a weighted sum would happily change a piece's key to save button presses.
/// </summary>
/// <param name="Transpose">Semitones added to every source pitch.</param>
/// <param name="HardInfeasible">Transitions the articulation ladder will have to merge or drop. A COUNT, never normalised - normalising made long songs look cheaper to break.</param>
/// <param name="MusicalLossPpm">Duration-weighted loss, in parts per million of the piece.</param>
/// <param name="KeyDistance">Distance from the original key, ignoring octaves: <c>|signedMod12(t)|</c>.</param>
/// <param name="AbsTranspose">Tie-break within a key distance: prefer the smaller absolute shift.</param>
/// <param name="Mechanical">The planner's cost in micro-units.</param>
public readonly record struct CandidateScore(
    int Transpose,
    int HardInfeasible,
    long MusicalLossPpm,
    int KeyDistance,
    int AbsTranspose,
    long Mechanical)
{
    /// <summary>0.5% of the piece's duration. The ONE epsilon in the whole objective, on ONE tier, in ONE unit.</summary>
    public const long MusicalLossEpsilonPpm = 5_000;

    public static int Compare(CandidateScore a, CandidateScore b)
    {
        // Tier 1: hard infeasibility, exact. Never tradeable against anything below it.
        if (a.HardInfeasible != b.HardInfeasible)
        {
            return a.HardInfeasible.CompareTo(b.HardInfeasible);
        }

        // Tier 2: musical loss, with a tolerance so a rounding-level difference does not
        // outrank staying in the original key.
        if (Math.Abs(a.MusicalLossPpm - b.MusicalLossPpm) > MusicalLossEpsilonPpm)
        {
            return a.MusicalLossPpm.CompareTo(b.MusicalLossPpm);
        }

        // Tier 3: key distance, exact. This is the tier that kills the degeneracy trap.
        if (a.KeyDistance != b.KeyDistance)
        {
            return a.KeyDistance.CompareTo(b.KeyDistance);
        }

        if (a.AbsTranspose != b.AbsTranspose)
        {
            return a.AbsTranspose.CompareTo(b.AbsTranspose);
        }

        // Tier 4: mechanics. Only ever a tie-break between musically equivalent options.
        if (a.Mechanical != b.Mechanical)
        {
            return a.Mechanical.CompareTo(b.Mechanical);
        }

        // Fully deterministic final ordering, so two runs produce byte-identical output.
        return a.Transpose.CompareTo(b.Transpose);
    }

    /// <summary>Semitone distance to the nearest octave of the original key, in [-6, +6].</summary>
    public static int SignedMod12(int transpose)
    {
        var m = ((transpose % 12) + 12) % 12;
        return m > 6 ? m - 12 : m;
    }
}

public sealed record TranspositionResult(
    CandidateScore Best,
    byte[] States,
    int[] Offsets,
    LegaliseOutcome Legalise,
    IReadOnlyList<CandidateScore> TopCandidates,
    int CandidatesEvaluated);

/// <summary>
/// Sweeps every transposition that could place any note in range, legalises, plans, and scores.
///
/// There is deliberately NO top-K prefilter. It saves nothing measurable - the whole sweep is a
/// few million integer operations - and it prunes on the wrong axis: out-of-range count is nearly
/// flat across neighbouring transpositions while modifier churn is spiky, so a prefilter can
/// discard the churn-optimal candidate before it is ever scored.
/// </summary>
public static class TranspositionSearch
{
    public static TranspositionResult Search(
        ArticulationModel model,
        IReadOnlyList<byte> pitches,
        IReadOnlyList<long> onsetUs,
        IReadOnlyList<long> durationUs,
        ConversionSettings settings,
        ObjectiveWeights weights)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(pitches);
        ArgumentNullException.ThrowIfNull(settings);

        var emission = model.Emission;
        var p0 = emission.Profile.BaseMidiNote;

        if (pitches.Count == 0)
        {
            return new TranspositionResult(
                new CandidateScore(0, 0, 0, 0, 0, 0), [], [], default, [], 0);
        }

        var minPitch = pitches.Min();
        var maxPitch = pitches.Max();
        var totalDurationUs = Math.Max(1, durationUs.Sum());

        // Widest useful sweep: from "the highest note sits at the bottom of the range" to
        // "the lowest note sits at the top". Roughly 60-120 candidates for real music.
        var lo = Math.Max(-36, p0 + emission.LoOffset - maxPitch);
        var hi = Math.Min(36, p0 + emission.HiOffset - minPitch);

        var candidates = EnumerateCandidates(lo, hi, settings).ToList();
        if (candidates.Count == 0)
        {
            candidates.Add(0);
        }

        CandidateScore? best = null;
        byte[] bestStates = [];
        int[] bestOffsets = [];
        LegaliseOutcome bestLegalise = default;
        var scores = new List<CandidateScore>(candidates.Count);

        var offsets = new int[pitches.Count];
        var onsets = onsetUs.ToArray();
        var durations = durationUs.ToArray();

        foreach (var transpose in candidates)
        {
            for (var i = 0; i < pitches.Count; i++)
            {
                offsets[i] = pitches[i] + transpose - p0;
            }

            var outcome = RangeLegaliser.Legalise(emission, offsets, onsets, durations, settings);

            // Survivors only, with spans recomputed - dropping a note genuinely changes the
            // inter-onset gaps the planner reasons about.
            var survivors = new List<PlanNote>(pitches.Count);
            for (var i = 0; i < offsets.Length; i++)
            {
                if (offsets[i] != int.MinValue)
                {
                    survivors.Add(new PlanNote(offsets[i], onsets[i], durations[i]));
                }
            }

            var plan = survivors.Count == 0
                ? new PlanResult([], 0, 0, 0, 0, 0)
                : ModifierPlanner.Plan(model, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(survivors), weights);

            var score = new CandidateScore(
                transpose,
                plan.HardInfeasibleCount,
                MusicalLossPpm(outcome, totalDurationUs),
                Math.Abs(CandidateScore.SignedMod12(transpose)),
                Math.Abs(transpose),
                plan.Cost == long.MaxValue ? long.MaxValue : plan.Cost);

            scores.Add(score);

            if (best is null || CandidateScore.Compare(score, best.Value) < 0)
            {
                best = score;
                bestStates = plan.States;
                bestOffsets = (int[])offsets.Clone();
                bestLegalise = outcome;
            }
        }

        scores.Sort(CandidateScore.Compare);

        return new TranspositionResult(
            best!.Value,
            bestStates,
            bestOffsets,
            bestLegalise,
            scores.Take(5).ToList(),
            candidates.Count);
    }

    /// <summary>
    /// Duration-weighted, in parts per million, entirely in integers.
    ///
    /// Dropping a HIGH note costs 4x a low one: the melody is on top, so losing it is a
    /// different kind of damage from losing an inner voice. An octave fold costs 0.35 - real,
    /// but far less than silence.
    /// </summary>
    private static long MusicalLossPpm(LegaliseOutcome outcome, long totalDurationUs)
    {
        // Scaled by 20 so 1 / 4 / 0.35 become 20 / 80 / 7 with no floating point.
        var weighted = (outcome.DroppedDurationLowUs * 20)
            + (outcome.DroppedDurationHighUs * 80)
            + (outcome.FoldedDurationUs * 7);

        // ppm = (weighted / 20) / total * 1e6 = weighted * 50_000 / total.
        // Computed in this order so the division happens last: dividing the denominator first
        // truncates badly on short pieces (a 175 ms fixture would be off by 16%).
        // Bound: a 30-minute piece gives weighted <= 1.5e11, so weighted * 50_000 <= 7.5e15,
        // comfortably inside int64.
        return weighted / Math.Max(1, totalDurationUs) * 50_000
            + ((weighted % Math.Max(1, totalDurationUs)) * 50_000 / Math.Max(1, totalDurationUs));
    }

    private static IEnumerable<int> EnumerateCandidates(int lo, int hi, ConversionSettings settings)
    {
        switch (settings.TranspositionMode)
        {
            case TranspositionMode.Manual:
                yield return settings.ManualTranspose;
                break;

            case TranspositionMode.PreserveKey:
                // Whole octaves only, so the piece keeps its key for playing along with a recording.
                for (var t = lo; t <= hi; t++)
                {
                    if (t % 12 == 0)
                    {
                        yield return t;
                    }
                }

                break;

            default:
                for (var t = lo; t <= hi; t++)
                {
                    yield return t;
                }

                break;
        }
    }
}
