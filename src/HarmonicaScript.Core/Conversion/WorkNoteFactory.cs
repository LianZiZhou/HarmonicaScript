using HarmonicaScript.Core.Source;

namespace HarmonicaScript.Core.Conversion;

/// <summary>Seeds the pipeline's working notes from a parsed song.</summary>
public static class WorkNoteFactory
{
    public static List<WorkNote> Create(SourceSong song)
    {
        ArgumentNullException.ThrowIfNull(song);

        var notes = new List<WorkNote>(song.Notes.Count);
        foreach (var source in song.Notes)
        {
            notes.Add(new WorkNote
            {
                SourceIndex = source.Index,
                TrackIndex = source.TrackIndex,
                Channel = source.Channel,
                SourceMidiNote = source.Pitch,
                Velocity = source.Velocity,
                OnsetTicks = source.OnsetTicks,
                DurationTicks = source.DurationTicks,
                OnsetUs = source.OnsetUs,
                DurationUs = source.DurationUs,
                EffectiveMidiNote = source.Pitch,
            });
        }

        return notes;
    }
}
