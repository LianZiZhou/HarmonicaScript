namespace HarmonicaScript.Core.Conversion.Passes;

/// <summary>Keeps only the selected tracks, and drops the GM drum channel unless asked not to.</summary>
public sealed class TrackSelectPass : IScorePass
{
    public string Id => "track.select";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var selected = context.Settings.SelectedTracks.Count > 0
            ? context.Settings.SelectedTracks.ToHashSet()
            : context.Song.AutoSelectedTrack >= 0 ? [context.Song.AutoSelectedTrack] : [];

        var droppedTrack = 0;
        var droppedPercussion = 0;

        foreach (var note in context.Notes.Where(n => n.IsAudible))
        {
            if (context.Settings.ExcludePercussion && note.Channel == 9)
            {
                note.Drop(NoteAlteration.None);
                droppedPercussion++;
            }
            else if (selected.Count > 0 && !selected.Contains(note.TrackIndex))
            {
                note.Drop(NoteAlteration.None);
                droppedTrack++;
            }
        }

        return new PassResult(
            droppedTrack + droppedPercussion > 0,
            [
                new PassCounter("track.select.droppedTrack", droppedTrack),
                new PassCounter("track.select.droppedPercussion", droppedPercussion),
            ],
            []);
    }
}

/// <summary>
/// Removes leading silence. Community MIDIs routinely open with a bar or two of nothing, and
/// nobody wants to stand in the game watching a countdown finish and then wait.
/// </summary>
public sealed class TrimSilencePass : IScorePass
{
    public string Id => "time.trim";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Settings.TrimLeadingSilence)
        {
            return PassResult.Unchanged;
        }

        var audible = context.Audible();
        if (audible.Count == 0)
        {
            return PassResult.Unchanged;
        }

        var offset = audible[0].OnsetUs;
        if (offset <= 0)
        {
            return PassResult.Unchanged;
        }

        foreach (var note in context.Notes)
        {
            note.OnsetUs -= offset;
        }

        context.TrimmedUs = offset;
        return new PassResult(true, [new PassCounter("time.trim.removedMs", offset / 1000)], []);
    }
}

/// <summary>
/// Applies the global speed multiplier to the microsecond timeline.
///
/// Runs AFTER the reduction group on purpose. The articulation floors deliberately do NOT scale
/// with it: they are properties of the input path, not of the music, so playing a piece at 2x
/// makes it harder to play rather than proportionally faster.
/// </summary>
public sealed class SpeedPass : IScorePass
{
    public string Id => "time.speed";

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var speed = context.Settings.Speed;
        if (Math.Abs(speed - 1.0) < 1e-9)
        {
            return PassResult.Unchanged;
        }

        if (speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), speed, "Speed must be positive.");
        }

        foreach (var note in context.Notes)
        {
            note.OnsetUs = (long)Math.Round(note.OnsetUs / speed);
            note.DurationUs = (long)Math.Round(note.DurationUs / speed);
        }

        return new PassResult(true, [new PassCounter("time.speed.percent", (long)Math.Round(speed * 100))], []);
    }
}
