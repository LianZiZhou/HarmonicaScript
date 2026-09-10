using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace HarmonicaScript.Midi.Fixtures;

/// <summary>A note to write into a fixture, in ticks.</summary>
public readonly record struct FixtureNote(long OnsetTicks, long DurationTicks, byte Pitch, byte Velocity, byte Channel);

/// <summary>
/// Writes deterministic MIDI fixtures. Determinism matters: golden files are compared byte for
/// byte, so nothing here may consult the clock or a random source. Variation comes from the
/// fixture's own parameters, never from entropy.
/// </summary>
public static class FixtureWriter
{
    public const short TicksPerQuarter = 480;

    public static MidiFile Build(
        IEnumerable<IReadOnlyList<FixtureNote>> tracks,
        int bpm = 120,
        IReadOnlyList<string>? trackNames = null,
        IReadOnlyList<(long Ticks, int Bpm)>? tempoChanges = null,
        (int Numerator, int Denominator)? timeSignature = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var file = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarter) };

        var conductor = new TrackChunk();
        conductor.Events.Add(new SetTempoEvent(60_000_000L / bpm));
        if (timeSignature is { } ts)
        {
            conductor.Events.Add(new TimeSignatureEvent((byte)ts.Numerator, (byte)ts.Denominator));
        }

        if (tempoChanges is not null)
        {
            long previous = 0;
            foreach (var (ticks, changeBpm) in tempoChanges.OrderBy(c => c.Ticks))
            {
                conductor.Events.Add(new SetTempoEvent(60_000_000L / changeBpm) { DeltaTime = ticks - previous });
                previous = ticks;
            }
        }

        file.Chunks.Add(conductor);

        var index = 0;
        foreach (var notes in tracks)
        {
            var chunk = new TrackChunk();
            var name = trackNames is not null && index < trackNames.Count ? trackNames[index] : null;
            if (name is not null)
            {
                chunk.Events.Add(new SequenceTrackNameEvent(name));
            }

            var events = new List<(long Time, MidiEvent Event, int Order)>();
            foreach (var note in notes)
            {
                // Note-offs sort before note-ons at the same instant so a repeated pitch does not
                // look like an orphaned overlap to the reader.
                events.Add((note.OnsetTicks, new NoteOnEvent(new SevenBitNumber(note.Pitch), new SevenBitNumber(note.Velocity)) { Channel = new FourBitNumber(note.Channel) }, 1));
                events.Add((note.OnsetTicks + note.DurationTicks, new NoteOffEvent(new SevenBitNumber(note.Pitch), new SevenBitNumber(0)) { Channel = new FourBitNumber(note.Channel) }, 0));
            }

            long previous = 0;
            foreach (var (time, midiEvent, _) in events.OrderBy(e => e.Time).ThenBy(e => e.Order))
            {
                midiEvent.DeltaTime = time - previous;
                previous = time;
                chunk.Events.Add(midiEvent);
            }

            file.Chunks.Add(chunk);
            index++;
        }

        return file;
    }

    /// <summary>Renders one <see cref="PublicDomainMelody"/> to a single-track file.</summary>
    public static MidiFile FromMelody(PublicDomainMelody melody)
    {
        ArgumentNullException.ThrowIfNull(melody);

        var notes = new List<FixtureNote>();
        long cursor = 0;
        const long Eighth = TicksPerQuarter / 2;

        foreach (var step in MelodyNotation.Parse(melody.Notation))
        {
            var length = step.EighthNotes * Eighth;
            if (step.Pitch is { } pitch)
            {
                // Slightly detached, as a performer would play: leaves a real gap for the
                // reduction passes to work with instead of a synthetic legato chain.
                notes.Add(new FixtureNote(cursor, Math.Max(1, (length * 15) / 16), (byte)pitch, 80, 0));
            }

            cursor += length;
        }

        return Build([notes], melody.Bpm, [melody.Title], timeSignature: (4, 4));
    }

    /// <summary>
    /// Writes via a temporary file and an atomic move, so a concurrent reader in another test
    /// process can never observe a half-written fixture.
    /// </summary>
    public static void Write(MidiFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var temporary = full + "." + Environment.ProcessId + ".tmp";

        // Must mirror MidiReader's encoding, or a Chinese track name round-trips as "???".
        file.Write(temporary, overwriteFile: true, settings: new WritingSettings
        {
            TextEncoding = MidiReader.DefaultTextEncoding,
        });

        File.Move(temporary, full, overwrite: true);
    }
}
