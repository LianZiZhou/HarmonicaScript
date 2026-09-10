## Root

``

```
HarmonicaScript.slnx
global.json                  { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
Directory.Build.props        net10.0 · Nullable=enable · TreatWarningsAsErrors · Deterministic
                             ContinuousIntegrationBuild · InvariantGlobalization=false
                             LangVersion=latest · EnforceCodeStyleInBuild
Directory.Packages.props     Central Package Management — every pin in one file
.editorconfig  .gitattributes  nuget.config
LICENSE                      MIT
NOTICE.md                    third-party attributions
docs/PROVENANCE.md           clean-room record, maintained from M1 (not written at the end)
docs/CALIBRATION.md          the six in-game experiments
.github/workflows/{ci.yml,windows.yml,release.yml}
.github/ISSUE_TEMPLATE/{no-sound.yml,vendor-sample.yml,profile-correction.yml,calibration-result.yml}
.github/pull_request_template.md    (contains the clean-room checkbox)
```

## src/ — 11 projects (each boundary is a package-isolation or platform boundary; nothing else)

| Project | TFM | Owns | Forbidden (enforced by `Policy.Tests`) |
|---|---|---|---|
| `HarmonicaScript.Core` | net10.0 | `SourceSong` / `HarmonicaScore` / `InputTimeline`; `InstrumentProfile` model; `EmissionTable`; the pass pipeline; `TranspositionSearch`; `ModifierPlanner` (the DP); `ArticulationScheduler`; `TimelineLowering` (hold+toggle); `HoldCapPass`; `TimelineValidator`; `InstrumentSimulator`; `ConversionReport`. **Zero package refs, zero I/O.** | `System.IO`, `System.Net.*`, Avalonia, DryWetMidi, any P/Invoke |
| `HarmonicaScript.Profiles` | net10.0 | JSON schemas + STJ source-gen context, embedded defaults, 4-level override chain with per-field origin, hand-written migration classes, `KeyTable`, `GameProfile` | game-varying *logic* |
| `HarmonicaScript.Midi` | net10.0 | **the only DryWetMidi reference**; tolerant read → `SourceSong`; `.reduced.mid` writer | `Melanchall.DryWetMidi.Multimedia` (banned symbol) |
| `HarmonicaScript.Export` | net10.0 | `IMacroExporter`, `ExporterCapabilities`, the shared lowering chain, generic writers, vendor writers, the three readers that have an independent consumer | re-deriving anything from `HarmonicaScore` |
| `HarmonicaScript.Playback` | net10.0 | `IInputBackend`, `IClock`, `IEnvironmentProbe`, `Scheduler`, `TraceWriter`/`TraceReader` (**one canonical writer**), `ReleaseAllGuard`, `PreflightRules`, `TraceBackend`, `VirtualClock`, `FaultInjectingBackend` | platform APIs |
| `HarmonicaScript.Playback.Windows` | net10.0 + `[SupportedOSPlatform("windows")]` | `ISendInputApi` + CsWin32 impl, `WindowsSendInputBackend`, `WindowsEnvironmentProbe`, `WindowsHotkeys`, `NotepadDeliveryTest` | — |
| `HarmonicaScript.Playback.MacOs` | net10.0 + `[SupportedOSPlatform("macos")]` | `MacCgEventBackend`, TextEdit loopback. Dev/demo only, never advertised | — |
| `HarmonicaScript.Project` | net10.0 | `.hsproj` ZIP, `ScoreEdit` journal, open-time profile-snapshot diff | — |
| `HarmonicaScript.Audition` | net10.0 | `SimulatedNote[]` → MeltySynth → WAV. Offline only | any audio device API |
| `HarmonicaScript.Cli` | net10.0 | `hsc` | — |
| `HarmonicaScript.App` | net10.0 | Avalonia shell, MVVM, i18n | any algorithm; any DryWetMidi type |

`tests/`: `Core.Tests`, `Midi.Tests`, `Export.Tests`, `Playback.Tests`, `Policy.Tests` (5).

`data/` (loose files next to the exe **and** embedded fallbacks): `profiles/df.harmonica.v1.json`, `profiles/loopback.text.v1.json`, `games/df.game.json`, `timing/{df.reference,df.conservative,df.humanised}.json`, `keytable.json`, `exporters/*.capabilities.json`, `diagnostics/rules.json`, `weights/objective.v1.json`.

`testdata/`: `pd/*.mid` (**25–30 public-domain files committed**), `generated/` (procedural fixture generator, code + seeds), `corpus/manifest.json` (hash + provenance for the private extended set, gitignored files), `vendor-samples/{razer3,bloody,msmacro,logitech}/` + `PROVENANCE.md`, `snapshots/**/*.verified.*`.

## Pins (Directory.Packages.props)

`Melanchall.DryWetMidi 8.0.3` **with `ExcludeAssets="build"`** · `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.Diagnostics` **11.3.21** · `CommunityToolkit.Mvvm 8.4.2` · `MeltySynth 2.4.1` · `System.CommandLine 2.0.12` · `Microsoft.Windows.CsWin32 0.3.333` · `System.Text.Encoding.CodePages` (GB18030) · `xunit.v3 4.0.0` · `Verify.XunitV3` (**pin the latest *stable* at first restore — the version list contains 33.0.0-beta.x, so 32.x is probably it; verify, do not trust**) · `CsCheck` · `Mono.Cecil 0.11.6` (Policy.Tests only) · `Microsoft.CodeAnalysis.BannedApiAnalyzers` · `HotAvalonia 3.1.4` (dev-only).

**Not referenced, ever:** `SharpHook` (bundled libuiohook is GPL-3.0/LGPL-3.0 — the one contamination trap in the graph), `Avalonia.Controls.TreeDataGrid` (paid, hard dep on `AvaloniaUI.Licensing`), `Avalonia.Headless.*` (removes the xunit v2/v3 conflict outright), `Velopack`, `NAudio`, `Silk.NET.*`, `System.Net.*`.

## Build posture

`PublishAot=false`, `PublishTrimmed=false`, `EnableCompressionInSingleFile=false` on every shipped artefact — NativeAOT cannot cross-compile macOS→Windows and compression is a top AV false-positive trigger. But AOT-clean **discipline** from commit one in Core/Profiles/Midi/Export/Project: `<IsAotCompatible>true</IsAotCompatible>`, source-generated `JsonSerializerContext`, `JsonSerializerIsReflectionEnabledByDefault=false`, flat records with enum discriminators, never `[JsonDerivedType]`. Primary artefact is a plain uncompressed self-contained zip.