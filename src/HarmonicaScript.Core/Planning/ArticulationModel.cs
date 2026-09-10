using System.Numerics;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Planning;

/// <summary>
/// The instrument's transition physics, precomputed once per (profile, timing) pair as a set of
/// k x k tables. Both the planner's dynamic program and the articulation scheduler read the
/// SAME tables, which is what stops the two from disagreeing about what is feasible - the
/// failure mode where a timeline passes validation while arming a modifier inside the previous
/// note's sustain.
/// </summary>
public sealed class ArticulationModel
{
    private readonly int[] _evCount;      // [a * k + b]
    private readonly bool[] _exclSwap;    // [a * k + b]
    private readonly int[] _setup;        // [a * k + b]
    private readonly int[] _reqSilSame;   // [a * k + b] when the same key is struck again
    private readonly int[] _reqSilDiff;   // [a * k + b] when a different key is struck

    private ArticulationModel(
        EmissionTable emission,
        TimingProfile timing,
        int[] evCount,
        bool[] exclSwap,
        int[] setup,
        int[] reqSilSame,
        int[] reqSilDiff)
    {
        Emission = emission;
        Timing = timing;
        _evCount = evCount;
        _exclSwap = exclSwap;
        _setup = setup;
        _reqSilSame = reqSilSame;
        _reqSilDiff = reqSilDiff;
    }

    public EmissionTable Emission { get; }

    public TimingProfile Timing { get; }

    public int StateCount => Emission.StateCount;

    /// <summary>Number of physical control events needed to move from state <paramref name="a"/> to <paramref name="b"/>.</summary>
    public int EventCount(int a, int b) => _evCount[(a * StateCount) + b];

    /// <summary>True when the transition releases one member of an exclusion group and presses another.</summary>
    public bool IsExclusiveSwap(int a, int b) => _exclSwap[(a * StateCount) + b];

    /// <summary>Time the controls need to settle before the next note key may go down.</summary>
    public int SetupMs(int a, int b) => _setup[(a * StateCount) + b];

    /// <summary>
    /// Silence required between the previous note's key-up and this note's key-down.
    /// Folds together the same-key retrigger floor, the key-change gap and - when modifiers
    /// re-pitch sustained notes - the modifier setup time.
    /// </summary>
    public int RequiredSilenceMs(int a, int b, bool sameKey) =>
        (sameKey ? _reqSilSame : _reqSilDiff)[(a * StateCount) + b];

    /// <summary>Cheapest silence achievable for a note pair, over every legal pair of states.</summary>
    public int BaselineSilenceMs(ushort fromMask, ushort toMask, Func<int, int, bool> sameKey)
    {
        ArgumentNullException.ThrowIfNull(sameKey);
        var best = int.MaxValue;

        for (var a = 0; a < StateCount; a++)
        {
            if ((fromMask & (1 << a)) == 0)
            {
                continue;
            }

            for (var b = 0; b < StateCount; b++)
            {
                if ((toMask & (1 << b)) == 0)
                {
                    continue;
                }

                best = Math.Min(best, RequiredSilenceMs(a, b, sameKey(a, b)));
            }
        }

        return best == int.MaxValue ? 0 : best;
    }

    // ---- Reported ceilings -------------------------------------------------------------
    //
    // Three numbers, not one. A single "max notes per second" is optimistic by up to 60%
    // because it silently assumes the cheapest transition. Which of these actually binds
    // depends on a score's transition mix, so EffectiveCeiling is computed per score.

    /// <summary>Repeating one key, no modifier change.</summary>
    public double SameKeyCeilingNps => 1000.0 / (Timing.NoteHoldMinMs.V + Timing.SameKeyRetriggerMs.V);

    /// <summary>Alternating different keys, no modifier change.</summary>
    public double KeyChangeCeilingNps => 1000.0 / (Timing.NoteHoldMinMs.V + Timing.KeyChangeGapMs.V);

    /// <summary>
    /// Every note also swapping mutually exclusive modifiers. Correctly the MAXIMUM of the
    /// key-change gap and the modifier setup, not the setup alone: a modifier swap does not
    /// excuse the note keys from their own separation requirement.
    /// </summary>
    public double ModifierSwapCeilingNps
    {
        get
        {
            var worst = 0;
            for (var a = 0; a < StateCount; a++)
            {
                for (var b = 0; b < StateCount; b++)
                {
                    if (IsExclusiveSwap(a, b))
                    {
                        worst = Math.Max(worst, RequiredSilenceMs(a, b, sameKey: false));
                    }
                }
            }

            if (worst == 0)
            {
                worst = Timing.KeyChangeGapMs.V;
            }

            return 1000.0 / (Timing.NoteHoldMinMs.V + worst);
        }
    }

    public static ArticulationModel Build(EmissionTable emission, TimingProfile timing)
    {
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentNullException.ThrowIfNull(timing);

        var modifiers = emission.Profile.Modifiers;
        var k = emission.StateCount;
        var evCount = new int[k * k];
        var exclSwap = new bool[k * k];
        var setup = new int[k * k];
        var reqSame = new int[k * k];
        var reqDiff = new int[k * k];

        for (var a = 0; a < k; a++)
        {
            for (var b = 0; b < k; b++)
            {
                var maskA = emission.States[a].Mask;
                var maskB = emission.States[b].Mask;
                var changed = (ushort)(maskA ^ maskB);
                var released = (ushort)(maskA & ~maskB);
                var pressed = (ushort)(maskB & ~maskA);

                var events = BitOperations.PopCount(changed);
                var isSwap = IsExclusive(modifiers, released, pressed);

                var lead = 0;
                for (var i = 0; i < modifiers.Count; i++)
                {
                    if ((released & (1 << i)) != 0)
                    {
                        lead = Math.Max(lead, modifiers[i].ReleaseLeadMs);
                    }

                    if ((pressed & (1 << i)) != 0)
                    {
                        lead = Math.Max(lead, modifiers[i].PressLeadMs);
                    }
                }

                if (isSwap)
                {
                    lead = Math.Max(lead, timing.ExclusiveSwapMs.V);
                }

                if (events > 1)
                {
                    lead += timing.InterModifierStaggerMs.V * (events - 1);
                }

                var slot = (a * k) + b;
                evCount[slot] = events;
                exclSwap[slot] = isSwap;
                setup[slot] = events == 0 ? 0 : lead;

                // If a modifier armed during a sustain would re-pitch the sounding note, the
                // setup cannot overlap the previous note and must fit inside the silence.
                var repitch = emission.Profile.ModifiersRepitchSustainedNotes;
                reqSame[slot] = repitch
                    ? Math.Max(timing.SameKeyRetriggerMs.V, setup[slot])
                    : timing.SameKeyRetriggerMs.V;
                reqDiff[slot] = repitch
                    ? Math.Max(timing.KeyChangeGapMs.V, setup[slot])
                    : timing.KeyChangeGapMs.V;
            }
        }

        return new ArticulationModel(emission, timing, evCount, exclSwap, setup, reqSame, reqDiff);
    }

    private static bool IsExclusive(IReadOnlyList<ModifierSpec> modifiers, ushort released, ushort pressed)
    {
        for (var i = 0; i < modifiers.Count; i++)
        {
            if ((released & (1 << i)) == 0 || modifiers[i].ExclusionGroup == 0)
            {
                continue;
            }

            for (var j = 0; j < modifiers.Count; j++)
            {
                if ((pressed & (1 << j)) != 0 && modifiers[j].ExclusionGroup == modifiers[i].ExclusionGroup)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
