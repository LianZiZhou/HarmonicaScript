namespace HarmonicaScript.Midi.Fixtures;

/// <summary>A deliberately corrupt file, and the damage it carries.</summary>
public sealed record MalformedFixture(string Name, string Damage, Func<byte[]> Build);

/// <summary>
/// Files that a strict reader rejects and every other MIDI player opens anyway.
///
/// These are not hypothetical: an illegal key signature, a truncated final chunk and an
/// orphaned note-on are all common in the community corpus this tool exists to consume.
/// Refusing them would be technically defensible and practically useless.
/// </summary>
public static class MalformedFixtures
{
    public static IReadOnlyList<MalformedFixture> All { get; } =
    [
        new("bad-key-signature", "KeySignature with scale=255, which is outside the legal 0..1", () =>
        {
            var track = new List<byte>();
            Meta(track, 0x59, [0x00, 0xFF]);      // key signature, scale = 255
            NoteOn(track, 0, 60, 80);
            NoteOff(track, 480, 60);
            EndOfTrack(track);
            return Assemble([track]);
        }),

        new("truncated-chunk", "final track chunk declares more bytes than the file contains", () =>
        {
            var track = new List<byte>();
            NoteOn(track, 0, 62, 80);
            NoteOff(track, 480, 62);
            EndOfTrack(track);
            var full = Assemble([track]);
            return full[..(full.Length - 4)];      // lop off the tail
        }),

        new("orphaned-note-on", "a note-on with no matching note-off before end of track", () =>
        {
            var track = new List<byte>();
            NoteOn(track, 0, 64, 80);
            NoteOn(track, 480, 67, 80);
            NoteOff(track, 480, 67);
            EndOfTrack(track);                      // 64 is never released
            return Assemble([track]);
        }),

        new("gbk-encoded-name", "track name in GBK bytes, which is not valid UTF-8", () =>
        {
            var track = new List<byte>();
            // "主旋律" in GBK: D6 F7 D0 FD C2 C9
            Meta(track, 0x03, [0xD6, 0xF7, 0xD0, 0xFD, 0xC2, 0xC9]);
            NoteOn(track, 0, 65, 80);
            NoteOff(track, 480, 65);
            EndOfTrack(track);
            return Assemble([track]);
        }),

        new("zero-length-notes", "note-on immediately followed by note-off at the same tick", () =>
        {
            var track = new List<byte>();
            for (var i = 0; i < 8; i++)
            {
                NoteOn(track, i == 0 ? 0 : 240, (byte)(60 + i), 80);
                NoteOff(track, 0, (byte)(60 + i));
            }

            EndOfTrack(track);
            return Assemble([track]);
        }),

        new("no-tempo-event", "no SetTempo anywhere; the reader must fall back to 120 BPM", () =>
        {
            var track = new List<byte>();
            for (var i = 0; i < 8; i++)
            {
                NoteOn(track, i == 0 ? 0 : 0, (byte)(60 + i), 80);
                NoteOff(track, 240, (byte)(60 + i));
            }

            EndOfTrack(track);
            return Assemble([track], includeTempo: false);
        }),
    ];

    private static void Vlq(List<byte> output, long value)
    {
        var buffer = new Stack<byte>();
        buffer.Push((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            buffer.Push((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        output.AddRange(buffer);
    }

    private static void NoteOn(List<byte> track, long delta, byte pitch, byte velocity)
    {
        Vlq(track, delta);
        track.AddRange([0x90, pitch, velocity]);
    }

    private static void NoteOff(List<byte> track, long delta, byte pitch)
    {
        Vlq(track, delta);
        track.AddRange([0x80, pitch, 0x00]);
    }

    private static void Meta(List<byte> track, byte type, byte[] data)
    {
        Vlq(track, 0);
        track.AddRange([0xFF, type]);
        Vlq(track, data.Length);
        track.AddRange(data);
    }

    private static void EndOfTrack(List<byte> track)
    {
        Vlq(track, 0);
        track.AddRange([0xFF, 0x2F, 0x00]);
    }

    private static byte[] Assemble(IReadOnlyList<List<byte>> tracks, bool includeTempo = true)
    {
        var output = new List<byte>();

        var conductor = new List<byte>();
        if (includeTempo)
        {
            Meta(conductor, 0x51, [0x07, 0xA1, 0x20]);  // 500000 us per quarter = 120 BPM
        }

        EndOfTrack(conductor);

        var all = new List<List<byte>> { conductor };
        all.AddRange(tracks);

        output.AddRange("MThd"u8.ToArray());
        output.AddRange(BeUInt32(6));
        output.AddRange(BeUInt16(1));                      // format 1
        output.AddRange(BeUInt16((ushort)all.Count));
        output.AddRange(BeUInt16((ushort)FixtureWriter.TicksPerQuarter));

        foreach (var track in all)
        {
            output.AddRange("MTrk"u8.ToArray());
            output.AddRange(BeUInt32((uint)track.Count));
            output.AddRange(track);
        }

        return [.. output];
    }

    private static byte[] BeUInt16(ushort value) => [(byte)(value >> 8), (byte)value];

    private static byte[] BeUInt32(uint value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
