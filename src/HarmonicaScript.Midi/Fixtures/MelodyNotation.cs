using System.Globalization;

namespace HarmonicaScript.Midi.Fixtures;

/// <summary>
/// A tiny notation for writing fixture melodies inline: whitespace-separated
/// <c>PITCH:DURATION</c> tokens, where PITCH is scientific pitch (<c>C4</c>, <c>F#5</c>,
/// <c>Bb3</c>) or <c>R</c> for a rest, and DURATION counts eighth notes.
///
/// Exists so the public-domain corpus is readable, reviewable and diffable as source rather
/// than arriving as opaque binaries of uncertain origin.
/// </summary>
public static class MelodyNotation
{
    private static readonly Dictionary<char, int> Naturals = new()
    {
        ['C'] = 0, ['D'] = 2, ['E'] = 4, ['F'] = 5, ['G'] = 7, ['A'] = 9, ['B'] = 11,
    };

    public readonly record struct Step(int? Pitch, int EighthNotes);

    public static IReadOnlyList<Step> Parse(string notation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notation);
        var steps = new List<Step>();

        foreach (var token in notation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.LastIndexOf(':');
            if (colon <= 0)
            {
                throw new FormatException($"Token '{token}' is not PITCH:DURATION.");
            }

            var pitchText = token[..colon];
            var duration = int.Parse(token[(colon + 1)..], CultureInfo.InvariantCulture);
            if (duration <= 0)
            {
                throw new FormatException($"Token '{token}' has a non-positive duration.");
            }

            steps.Add(new Step(pitchText is "R" or "r" ? null : ParsePitch(pitchText), duration));
        }

        return steps;
    }

    public static int ParsePitch(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var letter = char.ToUpperInvariant(text[0]);
        if (!Naturals.TryGetValue(letter, out var semitone))
        {
            throw new FormatException($"'{text}' does not start with a note letter A-G.");
        }

        var i = 1;
        while (i < text.Length && (text[i] == '#' || text[i] == 'b'))
        {
            semitone += text[i] == '#' ? 1 : -1;
            i++;
        }

        var octave = int.Parse(text[i..], CultureInfo.InvariantCulture);

        // MIDI note 60 == C4 (scientific pitch), the convention every tool in this space uses.
        return ((octave + 1) * 12) + semitone;
    }
}
