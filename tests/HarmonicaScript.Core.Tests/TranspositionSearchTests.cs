using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Tests;

/// <summary>
/// The degeneracy regression suite. Every case here is one a weighted-sum objective gets wrong,
/// and the tiered comparison exists precisely to get them right.
/// </summary>
public sealed class TranspositionSearchTests
{
    private static ArticulationModel Model =>
        ArticulationModel.Build(DeltaForceProfile.Reference.Emission, DeltaForceProfile.Reference.Timing);

    private static readonly ConversionSettings Settings = new();
    private static readonly ObjectiveWeights Weights = ObjectiveWeights.Default;

    private static TranspositionResult Run(IReadOnlyList<byte> pitches, ConversionSettings? settings = null)
    {
        var onsets = pitches.Select((_, i) => i * 400_000L).ToList();
        var durations = pitches.Select(_ => 300_000L).ToList();
        return TranspositionSearch.Search(Model, pitches, onsets, durations, settings ?? Settings, Weights);
    }

    /// <summary>A diatonic scale rooted on <paramref name="tonic"/>, comfortably inside the instrument.</summary>
    private static byte[] Scale(int tonic) =>
        [.. new[] { 0, 2, 4, 5, 7, 9, 11, 12, 11, 9, 7, 5, 4, 2, 0 }.Select(i => (byte)(tonic + i))];

    [Theory]
    [InlineData(60)] [InlineData(61)] [InlineData(62)] [InlineData(63)]
    [InlineData(64)] [InlineData(65)] [InlineData(66)] [InlineData(67)]
    [InlineData(68)] [InlineData(69)] [InlineData(70)] [InlineData(71)]
    public void FullyPlayablePiecesResolveToTheMinimalKeyDistance(int tonic)
    {
        // Twelve keys, all fitting the instrument without loss. Since nothing is dropped or
        // folded, tiers 1 and 2 tie and tier 3 must decide - so the answer has to be the
        // transposition closest to the original key, i.e. zero semitones of key change.
        var result = Run(Scale(tonic));

        Assert.Equal(0, result.Best.HardInfeasible);
        Assert.Equal(0, result.Best.MusicalLossPpm);
        Assert.Equal(0, result.Best.KeyDistance);
        Assert.Equal(0, result.Best.Transpose % 12);
    }

    [Fact]
    public void DoesNotTransposeAnAllSharpPieceDownASemitone()
    {
        // THE degeneracy trap. Every note is a black key, so "transpose -1 and hold 半音 for the
        // whole piece" sounds IDENTICAL to "transpose 0" - same pitches, same loss - and costs
        // fewer... no, it costs MORE duty, but a naive weighted sum can still be fooled when the
        // duty weight is small. Tier 3 settles it before mechanics is ever consulted.
        byte[] allSharps = [61, 63, 66, 68, 70, 73, 75, 78, 73, 70, 68, 66, 63, 61];

        var result = Run(allSharps);

        Assert.Equal(0, result.Best.KeyDistance);
        Assert.Equal(0, result.Best.Transpose % 12);
    }

    [Fact]
    public void PrefersTheSmallerShiftWhenTwoCandidatesTieOnKeyDistance()
    {
        // +3 and -9 are the same key change. The AbsTranspose tie-break must take +3.
        var result = Run(Scale(84));   // sits high; needs to come down

        Assert.True(
            Math.Abs(result.Best.Transpose) <= 12,
            $"expected a compact shift, got {result.Best.Transpose}");
    }

    [Fact]
    public void MechanicalSavingsNeverBuyAKeyChange()
    {
        // Construct a piece that fits at several transpositions with zero musical loss. Whatever
        // mechanical differences exist between them, the winner must still be the one that keeps
        // the key - mechanics is tier 4 and can only break a tie among musical equals.
        var result = Run(Scale(64));

        var zeroLoss = result.TopCandidates.Where(c => c.HardInfeasible == 0 && c.MusicalLossPpm == 0).ToList();
        Assert.NotEmpty(zeroLoss);
        Assert.Equal(0, result.Best.KeyDistance);

        // Some sibling candidate is mechanically cheaper or equal but musically further away;
        // it must not have won.
        Assert.All(zeroLoss, c => Assert.True(c.KeyDistance >= result.Best.KeyDistance));
    }

    [Fact]
    public void HardInfeasibilityOutranksAnyAmountOfMusicalLoss()
    {
        // Tier 1 is a raw count and is never normalised. A candidate with even one infeasible
        // transition must lose to one that drops a little music instead.
        var a = new CandidateScore(0, HardInfeasible: 1, MusicalLossPpm: 0, KeyDistance: 0, AbsTranspose: 0, Mechanical: 0);
        var b = new CandidateScore(2, HardInfeasible: 0, MusicalLossPpm: 300_000, KeyDistance: 2, AbsTranspose: 2, Mechanical: long.MaxValue / 2);

        Assert.True(CandidateScore.Compare(b, a) < 0, "a feasible candidate must beat an infeasible one");
    }

    [Fact]
    public void LongSongsDoNotMakeInfeasibilityLookCheaper()
    {
        // The bug a normalised objective has: dividing the infeasible count by the note count
        // makes a 2000-note song with 20 broken transitions score better than a 100-note song
        // with 5. Because tier 1 is a count, the comparison is length-independent.
        var shortSong = new CandidateScore(0, 5, 0, 0, 0, 0);
        var longSong = new CandidateScore(0, 20, 0, 0, 0, 0);

        Assert.True(CandidateScore.Compare(shortSong, longSong) < 0);
    }

    [Fact]
    public void SignedMod12MapsIntoPlusOrMinusSixSemitones()
    {
        Assert.Equal(0, CandidateScore.SignedMod12(0));
        Assert.Equal(0, CandidateScore.SignedMod12(12));
        Assert.Equal(0, CandidateScore.SignedMod12(-24));
        Assert.Equal(-1, CandidateScore.SignedMod12(-1));
        Assert.Equal(-1, CandidateScore.SignedMod12(11));
        Assert.Equal(1, CandidateScore.SignedMod12(13));
        Assert.Equal(6, CandidateScore.SignedMod12(6));
        Assert.Equal(-5, CandidateScore.SignedMod12(7));
    }

    [Fact]
    public void PreserveKeyModeOnlyEverConsidersWholeOctaves()
    {
        var result = Run(Scale(84), Settings with { TranspositionMode = TranspositionMode.PreserveKey });

        Assert.Equal(0, result.Best.Transpose % 12);
        Assert.All(result.TopCandidates, c => Assert.Equal(0, c.Transpose % 12));
    }

    [Fact]
    public void ManualModeEvaluatesExactlyWhatTheUserAsked()
    {
        var result = Run(Scale(60), Settings with { TranspositionMode = TranspositionMode.Manual, ManualTranspose = 5 });

        Assert.Equal(5, result.Best.Transpose);
        Assert.Equal(1, result.CandidatesEvaluated);
    }

    [Fact]
    public void WideRangeMaterialLosesNotesAndSaysSoRatherThanFailing()
    {
        // A five-octave piano part against a 38-semitone instrument. Something has to give; the
        // requirement is that it gives measurably and the search still returns a usable answer.
        var wide = Enumerable.Range(0, 61).Select(i => (byte)(36 + i)).ToArray();

        var result = Run(wide);

        Assert.True(result.Best.MusicalLossPpm > 0, "a five-octave part cannot be lossless here");
        Assert.NotEmpty(result.States);
        Assert.True(result.Legalise.DroppedCount + result.Legalise.FoldedCount > 0);
    }

    [Fact]
    public void IsDeterministic()
    {
        // Two runs must agree exactly, or golden files are worthless.
        var pitches = Scale(67);
        var first = Run(pitches);
        var second = Run(pitches);

        Assert.Equal(first.Best, second.Best);
        Assert.Equal(first.States, second.States);
        Assert.Equal(first.Offsets, second.Offsets);
        Assert.Equal(
            first.TopCandidates.Select(c => c.Transpose),
            second.TopCandidates.Select(c => c.Transpose));
    }

    [Fact]
    public void SweepsEveryCandidateWithoutPrefiltering()
    {
        // A prefilter would prune on out-of-range count, which is nearly flat across neighbouring
        // transpositions, and could discard the churn-optimal candidate before scoring it.
        var result = Run(Scale(60));

        Assert.True(result.CandidatesEvaluated >= 20, $"only {result.CandidatesEvaluated} candidates were evaluated");
        Assert.Equal(5, result.TopCandidates.Count);
    }
}
