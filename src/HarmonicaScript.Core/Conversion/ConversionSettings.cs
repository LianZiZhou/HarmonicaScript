namespace HarmonicaScript.Core.Conversion;

/// <summary>Which note survives when several sound at once.</summary>
public enum ReductionPolicy
{
    /// <summary>Skyline. Melody lives on top; this is right far more often than anything else.</summary>
    Highest = 0,
    Lowest = 1,
    Loudest = 2,
    TrackPriority = 3,
}

/// <summary>What to do with a note that still will not fit after the best transposition.</summary>
public enum OutOfRangePolicy
{
    /// <summary>Drop short ornaments, fold longer notes by at most one octave, drop when folding would wreck the contour.</summary>
    FoldThenDrop = 0,

    /// <summary>Never fold. Honest, and sometimes what a purist wants.</summary>
    DropOnly = 1,

    /// <summary>Fold as many octaves as it takes. Keeps every note, at the cost of octave-jumping.</summary>
    FoldUnbounded = 2,
}

/// <summary>How much freedom the transposition search has.</summary>
public enum TranspositionMode
{
    /// <summary>Whole octaves only, so the piece stays in its original key. Matters for playing along with a recording.</summary>
    PreserveKey = 0,

    /// <summary>Any semitone, but the objective prefers staying near the original key.</summary>
    Balanced = 1,

    /// <summary>Any semitone, judged purely on playability.</summary>
    Free = 2,

    /// <summary>Whatever the user typed.</summary>
    Manual = 3,
}

/// <summary>Everything the user can turn. Hashed into the score so a cached result is never stale.</summary>
public sealed record ConversionSettings
{
    /// <summary>Empty means "use <see cref="Source.SourceSong.AutoSelectedTrack"/>".</summary>
    public IReadOnlyList<int> SelectedTracks { get; init; } = [];

    public bool ExcludePercussion { get; init; } = true;

    public ReductionPolicy Reduction { get; init; } = ReductionPolicy.Highest;

    /// <summary>
    /// Notes starting within this window collapse into one chord. Applied in MUSICAL time,
    /// before the speed multiplier, because it is a NOTATIONAL tolerance rather than a physical
    /// one: at 2x speed a genuine 30 ms passage would otherwise be misread as a chord, and at
    /// 0.5x a real chord would escape collapse. 45 ms catches DAW-humanised and lightly-rolled
    /// chords that a 25 ms window misses.
    /// </summary>
    public int ChordWindowMs { get; init; } = 45;

    public bool TrimLeadingSilence { get; init; } = true;

    public double Speed { get; init; } = 1.0;

    public TranspositionMode TranspositionMode { get; init; } = TranspositionMode.Balanced;

    public int ManualTranspose { get; init; }

    public OutOfRangePolicy OutOfRange { get; init; } = OutOfRangePolicy.FoldThenDrop;

    public int MaxFoldOctaves { get; init; } = 1;

    /// <summary>An out-of-range note shorter than this is dropped rather than folded: an ornament in the wrong octave is more distracting than its absence.</summary>
    public int FoldMinDurationMs { get; init; } = 120;

    /// <summary>A fold is rejected when it creates a leap wider than this against BOTH neighbours.</summary>
    public int ContourGuardSemitones { get; init; } = 14;

    /// <summary>A gap at least this long counts as a phrase boundary, where a big leap is musically fine.</summary>
    public int PhraseGapMs { get; init; } = 250;

    /// <summary>Bounds the reduction fixpoint. Four is generous; the counter reports if it ever binds.</summary>
    public int MaxReductionIterations { get; init; } = 4;

    /// <summary>Off by default, seeded when on, and never described as evading anything.</summary>
    public bool Humanise { get; init; }

    public int HumaniseSigmaMs { get; init; } = 8;

    public int HumaniseSeed { get; init; } = 20260910;

    /// <summary>Null means "follow the timing profile".</summary>
    public bool? BreathRests { get; init; }
}
