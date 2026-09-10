using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Audition;

public sealed record AuditionOptions(int SampleRate = 44100, float Gain = 0.35f, string? SoundFontPath = null)
{
    public int TailMs { get; init; } = 600;
}

/// <summary>
/// Renders what the timeline will ACTUALLY produce, offline, to a WAV file.
///
/// This is a correctness instrument, not a toy. The developer cannot run the game, so listening
/// to the reduced monophonic score is the only way to evaluate conversion quality at all - and it
/// must be the SIMULATED output, not the source MIDI, or it would be auditioning the wrong thing.
///
/// Offline only. There is no audio-device dependency anywhere in this project: NAudio has no
/// macOS backend, and every alternative adds a platform-specific failure mode in exchange for
/// convenience CI cannot use. The WAV is written and the OS default player opens it.
/// </summary>
public static class AuditionRenderer
{
    /// <summary>Renders <see cref="SimulatedNote"/>s - i.e. the simulator's reading of the finished timeline.</summary>
    public static float[] Render(IReadOnlyList<SimulatedNote> notes, AuditionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var opts = options ?? new AuditionOptions();

        if (notes.Count == 0)
        {
            return [];
        }

        var endMs = notes.Max(n => n.UpMs) + opts.TailMs;
        var buffer = new float[(long)endMs * opts.SampleRate / 1000];

        if (opts.SoundFontPath is not null && File.Exists(opts.SoundFontPath))
        {
            RenderWithSoundFont(notes, buffer, opts);
        }
        else
        {
            RenderWithBuiltInVoice(notes, buffer, opts);
        }

        return buffer;
    }

    public static void RenderToFile(IReadOnlyList<SimulatedNote> notes, string path, AuditionOptions? options = null)
    {
        var opts = options ?? new AuditionOptions();
        WavWriter.Write(path, Render(notes, opts), opts.SampleRate);
    }

    /// <summary>
    /// A small additive voice shaped like a reed instrument: odd harmonics with a soft attack.
    ///
    /// Deliberately built in rather than requiring a SoundFont. It makes the audition path work
    /// on a bare checkout and in CI with no downloadable asset, and it is fully deterministic,
    /// which matters because the SimulatedNote list IS golden-filed even though the audio is not.
    /// </summary>
    private static void RenderWithBuiltInVoice(IReadOnlyList<SimulatedNote> notes, float[] buffer, AuditionOptions opts)
    {
        var rate = opts.SampleRate;

        foreach (var note in notes)
        {
            var frequency = 440.0 * Math.Pow(2.0, (note.Pitch - 69) / 12.0);
            var start = (long)note.DownMs * rate / 1000;
            var sustain = (long)note.DurationMs * rate / 1000;
            var release = Math.Min(rate / 8, 5000);
            var attack = Math.Min(rate / 200, sustain / 2 + 1);

            for (long i = 0; i < sustain + release && start + i < buffer.Length; i++)
            {
                var t = (double)i / rate;

                var envelope = i < attack
                    ? (double)i / attack
                    : i < sustain
                        ? 1.0
                        : 1.0 - ((double)(i - sustain) / release);

                var phase = 2.0 * Math.PI * frequency * t;
                var value = Math.Sin(phase)
                    + (0.35 * Math.Sin(3 * phase))
                    + (0.15 * Math.Sin(5 * phase))
                    + (0.07 * Math.Sin(7 * phase));

                buffer[start + i] += (float)(value * envelope * opts.Gain * 0.55);
            }
        }
    }

    /// <summary>Uses a user-supplied SoundFont when one is configured. Serialised: MeltySynth is explicitly not thread-safe.</summary>
    private static void RenderWithSoundFont(IReadOnlyList<SimulatedNote> notes, float[] buffer, AuditionOptions opts)
    {
        var synthesizer = new MeltySynth.Synthesizer(opts.SoundFontPath!, opts.SampleRate);
        var left = new float[buffer.Length];
        var right = new float[buffer.Length];

        var events = new List<(int Sample, bool On, int Pitch)>(notes.Count * 2);
        foreach (var note in notes)
        {
            events.Add(((int)((long)note.DownMs * opts.SampleRate / 1000), true, note.Pitch));
            events.Add(((int)((long)note.UpMs * opts.SampleRate / 1000), false, note.Pitch));
        }

        events.Sort((a, b) => a.Sample != b.Sample ? a.Sample.CompareTo(b.Sample) : a.On.CompareTo(b.On));

        var cursor = 0;
        var index = 0;
        while (cursor < buffer.Length)
        {
            while (index < events.Count && events[index].Sample <= cursor)
            {
                if (events[index].On)
                {
                    synthesizer.NoteOn(0, events[index].Pitch, 100);
                }
                else
                {
                    synthesizer.NoteOff(0, events[index].Pitch);
                }

                index++;
            }

            var next = index < events.Count ? Math.Min(events[index].Sample, buffer.Length) : buffer.Length;
            var count = Math.Max(1, next - cursor);
            count = Math.Min(count, buffer.Length - cursor);

            synthesizer.Render(left.AsSpan(cursor, count), right.AsSpan(cursor, count));
            cursor += count;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (left[i] + right[i]) * 0.5f * (opts.Gain / 0.35f);
        }
    }
}
