namespace HarmonicaScript.Core.Source;

/// <summary>
/// Heuristic for "which track is the tune". Kept in Core, and deliberately explainable: the UI
/// shows the score next to each track so a user can see why the star landed where it did.
/// </summary>
public static class MelodyScoring
{
    /// <summary>
    /// Weighted blend of four signals, each normalised to 0..1:
    ///   monophony  - a melody is mostly one note at a time (the strongest signal)
    ///   substance  - enough notes and enough duration to be the tune rather than a stab
    ///   register   - melodies sit above accompaniment
    ///   mobility   - a melody moves; a pad or a pedal tone does not
    /// </summary>
    public static double Score(
        int noteCount,
        long durationUs,
        long songDurationUs,
        double meanPitch,
        int pitchSpan,
        double meanPolyphony,
        bool isPercussion)
    {
        if (isPercussion || noteCount == 0)
        {
            return 0;
        }

        var monophony = 1.0 / Math.Max(1.0, meanPolyphony);
        var coverage = songDurationUs <= 0 ? 0 : Math.Clamp((double)durationUs / songDurationUs, 0, 1);
        var substance = Math.Clamp(noteCount / 64.0, 0, 1) * 0.5 + coverage * 0.5;
        var register = Math.Clamp((meanPitch - 36) / 60.0, 0, 1);
        var mobility = Math.Clamp(pitchSpan / 24.0, 0, 1);

        return (monophony * 0.45) + (substance * 0.25) + (register * 0.20) + (mobility * 0.10);
    }

    /// <summary>
    /// Mean simultaneous-note count over the track's sounding time. Computed by sweeping note
    /// starts and ends, so a chord of three held notes counts as 3 for exactly as long as it sounds.
    /// </summary>
    public static double MeanPolyphony(IReadOnlyList<SourceNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0)
        {
            return 0;
        }

        var edges = new List<(long Time, int Delta)>(notes.Count * 2);
        foreach (var note in notes)
        {
            edges.Add((note.OnsetUs, +1));
            edges.Add((note.EndUs, -1));
        }

        edges.Sort((x, y) => x.Time != y.Time ? x.Time.CompareTo(y.Time) : x.Delta.CompareTo(y.Delta));

        long soundingUs = 0;
        double weighted = 0;
        var depth = 0;
        var previous = edges[0].Time;

        foreach (var (time, delta) in edges)
        {
            if (depth > 0 && time > previous)
            {
                var span = time - previous;
                soundingUs += span;
                weighted += (double)span * depth;
            }

            depth += delta;
            previous = time;
        }

        return soundingUs == 0 ? 1 : weighted / soundingUs;
    }
}
