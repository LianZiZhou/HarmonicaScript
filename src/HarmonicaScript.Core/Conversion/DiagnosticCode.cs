namespace HarmonicaScript.Core.Conversion;

/// <summary>
/// Machine-readable reasons the converter changed or refused something.
///
/// The core NEVER produces localised prose: it emits a code plus numeric arguments, and the GUI
/// maps the code through resx. That is what makes "只看有问题的音" filterable, makes every
/// diagnostic clickable, and stops a half-translated release from shipping - a test asserts
/// every member here has an entry in both resource files.
/// </summary>
public enum DiagnosticCode
{
    None = 0,

    // ---- reduction ----
    ChordCollapsed,
    NoteTruncatedByNextOnset,
    NoteDroppedTooShort,
    MelodySpikeSmoothed,
    MelodySpikePromotedSibling,

    // ---- range ----
    NoteOctaveFolded,
    NoteDroppedOutOfRange,
    NoteDroppedFoldWouldBreakContour,

    // ---- articulation ----
    DurationClamped,
    OnsetShifted,
    MergedWithPrevious,
    DroppedInsufficientGap,
    BreathRestInserted,
    HoldSplit,

    // ---- planning ----
    ModifierSwapInfeasible,
    ConservativeRespell,

    // ---- edits ----
    EditReanchored,
    EditOrphaned,
    UserEditApplied,
}

/// <summary>
/// One diagnostic, anchored to a note so the UI can select it. <paramref name="Args"/> carries
/// the numbers ("缺 38 毫秒"), never a sentence.
/// </summary>
public readonly record struct Diagnostic(
    DiagnosticCode Code,
    int NoteId,
    long AtUs,
    long Arg0 = 0,
    long Arg1 = 0)
{
    /// <summary>True for diagnostics that changed what the listener hears.</summary>
    public bool IsAudible => Code is
        DiagnosticCode.NoteDroppedTooShort or
        DiagnosticCode.NoteDroppedOutOfRange or
        DiagnosticCode.NoteDroppedFoldWouldBreakContour or
        DiagnosticCode.DroppedInsufficientGap or
        DiagnosticCode.MergedWithPrevious or
        DiagnosticCode.NoteOctaveFolded or
        DiagnosticCode.MelodySpikeSmoothed;
}
