namespace HarmonicaScript.Core.Source;

/// <summary>
/// One note as the file described it, before any reduction. <paramref name="Index"/> is the
/// permanent anchor for the edit journal.
/// </summary>
public readonly record struct SourceNote(
    int Index,
    int TrackIndex,
    byte Channel,
    byte Pitch,
    byte Velocity,
    long OnsetTicks,
    long DurationTicks,
    long OnsetUs,
    long DurationUs)
{
    public long EndUs => OnsetUs + DurationUs;

    public long EndTicks => OnsetTicks + DurationTicks;

    /// <summary>Channel 9 zero-based is the General MIDI drum channel (displayed as "channel 10").</summary>
    public bool IsPercussion => Channel == 9;
}

public readonly record struct TempoChange(long Ticks, long Us, int MicrosecondsPerQuarterNote)
{
    public double Bpm => 60_000_000.0 / MicrosecondsPerQuarterNote;
}

public readonly record struct TimeSignatureChange(long Ticks, long Us, int Numerator, int Denominator);

/// <summary>Per-track statistics, used to drive track selection and the auto-pick heuristic.</summary>
public sealed record SourceTrack(
    int Index,
    string? Name,
    ushort ChannelMask,
    int NoteCount,
    long DurationUs,
    byte MinPitch,
    byte MaxPitch,
    double MeanPitch,
    double MeanPolyphony,
    bool IsPercussion,
    double MelodyScore)
{
    public int PitchSpan => MaxPitch - MinPitch;
}

/// <summary>
/// A parsed MIDI file. Both tick and microsecond times are kept: ticks are what the edit
/// journal's content fingerprint anchors to and what a re-export needs, microseconds are what
/// every downstream pass computes in.
/// </summary>
/// <param name="LoaderVersion">Bumped whenever parsing changes in a way that could renumber notes.</param>
/// <param name="SanitizeSettingsHash">Same purpose: a different value means indices may have moved.</param>
public sealed record SourceSong(
    string FileName,
    string Sha256,
    string LoaderVersion,
    string SanitizeSettingsHash,
    short TicksPerQuarterNote,
    IReadOnlyList<TempoChange> TempoMap,
    IReadOnlyList<TimeSignatureChange> TimeSignatures,
    IReadOnlyList<SourceTrack> Tracks,
    IReadOnlyList<SourceNote> Notes,
    IReadOnlyList<string> RepairedEvents)
{
    public long DurationUs => Notes.Count == 0 ? 0 : Notes.Max(n => n.EndUs);

    /// <summary>
    /// The track most likely to carry the melody: not percussion, mostly monophonic, enough
    /// notes to matter, and sitting high. Returns -1 when there is nothing to pick.
    /// </summary>
    public int AutoSelectedTrack =>
        Tracks.Where(t => !t.IsPercussion && t.NoteCount > 0)
            .OrderByDescending(t => t.MelodyScore)
            .Select(t => t.Index)
            .DefaultIfEmpty(-1)
            .First();
}
