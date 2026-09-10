using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Timeline;

/// <summary>Which key, in which modifier state. The state determines the key uniquely, so this is really just a convenience pair.</summary>
public readonly record struct Fingering(byte DegreeIndex, byte StateIndex);

/// <summary>One note in the finished score.</summary>
public sealed record ScoreNote
{
    public required int Id { get; init; }

    /// <summary>Anchor into <see cref="Source.SourceSong"/>. -1 only for user-inserted notes.</summary>
    public required int SourceIndex { get; init; }

    public required long OnsetTicks { get; init; }

    public required long DurationTicks { get; init; }

    public required long OnsetUs { get; init; }

    public required long DurationUs { get; init; }

    /// <summary>When the key actually goes down, after the articulation ladder and the global lead.</summary>
    public required long ScheduledDownUs { get; init; }

    public required long ScheduledUpUs { get; init; }

    public Fingering? Fingering { get; init; }

    /// <summary>What the file asked for. Never overwritten.</summary>
    public required byte SourceMidiNote { get; init; }

    /// <summary>What will actually sound.</summary>
    public required byte EffectiveMidiNote { get; init; }

    public NoteAlteration Alteration { get; init; }

    public bool Muted { get; init; }

    public IReadOnlyList<int>? ChordSiblingSourceIndices { get; init; }

    public bool IsAudible =>
        !Muted && (Alteration & (NoteAlteration.Dropped | NoteAlteration.ChordSibling)) == 0;

    /// <summary>The single most useful number in the UI: how far the note moved from what was written.</summary>
    public int SemitoneError => EffectiveMidiNote - SourceMidiNote;
}

/// <summary>Identifies a profile version so a stale cache is never silently reused.</summary>
public readonly record struct ProfileRef(string Id, int Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

public sealed record ScoreMetadata(string Title, string SourceFileName, string SourceSha256, double Bpm);

public sealed record HarmonicaScore(
    int FormatVersion,
    ProfileRef Instrument,
    ProfileRef Timing,
    string SettingsSha256,
    ScoreMetadata Meta,
    int Transpose,
    ConversionSettings Settings,
    IReadOnlyList<ScoreNote> Notes,
    ConversionReport Report)
{
    public const int CurrentFormatVersion = 1;

    public IEnumerable<ScoreNote> Audible => Notes.Where(n => n.IsAudible);
}
