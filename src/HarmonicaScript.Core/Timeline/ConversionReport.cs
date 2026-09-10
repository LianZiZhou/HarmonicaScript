using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Core.Timeline;

/// <summary>
/// A PROJECTION over the counter bag, not a parallel set of typed fields.
///
/// Forty typed fields duplicating the counters would be two sources of truth for the same
/// number, both golden-snapshotted, guaranteed to drift. Here, adding a metric means adding a
/// counter, and every headline number below is derived.
///
/// There is deliberately NO "notes out of scale" metric. On a fully chromatic instrument it is
/// structurally always zero; every lyre tool reports it as a headline, and here it would be theatre.
/// </summary>
public sealed record ConversionReport(
    IReadOnlyList<PassCounter> Counters,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<CandidateScore> TopCandidates,
    int SourceNoteCount,
    int AudibleNoteCount,
    double SameKeyCeilingNps,
    double KeyChangeCeilingNps,
    double ModifierSwapCeilingNps,
    double EffectiveCeilingNps,
    double PeakNps,
    double P95Nps,
    double P50Nps,
    int TotalModifierEvents,
    int ExclusiveSwaps,
    int LongestModifierRun)
{
    public long Counter(string id) => Counters.FirstOrDefault(c => c.Id == id).Value;

    public int DroppedCount => Diagnostics.Count(d =>
        d.Code is DiagnosticCode.NoteDroppedTooShort
            or DiagnosticCode.NoteDroppedOutOfRange
            or DiagnosticCode.NoteDroppedFoldWouldBreakContour
            or DiagnosticCode.DroppedInsufficientGap
            or DiagnosticCode.MelodySpikeSmoothed);

    public int FoldedCount => Diagnostics.Count(d => d.Code == DiagnosticCode.NoteOctaveFolded);

    public int MergedCount => Diagnostics.Count(d => d.Code == DiagnosticCode.MergedWithPrevious);

    public int ShiftedCount => Diagnostics.Count(d => d.Code == DiagnosticCode.OnsetShifted);

    /// <summary>Notes the ladder had to merge or drop. Well-defined precisely as those two outcomes.</summary>
    public int DeficitEventCount => MergedCount
        + Diagnostics.Count(d => d.Code == DiagnosticCode.DroppedInsufficientGap);

    public double PreservedFraction =>
        SourceNoteCount == 0 ? 1 : (double)AudibleNoteCount / SourceNoteCount;

    /// <summary>
    /// The stacked penalty breakdown the UI renders. A bare "78/100" tells a user nothing; the
    /// decomposition tells them which setting to change.
    /// </summary>
    public IReadOnlyList<(string Reason, int Penalty)> PenaltyBreakdown()
    {
        var total = Math.Max(1, SourceNoteCount);
        return
        [
            ("dropped", (int)Math.Round(40.0 * DroppedCount / total)),
            ("folded", (int)Math.Round(20.0 * FoldedCount / total)),
            ("merged", (int)Math.Round(25.0 * MergedCount / total)),
            ("shifted", (int)Math.Round(10.0 * ShiftedCount / total)),
        ];
    }

    /// <summary>0-100. Derived from the breakdown, so the number and its explanation can never disagree.</summary>
    public int PlayabilityScore => Math.Clamp(100 - PenaltyBreakdown().Sum(p => p.Penalty), 0, 100);

    public string Grade => PlayabilityScore switch
    {
        >= 95 => "A",
        >= 85 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "E",
    };
}
