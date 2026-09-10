namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// A display string in the two shipped cultures. The core never localises diagnostics - it
/// returns <c>DiagnosticCode</c> plus numeric arguments and the GUI maps them through resx -
/// but instrument and modifier *names* are profile data, so they travel with the profile.
/// </summary>
public readonly record struct LocalizedText(string En, string ZhHans)
{
    public static LocalizedText Same(string both) => new(both, both);

    /// <summary>Picks a string for a culture name such as "zh-CN", "zh-Hans" or "en-US".</summary>
    public string For(string cultureName) =>
        cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ZhHans)
            ? ZhHans
            : (string.IsNullOrEmpty(En) ? ZhHans : En);

    public override string ToString() => string.IsNullOrEmpty(En) ? ZhHans : En;
}
