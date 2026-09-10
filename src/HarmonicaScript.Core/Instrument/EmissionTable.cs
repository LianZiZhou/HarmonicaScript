using System.Numerics;

namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// Everything the optimiser needs to know about which fingerings render which pitches,
/// precomputed once per profile.
///
/// The structural fact this exploits: because the degrees have pairwise-distinct semitone
/// offsets, <em>a modifier state uniquely determines the physical key</em> for any playable
/// pitch. So a "fingering" collapses to a state, and the joint transposition + modifier
/// problem becomes an exact Viterbi over a state space derived from the profile rather than
/// a search over (key, state) pairs.
///
/// A corollary that matters, and that killed an attractive-looking optimisation: alternative
/// spellings of the same pitch <em>always</em> differ in state, so alternating keys to dodge
/// the same-key retrigger floor always costs a modifier transition. There is no free lunch at
/// the band seams.
/// </summary>
public sealed class EmissionTable
{
    private readonly sbyte[] _degreeOf;   // [stateIndex * OffsetCount + (offset - LoOffset)] -> degree index, or -1
    private readonly ushort[] _stateMask; // [offset - LoOffset] -> bitset over state indices

    private EmissionTable(
        InstrumentProfile profile,
        IReadOnlyList<ModState> states,
        int loOffset,
        int hiOffset,
        sbyte[] degreeOf,
        ushort[] stateMask)
    {
        Profile = profile;
        States = states;
        LoOffset = loOffset;
        HiOffset = hiOffset;
        _degreeOf = degreeOf;
        _stateMask = stateMask;
    }

    public InstrumentProfile Profile { get; }

    /// <summary>The derived legal modifier states, in ascending mask order. Index 0 is always neutral.</summary>
    public IReadOnlyList<ModState> States { get; }

    public int StateCount => States.Count;

    public int DegreeCount => Profile.Degrees.Count;

    /// <summary>Lowest renderable semitone offset relative to the profile's base pitch (-12 here).</summary>
    public int LoOffset { get; }

    /// <summary>Highest renderable semitone offset relative to the profile's base pitch (+25 here).</summary>
    public int HiOffset { get; }

    public int OffsetCount => HiOffset - LoOffset + 1;

    /// <summary>Total number of (state, degree) pairs, i.e. StateCount * DegreeCount (48 here).</summary>
    public int RenderingCount => StateCount * DegreeCount;

    public bool IsInRange(int offset) => offset >= LoOffset && offset <= HiOffset;

    /// <summary>Which degree renders <paramref name="offset"/> in <paramref name="stateIndex"/>, or -1.</summary>
    public sbyte DegreeOf(int stateIndex, int offset) =>
        IsInRange(offset) ? _degreeOf[(stateIndex * OffsetCount) + (offset - LoOffset)] : (sbyte)-1;

    /// <summary>Bitset of the states that can render <paramref name="offset"/>. Zero means unplayable.</summary>
    public ushort StateMask(int offset) => IsInRange(offset) ? _stateMask[offset - LoOffset] : (ushort)0;

    public bool IsPlayable(int offset) => StateMask(offset) != 0;

    /// <summary>How many distinct (state, degree) pairs render <paramref name="offset"/>.</summary>
    public int Multiplicity(int offset) => BitOperations.PopCount(StateMask(offset));

    /// <summary>Every renderable offset, ascending.</summary>
    public IEnumerable<int> Offsets()
    {
        for (var o = LoOffset; o <= HiOffset; o++)
        {
            yield return o;
        }
    }

    /// <summary>
    /// Builds the table by enumerating every modifier subset that respects the exclusion groups
    /// and the simultaneity cap, then every (state, degree) pair.
    /// </summary>
    public static EmissionTable Build(InstrumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var states = DeriveStates(profile);
        if (states.Count == 0)
        {
            throw new InvalidOperationException($"Profile '{profile.Id}' derives no legal modifier states.");
        }

        if (states.Count > 16)
        {
            // The state mask is a ushort and the planner's transition tables are k*k.
            throw new InvalidOperationException(
                $"Profile '{profile.Id}' derives {states.Count} modifier states; the engine supports at most 16.");
        }

        var lo = int.MaxValue;
        var hi = int.MinValue;
        foreach (var state in states)
        {
            foreach (var degree in profile.Degrees)
            {
                var offset = state.SemitoneDelta + degree.Semitone;
                lo = Math.Min(lo, offset);
                hi = Math.Max(hi, offset);
            }
        }

        var offsetCount = hi - lo + 1;
        var degreeOf = new sbyte[states.Count * offsetCount];
        Array.Fill(degreeOf, (sbyte)-1);
        var stateMask = new ushort[offsetCount];

        for (var s = 0; s < states.Count; s++)
        {
            for (var d = 0; d < profile.Degrees.Count; d++)
            {
                var offset = states[s].SemitoneDelta + profile.Degrees[d].Semitone;
                var slot = (s * offsetCount) + (offset - lo);

                // Within-state injectivity is a validated invariant (degrees have pairwise
                // distinct semitones), so this slot must still be free.
                if (degreeOf[slot] >= 0)
                {
                    throw new InvalidOperationException(
                        $"Profile '{profile.Id}' violates within-state injectivity: degrees "
                        + $"{profile.Degrees[degreeOf[slot]].Index} and {profile.Degrees[d].Index} both render "
                        + $"offset {offset} in state {s}.");
                }

                degreeOf[slot] = (sbyte)d;
                stateMask[offset - lo] |= (ushort)(1 << s);
            }
        }

        return new EmissionTable(profile, states, lo, hi, degreeOf, stateMask);
    }

    private static List<ModState> DeriveStates(InstrumentProfile profile)
    {
        var modifiers = profile.Modifiers;
        var cap = Math.Min(profile.MaxSimultaneousModifiers, modifiers.Count);
        var result = new List<ModState>();

        for (var mask = 0; mask < (1 << modifiers.Count); mask++)
        {
            if (BitOperations.PopCount((uint)mask) > cap)
            {
                continue;
            }

            if (ViolatesExclusion(modifiers, mask))
            {
                continue;
            }

            var delta = 0;
            for (var i = 0; i < modifiers.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    delta += modifiers[i].SemitoneDelta;
                }
            }

            result.Add(new ModState((byte)result.Count, (ushort)mask, delta));
        }

        return result;
    }

    private static bool ViolatesExclusion(IReadOnlyList<ModifierSpec> modifiers, int mask)
    {
        for (var i = 0; i < modifiers.Count; i++)
        {
            if ((mask & (1 << i)) == 0 || modifiers[i].ExclusionGroup == 0)
            {
                continue;
            }

            for (var j = i + 1; j < modifiers.Count; j++)
            {
                if ((mask & (1 << j)) != 0 && modifiers[j].ExclusionGroup == modifiers[i].ExclusionGroup)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
