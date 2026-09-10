using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonicaScript.Core.Source;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

namespace HarmonicaScript.Midi;

/// <summary>
/// Reads a Standard MIDI File into <see cref="SourceSong"/>.
///
/// Deliberately tolerant. The files this tool exists to consume are community rips and DAW
/// exports circulated for FFXIV Bard and Genshin lyre players; a meaningful fraction of them
/// carry a truncated chunk, an orphaned note-on or an illegal key signature. Aborting on those
/// would reject files that every other player opens fine.
/// </summary>
public static class MidiReader
{
    /// <summary>Bump when parsing changes in a way that could renumber notes; .hsproj re-anchors on mismatch.</summary>
    public const string LoaderVersion = "1";

    private static readonly bool CodePagesRegistered = RegisterCodePages();

    /// <summary>
    /// Chinese track names are common in this corpus and are almost never UTF-8. GB18030 is a
    /// superset of GBK/GB2312 and decodes ASCII identically, so it is a safe default.
    /// </summary>
    public static Encoding DefaultTextEncoding { get; } =
        CodePagesRegistered ? Encoding.GetEncoding("GB18030") : Encoding.UTF8;

    public static ReadingSettings TolerantSettings() => new()
    {
        NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
        InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
        NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
        InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.SnapToLimits,

        // This is the one that survives the notorious `KeySignature scale=255`.
        InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
        InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits,

        MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
        UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
        UnknownChunkIdPolicy = UnknownChunkIdPolicy.Skip,
        UnknownFileFormatPolicy = UnknownFileFormatPolicy.Ignore,
        UnknownChannelEventPolicy = UnknownChannelEventPolicy.SkipStatusByteAndTwoDataBytes,
        SilentNoteOnPolicy = SilentNoteOnPolicy.NoteOff,
        ZeroLengthDataPolicy = ZeroLengthDataPolicy.ReadAsEmptyObject,
        TextEncoding = DefaultTextEncoding,
    };

    public static SanitizingSettings SanitizeSettings(int noteMinLengthMs = 10) => new()
    {
        NoteMinLength = new MetricTimeSpan(0, 0, 0, noteMinLengthMs),
        OrphanedNoteOnEventsPolicy = OrphanedNoteOnEventsPolicy.Remove,
        RemoveDuplicatedNotes = true,
        RemoveEmptyTrackChunks = false,
    };

    /// <summary>Identifies the sanitiser configuration so a .hsproj can detect that indices may have moved.</summary>
    public static string SanitizeSettingsHash(int noteMinLengthMs) =>
        $"v{LoaderVersion}.min{noteMinLengthMs}.orphan-remove.dedup";

    public static SourceSong Read(string path, int noteMinLengthMs = 10)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var bytes = File.ReadAllBytes(path);
        return ReadBytes(bytes, Path.GetFileName(path), noteMinLengthMs);
    }

    public static SourceSong ReadBytes(byte[] bytes, string fileName, int noteMinLengthMs = 10)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var repaired = new List<string>();
        using var stream = new MemoryStream(bytes, writable: false);
        var file = MidiFile.Read(stream, TolerantSettings());

        var beforeNotes = file.GetNotes().Count;
        file.Sanitize(SanitizeSettings(noteMinLengthMs));
        var afterNotes = file.GetNotes().Count;
        if (afterNotes != beforeNotes)
        {
            repaired.Add(string.Create(CultureInfo.InvariantCulture, $"sanitize removed {beforeNotes - afterNotes} note(s)"));
        }

        var tempoMap = file.GetTempoMap();
        var ticksPerQuarter = tempoMap.TimeDivision is TicksPerQuarterNoteTimeDivision tpqn
            ? tpqn.TicksPerQuarterNote
            : (short)480;

        if (tempoMap.TimeDivision is not TicksPerQuarterNoteTimeDivision)
        {
            repaired.Add("non-PPQN time division; assuming 480 ticks per quarter note");
        }

        var trackChunks = file.GetTrackChunks().ToList();
        var notes = new List<SourceNote>();
        var perTrack = new List<List<SourceNote>>();

        for (var t = 0; t < trackChunks.Count; t++)
        {
            var trackNotes = new List<SourceNote>();
            foreach (var note in trackChunks[t].GetNotes())
            {
                var onsetUs = (long)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;
                var endUs = (long)note.EndTimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;

                var source = new SourceNote(
                    Index: 0, // assigned after the global sort
                    TrackIndex: t,
                    Channel: note.Channel,
                    Pitch: note.NoteNumber,
                    Velocity: note.Velocity,
                    OnsetTicks: note.Time,
                    DurationTicks: note.Length,
                    OnsetUs: onsetUs,
                    DurationUs: Math.Max(0, endUs - onsetUs));

                trackNotes.Add(source);
                notes.Add(source);
            }

            perTrack.Add(trackNotes);
        }

        // Stable global ordering, then index assignment. The comparison must be total so that
        // two runs over the same bytes produce identical indices - the edit journal depends on it.
        notes.Sort(CompareForIndexing);
        for (var i = 0; i < notes.Count; i++)
        {
            notes[i] = notes[i] with { Index = i };
        }

        var songDurationUs = notes.Count == 0 ? 0 : notes.Max(n => n.EndUs);
        var tracks = new List<SourceTrack>(trackChunks.Count);
        for (var t = 0; t < trackChunks.Count; t++)
        {
            tracks.Add(BuildTrack(t, trackChunks[t], perTrack[t], songDurationUs));
        }

        return new SourceSong(
            fileName,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            LoaderVersion,
            SanitizeSettingsHash(noteMinLengthMs),
            ticksPerQuarter,
            ReadTempoMap(tempoMap),
            ReadTimeSignatures(tempoMap),
            tracks,
            notes,
            repaired);
    }

    private static int CompareForIndexing(SourceNote a, SourceNote b)
    {
        var c = a.OnsetUs.CompareTo(b.OnsetUs);
        if (c != 0)
        {
            return c;
        }

        c = a.TrackIndex.CompareTo(b.TrackIndex);
        if (c != 0)
        {
            return c;
        }

        c = a.Channel.CompareTo(b.Channel);
        if (c != 0)
        {
            return c;
        }

        c = a.Pitch.CompareTo(b.Pitch);
        return c != 0 ? c : a.DurationUs.CompareTo(b.DurationUs);
    }

    private static SourceTrack BuildTrack(int index, TrackChunk chunk, List<SourceNote> notes, long songDurationUs)
    {
        var name = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text?.Trim();

        ushort channelMask = 0;
        foreach (var note in notes)
        {
            channelMask |= (ushort)(1 << note.Channel);
        }

        if (notes.Count == 0)
        {
            return new SourceTrack(index, name, 0, 0, 0, 0, 0, 0, 0, false, 0);
        }

        var isPercussion = notes.All(n => n.IsPercussion);
        var minPitch = notes.Min(n => n.Pitch);
        var maxPitch = notes.Max(n => n.Pitch);
        var meanPitch = notes.Average(n => (double)n.Pitch);
        var durationUs = notes.Max(n => n.EndUs) - notes.Min(n => n.OnsetUs);
        var polyphony = MelodyScoring.MeanPolyphony(notes);

        var score = MelodyScoring.Score(
            notes.Count, durationUs, songDurationUs, meanPitch, maxPitch - minPitch, polyphony, isPercussion);

        return new SourceTrack(
            index, name, channelMask, notes.Count, durationUs,
            minPitch, maxPitch, meanPitch, polyphony, isPercussion, score);
    }

    private static List<TempoChange> ReadTempoMap(TempoMap tempoMap)
    {
        var result = new List<TempoChange>();
        foreach (var change in tempoMap.GetTempoChanges())
        {
            var us = (long)TimeConverter.ConvertTo<MetricTimeSpan>(change.Time, tempoMap).TotalMicroseconds;
            result.Add(new TempoChange(change.Time, us, (int)change.Value.MicrosecondsPerQuarterNote));
        }

        if (result.Count == 0 || result[0].Ticks != 0)
        {
            var initial = tempoMap.GetTempoAtTime((MidiTimeSpan)0);
            result.Insert(0, new TempoChange(0, 0, (int)initial.MicrosecondsPerQuarterNote));
        }

        return result;
    }

    private static List<TimeSignatureChange> ReadTimeSignatures(TempoMap tempoMap)
    {
        var result = new List<TimeSignatureChange>();
        foreach (var change in tempoMap.GetTimeSignatureChanges())
        {
            var us = (long)TimeConverter.ConvertTo<MetricTimeSpan>(change.Time, tempoMap).TotalMicroseconds;
            result.Add(new TimeSignatureChange(change.Time, us, change.Value.Numerator, change.Value.Denominator));
        }

        if (result.Count == 0 || result[0].Ticks != 0)
        {
            var initial = tempoMap.GetTimeSignatureAtTime((MidiTimeSpan)0);
            result.Insert(0, new TimeSignatureChange(0, 0, initial.Numerator, initial.Denominator));
        }

        return result;
    }

    private static bool RegisterCodePages()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
