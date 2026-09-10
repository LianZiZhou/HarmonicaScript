namespace HarmonicaScript.Audition;

/// <summary>Minimal 16-bit PCM WAV writer. No dependency, no audio device, no platform backend.</summary>
public static class WavWriter
{
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate, int channels = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataBytes = samples.Length * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                                   // PCM chunk size
        writer.Write((short)1);                             // format: PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);            // byte rate
        writer.Write((short)(channels * 2));                // block align
        writer.Write((short)16);                            // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (var sample in samples)
        {
            writer.Write((short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        }
    }
}
