using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Timeline;

/// <summary>A pitch the simulator believes actually sounded, and for how long.</summary>
public readonly record struct SimulatedNote(int DownMs, int UpMs, int Pitch, int DegreeIndex, ushort ActiveMask)
{
    public int DurationMs => UpMs - DownMs;
}

/// <summary>A moment where the sounding pitch changed while a key was already down.</summary>
public readonly record struct RepitchEvent(int AtMs, int DegreeIndex, int FromPitch, int ToPitch);

public sealed record SimulationResult(
    IReadOnlyList<SimulatedNote> Notes,
    IReadOnlyList<RepitchEvent> Repitches);

/// <summary>
/// Replays an <see cref="InputTimeline"/> the way the instrument would, and reports what it
/// believes sounded.
///
/// This is the project's central correctness gate, and its independence is the whole point: it
/// computes pitch from the profile's degree semitones and modifier deltas DIRECTLY, never from
/// the <see cref="EmissionTable"/> the planner used. If the planner and this agree, two separate
/// derivations of the same physics agree. If it read the emission table, it would only be
/// checking that a lookup table equals itself.
///
/// It models pitch as a STEP FUNCTION over time, not a value sampled at onset. That is what
/// catches a modifier armed one note early: sampling at the strike would show the right pitch
/// while the previous note was silently bent underneath it.
/// </summary>
public static class InstrumentSimulator
{
    public static SimulationResult Simulate(
        InstrumentProfile profile,
        IReadOnlyList<InputEvent> events,
        ModifierBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(events);

        var degreeByBinding = new Dictionary<Binding, int>();
        for (var i = 0; i < profile.Degrees.Count; i++)
        {
            degreeByBinding[profile.Degrees[i].Binding] = i;
        }

        var modifierByBinding = new Dictionary<Binding, int>();
        for (var i = 0; i < profile.Modifiers.Count; i++)
        {
            modifierByBinding[profile.Modifiers[i].Binding] = i;
        }

        var active = new bool[profile.Modifiers.Count];
        var sounding = new Dictionary<int, (int DownMs, int Pitch)>();
        var result = new List<SimulatedNote>();
        var repitches = new List<RepitchEvent>();

        foreach (var e in events)
        {
            var binding = new Binding(e.Device, e.Code);

            if (modifierByBinding.TryGetValue(binding, out var modifier))
            {
                var wasActive = active[modifier];
                active[modifier] = behavior == ModifierBehavior.Hold
                    ? e.IsDown
                    : e.IsDown ? !active[modifier] : active[modifier];

                if (wasActive != active[modifier])
                {
                    // A modifier change while a key is down re-pitches the sounding note. That is
                    // the conservative reading of the instrument, and the one that makes the
                    // constant-pitch assertion meaningful.
                    foreach (var (degree, state) in sounding.ToList())
                    {
                        var newPitch = PitchOf(profile, degree, active);
                        if (newPitch != state.Pitch)
                        {
                            repitches.Add(new RepitchEvent(e.TimeMs, degree, state.Pitch, newPitch));
                            sounding[degree] = (state.DownMs, newPitch);
                        }
                    }
                }

                continue;
            }

            if (!degreeByBinding.TryGetValue(binding, out var degreeIndex))
            {
                continue;
            }

            if (e.IsDown)
            {
                sounding[degreeIndex] = (e.TimeMs, PitchOf(profile, degreeIndex, active));
            }
            else if (sounding.Remove(degreeIndex, out var state))
            {
                result.Add(new SimulatedNote(state.DownMs, e.TimeMs, state.Pitch, degreeIndex, Mask(active)));
            }
        }

        result.Sort((a, b) => a.DownMs != b.DownMs ? a.DownMs.CompareTo(b.DownMs) : a.DegreeIndex.CompareTo(b.DegreeIndex));
        return new SimulationResult(result, repitches);
    }

    /// <summary>pitch = base + sum(active modifier deltas) + degree semitone. The closed form, direct from the profile.</summary>
    private static int PitchOf(InstrumentProfile profile, int degreeIndex, bool[] active)
    {
        var pitch = profile.BaseMidiNote + profile.Degrees[degreeIndex].Semitone;
        for (var i = 0; i < active.Length; i++)
        {
            if (active[i])
            {
                pitch += profile.Modifiers[i].SemitoneDelta;
            }
        }

        return pitch;
    }

    private static ushort Mask(bool[] active)
    {
        ushort mask = 0;
        for (var i = 0; i < active.Length; i++)
        {
            if (active[i])
            {
                mask |= (ushort)(1 << i);
            }
        }

        return mask;
    }
}
