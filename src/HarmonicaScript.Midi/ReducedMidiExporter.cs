using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Export;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace HarmonicaScript.Midi;

/// <summary>
/// Re-exports the reduced, transposed, monophonic result as a Type-0 MIDI file.
///
/// The highest-leverage interop in the whole plan: it makes HarmonicaScript useful to the ENTIRE
/// existing FFXIV-Bard and Genshin-lyre tool ecosystem without anyone adopting our playback
/// engine, our score format or our opinions. Someone can run their file through the converter for
/// the reduction and transposition alone, then feed the result to whatever they already use.
/// </summary>
public sealed class ReducedMidiExporter : IMacroExporter
{
    public string Id => "reduced-mid";

    public LocalizedText DisplayName => new("Reduced MIDI (Type 0)", "降调后的单轨 MIDI (Type 0)");

    public string FileExtension => ".reduced.mid";

    public ExporterCapabilities Capabilities { get; } = new();

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        const short TicksPerQuarter = 480;
        var bpm = Math.Max(1, context.Meta.Bpm);
        var usPerQuarter = (long)(60_000_000.0 / bpm);
        var usPerTick = usPerQuarter / (double)TicksPerQuarter;

        var file = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarter) };
        var chunk = new TrackChunk();
        chunk.Events.Add(new SequenceTrackNameEvent(context.Meta.Title));
        chunk.Events.Add(new SetTempoEvent(usPerQuarter));

        // Uses the SCHEDULED times, not the musical ones: the point is to reproduce what the
        // instrument will actually play, including every articulation repair.
        var events = new List<(long Tick, MidiEvent Event, int Order)>();
        foreach (var note in context.Score.Audible.OrderBy(n => n.ScheduledDownUs))
        {
            var onTick = (long)Math.Round(note.ScheduledDownUs / usPerTick);
            var offTick = Math.Max(onTick + 1, (long)Math.Round(note.ScheduledUpUs / usPerTick));

            events.Add((onTick, new NoteOnEvent(new SevenBitNumber(note.EffectiveMidiNote), new SevenBitNumber(80)), 1));
            events.Add((offTick, new NoteOffEvent(new SevenBitNumber(note.EffectiveMidiNote), new SevenBitNumber(0)), 0));
        }

        long previous = 0;
        foreach (var (tick, midiEvent, _) in events.OrderBy(e => e.Tick).ThenBy(e => e.Order))
        {
            midiEvent.DeltaTime = tick - previous;
            previous = tick;
            chunk.Events.Add(midiEvent);
        }

        file.Chunks.Add(chunk);
        file.Write(output, MidiFileFormat.SingleTrack, new WritingSettings { TextEncoding = MidiReader.DefaultTextEncoding });

        return ExportResult.Success(
            "这个 .mid 是降调归约之后的单轨结果，可以直接喂给现有的 FF14 / 原神 自动演奏器。",
            "This .mid is the reduced, transposed, monophonic result - feed it to any existing FFXIV or Genshin auto-player.");
    }
}
