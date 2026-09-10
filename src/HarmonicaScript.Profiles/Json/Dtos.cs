using System.Text.Json.Serialization;

namespace HarmonicaScript.Profiles.Json;

// Wire shapes only. They mirror the JSON exactly and are converted to the Core model by
// ProfileFactory, so a schema change never leaks into the engine's types.

public sealed class BindingDto
{
    [JsonPropertyName("device")] public string Device { get; set; } = "keyboard";
    [JsonPropertyName("hid")] public ushort? Hid { get; set; }
    [JsonPropertyName("button")] public string? Button { get; set; }
}

public sealed class LocalizedTextDto
{
    [JsonPropertyName("en")] public string? En { get; set; }
    [JsonPropertyName("zh-Hans")] public string? ZhHans { get; set; }
}

public sealed class DegreeDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("semitone")] public int Semitone { get; set; }
    [JsonPropertyName("jianpu")] public string Jianpu { get; set; } = string.Empty;
    [JsonPropertyName("bind")] public BindingDto? Bind { get; set; }
}

public sealed class ModifierDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public LocalizedTextDto? DisplayName { get; set; }
    [JsonPropertyName("semitoneDelta")] public int SemitoneDelta { get; set; }
    [JsonPropertyName("bind")] public BindingDto? Bind { get; set; }
    [JsonPropertyName("behavior")] public string Behavior { get; set; } = "hold";
    [JsonPropertyName("exclusionGroup")] public int ExclusionGroup { get; set; }
    [JsonPropertyName("pressLeadMs")] public int PressLeadMs { get; set; }
    [JsonPropertyName("releaseLeadMs")] public int ReleaseLeadMs { get; set; }
    [JsonPropertyName("dutyWeightMilli")] public int DutyWeightMilli { get; set; }
}

public sealed class ProvenanceDto
{
    [JsonPropertyName("confidence")] public string Confidence { get; set; } = "inferred";
    [JsonPropertyName("verifiedInGame")] public bool VerifiedInGame { get; set; }
    [JsonPropertyName("sources")] public List<string> Sources { get; set; } = [];
}

public sealed class InstrumentProfileDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("profileVersion")] public int ProfileVersion { get; set; }
    [JsonPropertyName("gameProfileId")] public string GameProfileId { get; set; } = string.Empty;
    [JsonPropertyName("validatedAgainstGameBuild")] public string ValidatedAgainstGameBuild { get; set; } = "unverified";
    [JsonPropertyName("displayName")] public LocalizedTextDto? DisplayName { get; set; }
    [JsonPropertyName("baseMidiNote")] public int BaseMidiNote { get; set; } = 60;
    [JsonPropertyName("degrees")] public List<DegreeDto> Degrees { get; set; } = [];
    [JsonPropertyName("modifiers")] public List<ModifierDto> Modifiers { get; set; } = [];
    [JsonPropertyName("maxSimultaneousModifiers")] public int MaxSimultaneousModifiers { get; set; } = 1;
    [JsonPropertyName("modifiersRepitchSustainedNotes")] public bool ModifiersRepitchSustainedNotes { get; set; } = true;
    [JsonPropertyName("provenance")] public ProvenanceDto? Provenance { get; set; }
}

public sealed class TimingValueDto
{
    [JsonPropertyName("v")] public int V { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "invented";
    [JsonPropertyName("measured")] public bool Measured { get; set; }
}

public sealed class BreathRestDto
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("afterMs")] public int AfterMs { get; set; } = 8000;
    [JsonPropertyName("restMs")] public int RestMs { get; set; } = 90;
    [JsonPropertyName("phraseGapMs")] public int PhraseGapMs { get; set; } = 180;
}

public sealed class TimingProfileDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public LocalizedTextDto? DisplayName { get; set; }
    [JsonPropertyName("sameKeyRetriggerMs")] public TimingValueDto? SameKeyRetriggerMs { get; set; }
    [JsonPropertyName("keyChangeGapMs")] public TimingValueDto? KeyChangeGapMs { get; set; }
    [JsonPropertyName("noteHoldMinMs")] public TimingValueDto? NoteHoldMinMs { get; set; }
    [JsonPropertyName("keyHeldMinMs")] public TimingValueDto? KeyHeldMinMs { get; set; }
    [JsonPropertyName("globalLeadMs")] public TimingValueDto? GlobalLeadMs { get; set; }
    [JsonPropertyName("exclusiveSwapMs")] public TimingValueDto? ExclusiveSwapMs { get; set; }
    [JsonPropertyName("interModifierStaggerMs")] public TimingValueDto? InterModifierStaggerMs { get; set; }
    [JsonPropertyName("maxOnsetShiftMs")] public TimingValueDto? MaxOnsetShiftMs { get; set; }
    [JsonPropertyName("maxDriftMs")] public TimingValueDto? MaxDriftMs { get; set; }
    [JsonPropertyName("maxKeyHoldMs")] public TimingValueDto? MaxKeyHoldMs { get; set; }
    [JsonPropertyName("maxModifierHoldMs")] public TimingValueDto? MaxModifierHoldMs { get; set; }
    [JsonPropertyName("minEventSeparationMs")] public TimingValueDto? MinEventSeparationMs { get; set; }
    [JsonPropertyName("breathRest")] public BreathRestDto? BreathRest { get; set; }
}

public sealed class KeyRowDto
{
    [JsonPropertyName("hid")] public ushort Hid { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("scanCode1")] public int ScanCode1 { get; set; }
    [JsonPropertyName("extended")] public bool Extended { get; set; }
    [JsonPropertyName("windowsVk")] public int WindowsVk { get; set; }
    [JsonPropertyName("ahk")] public string Ahk { get; set; } = string.Empty;
}

public sealed class MouseRowDto
{
    [JsonPropertyName("button")] public string Button { get; set; } = string.Empty;
    [JsonPropertyName("razer3")] public int? Razer3 { get; set; }
    [JsonPropertyName("ahk")] public string Ahk { get; set; } = string.Empty;
    [JsonPropertyName("winDown")] public string WinDown { get; set; } = string.Empty;
    [JsonPropertyName("winUp")] public string WinUp { get; set; } = string.Empty;
}

public sealed class KeyTableDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("keys")] public List<KeyRowDto> Keys { get; set; } = [];
    [JsonPropertyName("mouse")] public List<MouseRowDto> Mouse { get; set; } = [];
}

public sealed class GameProfileDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public LocalizedTextDto? DisplayName { get; set; }
    [JsonPropertyName("processNames")] public List<string> ProcessNames { get; set; } = [];
    [JsonPropertyName("windowTitleHints")] public List<string> WindowTitleHints { get; set; } = [];
    [JsonPropertyName("antiCheat")] public string AntiCheat { get; set; } = string.Empty;
    [JsonPropertyName("prohibitedSoftwareNotice")] public LocalizedTextDto? ProhibitedSoftwareNotice { get; set; }
    [JsonPropertyName("sources")] public List<string> Sources { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(InstrumentProfileDto))]
[JsonSerializable(typeof(TimingProfileDto))]
[JsonSerializable(typeof(KeyTableDto))]
[JsonSerializable(typeof(GameProfileDto))]
public sealed partial class ProfileJsonContext : JsonSerializerContext;
