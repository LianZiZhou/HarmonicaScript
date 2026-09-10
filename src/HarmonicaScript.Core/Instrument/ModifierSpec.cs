namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// A control that shifts the whole note row while it is active. Modelled generically:
/// nothing in the engine knows the words "octave" or "semitone", so a second instrument is
/// a data file rather than a code change.
/// </summary>
/// <param name="Id">Stable identifier used in diagnostics, traces and golden files.</param>
/// <param name="DisplayName">Shown in the UI. For this instrument: 降调 / 半音 / 升调.</param>
/// <param name="SemitoneDelta">Semitones added to every degree while active.</param>
/// <param name="Binding">The physical control.</param>
/// <param name="Behavior">Hold or toggle. Confirmed Hold for this instrument by the user, but kept as data so a calibration report can correct it without a rebuild.</param>
/// <param name="ExclusionGroup">0 means none. Equal non-zero values are mutually exclusive - 降调 and 升调 share group 1 and are never emitted together.</param>
/// <param name="PressLeadMs">How far ahead of a dependent note the control must be pressed.</param>
/// <param name="ReleaseLeadMs">How far ahead of a dependent note the control must be released.</param>
/// <param name="DutyWeightMilli">
/// Per-second ergonomic/safety cost, in thousandths, charged by the planner for holding this
/// control. 降调 is weighted 3x the others because LEFT MOUSE IS FIRE in this game: the
/// objective must actively avoid parking a melody in the low band.
/// </param>
public sealed record ModifierSpec(
    string Id,
    LocalizedText DisplayName,
    int SemitoneDelta,
    Binding Binding,
    ModifierBehavior Behavior,
    int ExclusionGroup,
    int PressLeadMs,
    int ReleaseLeadMs,
    int DutyWeightMilli);
