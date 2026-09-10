using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Core.Planning;

/// <summary>What legalising a candidate transposition cost, in microseconds of music.</summary>
public readonly record struct LegaliseOutcome(
    long DroppedDurationLowUs,
    long DroppedDurationHighUs,
    long FoldedDurationUs,
    int DroppedCount,
    int FoldedCount);

/// <summary>
/// Brings out-of-range notes inside the instrument's span.
///
/// Runs INSIDE the candidate loop and BEFORE the planner, never after. Folding a note changes
/// which band it lands in, hence which modifier states can render it - so folding after planning
/// would invalidate the plan it was folded under.
/// </summary>
public static class RangeLegaliser
{
    /// <summary>
    /// Rewrites <paramref name="offsets"/> in place and marks unplayable entries with
    /// <see cref="int.MinValue"/> so the caller can drop them.
    /// </summary>
    public static LegaliseOutcome Legalise(
        EmissionTable emission,
        Span<int> offsets,
        ReadOnlySpan<long> onsetUs,
        ReadOnlySpan<long> durationUs,
        ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentNullException.ThrowIfNull(settings);

        long droppedLow = 0, droppedHigh = 0, folded = 0;
        var droppedCount = 0;
        var foldedCount = 0;

        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = offsets[i];
            if (emission.IsPlayable(offset))
            {
                continue;
            }

            var tooHigh = offset > emission.HiOffset;
            var drop = false;

            if (settings.OutOfRange == OutOfRangePolicy.DropOnly)
            {
                drop = true;
            }
            else if (durationUs[i] < settings.FoldMinDurationMs * 1000L)
            {
                // An ornament in the wrong octave is more distracting than its absence.
                drop = true;
            }
            else
            {
                var maxOctaves = settings.OutOfRange == OutOfRangePolicy.FoldUnbounded ? 8 : settings.MaxFoldOctaves;
                var candidate = Fold(emission, offset, maxOctaves);

                if (candidate is null || BreaksContour(emission, offsets, onsetUs, i, candidate.Value, settings))
                {
                    drop = true;
                }
                else
                {
                    offsets[i] = candidate.Value;
                    folded += durationUs[i];
                    foldedCount++;
                }
            }

            if (drop)
            {
                // Dropping a HIGH note is weighted more heavily by the transposition objective,
                // because melody lives on top - losing the tune is worse than losing a bass note.
                if (tooHigh)
                {
                    droppedHigh += durationUs[i];
                }
                else
                {
                    droppedLow += durationUs[i];
                }

                offsets[i] = int.MinValue;
                droppedCount++;
            }
        }

        return new LegaliseOutcome(droppedLow, droppedHigh, folded, droppedCount, foldedCount);
    }

    /// <summary>Moves an offset by whole octaves until it lands in range, or gives up.</summary>
    private static int? Fold(EmissionTable emission, int offset, int maxOctaves)
    {
        for (var octaves = 1; octaves <= maxOctaves; octaves++)
        {
            var down = offset - (12 * octaves);
            if (emission.IsPlayable(down))
            {
                return down;
            }

            var up = offset + (12 * octaves);
            if (emission.IsPlayable(up))
            {
                return up;
            }
        }

        return null;
    }

    /// <summary>
    /// A fold is rejected when it creates a leap wider than the guard against BOTH neighbours and
    /// neither adjacent gap is a phrase boundary. Inside a phrase a folded note reads as a wrong
    /// note; across a rest the ear forgives an octave displacement completely.
    ///
    /// Clamp-to-edge is deliberately not offered here: it destroys the pitch class and produces
    /// unison plateaus that listeners hear as a stuck key.
    /// </summary>
    private static bool BreaksContour(
        EmissionTable emission,
        ReadOnlySpan<int> offsets,
        ReadOnlySpan<long> onsetUs,
        int index,
        int folded,
        ConversionSettings settings)
    {
        var guard = settings.ContourGuardSemitones;
        var phraseGapUs = settings.PhraseGapMs * 1000L;

        var previous = FindNeighbour(emission, offsets, index, -1);
        var next = FindNeighbour(emission, offsets, index, +1);

        if (previous < 0 && next < 0)
        {
            return false;
        }

        var breaksPrevious = previous >= 0 && Math.Abs(folded - offsets[previous]) > guard;
        var breaksNext = next >= 0 && Math.Abs(folded - offsets[next]) > guard;

        if (previous >= 0 && breaksPrevious && onsetUs[index] - onsetUs[previous] >= phraseGapUs)
        {
            return false;
        }

        if (next >= 0 && breaksNext && onsetUs[next] - onsetUs[index] >= phraseGapUs)
        {
            return false;
        }

        return (previous < 0 || breaksPrevious) && (next < 0 || breaksNext) && (breaksPrevious || breaksNext);
    }

    private static int FindNeighbour(EmissionTable emission, ReadOnlySpan<int> offsets, int from, int step)
    {
        for (var i = from + step; i >= 0 && i < offsets.Length; i += step)
        {
            if (offsets[i] != int.MinValue && emission.IsPlayable(offsets[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
