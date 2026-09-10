using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Timeline;

public sealed record TimelineViolation(string Rule, int TimeMs, ushort Code, string Detail);

public sealed class TimelineValidationException(IReadOnlyList<TimelineViolation> violations)
    : Exception($"Timeline failed validation with {violations.Count} violation(s):{Environment.NewLine}"
        + string.Join(Environment.NewLine, violations.Take(10).Select(v => $"  - [{v.Rule}] @{v.TimeMs}ms: {v.Detail}")))
{
    public IReadOnlyList<TimelineViolation> Violations { get; } = violations;
}

/// <summary>
/// The hard gate. A timeline that fails here is neither playable nor exportable.
///
/// It is a LOGICAL STATE MACHINE, not event bookkeeping, and that distinction is the whole point.
/// Under toggle lowering a "press" is a click, so a timeline that leaves 半音 latched ON in the
/// game satisfies every hold-shaped invariant - pairing, no double-down, nothing held at the end,
/// exclusivity, minimum hold, minimum gap, sortedness - and passes green while leaving the
/// player's instrument in the wrong state. So the validator replays the lowering's own semantics.
/// </summary>
public static class TimelineValidator
{
    public static IReadOnlyList<TimelineViolation> Validate(
        ArticulationModel model,
        IReadOnlyList<InputEvent> events,
        ModifierBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(events);

        var profile = model.Emission.Profile;
        var violations = new List<TimelineViolation>();

        CheckOrderingAndPhases(events, violations);

        var modifierByBinding = profile.Modifiers
            .Select((m, i) => (Spec: m, Index: i))
            .ToDictionary(x => x.Spec.Binding, x => x);
        var degreeBindings = profile.Degrees.Select(d => d.Binding).ToHashSet();

        // Logical state: which modifiers are ACTIVE as the instrument sees them, plus when each
        // became active, so the lead-time invariant can be checked at each strike.
        var active = new bool[profile.Modifiers.Count];
        var activeSince = new int[profile.Modifiers.Count];
        var physicallyDown = new bool[profile.Modifiers.Count];
        var noteDown = new Dictionary<ushort, int>();
        var lastEventFor = new Dictionary<(InputDeviceKind, ushort), int>();

        foreach (var e in events)
        {
            var key = (e.Device, e.Code);
            if (lastEventFor.TryGetValue(key, out var previous)
                && e.TimeMs - previous < model.Timing.MinEventSeparationMs.V)
            {
                violations.Add(new TimelineViolation(
                    "minEventSeparation", e.TimeMs, e.Code,
                    $"two events on the same control {e.TimeMs - previous}ms apart, minimum is {model.Timing.MinEventSeparationMs.V}ms"));
            }

            lastEventFor[key] = e.TimeMs;

            var binding = new Binding(e.Device, e.Code);

            if (modifierByBinding.TryGetValue(binding, out var modifier))
            {
                HandleModifier(model, behavior, e, modifier.Index, active, activeSince, physicallyDown, violations);
                continue;
            }

            if (!degreeBindings.Contains(binding))
            {
                violations.Add(new TimelineViolation("unknownBinding", e.TimeMs, e.Code, $"{binding} is not in the profile"));
                continue;
            }

            HandleNote(model, e, noteDown, active, activeSince, violations);
        }

        CheckTerminalState(model, events, active, physicallyDown, noteDown, violations);
        return violations;
    }

    public static void ValidateOrThrow(
        ArticulationModel model,
        IReadOnlyList<InputEvent> events,
        ModifierBehavior behavior)
    {
        var violations = Validate(model, events, behavior);
        if (violations.Count > 0)
        {
            throw new TimelineValidationException(violations);
        }
    }

    private static void CheckOrderingAndPhases(IReadOnlyList<InputEvent> events, List<TimelineViolation> violations)
    {
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i - 1].CompareTo(events[i]) > 0)
            {
                violations.Add(new TimelineViolation(
                    "sorted", events[i].TimeMs, events[i].Code, "events are not in canonical order"));
                break;
            }
        }

        foreach (var e in events)
        {
            var shouldBeDown = e.Phase is InputPhase.NoteDown or InputPhase.ModifierDown;
            if (shouldBeDown != e.IsDown)
            {
                violations.Add(new TimelineViolation(
                    "phaseMatchesDirection", e.TimeMs, e.Code, $"phase {e.Phase} with IsDown={e.IsDown}"));
            }
        }
    }

    private static void HandleModifier(
        ArticulationModel model,
        ModifierBehavior behavior,
        InputEvent e,
        int index,
        bool[] active,
        int[] activeSince,
        bool[] physicallyDown,
        List<TimelineViolation> violations)
    {
        var profile = model.Emission.Profile;

        if (behavior == ModifierBehavior.Hold)
        {
            if (e.IsDown)
            {
                if (physicallyDown[index])
                {
                    violations.Add(new TimelineViolation(
                        "noDoubleDown", e.TimeMs, e.Code, $"modifier '{profile.Modifiers[index].Id}' pressed while already down"));
                }

                physicallyDown[index] = true;
                active[index] = true;
                activeSince[index] = e.TimeMs;
            }
            else
            {
                if (!physicallyDown[index])
                {
                    violations.Add(new TimelineViolation(
                        "pairedRelease", e.TimeMs, e.Code, $"modifier '{profile.Modifiers[index].Id}' released while not down"));
                }

                var heldMs = e.TimeMs - activeSince[index];
                if (physicallyDown[index] && heldMs > model.Timing.MaxModifierHoldMs.V)
                {
                    violations.Add(new TimelineViolation(
                        "maxModifierHold", e.TimeMs, e.Code,
                        $"modifier '{profile.Modifiers[index].Id}' held {heldMs}ms, cap is {model.Timing.MaxModifierHoldMs.V}ms"));
                }

                physicallyDown[index] = false;
                active[index] = false;
            }
        }
        else
        {
            // Toggle: the logical state flips on the press half of each click.
            if (e.IsDown)
            {
                physicallyDown[index] = true;
                active[index] = !active[index];
                if (active[index])
                {
                    activeSince[index] = e.TimeMs;
                }
            }
            else
            {
                physicallyDown[index] = false;
            }
        }

        // Exclusion holds over the LOGICAL state, which is the only state the game reacts to.
        var group = profile.Modifiers[index].ExclusionGroup;
        if (group != 0 && active[index])
        {
            for (var other = 0; other < profile.Modifiers.Count; other++)
            {
                if (other != index && active[other] && profile.Modifiers[other].ExclusionGroup == group)
                {
                    violations.Add(new TimelineViolation(
                        "exclusionGroup", e.TimeMs, e.Code,
                        $"'{profile.Modifiers[index].Id}' and '{profile.Modifiers[other].Id}' are simultaneously active"));
                }
            }
        }

        var activeCount = active.Count(a => a);
        if (activeCount > profile.MaxSimultaneousModifiers)
        {
            violations.Add(new TimelineViolation(
                "maxSimultaneous", e.TimeMs, e.Code,
                $"{activeCount} modifiers active, cap is {profile.MaxSimultaneousModifiers}"));
        }
    }

    private static void HandleNote(
        ArticulationModel model,
        InputEvent e,
        Dictionary<ushort, int> noteDown,
        bool[] active,
        int[] activeSince,
        List<TimelineViolation> violations)
    {
        var profile = model.Emission.Profile;

        if (e.IsDown)
        {
            if (noteDown.ContainsKey(e.Code))
            {
                violations.Add(new TimelineViolation("noDoubleDown", e.TimeMs, e.Code, "note key pressed while already down"));
            }

            noteDown[e.Code] = e.TimeMs;

            // THE lead-time invariant. Without it a timeline can arm a modifier at the same
            // millisecond as the strike and still round-trip green, which is exactly the bug
            // that produces "it plays but every accidental is wrong".
            for (var m = 0; m < profile.Modifiers.Count; m++)
            {
                if (!active[m])
                {
                    continue;
                }

                var lead = e.TimeMs - activeSince[m];
                if (lead < profile.Modifiers[m].PressLeadMs)
                {
                    violations.Add(new TimelineViolation(
                        "modifierLeadTime", e.TimeMs, e.Code,
                        $"'{profile.Modifiers[m].Id}' became active only {lead}ms before this strike, needs {profile.Modifiers[m].PressLeadMs}ms"));
                }
            }
        }
        else
        {
            if (!noteDown.Remove(e.Code, out var downAt))
            {
                violations.Add(new TimelineViolation("pairedRelease", e.TimeMs, e.Code, "note key released while not down"));
                return;
            }

            var heldMs = e.TimeMs - downAt;
            if (heldMs < model.Timing.KeyHeldMinMs.V)
            {
                violations.Add(new TimelineViolation(
                    "minKeyHold", e.TimeMs, e.Code, $"note key held {heldMs}ms, minimum is {model.Timing.KeyHeldMinMs.V}ms"));
            }

            if (heldMs > model.Timing.MaxKeyHoldMs.V)
            {
                violations.Add(new TimelineViolation(
                    "maxKeyHold", e.TimeMs, e.Code, $"note key held {heldMs}ms, cap is {model.Timing.MaxKeyHoldMs.V}ms"));
            }
        }
    }

    private static void CheckTerminalState(
        ArticulationModel model,
        IReadOnlyList<InputEvent> events,
        bool[] active,
        bool[] physicallyDown,
        Dictionary<ushort, int> noteDown,
        List<TimelineViolation> violations)
    {
        var endMs = events.Count == 0 ? 0 : events[^1].TimeMs;
        var profile = model.Emission.Profile;

        for (var m = 0; m < active.Length; m++)
        {
            if (active[m])
            {
                violations.Add(new TimelineViolation(
                    "terminalNeutral", endMs, profile.Modifiers[m].Binding.Code,
                    $"modifier '{profile.Modifiers[m].Id}' is still ACTIVE at the end of the timeline"));
            }

            if (physicallyDown[m])
            {
                violations.Add(new TimelineViolation(
                    "terminalReleased", endMs, profile.Modifiers[m].Binding.Code,
                    $"modifier '{profile.Modifiers[m].Id}' is still physically held at the end of the timeline"));
            }
        }

        foreach (var code in noteDown.Keys)
        {
            violations.Add(new TimelineViolation("terminalReleased", endMs, code, "note key is still down at the end of the timeline"));
        }
    }
}
