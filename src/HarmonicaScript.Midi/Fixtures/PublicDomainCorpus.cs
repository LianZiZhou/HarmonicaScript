namespace HarmonicaScript.Midi.Fixtures;

/// <summary>One public-domain melody, written out as source so its provenance is self-evident.</summary>
/// <param name="FileName">Output file stem.</param>
/// <param name="Title">Human title.</param>
/// <param name="Attribution">Composer/origin and why it is unambiguously public domain.</param>
/// <param name="Bpm">Tempo.</param>
/// <param name="Notation">See <see cref="MelodyNotation"/>.</param>
public sealed record PublicDomainMelody(string FileName, string Title, string Attribution, int Bpm, string Notation);

/// <summary>
/// The everyday regression corpus.
///
/// These are TYPED OUT, not downloaded. That is a deliberate copyright posture: HarmonicaScript
/// ships no song library, and a committed pop MIDI - however widely it circulates - would turn a
/// neutral converter into a distribution platform. Everything here is by a composer dead more
/// than 70 years, or is traditional with no identifiable author.
/// </summary>
public static class PublicDomainCorpus
{
    public static IReadOnlyList<PublicDomainMelody> All { get; } =
    [
        new("ode-to-joy", "Ode to Joy", "Beethoven, Symphony No. 9 (1824)", 120,
            "E4:2 E4:2 F4:2 G4:2 G4:2 F4:2 E4:2 D4:2 C4:2 C4:2 D4:2 E4:2 E4:3 D4:1 D4:4 "
            + "E4:2 E4:2 F4:2 G4:2 G4:2 F4:2 E4:2 D4:2 C4:2 C4:2 D4:2 E4:2 D4:3 C4:1 C4:4"),

        new("twinkle", "Twinkle, Twinkle, Little Star / 小星星", "Traditional; melody 'Ah! vous dirai-je, maman' (1761)", 100,
            "C4:2 C4:2 G4:2 G4:2 A4:2 A4:2 G4:4 F4:2 F4:2 E4:2 E4:2 D4:2 D4:2 C4:4 "
            + "G4:2 G4:2 F4:2 F4:2 E4:2 E4:2 D4:4 G4:2 G4:2 F4:2 F4:2 E4:2 E4:2 D4:4"),

        new("jingle-bells", "Jingle Bells", "James Lord Pierpont (1857)", 140,
            "E4:2 E4:2 E4:4 E4:2 E4:2 E4:4 E4:2 G4:2 C4:3 D4:1 E4:8 "
            + "F4:2 F4:2 F4:3 F4:1 F4:2 E4:2 E4:2 E4:1 E4:1 E4:2 D4:2 D4:2 E4:2 D4:4 G4:4"),

        new("fur-elise", "Für Elise (opening)", "Beethoven, WoO 59 (1810)", 72,
            "E5:1 D#5:1 E5:1 D#5:1 E5:1 B4:1 D5:1 C5:1 A4:3 R:1 C4:1 E4:1 A4:1 B4:3 R:1 "
            + "E4:1 G#4:1 B4:1 C5:3 R:1 E4:1 E5:1 D#5:1"),

        new("amazing-grace", "Amazing Grace", "Traditional; tune 'New Britain' (1829)", 84,
            "G4:2 C5:4 E5:1 C5:1 E5:4 D5:2 C5:4 A4:2 G4:6 G4:2 C5:4 E5:1 C5:1 E5:4 D5:2 G5:6"),

        new("greensleeves", "Greensleeves", "English traditional (16th century)", 96,
            "A4:2 C5:3 D5:1 E5:3 F5:1 E5:2 D5:3 B4:1 G4:3 A4:1 B4:2 C5:3 A4:1 A4:3 G#4:1 A4:2 "
            + "B4:3 G#4:1 E4:4 R:2 A4:2 C5:3 D5:1 E5:3 F5:1 E5:2 D5:3 B4:1 G4:6"),

        new("scarborough-fair", "Scarborough Fair", "English traditional (Dorian mode)", 88,
            "A4:4 A4:4 E5:8 E5:2 B4:2 C5:2 B4:2 A4:4 F4:4 E4:4 D4:4 "
            + "D4:2 A4:2 A4:4 C5:4 B4:2 A4:2 G4:4 A4:8"),

        new("silent-night", "Silent Night", "Franz Gruber (1818)", 76,
            "G4:3 A4:1 G4:2 E4:6 G4:3 A4:1 G4:2 E4:6 D5:4 D5:2 B4:6 C5:4 C5:2 G4:6 "
            + "A4:4 A4:2 C5:3 B4:1 A4:2 G4:3 A4:1 G4:2 E4:6"),

        new("auld-lang-syne", "Auld Lang Syne", "Robert Burns / Scottish traditional (1788)", 92,
            "C4:2 F4:3 F4:1 F4:2 A4:2 G4:3 F4:1 G4:2 A4:2 G4:3 F4:1 F4:2 A4:2 C5:6 "
            + "D5:2 C5:3 A4:1 A4:2 F4:2 G4:3 F4:1 G4:2 A4:2 G4:2 D4:2 D4:2 C4:6"),

        new("mo-li-hua", "茉莉花 Jasmine Flower", "Chinese traditional (Jiangsu, 18th century)", 84,
            "E4:2 E4:2 G4:2 A4:4 C5:2 C5:2 A4:4 G4:2 A4:2 G4:2 E4:4 "
            + "E4:2 D4:2 E4:2 G4:2 A4:4 G4:2 E4:2 D4:2 C4:6"),

        new("bach-minuet-g", "Minuet in G", "Petzold, BWV Anh. 114 (attributed to J. S. Bach)", 108,
            "D5:4 G4:1 A4:1 B4:1 C5:1 D5:2 G4:2 G4:2 E5:4 C5:1 D5:1 E5:1 F#5:1 G5:2 G4:2 G4:2 "
            + "C5:4 D5:1 C5:1 B4:1 A4:1 B4:4 C5:1 B4:1 A4:1 G4:1 F#4:4 G4:1 A4:1 B4:1 G4:1 A4:6"),

        new("joy-of-man", "Jesu, Joy of Man's Desiring (opening)", "J. S. Bach, BWV 147 (1723)", 96,
            "G4:1 A4:1 B4:1 D5:1 C5:1 C5:1 E5:1 D5:1 D5:1 G5:1 F#5:1 G5:1 D5:1 B4:1 G4:1 A4:1 "
            + "B4:1 A4:1 G4:1 A4:1 F#4:1 G4:1 D4:1 G4:1 B4:1 A4:1 G4:6"),

        new("hall-mountain-king", "In the Hall of the Mountain King (theme)", "Edvard Grieg, Peer Gynt (1875)", 120,
            "B3:1 C#4:1 D4:1 E4:1 F#4:1 D4:1 F#4:2 F4:1 D4:1 F4:2 E4:1 C#4:1 E4:2 "
            + "B3:1 C#4:1 D4:1 E4:1 F#4:1 D4:1 F#4:1 B4:1 A4:1 F#4:1 D4:1 F#4:1 A4:4"),

        new("danny-boy", "Londonderry Air / Danny Boy", "Irish traditional (1855)", 72,
            "C4:2 F4:3 A4:1 A#4:2 C5:4 D5:2 C5:2 A#4:2 A4:4 F4:2 G4:2 A4:2 F4:6 "
            + "G4:2 A4:3 A#4:1 C5:2 A4:4 F4:2 G4:2 A4:2 G4:2 F4:6"),

        new("swan-lake", "Swan Lake (theme)", "Tchaikovsky, Op. 20 (1876)", 100,
            "B4:2 E5:4 F#5:1 G5:1 A5:2 B5:4 A5:1 G5:1 F#5:2 E5:4 "
            + "B4:2 E5:4 F#5:1 G5:1 A5:2 G5:2 F#5:2 E5:2 D#5:2 E5:6"),
    ];
}
