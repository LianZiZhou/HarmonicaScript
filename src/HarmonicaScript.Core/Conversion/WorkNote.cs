using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Conversion;

/// <summary>What happened to a note on its way through the pipeline.</summary>
[Flags]
public enum NoteAlteration : ushort
{
    None = 0,
    OctaveFolded = 1 << 0,
    ChordReduced = 1 << 1,
    ChordSibling = 1 << 2,
    Dropped = 1 << 3,
    MergedWithPrev = 1 << 4,
    DurationClamped = 1 << 5,
    OnsetShifted = 1 << 6,
    Respelled = 1 << 7,
    SmoothedOut = 1 << 8,
    HoldSplit = 1 << 9,
    BreathRest = 1 << 10,
    UserEdited = 1 << 11,
}

/// <summary>
/// The pipeline's mutable note. Dropped notes and losing chord members are FLAGGED, never
/// removed: keeping them is what makes "swap which chord note survives" a one-click fix and
/// what lets the report show the user exactly what they lost.
/// </summary>
public sealed class WorkNote
{
    public required int SourceIndex { get; init; }

    public required int TrackIndex { get; init; }

    public required byte Channel { get; init; }

    /// <summary>The pitch the file asked for. NEVER overwritten - the report's whole credibility rests on this.</summary>
    public required byte SourceMidiNote { get; init; }

    public required byte Velocity { get; init; }

    public required long OnsetTicks { get; init; }

    public required long DurationTicks { get; init; }

    public long OnsetUs { get; set; }

    public long DurationUs { get; set; }

    /// <summary>The pitch that will actually sound: source plus transposition plus any octave fold.</summary>
    public byte EffectiveMidiNote { get; set; }

    public NoteAlteration Alteration { get; set; }

    public bool Muted { get; set; }

    /// <summary>Chord members this note beat. Populated on the survivor so the UI can offer them.</summary>
    public List<int>? ChordSiblingSourceIndices { get; set; }

    /// <summary>Chosen state index in the emission table; -1 until the planner runs.</summary>
    public int StateIndex { get; set; } = -1;

    /// <summary>Chosen degree index; -1 until the planner runs.</summary>
    public int DegreeIndex { get; set; } = -1;

    public long EndUs => OnsetUs + DurationUs;

    /// <summary>True when this note will be struck. Dropped, muted and losing chord members are not.</summary>
    public bool IsAudible =>
        !Muted && (Alteration & (NoteAlteration.Dropped | NoteAlteration.ChordSibling)) == 0;

    public void Drop(NoteAlteration reason)
    {
        Alteration |= NoteAlteration.Dropped | reason;
    }

    public WorkNote Clone() => (WorkNote)MemberwiseClone();
}
