namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// One timing constant plus the evidence behind it. Every number describing the game is
/// wrapped like this, because the developer cannot measure any of them and a bare integer
/// would launder a guess into a fact. The UI renders <see cref="Measured"/> == false with a
/// dotted underline and a tooltip.
/// </summary>
/// <param name="V">The value, in milliseconds unless the field says otherwise.</param>
/// <param name="Source">Free-text citation, e.g. "ChickenD233 FixedLeadMs" or "invented".</param>
/// <param name="Measured">True only if someone measured it in the running game.</param>
public readonly record struct TimingValue(int V, string Source, bool Measured)
{
    public Provenance Provenance => Measured
        ? Provenance.Measured
        : Source.Equals("invented", StringComparison.OrdinalIgnoreCase)
            ? Provenance.Invented
            : Source.Contains("infer", StringComparison.OrdinalIgnoreCase)
                ? Provenance.Inferred
                : Provenance.ThirdPartyTool;

    public static implicit operator int(TimingValue v) => v.V;

    public override string ToString() => Measured ? $"{V}ms" : $"{V}ms({Source})";
}

/// <summary>Opt-in guard against the "判定粘连" sticking the reference tool observed on long continuous play.</summary>
/// <param name="Enabled">Default ON: it is the one guard a shipping tool added against an actually-observed in-game failure.</param>
/// <param name="AfterMs">Insert a rest once this much continuous sound has accumulated.</param>
/// <param name="RestMs">Length of the inserted rest.</param>
/// <param name="PhraseGapMs">A natural gap at least this long resets the accumulator, so the rest lands musically where possible.</param>
public readonly record struct BreathRestSpec(bool Enabled, int AfterMs, int RestMs, int PhraseGapMs);

/// <summary>
/// The physical input-latency limits the game imposes. These do NOT scale with the playback
/// speed multiplier: they are properties of the input path, not of the music.
/// </summary>
public sealed record TimingProfile(
    string Id,
    int SchemaVersion,
    LocalizedText DisplayName,

    /// <summary>Minimum key-up before the same key may be struck again.</summary>
    TimingValue SameKeyRetriggerMs,

    /// <summary>Minimum silence between two different note keys.</summary>
    TimingValue KeyChangeGapMs,

    /// <summary>Minimum time a note key must stay down for the game to register it.</summary>
    TimingValue NoteHoldMinMs,

    /// <summary>Floor on how briefly a key may be held once down (distinct from the note's musical length).</summary>
    TimingValue KeyHeldMinMs,

    /// <summary>Whole event stream is shifted this far earlier, absorbing constant input latency.</summary>
    TimingValue GlobalLeadMs,

    /// <summary>Extra settle time when two mutually exclusive modifiers must swap.</summary>
    TimingValue ExclusiveSwapMs,

    /// <summary>Spacing added per extra modifier when several change at the same instant.</summary>
    TimingValue InterModifierStaggerMs,

    /// <summary>How far a single note may be nudged later to resolve a timing conflict.</summary>
    TimingValue MaxOnsetShiftMs,

    /// <summary>Cumulative nudge budget before the scheduler drops rather than drifts.</summary>
    TimingValue MaxDriftMs,

    /// <summary>A note key held longer than this is split into a re-press (decision D9).</summary>
    TimingValue MaxKeyHoldMs,

    /// <summary>A modifier held longer than this is broken at a rest (decision D9).</summary>
    TimingValue MaxModifierHoldMs,

    /// <summary>Two events on the same control must be at least this far apart in the emitted timeline.</summary>
    TimingValue MinEventSeparationMs,

    BreathRestSpec BreathRest)
{
    public string Ref => $"{Id}@{SchemaVersion}";

    /// <summary>
    /// DERIVED, never configured: a note shorter than this cannot survive the reduction,
    /// because it could not be held and separated from its neighbour. The plan's earlier
    /// <c>max(45, noteHoldMin+5)</c> evaluated to a constant 45 under both shipped profiles,
    /// which made it vacuous; this form actually tracks the profile (40 here, 65 conservative).
    /// </summary>
    public int MinSurvivingDurationMs => NoteHoldMinMs.V + KeyChangeGapMs.V;

}
