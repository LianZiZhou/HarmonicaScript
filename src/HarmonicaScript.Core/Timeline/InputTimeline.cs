using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Timeline;

/// <summary>
/// Ordering class within one instant. The order is the physically correct one: release the old
/// note, release outgoing modifiers, press incoming modifiers, then strike.
/// </summary>
public enum InputPhase : byte
{
    NoteUp = 0,
    ModifierUp = 1,
    ModifierDown = 2,
    NoteDown = 3,
}

/// <summary>
/// One control event at an ABSOLUTE millisecond. Milliseconds because every consumer without
/// exception is integer milliseconds - Razer's &lt;Delay&gt;, Bloody's "Delay n ms", AutoHotkey's
/// Sleep, and the scheduler's own deadlines. Microseconds live only inside the pipeline, and
/// <c>emit.quantise</c> is the single place rounding happens.
/// </summary>
/// <param name="NoteId">Provenance for the UI; -1 for modifier events. Deliberately NOT part of ordering.</param>
public readonly record struct InputEvent(
    int TimeMs,
    InputPhase Phase,
    InputDeviceKind Device,
    ushort Code,
    bool IsDown,
    int NoteId) : IComparable<InputEvent>
{
    public int CompareTo(InputEvent other)
    {
        var c = TimeMs.CompareTo(other.TimeMs);
        if (c != 0)
        {
            return c;
        }

        c = Phase.CompareTo(other.Phase);
        if (c != 0)
        {
            return c;
        }

        c = Device.CompareTo(other.Device);
        return c != 0 ? c : Code.CompareTo(other.Code);
    }

    public bool IsModifier => Phase is InputPhase.ModifierDown or InputPhase.ModifierUp;
}

public readonly record struct TimelineDiagnostic(string Code, int NoteId, int TimeMs, long Arg0);

/// <summary>
/// THE contract. Live playback and every macro exporter consume this and nothing else, which is
/// what guarantees an exported macro does exactly what the preview did.
///
/// Deliberately absent: BatchId (derivable - events sharing a TimeMs are one batch, and a stored
/// id is a second source of truth that can disagree), Label (NoteId indexes the score), and Value
/// (three buttons, no wheel, no cursor movement, ever).
/// </summary>
public sealed record InputTimeline(
    int FormatVersion,
    string InstrumentRef,
    string TimingRef,
    string ScoreSha256,
    string SettingsSha256,
    IReadOnlyList<InputEvent> Events,
    int TotalDurationMs,
    IReadOnlyList<TimelineDiagnostic> Diagnostics)
{
    public const int CurrentFormatVersion = 1;

    /// <summary>Groups events that share an instant. Each group becomes ONE SendInput call, which is what makes modifier-arm plus strike atomic.</summary>
    public IEnumerable<(int TimeMs, IReadOnlyList<InputEvent> Batch)> Batches()
    {
        var i = 0;
        while (i < Events.Count)
        {
            var time = Events[i].TimeMs;
            var j = i;
            while (j < Events.Count && Events[j].TimeMs == time)
            {
                j++;
            }

            yield return (time, Events.Skip(i).Take(j - i).ToList());
            i = j;
        }
    }
}
