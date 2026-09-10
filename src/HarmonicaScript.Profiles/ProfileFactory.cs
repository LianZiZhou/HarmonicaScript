using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Profiles.Json;

namespace HarmonicaScript.Profiles;

/// <summary>Converts wire DTOs into the Core model, failing loudly on anything nonsensical.</summary>
public static class ProfileFactory
{
    public static InstrumentProfile ToInstrument(InstrumentProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var degrees = dto.Degrees
            .Select(d => new DegreeSpec(d.Index, d.Semitone, d.Jianpu, ToBinding(d.Bind, $"degree {d.Index}")))
            .ToList();

        var modifiers = dto.Modifiers
            .Select(m => new ModifierSpec(
                m.Id,
                ToText(m.DisplayName, m.Id),
                m.SemitoneDelta,
                ToBinding(m.Bind, $"modifier '{m.Id}'"),
                ToBehavior(m.Behavior, m.Id),
                m.ExclusionGroup,
                m.PressLeadMs,
                m.ReleaseLeadMs,
                m.DutyWeightMilli))
            .ToList();

        var provenance = new ProfileProvenance(
            ToProvenance(dto.Provenance?.Confidence),
            dto.Provenance?.VerifiedInGame ?? false,
            dto.Provenance?.Sources ?? []);

        return new InstrumentProfile(
            dto.Id,
            dto.SchemaVersion,
            dto.ProfileVersion,
            dto.ValidatedAgainstGameBuild,
            ToText(dto.DisplayName, dto.Id),
            dto.GameProfileId,
            dto.BaseMidiNote,
            degrees,
            modifiers,
            dto.MaxSimultaneousModifiers,
            dto.ModifiersRepitchSustainedNotes,
            provenance);
    }

    public static TimingProfile ToTiming(TimingProfileDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new TimingProfile(
            dto.Id,
            dto.SchemaVersion,
            ToText(dto.DisplayName, dto.Id),
            V(dto.SameKeyRetriggerMs, nameof(dto.SameKeyRetriggerMs), dto.Id),
            V(dto.KeyChangeGapMs, nameof(dto.KeyChangeGapMs), dto.Id),
            V(dto.NoteHoldMinMs, nameof(dto.NoteHoldMinMs), dto.Id),
            V(dto.KeyHeldMinMs, nameof(dto.KeyHeldMinMs), dto.Id),
            V(dto.GlobalLeadMs, nameof(dto.GlobalLeadMs), dto.Id),
            V(dto.ExclusiveSwapMs, nameof(dto.ExclusiveSwapMs), dto.Id),
            V(dto.InterModifierStaggerMs, nameof(dto.InterModifierStaggerMs), dto.Id),
            V(dto.MaxOnsetShiftMs, nameof(dto.MaxOnsetShiftMs), dto.Id),
            V(dto.MaxDriftMs, nameof(dto.MaxDriftMs), dto.Id),
            V(dto.MaxKeyHoldMs, nameof(dto.MaxKeyHoldMs), dto.Id),
            V(dto.MaxModifierHoldMs, nameof(dto.MaxModifierHoldMs), dto.Id),
            V(dto.MinEventSeparationMs, nameof(dto.MinEventSeparationMs), dto.Id),
            new BreathRestSpec(
                dto.BreathRest?.Enabled ?? false,
                dto.BreathRest?.AfterMs ?? 8000,
                dto.BreathRest?.RestMs ?? 90,
                dto.BreathRest?.PhraseGapMs ?? 180));
    }

    private static TimingValue V(TimingValueDto? dto, string field, string profileId) =>
        dto is null
            ? throw new ProfileValidationException(profileId, [new ProfileProblem(field, "is required.")])
            : new TimingValue(dto.V, dto.Source, dto.Measured);

    private static LocalizedText ToText(LocalizedTextDto? dto, string fallback) =>
        dto is null
            ? LocalizedText.Same(fallback)
            : new LocalizedText(dto.En ?? dto.ZhHans ?? fallback, dto.ZhHans ?? dto.En ?? fallback);

    private static Binding ToBinding(BindingDto? dto, string what)
    {
        if (dto is null)
        {
            throw new InvalidOperationException($"{what} has no binding.");
        }

        if (dto.Device.Equals("mouse", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<MouseButton>(dto.Button, ignoreCase: true, out var button) || button == MouseButton.None)
            {
                throw new InvalidOperationException(
                    $"{what} has mouse binding '{dto.Button}'; expected one of Left, Right, Middle, X1, X2.");
            }

            return Binding.Mouse(button);
        }

        if (dto.Hid is not { } hid || hid == 0)
        {
            throw new InvalidOperationException($"{what} has no 'hid' usage id.");
        }

        return Binding.Key(hid);
    }

    private static ModifierBehavior ToBehavior(string value, string id) =>
        value.ToLowerInvariant() switch
        {
            "hold" => ModifierBehavior.Hold,
            "toggle" => ModifierBehavior.Toggle,
            _ => throw new InvalidOperationException($"modifier '{id}' has behavior '{value}'; expected 'hold' or 'toggle'."),
        };

    private static Provenance ToProvenance(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "measured" => Provenance.Measured,
            "thirdpartytool" or "third-party-tool" => Provenance.ThirdPartyTool,
            "invented" => Provenance.Invented,
            _ => Provenance.Inferred,
        };
}
