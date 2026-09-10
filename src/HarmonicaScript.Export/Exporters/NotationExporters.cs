using System.Globalization;
using System.Text;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Export.Exporters;

/// <summary>
/// Human-readable jianpu (numbered notation), which is how this community actually reads music.
///
/// The grammar is deliberately ASCII-safe and whitespace-tokenised, which resolves the one real
/// ambiguity up front: a '-' GLUED to a digit is an octave-down marker ("-5"), while a STANDALONE
/// '-' is a duration extension (增时线). Both readings match existing habits, and tokenisation
/// makes them unambiguous rather than a matter of taste.
/// </summary>
public sealed class JianpuExporter : IMacroExporter
{
    public string Id => "jianpu-hsq";

    public LocalizedText DisplayName => new("Jianpu sheet (text)", "简谱 (文本)");

    public string FileExtension => ".hsq";

    public ExporterCapabilities Capabilities { get; } = new(NewLine: "\n");

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.Append("# ").Append(context.Meta.Title).Append('\n');
        builder.Append("@instrument: ").Append(context.Instrument.Ref).Append('\n');
        builder.Append("@bpm: ").Append(context.Meta.Bpm.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("@transpose: ").Append(context.Score.Transpose.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("@grammar: 'PITCH' is 1-7; '#' prefix raises a semitone; a '-' or '+' GLUED to the\n");
        builder.Append("#          digit lowers or raises an octave (-5, +1); a STANDALONE '-' extends the\n");
        builder.Append("#          previous note; '0' is a rest; '|' is a bar line.\n");
        builder.Append("@note: stores musical PITCH, not fingering. The modifier plan is re-derived on import,\n");
        builder.Append("#      because the best fingering depends on settings that may have changed.\n\n");

        var audible = context.Score.Audible.OrderBy(n => n.ScheduledDownUs).ToList();
        if (audible.Count == 0)
        {
            builder.Append("0\n");
            WriteOut(builder, output, Capabilities);
            return ExportResult.Success();
        }

        var beatUs = (long)(60_000_000.0 / Math.Max(1, context.Meta.Bpm));
        long previousEndUs = audible[0].ScheduledDownUs;
        var sinceBar = 0;

        foreach (var note in audible)
        {
            // A gap of at least half a beat reads as a rest.
            var gapUs = note.ScheduledDownUs - previousEndUs;
            if (gapUs > beatUs / 2)
            {
                var rests = (int)(gapUs / beatUs);
                for (var i = 0; i < Math.Min(rests, 8); i++)
                {
                    builder.Append("0 ");
                    sinceBar++;
                }
            }

            builder.Append(Jianpu(context.Instrument, note)).Append(' ');
            sinceBar++;

            // Sustain marks for anything appreciably longer than a beat.
            var beats = (int)(note.ScheduledUpUs - note.ScheduledDownUs) / Math.Max(1, (int)beatUs);
            for (var i = 1; i < Math.Min(beats, 8); i++)
            {
                builder.Append("- ");
                sinceBar++;
            }

            if (sinceBar >= 8)
            {
                builder.Append("|\n");
                sinceBar = 0;
            }

            previousEndUs = note.ScheduledUpUs;
        }

        builder.Append("\n");
        WriteOut(builder, output, Capabilities);
        return ExportResult.Success();
    }

    /// <summary>Renders one note relative to the instrument's base pitch.</summary>
    private static string Jianpu(InstrumentProfile profile, ScoreNote note)
    {
        var offset = note.EffectiveMidiNote - profile.BaseMidiNote;
        var octave = (int)Math.Floor(offset / 12.0);
        var within = offset - (octave * 12);

        // Degree within the octave, plus a sharp when it falls between two naturals.
        int[] naturals = [0, 2, 4, 5, 7, 9, 11];
        var degree = Array.IndexOf(naturals, within);
        var sharp = false;

        if (degree < 0)
        {
            degree = Array.IndexOf(naturals, within - 1);
            sharp = true;
        }

        var text = (sharp ? "#" : string.Empty) + (degree + 1).ToString(CultureInfo.InvariantCulture);
        return octave switch
        {
            0 => text,
            < 0 => new string('-', -octave) + text,
            _ => new string('+', octave) + text,
        };
    }

    private static void WriteOut(StringBuilder builder, Stream output, ExporterCapabilities capabilities)
    {
        var bytes = capabilities.Encoding.GetBytes(builder.ToString());
        output.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// The character tape this community already recognises from Genshin lyre tools: one character
/// per note, with a bracketed prefix when a modifier is engaged.
/// </summary>
public sealed class KeyTapeExporter : IMacroExporter
{
    public string Id => "keytape";

    public LocalizedText DisplayName => new("Key tape (text)", "按键纸带 (文本)");

    public string FileExtension => ".keytape.txt";

    public ExporterCapabilities Capabilities { get; } = new(NewLine: "\n");

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.Append("# ").Append(context.Meta.Title).Append(" - HarmonicaScript key tape\n");
        builder.Append("# [L] = 降调 hold left mouse, [M] = 半音 hold middle, [R] = 升调 hold right\n\n");

        var emission = Core.Instrument.EmissionTable.Build(context.Instrument);
        ushort lastMask = 0;
        var column = 0;

        foreach (var note in context.Score.Audible.OrderBy(n => n.ScheduledDownUs))
        {
            if (note.Fingering is not { } fingering)
            {
                continue;
            }

            var mask = emission.States[fingering.StateIndex].Mask;
            if (mask != lastMask)
            {
                builder.Append('[');
                for (var m = 0; m < context.Instrument.Modifiers.Count; m++)
                {
                    if ((mask & (1 << m)) != 0)
                    {
                        builder.Append(context.Instrument.Modifiers[m].Id switch
                        {
                            "octaveDown" => 'L',
                            "semitone" => 'M',
                            "octaveUp" => 'R',
                            _ => (char)('a' + m),
                        });
                    }
                }

                builder.Append("] ");
                lastMask = mask;
            }

            var binding = context.Instrument.Degrees[fingering.DegreeIndex].Binding;
            var name = context.Keys.Describe(binding);
            builder.Append(name.Length == 1 ? char.ToLowerInvariant(name[0]) : ',');

            if (++column % 32 == 0)
            {
                builder.Append('\n');
            }
        }

        builder.Append('\n');
        var bytes = Capabilities.Encoding.GetBytes(builder.ToString());
        output.Write(bytes, 0, bytes.Length);
        return ExportResult.Success();
    }
}
