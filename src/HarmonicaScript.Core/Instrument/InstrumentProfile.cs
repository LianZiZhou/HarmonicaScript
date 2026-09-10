namespace HarmonicaScript.Core.Instrument;

/// <summary>Where a profile's numbers came from, carried so the UI can badge them.</summary>
/// <param name="Confidence">Weakest link across the profile's fields.</param>
/// <param name="VerifiedInGame">Only true once independent in-game reports agree.</param>
/// <param name="Sources">Human-readable citations.</param>
public sealed record ProfileProvenance(
    Provenance Confidence,
    bool VerifiedInGame,
    IReadOnlyList<string> Sources);

/// <summary>
/// A playable instrument, entirely as data. The engine derives the modifier state space and
/// the emission table from this; nothing downstream hard-codes the shape of the instrument,
/// which is what lets a game patch be a JSON edit instead of a release.
/// </summary>
public sealed record InstrumentProfile(
    string Id,
    int SchemaVersion,
    int ProfileVersion,
    string ValidatedAgainstGameBuild,
    LocalizedText DisplayName,
    string GameProfileId,
    int BaseMidiNote,
    IReadOnlyList<DegreeSpec> Degrees,
    IReadOnlyList<ModifierSpec> Modifiers,
    int MaxSimultaneousModifiers,
    bool ModifiersRepitchSustainedNotes,
    ProfileProvenance Provenance)
{
    /// <summary>Reference form used in scores, timelines and traces: <c>id@version</c>.</summary>
    public string Ref => $"{Id}@{ProfileVersion}";
}
