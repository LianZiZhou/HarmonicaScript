using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Planning;

/// <summary>One note as the planner sees it: a semitone offset plus its timing.</summary>
public readonly record struct PlanNote(int Offset, long OnsetUs, long DurationUs);

/// <summary>The chosen modifier plan and what it cost.</summary>
public sealed record PlanResult(
    byte[] States,
    long Cost,
    int HardInfeasibleCount,
    int TotalEvents,
    int ExclusiveSwaps,
    long TotalDeficitMs)
{
    public static PlanResult Infeasible { get; } = new([], long.MaxValue, int.MaxValue, 0, 0, 0);

    public bool IsFeasible => Cost != long.MaxValue;
}

/// <summary>
/// Exact Viterbi over the derived modifier states.
///
/// This is the piece that makes HarmonicaScript better than a lookup table. Every surveyed prior
/// tool maps each pitch to one fixed key; here, wherever the emission table offers a choice, the
/// choice is made to minimise real mechanical cost across the whole piece rather than note by note.
///
/// Complexity is O(k^2 * N) with k derived from the profile (6 here) - about 36 integer edge
/// evaluations per note, so a 2000-note piece across 120 transposition candidates is under ten
/// million cheap operations.
/// </summary>
public static class ModifierPlanner
{
    /// <summary>
    /// Silence achievable before note <paramref name="i"/> by truncating its predecessor ALONE.
    ///
    /// Deliberately PESSIMISTIC: it excludes the scheduler's onset-shift budget. That makes the
    /// planner's feasibility judgement a sound LOWER BOUND - any edge it believes feasible is
    /// realisable by a purely local operation that cannot cascade - which is what lets the whole
    /// thing be a single pass with no iteration and no convergence assumption. The price is mild
    /// conservatism, reported as ConservativeRespells rather than hidden.
    /// </summary>
    private static long AvailableSilenceUs(long spanUs, int noteHoldMinMs) =>
        Math.Max(0, spanUs - (noteHoldMinMs * 1000L));

    public static PlanResult Plan(
        ArticulationModel model,
        ReadOnlySpan<PlanNote> notes,
        ObjectiveWeights weights)
    {
        ArgumentNullException.ThrowIfNull(model);

        var n = notes.Length;
        if (n == 0)
        {
            return new PlanResult([], 0, 0, 0, 0, 0);
        }

        var k = model.StateCount;
        var emission = model.Emission;
        var holdMinMs = model.Timing.NoteHoldMinMs.V;

        var masks = new ushort[n];
        for (var i = 0; i < n; i++)
        {
            masks[i] = emission.StateMask(notes[i].Offset);
            if (masks[i] == 0)
            {
                // Out-of-range notes must be legalised away BEFORE planning. Reaching here means
                // a caller skipped LegaliseRange, which would otherwise make every candidate
                // infeasible and silently collapse the whole transposition search.
                throw new InvalidOperationException(
                    $"Note {i} has offset {notes[i].Offset}, which no state can render. "
                    + "Range legalisation must run inside the candidate loop, before planning.");
            }
        }

        var duty = BuildDutyPerSecond(emission, weights);

        var previous = new long[k];
        var current = new long[k];
        var back = new byte[k * n];
        Array.Fill(previous, long.MaxValue);

        // Entry: the instrument starts neutral, so the first note pays for arming whatever it needs.
        var firstSpan = SpanUs(notes, 0);
        for (var st = 0; st < k; st++)
        {
            if ((masks[0] & (1 << st)) == 0)
            {
                continue;
            }

            var (edge, _) = EdgeCost(model, weights, 0, st, sameKey: false, availSilUs: long.MaxValue, baseSilUs: 0);
            previous[st] = edge + NodeCost(duty, emission, st, firstSpan);
        }

        for (var i = 1; i < n; i++)
        {
            var spanUs = SpanUs(notes, i);
            var availSil = AvailableSilenceUs(notes[i].OnsetUs - notes[i - 1].OnsetUs, holdMinMs);
            var baseSil = BaselineSilenceUs(model, masks[i - 1], masks[i], notes[i - 1].Offset, notes[i].Offset);

            Array.Fill(current, long.MaxValue);

            for (var b = 0; b < k; b++)
            {
                if ((masks[i] & (1 << b)) == 0)
                {
                    continue;
                }

                var best = long.MaxValue;
                byte bestFrom = 0;

                for (var a = 0; a < k; a++)
                {
                    if ((masks[i - 1] & (1 << a)) == 0 || previous[a] == long.MaxValue)
                    {
                        continue;
                    }

                    var sameKey = emission.DegreeOf(a, notes[i - 1].Offset) == emission.DegreeOf(b, notes[i].Offset);
                    var (edge, _) = EdgeCost(model, weights, a, b, sameKey, availSil, baseSil);
                    var total = previous[a] + edge;
                    if (total < best)
                    {
                        best = total;
                        bestFrom = (byte)a;
                    }
                }

                if (best != long.MaxValue)
                {
                    current[b] = best + NodeCost(duty, emission, b, spanUs);
                    back[(i * k) + b] = bestFrom;
                }
            }

            (previous, current) = (current, previous);
        }

        // Exit: everything is released at the end of the piece.
        var answer = long.MaxValue;
        byte finalState = 0;
        for (var st = 0; st < k; st++)
        {
            if (previous[st] == long.MaxValue)
            {
                continue;
            }

            var (exit, _) = EdgeCost(model, weights, st, 0, sameKey: false, availSilUs: long.MaxValue, baseSilUs: 0);
            var total = previous[st] + exit;
            if (total < answer)
            {
                answer = total;
                finalState = (byte)st;
            }
        }

        if (answer == long.MaxValue)
        {
            return PlanResult.Infeasible;
        }

        var states = new byte[n];
        states[n - 1] = finalState;
        for (var i = n - 1; i > 0; i--)
        {
            states[i - 1] = back[(i * k) + states[i]];
        }

        var (hard, events, swaps, deficitMs) = Audit(model, weights, notes, states, holdMinMs);
        return new PlanResult(states, answer, hard, events, swaps, deficitMs);
    }

    /// <summary>
    /// Re-walks a chosen plan and recomputes its statistics from scratch.
    ///
    /// Deliberately independent of the forward pass: if the DP's bookkeeping and this disagree,
    /// a self-consistency test fails rather than the report quietly reporting fiction.
    /// </summary>
    public static (int Hard, int Events, int Swaps, long DeficitMs) Audit(
        ArticulationModel model,
        ObjectiveWeights weights,
        ReadOnlySpan<PlanNote> notes,
        ReadOnlySpan<byte> states,
        int holdMinMs)
    {
        ArgumentNullException.ThrowIfNull(model);

        var emission = model.Emission;
        var hard = 0;
        var events = 0;
        var swaps = 0;
        long deficitUs = 0;

        for (var i = 0; i < notes.Length; i++)
        {
            var from = i == 0 ? 0 : states[i - 1];
            var to = states[i];

            events += model.EventCount(from, to);
            if (model.IsExclusiveSwap(from, to))
            {
                swaps++;
            }

            if (i == 0)
            {
                continue;
            }

            var sameKey = emission.DegreeOf(from, notes[i - 1].Offset) == emission.DegreeOf(to, notes[i].Offset);
            var availSil = AvailableSilenceUs(notes[i].OnsetUs - notes[i - 1].OnsetUs, holdMinMs);
            var baseSil = BaselineSilenceUs(model, emission.StateMask(notes[i - 1].Offset), emission.StateMask(notes[i].Offset), notes[i - 1].Offset, notes[i].Offset);
            var (_, deficit) = EdgeCost(model, weights, from, to, sameKey, availSil, baseSil);

            if (deficit > 0)
            {
                hard++;
                deficitUs += deficit;
            }
        }

        // Release everything at the end.
        events += model.EventCount(states.Length == 0 ? 0 : states[^1], 0);

        return (hard, events, swaps, deficitUs / 1000);
    }

    /// <summary>
    /// Total cost of a GIVEN state sequence, computed independently of the forward pass.
    ///
    /// Two jobs. It lets a test brute-force the optimum over every sequence for small N and
    /// compare exactly against <see cref="Plan"/>. And it lets the planner check its own answer:
    /// if the DP's reported cost and this disagree, that is a bug in the bookkeeping, and a
    /// self-consistency test catches it rather than the report quietly stating fiction.
    /// </summary>
    public static long ScorePlan(
        ArticulationModel model,
        ReadOnlySpan<PlanNote> notes,
        ReadOnlySpan<byte> states,
        ObjectiveWeights weights)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (notes.Length != states.Length)
        {
            throw new ArgumentException("A state must be supplied for every note.", nameof(states));
        }

        if (notes.Length == 0)
        {
            return 0;
        }

        var emission = model.Emission;
        var holdMinMs = model.Timing.NoteHoldMinMs.V;
        var duty = BuildDutyPerSecond(emission, weights);
        long total = 0;

        for (var i = 0; i < notes.Length; i++)
        {
            if ((emission.StateMask(notes[i].Offset) & (1 << states[i])) == 0)
            {
                return long.MaxValue;   // the sequence asks for a state that cannot render the note
            }

            if (i == 0)
            {
                total += EdgeCost(model, weights, 0, states[0], sameKey: false, long.MaxValue, 0).Cost;
            }
            else
            {
                var sameKey = emission.DegreeOf(states[i - 1], notes[i - 1].Offset)
                    == emission.DegreeOf(states[i], notes[i].Offset);
                var availSil = AvailableSilenceUs(notes[i].OnsetUs - notes[i - 1].OnsetUs, holdMinMs);
                var baseSil = BaselineSilenceUs(
                    model,
                    emission.StateMask(notes[i - 1].Offset),
                    emission.StateMask(notes[i].Offset),
                    notes[i - 1].Offset,
                    notes[i].Offset);

                total += EdgeCost(model, weights, states[i - 1], states[i], sameKey, availSil, baseSil).Cost;
            }

            total += NodeCost(duty, emission, states[i], SpanUs(notes, i));
        }

        total += EdgeCost(model, weights, states[^1], 0, sameKey: false, long.MaxValue, 0).Cost;
        return total;
    }

    private static long SpanUs(ReadOnlySpan<PlanNote> notes, int i) =>
        i < notes.Length - 1 ? notes[i + 1].OnsetUs - notes[i].OnsetUs : notes[i].DurationUs;

    /// <summary>
    /// Duty is charged over the INTER-ONSET span, not the note's own duration, because that is
    /// exactly the hold time the maximal-run lowering will produce. Charging over the duration
    /// would under-count a staccato passage that still holds the modifier through the rests.
    /// </summary>
    private static long NodeCost(long[] dutyPerSecond, EmissionTable emission, int state, long spanUs)
    {
        var mask = emission.States[state].Mask;
        if (mask == 0)
        {
            return 0;
        }

        long total = 0;
        for (var m = 0; m < dutyPerSecond.Length; m++)
        {
            if ((mask & (1 << m)) != 0)
            {
                total += dutyPerSecond[m] * spanUs / 1_000_000L;
            }
        }

        return total;
    }

    private static long[] BuildDutyPerSecond(EmissionTable emission, ObjectiveWeights weights)
    {
        var modifiers = emission.Profile.Modifiers;
        var duty = new long[modifiers.Count];
        for (var i = 0; i < modifiers.Count; i++)
        {
            duty[i] = weights.DutyPerSecond * modifiers[i].DutyWeightMilli / 1000L;
        }

        return duty;
    }

    /// <summary>
    /// Cost of moving from state <paramref name="a"/> to <paramref name="b"/> between two notes,
    /// plus the marginal silence deficit that move is responsible for.
    /// </summary>
    private static (long Cost, long DeficitUs) EdgeCost(
        ArticulationModel model,
        ObjectiveWeights weights,
        int a,
        int b,
        bool sameKey,
        long availSilUs,
        long baseSilUs)
    {
        var events = model.EventCount(a, b);
        var swap = model.IsExclusiveSwap(a, b);

        var cost = (weights.Event * events) + (swap ? weights.ExclusiveSwap : 0);

        if (availSilUs == long.MaxValue)
        {
            return (cost, 0);
        }

        var requiredUs = model.RequiredSilenceMs(a, b, sameKey) * 1000L;

        // Bill ONLY the marginal deficit this plan is responsible for. Subtracting the baseline
        // stops pure note-density infeasibility - 15 ms inter-onsets against a 20 ms hold floor -
        // from being charged to the modifier plan and then amplified by the deficit weight.
        // Density infeasibility is reported separately, as DensityDeficitCount.
        var deficitUs = Math.Max(0, requiredUs - availSilUs) - Math.Max(0, baseSilUs - availSilUs);
        if (deficitUs <= 0)
        {
            return (cost, 0);
        }

        cost += weights.SoftDeficitPerMs * deficitUs / 1000L;
        cost += weights.HardPenalty;
        return (cost, deficitUs);
    }

    private static long BaselineSilenceUs(
        ArticulationModel model,
        ushort fromMask,
        ushort toMask,
        int fromOffset,
        int toOffset)
    {
        var emission = model.Emission;
        var best = int.MaxValue;

        for (var a = 0; a < model.StateCount; a++)
        {
            if ((fromMask & (1 << a)) == 0)
            {
                continue;
            }

            for (var b = 0; b < model.StateCount; b++)
            {
                if ((toMask & (1 << b)) == 0)
                {
                    continue;
                }

                var sameKey = emission.DegreeOf(a, fromOffset) == emission.DegreeOf(b, toOffset);
                best = Math.Min(best, model.RequiredSilenceMs(a, b, sameKey));
            }
        }

        return best == int.MaxValue ? 0 : best * 1000L;
    }
}
