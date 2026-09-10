using HarmonicaScript.Midi;
using HarmonicaScript.Midi.Fixtures;

namespace HarmonicaScript.Midi.Tests;

public sealed class MidiReaderTests
{
    [Theory]
    [MemberData(nameof(Corpus.PublicDomainNames), MemberType = typeof(Corpus))]
    public void PublicDomainFixturesParse(string name)
    {
        var song = Corpus.Read("pd", name);

        Assert.NotEmpty(song.Notes);
        Assert.All(song.Notes, n => Assert.InRange(n.Pitch, (byte)1, (byte)127));
        Assert.All(song.Notes, n => Assert.True(n.DurationUs > 0, $"note {n.Index} has non-positive duration"));
        Assert.NotEmpty(song.TempoMap);
    }

    [Theory]
    [MemberData(nameof(Corpus.ProceduralNames), MemberType = typeof(Corpus))]
    public void ProceduralFixturesParse(string name)
    {
        var song = Corpus.Read("generated", name);
        Assert.NotEmpty(song.Notes);
    }

    [Theory]
    [MemberData(nameof(Corpus.MalformedNames), MemberType = typeof(Corpus))]
    public void MalformedFixturesLoadInsteadOfThrowing(string name)
    {
        // The whole point of the tolerant ReadingSettings. A strict reader rejects all six of
        // these; every other MIDI player on earth opens them.
        var song = Corpus.Read("malformed", name);
        Assert.NotNull(song);
    }

    [Fact]
    public void IndicesAreDeterministicAcrossReads()
    {
        // The edit journal anchors to SourceNote.Index forever, so two reads of identical bytes
        // must produce identical indices or a saved project silently re-targets its edits.
        var a = Corpus.Read("generated", "melody-plus-accompaniment");
        var b = Corpus.Read("generated", "melody-plus-accompaniment");

        Assert.Equal(a.Sha256, b.Sha256);
        Assert.Equal(a.Notes.Count, b.Notes.Count);
        Assert.Equal(
            a.Notes.Select(n => (n.Index, n.Pitch, n.OnsetUs)),
            b.Notes.Select(n => (n.Index, n.Pitch, n.OnsetUs)));
    }

    [Fact]
    public void NotesAreSortedByOnsetAndIndexedContiguously()
    {
        var song = Corpus.Read("generated", "with-percussion");

        Assert.Equal(Enumerable.Range(0, song.Notes.Count), song.Notes.Select(n => n.Index));
        for (var i = 1; i < song.Notes.Count; i++)
        {
            Assert.True(song.Notes[i - 1].OnsetUs <= song.Notes[i].OnsetUs, "notes must be sorted by onset");
        }
    }

    [Fact]
    public void DecodesChineseTrackNames()
    {
        // Written through DryWetMidi with the GB18030 encoding, read back the same way.
        var song = Corpus.Read("generated", "gbk-track-name");
        Assert.Contains(song.Tracks, t => t.Name is not null && t.Name.Contains("主旋律", StringComparison.Ordinal));
    }

    [Fact]
    public void DecodesRawGbkBytesInAMalformedFile()
    {
        // This one carries literal GBK bytes that are not valid UTF-8, so it fails without the
        // CodePagesEncodingProvider registration.
        var song = Corpus.Read("malformed", "gbk-encoded-name");
        Assert.Contains(song.Tracks, t => t.Name == "主旋律");
    }

    [Fact]
    public void ExcludesPercussionFromTheAutoSelectedTrack()
    {
        var song = Corpus.Read("generated", "with-percussion");

        var drums = Assert.Single(song.Tracks, t => t.IsPercussion);
        Assert.NotEqual(drums.Index, song.AutoSelectedTrack);
        Assert.Equal(0, drums.MelodyScore);
    }

    [Fact]
    public void PicksTheMelodyOverTheAccompaniment()
    {
        var song = Corpus.Read("generated", "melody-plus-accompaniment");

        var picked = song.Tracks[song.AutoSelectedTrack];
        Assert.Equal("Melody", picked.Name);

        // The pad is chordal and low; the tune is monophonic and high.
        var pad = song.Tracks.Single(t => t.Name == "Strings");
        Assert.True(picked.MeanPolyphony < pad.MeanPolyphony);
        Assert.True(picked.MeanPitch > pad.MeanPitch);
        Assert.True(picked.MelodyScore > pad.MelodyScore);
    }

    [Fact]
    public void ConvertsTicksToMicrosecondsThroughTheTempoMap()
    {
        // 120 BPM, 480 ticks per quarter: a quarter note is exactly 500 000 us.
        var song = Corpus.Read("generated", "chromatic-full-range");
        var second = song.Notes[1];

        Assert.Equal(240, second.OnsetTicks);
        Assert.Equal(250_000, second.OnsetUs);
    }

    [Fact]
    public void TracksTempoChanges()
    {
        var song = Corpus.Read("generated", "multi-tempo");

        // 90 BPM initial plus three changes.
        Assert.Equal(4, song.TempoMap.Count);
        Assert.Equal(90, song.TempoMap[0].Bpm, 1);
        Assert.Equal([90.0, 140.0, 60.0, 180.0], song.TempoMap.Select(t => Math.Round(t.Bpm)));

        // A tempo ramp must make the microsecond timeline non-uniform even though ticks are uniform.
        var deltasUs = song.Notes.Zip(song.Notes.Skip(1), (a, b) => b.OnsetUs - a.OnsetUs).Distinct().ToList();
        Assert.True(deltasUs.Count > 1, "tempo changes must produce varying microsecond gaps");
    }

    [Fact]
    public void FallsBackToOneHundredTwentyBpmWhenNoTempoEventExists()
    {
        var song = Corpus.Read("malformed", "no-tempo-event");
        Assert.Equal(120, song.TempoMap[0].Bpm, 1);
    }

    [Fact]
    public void RemovesZeroLengthNotesDuringSanitisation()
    {
        var song = Corpus.Read("malformed", "zero-length-notes");

        // Sanitize's NoteMinLength drops them; whatever survives must have real duration.
        Assert.All(song.Notes, n => Assert.True(n.DurationUs > 0));
    }

    [Fact]
    public void StampsLoaderVersionAndSanitiseHash()
    {
        var song = Corpus.Read("pd", "twinkle");

        Assert.Equal(MidiReader.LoaderVersion, song.LoaderVersion);
        Assert.Equal(MidiReader.SanitizeSettingsHash(10), song.SanitizeSettingsHash);
        Assert.Equal(64, song.Sha256.Length);
    }

    [Fact]
    public void SanitiseHashChangesWithSettingsSoProjectsCanDetectRenumbering()
    {
        Assert.NotEqual(MidiReader.SanitizeSettingsHash(10), MidiReader.SanitizeSettingsHash(25));
    }
}
