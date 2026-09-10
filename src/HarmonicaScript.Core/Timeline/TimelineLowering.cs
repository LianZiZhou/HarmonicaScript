using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Timeline;

/// <summary>
/// A control event still in exact microseconds. Deliberately separate from
/// <see cref="InputEvent"/>: cramming microseconds into that type's int TimeMs would overflow
/// silently at about 36 minutes of music, and "works until the song is long enough" is the worst
/// kind of bug. Quantise is the only bridge between the two.
/// </summary>
public readonly record struct RawEvent(
    long TimeUs,
    InputPhase Phase,
    Instrument.InputDeviceKind Device,
    ushort Code,
    bool IsDown,
    int NoteId);

/// <summary>
/// Turns scheduled notes into physical control events.
///
/// Two lowerings, both golden-filed, chosen by the profile's <see cref="ModifierBehavior"/>.
/// Hold is what the Delta Force harmonica actually does; toggle exists because that fact came
/// from a third-party tool and a user report, not from the game, and a wrong guess there must be
/// a config change rather than a rewrite.
/// </summary>
public static class TimelineLowering
{
    /// <summary>
    /// HOLD lowering. Emits a control event only where the required mask CHANGES, which yields
    /// maximal runs for free - given fixed spellings, greedy maximal-run is provably optimal, so
    /// there is nothing cleverer to do here. All the real freedom lived in the planner.
    /// </summary>
    public static List<RawEvent> LowerHoldSpans(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(notes);

        var events = new List<RawEvent>(notes.Count * 3);
        var profile = model.Emission.Profile;
        var staggerUs = model.Timing.InterModifierStaggerMs.V * 1000L;
        ushort activeMask = 0;
        var previousState = 0;

        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var wanted = model.Emission.States[note.StateIndex].Mask;

            // A forced restart releases the control and presses it again at the same boundary.
            var released = (ushort)((activeMask & ~wanted) | (activeMask & wanted & note.ForceRestartMask));
            var pressed = (ushort)((wanted & ~activeMask) | (activeMask & wanted & note.ForceRestartMask));

            EmitModifierChange(events, profile, model, note, previousState, released, pressed, staggerUs);
            activeMask = wanted;
            previousState = note.StateIndex;

            var degree = profile.Degrees[note.DegreeIndex];
            events.Add(new RawEvent(
                note.DownUs, InputPhase.NoteDown, degree.Binding.Device, degree.Binding.Code, true, note.Note.SourceIndex));
            events.Add(new RawEvent(
                note.UpUs, InputPhase.NoteUp, degree.Binding.Device, degree.Binding.Code, false, note.Note.SourceIndex));
        }

        // Release everything at the end. Nothing may outlive the song.
        if (notes.Count > 0 && activeMask != 0)
        {
            var endUs = notes[^1].UpUs;
            var order = 0;
            for (var m = 0; m < profile.Modifiers.Count; m++)
            {
                if ((activeMask & (1 << m)) == 0)
                {
                    continue;
                }

                var binding = profile.Modifiers[m].Binding;
                events.Add(new RawEvent(
                    endUs + (staggerUs * order), InputPhase.ModifierUp, binding.Device, binding.Code, false, -1));
                order++;
            }
        }

        return events;
    }

    /// <summary>
    /// TOGGLE lowering. Every state change becomes a CLICK (down immediately followed by up), and
    /// the logical state is tracked separately from the physical one.
    ///
    /// The subtle failure this exists to make visible: a timeline that leaves a modifier LATCHED
    /// ON in the game satisfies every hold-shaped invariant - pairing, no double-down, nothing
    /// held at the end - and still leaves the player's instrument in the wrong state. The
    /// validator therefore checks the LOGICAL state, not the physical one.
    /// </summary>
    public static List<RawEvent> LowerToggleTransitions(ArticulationModel model, IReadOnlyList<ScheduledNote> notes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(notes);

        var events = new List<RawEvent>(notes.Count * 4);
        var profile = model.Emission.Profile;
        var clickUs = Math.Max(1, model.Timing.MinEventSeparationMs.V) * 1000L;
        var staggerUs = model.Timing.InterModifierStaggerMs.V * 1000L;
        ushort logicalMask = 0;

        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var wanted = model.Emission.States[note.StateIndex].Mask;
            var changed = (ushort)(logicalMask ^ wanted);
            var order = 0;

            for (var m = 0; m < profile.Modifiers.Count; m++)
            {
                if ((changed & (1 << m)) == 0)
                {
                    continue;
                }

                var spec = profile.Modifiers[m];
                var lead = (spec.PressLeadMs * 1000L) + (staggerUs * order);
                var at = note.DownUs - lead;

                events.Add(new RawEvent(at, InputPhase.ModifierDown, spec.Binding.Device, spec.Binding.Code, true, -1));
                events.Add(new RawEvent(at + clickUs, InputPhase.ModifierUp, spec.Binding.Device, spec.Binding.Code, false, -1));
                order++;
            }

            logicalMask = wanted;

            var degree = profile.Degrees[note.DegreeIndex];
            events.Add(new RawEvent(
                note.DownUs, InputPhase.NoteDown, degree.Binding.Device, degree.Binding.Code, true, note.Note.SourceIndex));
            events.Add(new RawEvent(
                note.UpUs, InputPhase.NoteUp, degree.Binding.Device, degree.Binding.Code, false, note.Note.SourceIndex));
        }

        // Clear every latched modifier, or the player's instrument is left mis-configured.
        if (notes.Count > 0 && logicalMask != 0)
        {
            var endUs = notes[^1].UpUs;
            var order = 0;
            for (var m = 0; m < profile.Modifiers.Count; m++)
            {
                if ((logicalMask & (1 << m)) == 0)
                {
                    continue;
                }

                var binding = profile.Modifiers[m].Binding;
                var at = endUs + (staggerUs * order * 2);
                events.Add(new RawEvent(at, InputPhase.ModifierDown, binding.Device, binding.Code, true, -1));
                events.Add(new RawEvent(at + clickUs, InputPhase.ModifierUp, binding.Device, binding.Code, false, -1));
                order++;
            }
        }

        return events;
    }

    /// <summary>
    /// Lays out one modifier change as an explicitly ORDERED block ending at the strike.
    ///
    /// The block runs from <c>down - setup</c> to <c>down</c>, with every release before every
    /// press and each event one stagger apart. Computing it this way rather than as
    /// <c>down - lead - stagger*order</c> per event is not a refactor: with the per-event form,
    /// a stagger larger than the difference between a control's release lead and another's press
    /// lead emits the PRESS EARLIER THAN THE RELEASE, producing a double-down followed by an
    /// unpaired release. That is exactly what the conservative profile (stagger 6, leads 16/12)
    /// did, and the validator caught it.
    ///
    /// <c>setup</c> already accounts for the largest lead, the exclusive-swap settle time and the
    /// stagger for every event, so the last press still lands at or before its own press lead.
    /// </summary>
    private static void EmitModifierChange(
        List<RawEvent> events,
        InstrumentProfile profile,
        ArticulationModel model,
        ScheduledNote note,
        int fromState,
        ushort released,
        ushort pressed,
        long staggerUs)
    {
        if (released == 0 && pressed == 0)
        {
            return;
        }

        var setupUs = model.SetupMs(fromState, note.StateIndex) * 1000L;
        var step = Math.Max(staggerUs, 1000);   // never let two events on one control share a millisecond

        var eventCount = System.Numerics.BitOperations.PopCount((uint)released)
            + System.Numerics.BitOperations.PopCount((uint)pressed);

        long maxPressLeadUs = 0;
        for (var m = 0; m < profile.Modifiers.Count; m++)
        {
            if ((pressed & (1 << m)) != 0)
            {
                maxPressLeadUs = Math.Max(maxPressLeadUs, profile.Modifiers[m].PressLeadMs * 1000L);
            }
        }

        // Size the block so the LAST event still lands at or before its own lead, then lay events
        // out forwards from its start. Clamping an individual press backwards instead would pull
        // it in front of a release that has to happen first - which is the inversion this whole
        // routine exists to avoid.
        var requiredUs = (step * Math.Max(0, eventCount - 1)) + maxPressLeadUs;
        var blockUs = Math.Max(setupUs, requiredUs);
        var slot = note.DownUs - blockUs;

        for (var m = 0; m < profile.Modifiers.Count; m++)
        {
            if ((released & (1 << m)) == 0)
            {
                continue;
            }

            var binding = profile.Modifiers[m].Binding;
            events.Add(new RawEvent(slot, InputPhase.ModifierUp, binding.Device, binding.Code, false, -1));
            slot += step;
        }

        for (var m = 0; m < profile.Modifiers.Count; m++)
        {
            if ((pressed & (1 << m)) == 0)
            {
                continue;
            }

            var binding = profile.Modifiers[m].Binding;
            events.Add(new RawEvent(slot, InputPhase.ModifierDown, binding.Device, binding.Code, true, -1));
            slot += step;
        }
    }

    /// <summary>
    /// THE single rounding point: exact microseconds to absolute integer milliseconds, rounded
    /// half away from zero, then stably re-sorted. Everything upstream is exact microseconds;
    /// everything downstream - the scheduler, every exporter, every golden file - is integer
    /// milliseconds.
    /// </summary>
    public static List<InputEvent> Quantise(IReadOnlyList<RawEvent> events, long globalLeadUs)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return [];
        }

        // The global lead pulls everything earlier to absorb constant input latency. Modifier
        // leads can also push the first event before zero. Re-basing on the true earliest event
        // keeps the timeline's own clock starting at zero without losing relative spacing.
        var originUs = Math.Min(0, events.Min(e => e.TimeUs) - globalLeadUs);

        var quantised = new List<InputEvent>(events.Count);
        foreach (var e in events)
        {
            var us = e.TimeUs - globalLeadUs - originUs;
            var ms = (us + (us >= 0 ? 500 : -500)) / 1000;
            quantised.Add(new InputEvent(
                (int)Math.Clamp(ms, 0, int.MaxValue), e.Phase, e.Device, e.Code, e.IsDown, e.NoteId));
        }

        quantised.Sort();
        return quantised;
    }
}
