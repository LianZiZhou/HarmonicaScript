## Layer 1 — `SourceSong` (what the file said)

```csharp
namespace HarmonicaScript.Core.Source;

public readonly record struct SourceNote(
    int  Index,            // stable anchor for the edit journal, FOREVER
    int  TrackIndex, byte Channel,
    byte Pitch, byte Velocity,
    long OnsetTicks, long DurationTicks,
    long OnsetUs,   long DurationUs);   // exact µs from MetricTimeSpan.TotalMicroseconds

public sealed record SourceTrack(int Index, string? Name, byte ChannelMask,
    int NoteCount, long DurationUs, byte MinPitch, byte MaxPitch,
    double MeanPitch, double MeanPolyphony, bool IsPercussion, double MelodyScore);

public sealed record SourceSong(
    string FileName, string Sha256, string LoaderVersion, string SanitizeSettingsHash,
    short TicksPerQuarterNote,
    IReadOnlyList<TempoChange> TempoMap, IReadOnlyList<TimeSignatureChange> TimeSignatures,
    IReadOnlyList<SourceTrack> Tracks, IReadOnlyList<SourceNote> Notes);   // sorted (OnsetUs, Index)
```

`LoaderVersion` + `SanitizeSettingsHash` are stamped into `.hsproj`. **The maintainability judge's `SourceIndex` flaw:** `Index` is an ordinal produced by a sanitising parse, so changing `NoteMinLength` in v0.9 would silently reassign every edit in a v0.7 project. Fix: on open, if either stamp differs, the app re-anchors edits by content fingerprint `(OnsetTicks, Pitch, Channel, occurrenceOrdinal)` and reports how many re-anchored and how many orphaned. Never silent.

Reader: DryWetMidi `ReadingSettings` = `NotEnoughBytesPolicy.Ignore`, `InvalidChunkSizePolicy.Ignore`, `NoHeaderChunkPolicy.Ignore`, `InvalidChannelEventParameterValuePolicy.SnapToLimits`, `InvalidMetaEventParameterValuePolicy.SnapToLimits` (this is what survives `KeySignature scale=255`), `MissedEndOfTrackPolicy.Ignore`, `SilentNoteOnPolicy.NoteOff`, `TextEncoding = GB18030` (register `CodePagesEncodingProvider` first), `NoteStartDetectionPolicy.LastNoteOn`. Then `Sanitize(NoteMinLength≈10ms, OrphanedNoteOnEventsPolicy.Remove, RemoveDuplicatedNotes)`.

## Layer 2 — Instrument model (generic; the 6-state space is *derived*, never typed)

```csharp
namespace HarmonicaScript.Core.Instrument;

public enum ModifierBehavior : byte { Hold = 0, Toggle = 1 }
public enum Provenance : byte { Measured, ThirdPartyTool, Inferred, Invented }

public sealed record ModifierSpec(
    string Id, LocalizedText DisplayName,
    int    SemitoneDelta,
    Binding Binding,                 // keyboard HID usage OR MouseButton
    ModifierBehavior Behavior,
    int    ExclusionGroup,           // 0 = none; equal nonzero = mutually exclusive
    int    PressLeadMs, int ReleaseLeadMs,
    int    DutyWeightMilli);         // per-second ergonomic/safety cost. LEFT mouse is weighted 3x.

public sealed record InstrumentProfile(
    string Id, int SchemaVersion, int ProfileVersion, string ValidatedAgainstGameBuild,
    LocalizedText DisplayName, string GameProfileId,
    int BaseMidiNote,                             // P0; cosmetic in a relative model
    IReadOnlyList<DegreeSpec> Degrees,            // { Index, Semitone, Jianpu, Binding }
    IReadOnlyList<ModifierSpec> Modifiers,
    int  MaxSimultaneousModifiers,
    bool ModifiersRepitchSustainedNotes,          // true (conservative)
    ProfileProvenance Provenance);

/// A legal modifier subset. Derived from Modifiers + ExclusionGroup + MaxSimultaneous at load.
public readonly record struct ModState(byte Index, ushort Mask);

public sealed class EmissionTable   // built once per profile; NEVER used by InstrumentSimulator
{
    public int StateCount { get; }  public int LoOffset { get; }  public int HiOffset { get; }
    public sbyte DegreeOf(int stateIdx, int offset);   // -1 if unrenderable
    public ushort StateMask(int offset);               // bitset of states that can render it
    public bool  IsPlayable(int offset);
}
```

**Generic validator (in `Profiles`) asserts only derived invariants:** within-state injectivity (degrees pairwise distinct ⇒ a state determines the key), coverage has no interior gaps, `Σ popcount == StateCount × DegreeCount`, every binding resolves in `KeyTable`, every exclusion group has ≥2 members, `MaxSimultaneousModifiers ≥ 1`.
**Instrument-specific numbers live in a pinned fixture test on `df.harmonica.v1.json`:** 6 states; span `[-12,+25]` = 38 offsets, none uncovered; 48 renderings; multiplicity `{1:30, 2:6, 3:2}`; 2-way exactly `{-7,+1,+5,+13,+17,+24}`; 3-way exactly `{0,+12}`. *(I verified all of these by hand — they are correct. Note the corollary that kills "seam alternation": alternatives always differ in state, so an alternative spelling always costs a modifier transition.)*

### `data/profiles/df.harmonica.v1.json`

```json
{ "schemaVersion": 1, "id": "df.harmonica.v1", "profileVersion": 1,
  "gameProfileId": "df.v1", "validatedAgainstGameBuild": "unverified",
  "displayName": { "zh-Hans": "三角洲行动 · 守夜人的口风琴", "en": "Delta Force · Nightwatchman's Melodica" },
  "baseMidiNote": 60,
  "degrees": [
    { "index":1, "semitone":0,  "jianpu":"1",  "bind":{"device":"keyboard","hid":29} },
    { "index":2, "semitone":2,  "jianpu":"2",  "bind":{"device":"keyboard","hid":27} },
    { "index":3, "semitone":4,  "jianpu":"3",  "bind":{"device":"keyboard","hid":6}  },
    { "index":4, "semitone":5,  "jianpu":"4",  "bind":{"device":"keyboard","hid":25} },
    { "index":5, "semitone":7,  "jianpu":"5",  "bind":{"device":"keyboard","hid":5}  },
    { "index":6, "semitone":9,  "jianpu":"6",  "bind":{"device":"keyboard","hid":17} },
    { "index":7, "semitone":11, "jianpu":"7",  "bind":{"device":"keyboard","hid":16} },
    { "index":8, "semitone":12, "jianpu":"1^", "bind":{"device":"keyboard","hid":54} }
  ],
  "modifiers": [
    { "id":"octaveDown", "displayName":{"zh-Hans":"降调"}, "semitoneDelta":-12,
      "bind":{"device":"mouse","button":"left"},   "behavior":"hold",
      "exclusionGroup":1, "pressLeadMs":12, "releaseLeadMs":16, "dutyWeightMilli":120 },
    { "id":"semitone",   "displayName":{"zh-Hans":"半音"}, "semitoneDelta":1,
      "bind":{"device":"mouse","button":"middle"}, "behavior":"hold",
      "exclusionGroup":0, "pressLeadMs":8,  "releaseLeadMs":8,  "dutyWeightMilli":40 },
    { "id":"octaveUp",   "displayName":{"zh-Hans":"升调"}, "semitoneDelta":12,
      "bind":{"device":"mouse","button":"right"},  "behavior":"hold",
      "exclusionGroup":1, "pressLeadMs":12, "releaseLeadMs":16, "dutyWeightMilli":40 }
  ],
  "maxSimultaneousModifiers": 2,
  "modifiersRepitchSustainedNotes": true,
  "provenance": { "confidence":"inferred", "verifiedInGame":false,
    "sources":["ChickenD233/harmonica-auto-player README + 口琴钢琴测试台.html","user screenshots"] } }
```

HID page 0x07: Z=29, X=27, C=6, V=25, B=5, N=17, M=16, `,`=54. Set-1 (`0x2C..0x33`, none extended) comes from `keytable.json`, never from here. **`octaveDown.dutyWeightMilli` is 3× the others** because LEFT mouse is FIRE in this game — the objective must actively avoid parking a melody in the low band.

### `data/timing/df.reference.json` (the **default**)

Every value is `{ "v": n, "source": "...", "measured": false }`. The shipped defaults are the reference tool's real in-anger constants — `sameKeyRetriggerMs 12`, `octaveReleaseLeadMs 16`, `octavePressLeadMs 12`, `sharpLeadMs 8`, `noteHoldMinMs 20`, `prevKeyCutMs/keyChangeGapMs 20`, `keyHeldMinMs 10`, `chordWindowMs 25`, `globalLeadMs 25`, `breathRest { enabled: true, afterMs: 8000, restMs: 90 }` — plus four **honestly labelled `"source":"invented"`** values: `exclusiveSwapMs 16`, `interModifierStaggerMs 4`, `maxOnsetShiftMs 15`, `maxDriftMs 120`, `maxKeyHoldMs 4000`, `maxModifierHoldMs 6000`. `minSurvivingDurationMs` is **derived** as `noteHoldMinMs + keyChangeGapMs` (= 40 here, 65 under conservative — non-vacuous). `df.conservative` (40/25/30/…) and `df.humanised` (seeded σ=8 ms) ship alongside. A cross-constant relation check runs at load: `prevKeyCutMs ≥ exclusiveSwapMs ≥ octavePressLeadMs ≥ sharpLeadMs`; a user edit that breaks it is refused with the exact reason.

## Layer 3 — `HarmonicaScore`

```csharp
public readonly record struct Fingering(byte DegreeIndex, ModState State);

[Flags] public enum NoteAlteration : ushort {
    None=0, OctaveFolded=1<<0, ChordReduced=1<<1, ChordSibling=1<<2, Dropped=1<<3,
    MergedWithPrev=1<<4, DurationClamped=1<<5, OnsetShifted=1<<6, Respelled=1<<7,
    SmoothedOut=1<<8, HoldSplit=1<<9, BreathRest=1<<10, UserEdited=1<<11 }

public sealed record ScoreNote {
    public required int  Id { get; init; }
    public required int  SourceIndex { get; init; }        // -1 only for user-inserted
    public required long OnsetTicks { get; init; }         public required long DurationTicks { get; init; }
    public required long OnsetUs { get; init; }            public required long DurationUs { get; init; }
    public required long ScheduledDownUs { get; init; }    public required long ScheduledUpUs { get; init; }
    public Fingering? Fingering { get; init; }             // null iff Dropped/ChordSibling
    public required byte SourceMidiNote { get; init; }     // NEVER overwritten
    public required byte EffectiveMidiNote { get; init; }
    public NoteAlteration Alteration { get; init; }
    public bool Muted { get; init; }
    public IReadOnlyList<int>? ChordSiblingSourceIndices { get; init; }
    public bool IsAudible => !Muted &&
        (Alteration & (NoteAlteration.Dropped | NoteAlteration.ChordSibling)) == 0; }

public sealed record HarmonicaScore(
    int FormatVersion, ProfileRef Instrument, ProfileRef Timing, string SettingsSha256,
    ScoreMetadata Meta, ScoreTiming Timing2, ConversionSettings Settings,
    IReadOnlyList<ScoreNote> Notes, ConversionReport Report);
```

`Validate()` (Debug + every test): sorted by `(OnsetUs, SourceIndex)`; audible projection strictly monophonic with `ScheduledUpUs[i] + reqSil(state[i]→state[i+1]) ≤ ScheduledDownUs[i+1]` — **`reqSil` is the same table the DP used, including `setupMs`**, closing the judge's hole where a timeline could pass `Validate` while arming a modifier inside the previous note's sustain; every audible `Fingering` reproduces its `EffectiveMidiNote`; dropped/sibling notes **retained**.

## Layer 4 — `InputTimeline`, the one contract

```csharp
public enum InputPhase  : byte { NoteUp=0, ModifierUp=1, ModifierDown=2, NoteDown=3 }
public enum InputDevice : byte { Keyboard=0, Mouse=1 }
public enum MouseButton : byte { Left=1, Right=2, Middle=3, X1=4, X2=5 }

public readonly record struct InputEvent(
    int TimeMs,          // ABSOLUTE from t=0. int => 24 days. The ONLY unit here.
    InputPhase Phase, InputDevice Device,
    ushort Code,         // HID usage (page 0x07) when Keyboard; MouseButton when Mouse
    bool IsDown,
    int NoteId)          // provenance; -1 for modifier events. NOT part of ordering.
    : IComparable<InputEvent>;   // (TimeMs, Phase, Device, Code)

public sealed record InputTimeline(
    int FormatVersion, ProfileRef Instrument, ProfileRef Timing,
    string ScoreSha256, string SettingsSha256,
    IReadOnlyList<InputEvent> Events, int TotalDurationMs,
    IReadOnlyList<TimelineDiagnostic> Diagnostics);
```

**No `BatchId`** (derived: equal `TimeMs` ⇒ one `SendInput` call — a stored field is a second source of truth). **No `Label`** (`NoteId` indexes the score). **No `Value`** (three buttons, no wheel, no move — ever). A consistency invariant `Phase ∈ {NoteDown, ModifierDown} ⟺ IsDown` is asserted by the validator.

## `.hsproj` (ZIP)

```
manifest.json          formatVersion, appVersion, createdUtc, loaderVersion, sanitizeSettingsHash
source/original.mid    byte-identical
source/source.json     SourceSong + sha256
settings.json          ConversionSettings + objective weights snapshot
edits.json             ordered List<ScoreEdit>, anchored to SourceIndex + content fingerprint
profiles/instrument.json  profiles/timing.json  profiles/game.json   ← full snapshots
cache/score.json  cache/report.json  cache/timeline.json             ← regenerable, ignored on mismatch
```

Truth = `(original.mid + settings + edits + profile snapshots)`. On open, if the installed profile's `profileVersion` differs from the snapshot, the app shows a **field-level diff** and offers 「使用项目内配置打开」/「迁移到新配置」. Old projects never silently change pitch. Migrations are hand-written `IProfileMigration` classes, not a DSL.