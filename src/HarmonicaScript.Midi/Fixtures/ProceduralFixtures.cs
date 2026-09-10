using Melanchall.DryWetMidi.Core;

namespace HarmonicaScript.Midi.Fixtures;

/// <summary>A named, deterministically generated stress fixture.</summary>
/// <param name="Name">Output file stem.</param>
/// <param name="Purpose">What it is designed to break. Shown in test failure output.</param>
/// <param name="Build">Deterministic generator - must never consult the clock or a random source.</param>
public sealed record ProceduralFixture(string Name, string Purpose, Func<MidiFile> Build);

/// <summary>
/// The pathological corpus. Real music exercises the happy path; these exist to exercise the
/// edges the happy path never reaches, and they are generated from code so the repository holds
/// intent rather than megabytes of opaque binaries.
///
/// P0 for the shipped instrument profile is MIDI 60, so its playable span is 48..85.
/// </summary>
public static class ProceduralFixtures
{
    private const int P0 = 60;
    private const int Lo = P0 - 12;  // 48
    private const int Hi = P0 + 25;  // 85
    private const short Q = FixtureWriter.TicksPerQuarter;

    /// <summary>At 120 BPM one quarter note is 500 ms, so ticks-per-millisecond is 480/500.</summary>
    private static long Ms(int milliseconds) => (long)Math.Round(milliseconds * (Q / 500.0));

    public static IReadOnlyList<ProceduralFixture> All { get; } =
    [
        new("chromatic-full-range", "every playable semitone in turn; must produce zero out-of-range notes", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var pitch = Lo; pitch <= Hi; pitch++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)pitch, 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["chromatic ascent"]);
        }),

        new("band-seam-oscillation", "hammers offsets 0 and 12 - the two three-way offsets - so the planner must choose spellings", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 48; i++, t += Q / 4)
            {
                notes.Add(new FixtureNote(t, (Q / 4) - Ms(20), (byte)(i % 2 == 0 ? P0 : P0 + 12), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["seam oscillation"]);
        }),

        new("long-sustains", "four-second notes; forces the maxKeyHoldMs split without breaking the constant-pitch invariant", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var pitch in new[] { P0, P0 + 7, P0 + 12, P0 - 5 })
            {
                notes.Add(new FixtureNote(t, Q * 8, (byte)pitch, 80, 0));  // 4 s at 120 BPM
                t += Q * 9;
            }

            return FixtureWriter.Build([notes], 120, ["sustains"]);
        }),

        new("dense-sixteenths", "sixteenths at 125 ms, comfortably above the floor; the everyday fast case", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            var scale = new[] { 0, 2, 4, 5, 7, 9, 11, 12 };
            for (var i = 0; i < 128; i++, t += Q / 4)
            {
                notes.Add(new FixtureNote(t, (Q / 4) - Ms(15), (byte)(P0 + scale[i % scale.Length]), 90, 0));
            }

            return FixtureWriter.Build([notes], 120, ["sixteenths"]);
        }),

        new("impossible-density", "30 ms inter-onsets - below every floor; the ladder MUST merge or drop and say so", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 200; i++, t += Ms(30))
            {
                notes.Add(new FixtureNote(t, Ms(25), (byte)(P0 + (i % 13)), 90, 0));
            }

            return FixtureWriter.Build([notes], 120, ["impossible"]);
        }),

        new("exclusive-swap-storm", "alternates the extreme low and high bands, forcing an exclusion-group swap on every note", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 64; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(40), (byte)(i % 2 == 0 ? Lo + 1 : Hi - 1), 85, 0));
            }

            return FixtureWriter.Build([notes], 120, ["swap storm"]);
        }),

        new("five-octave-piano", "a 60-semitone span against a 38-semitone instrument; exercises fold-then-drop", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 61; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(36 + i), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["wide"]);
        }),

        new("rolled-chords", "chord members spread 45 ms apart - inside the collapse window but not simultaneous", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var root in new[] { P0, P0 + 5, P0 + 7, P0 })
            {
                for (var v = 0; v < 3; v++)
                {
                    notes.Add(new FixtureNote(t + (Ms(45) * v), Q, (byte)(root + (v * 4)), 80, 0));
                }

                t += Q * 2;
            }

            return FixtureWriter.Build([notes], 120, ["rolled"]);
        }),

        new("block-chords", "simultaneous triads; the skyline reduction must keep exactly the top note", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var root in new[] { P0, P0 + 5, P0 + 7, P0 + 2, P0 })
            {
                foreach (var interval in new[] { 0, 4, 7 })
                {
                    notes.Add(new FixtureNote(t, Q - Ms(30), (byte)(root + interval), 80, 0));
                }

                t += Q;
            }

            return FixtureWriter.Build([notes], 120, ["block chords"]);
        }),

        new("melody-plus-accompaniment", "two tracks: a high monophonic tune over a low chordal pad; track auto-pick must find the tune", () =>
        {
            var melody = new List<FixtureNote>();
            var pad = new List<FixtureNote>();
            long t = 0;
            var tune = new[] { 12, 14, 16, 17, 19, 17, 16, 14 };
            for (var i = 0; i < 32; i++, t += Q / 2)
            {
                melody.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(P0 + tune[i % tune.Length]), 95, 0));
                if (i % 4 == 0)
                {
                    foreach (var interval in new[] { -12, -8, -5 })
                    {
                        pad.Add(new FixtureNote(t, Q * 2, (byte)(P0 + interval), 55, 1));
                    }
                }
            }

            return FixtureWriter.Build([melody, pad], 110, ["Melody", "Strings"]);
        }),

        new("with-percussion", "a drum track on channel 9; must be excluded by default and greyed out in the UI", () =>
        {
            var melody = new List<FixtureNote>();
            var drums = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 32; i++, t += Q / 2)
            {
                melody.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(P0 + ((i * 2) % 12)), 90, 0));
                drums.Add(new FixtureNote(t, Q / 8, (byte)(i % 2 == 0 ? 36 : 38), 100, 9));
            }

            return FixtureWriter.Build([melody, drums], 120, ["Lead", "Drums"]);
        }),

        new("multi-tempo", "three tempo changes; absolute microsecond conversion must track them", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 48; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(P0 + ((i * 5) % 15)), 80, 0));
            }

            return FixtureWriter.Build(
                [notes], 90, ["tempo ramp"],
                tempoChanges: [(Q * 8, 140), (Q * 16, 60), (Q * 20, 180)]);
        }),

        new("leading-silence", "eight seconds of nothing before the first note; the trim pass must remove it", () =>
        {
            var notes = new List<FixtureNote>();
            var t = Q * 16;
            for (var i = 0; i < 16; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(P0 + ((i * 2) % 12)), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["late start"]);
        }),

        new("legato-overlap", "every note overlaps the next by 50%; the monophony sweep must truncate cleanly", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 32; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, Q, (byte)(P0 + ((i * 3) % 13)), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["legato"]);
        }),

        new("grace-notes", "10 ms ornaments below the survival floor, attached to real notes", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 24; i++, t += Q)
            {
                notes.Add(new FixtureNote(t, Ms(10), (byte)(P0 + ((i % 7) * 2) - 1), 60, 0));
                notes.Add(new FixtureNote(t + Ms(12), Q - Ms(40), (byte)(P0 + ((i % 7) * 2)), 90, 0));
            }

            return FixtureWriter.Build([notes], 120, ["ornaments"]);
        }),

        new("spike-outliers", "isolated notes an octave above their neighbours; smoothMelody must promote or drop them", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 32; i++, t += Q / 2)
            {
                var spike = i % 8 == 3;
                notes.Add(new FixtureNote(t, spike ? Ms(60) : (Q / 2) - Ms(30), (byte)(P0 + (spike ? 20 : i % 5)), 80, 0));
                if (spike)
                {
                    notes.Add(new FixtureNote(t, Ms(60), (byte)(P0 + (i % 5)), 70, 0));  // the sibling to promote
                }
            }

            return FixtureWriter.Build([notes], 120, ["spikes"]);
        }),

        new("gbk-track-name", "a Chinese track name; proves GB18030 decoding rather than mojibake", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 16; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(30), (byte)(P0 + ((i * 2) % 12)), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["主旋律 - 高音部"]);
        }),

        new("five-minute-drift", "five minutes of steady eighths; the delta-time exporters must not accumulate drift", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            for (var i = 0; i < 1200; i++, t += Q / 2)
            {
                notes.Add(new FixtureNote(t, (Q / 2) - Ms(40), (byte)(P0 + ((i * 7) % 19) - 6), 80, 0));
            }

            return FixtureWriter.Build([notes], 120, ["drift"]);
        }),

        // ---- calibration probes (docs/CALIBRATION.md) --------------------------------------
        new("c1-single-note", "C1: one note. Did SendInput reach the game at all?", () =>
            FixtureWriter.Build([[new FixtureNote(0, Q, P0, 100, 0)]], 120, ["C1"])),

        new("c2-modifier-probe", "C2: four notes that all need the same modifier held. Hold or toggle?", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var offset in new[] { 12, 14, 16, 17 })
            {
                notes.Add(new FixtureNote(t, Q - Ms(60), (byte)(P0 + offset), 100, 0));
                t += Q;
            }

            return FixtureWriter.Build([notes], 100, ["C2"]);
        }),

        new("c3-octave-probe", "C3: the same pitch twice. Is the octave modifier exactly +-12?", () =>
            FixtureWriter.Build(
                [[new FixtureNote(0, Q, (byte)(P0 + 12), 100, 0), new FixtureNote(Q * 2, Q, (byte)(P0 + 12), 100, 0)]],
                100, ["C3"])),

        new("c4-retrigger-ladder", "C4: one pitch repeated at 8/12/16/20/25/30 ms gaps. Where is the floor?", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var gap in new[] { 8, 12, 16, 20, 25, 30 })
            {
                for (var i = 0; i < 6; i++)
                {
                    notes.Add(new FixtureNote(t, Ms(40), P0, 100, 0));
                    t += Ms(40 + gap);
                }

                t += Q;  // a clear break between rungs
            }

            return FixtureWriter.Build([notes], 120, ["C4"]);
        }),

        new("c5-modifier-lead-ladder", "C5: exclusive low/high swaps at shrinking gaps. How long does a modifier take to settle?", () =>
        {
            var notes = new List<FixtureNote>();
            long t = 0;
            foreach (var gap in new[] { 25, 20, 16, 12, 8 })
            {
                for (var i = 0; i < 6; i++)
                {
                    notes.Add(new FixtureNote(t, Ms(40), (byte)(i % 2 == 0 ? Lo + 2 : Hi - 2), 100, 0));
                    t += Ms(40 + gap);
                }

                t += Q;
            }

            return FixtureWriter.Build([notes], 120, ["C5"]);
        }),
    ];
}
