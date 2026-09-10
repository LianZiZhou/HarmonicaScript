## The IR is the only input

Both branches consume `InputTimeline` verbatim. Exporters never re-derive anything from `HarmonicaScore`; they receive an `ExportContext` carrying the metadata they provably need.

```csharp
public sealed record ExportContext(
    InputTimeline Timeline, InstrumentProfile Instrument, GameProfile Game,
    ScoreMetadata Meta,                       // title, source file, tempo map, transpose, bpm
    IReadOnlyDictionary<int,string> NoteLabels,   // NoteId -> "5̇" / "#3" — for comment-capable formats
    ExportOptions Options);

public sealed record ExporterCapabilities(
    int? MaxEvents, int? MaxDelayMs, int MinDelayMs, int DelayGranularityMs,
    bool SupportsSeparateDownUp, bool SupportsChords, bool SupportsMouse,
    IReadOnlyList<MouseButton> VerifiedMouseButtons,      // ← the load-bearing field
    bool SupportsComments, bool SupportsMultipleMacrosPerFile,
    Encoding TextEncoding, string NewLine,
    IReadOnlyDictionary<string,int?> Quirks);

public interface IMacroExporter {
    string Id { get; } LocalizedText DisplayName { get; } string[] FileExtensions { get; }
    ExporterCapabilities Capabilities { get; }
    ExportResult Write(ExportContext ctx, Stream output); }

public sealed record ExportResult(bool Ok, IReadOnlyList<ExportWarning> Warnings,
    IReadOnlyList<LocalizedText> ExtraInstructions, IReadOnlyList<ExtraFile> ExtraFiles);

public interface IExporterRegistry { IReadOnlyList<IMacroExporter> All { get; }
                                     IMacroExporter? ById(string id); }
```

`ExtraInstructions` is surfaced by the UI generically (Bloody needs a `GunLib` folder path and a `.bmc` copy; Redragon needs a `Config.ini` `[MACRO_LIST]` patch, **which we generate as text and never apply ourselves** — writing into another vendor's config is how you corrupt a user's setup). The registry means the CLI and the GUI enumerate exporters without a hard-coded switch, and a new vendor is a leaf change.

## Lowering happens once, in the core

`Deltaise(residual-carrying) → ClampDelays(MaxDelayMs, splitting long waits) → Fold(if !SupportsSeparateDownUp) → MergeWaits → TrimLastWait → CheckBudget(MaxEvents) → SplitIntoParts`. Deltas are computed from **absolute** `TimeMs`, the residual is carried and reabsorbed at the next rest > 200 ms, and `CumulativeDriftMs` is reported. Writers become 80–150 lines of pure serialisation.

## Tier 1 — generic, ships first (v0.1.0)

| Id | Notes |
|---|---|
| `ahk-v2` | **The most important export in the project.** Absolute-`T0` `QueryPerformanceCounter` scheduling, `timeBeginPeriod(1)` paired with `OnExit timeEndPeriod(1)`, `Send "{Blind}{sc02C down}"` — `{Blind}` is mandatory because we hold a mouse modifier across many notes and un-blinded `Send` will helpfully "restore" it. `{RButton down}` etc. is the **only** verified mouse syntax anywhere in Branch B, and this instrument needs all three buttons. `OnExit` releases every key and button we ever pressed |
| `hscore-json` / `hscore-csv` | our own; round-trip reader for both |
| `reduced-mid` | Type-0, transposed, monophonic. This alone makes HarmonicaScript useful to the **entire existing FF14-Bard / 原神-lyre tool ecosystem** without anyone adopting our playback engine — the highest-leverage interop in the plan |
| `keytape` | the community-recognised character tape with a bracketed modifier prefix, ~40 lines |
| `jianpu-hsq` | export only (`1 2 3 #4 5̇` + bar lines). **No importer in v1** |

`ahk-v2` shipping before `HarmonicaScript.Playback.Windows` is a deliberate ordering decision: it reaches the game through a mature injection engine thousands of people have validated, so it is Branch A with the developer's unverifiable surface removed, and it becomes possible the moment the timeline emitter works.

## Tier 2 — verified vendors, strictly sample-gated (v0.8+)

`razer3-xml` — verified against a real Synapse 3 **keyboard** macro: `<Macro><Name/><Guid/><MacroEvents><MacroEvent><Type>1</Type><Delay>N</Delay><KeyEvent><Makecode>…</Makecode><IsExtended/><State/></KeyEvent></MacroEvent>…</MacroEvents><IsFolder>false</IsFolder><FolderGuid>0000…</FolderGuid></Macro>`, UTF-8 **with BOM**, name ≤ 20 chars, fresh lowercase GUID, `Makecode` = PS/2 Set-1 (the sample's 91/30/42/15/57 decode as LWin(ext)/A/LShift/Tab/Space). `bloody-amc` — UTF-8 with BOM, CRLF, `KeyDown <hidUsage> 1` / `Delay <n> ms` / `KeyUp <hidUsage> 1`, HID is a direct cast; the empty `<KeyUp><Syntax>` block in every real sample is exactly where release-all belongs. `logitech-lua` — via the script editor's Script → Import, numeric Set-1 scan codes not keyname strings, `OnEvent("PROFILE_DEACTIVATED")` → release all.

**Every one of the three is blocked on one sample containing right/middle mouse events.** The verified Razer file proves only keyboard events and the left button; Bloody's `RightDown`/`MiddleDown` are inferred by analogy from a single observed `LeftDown`. So `keytable.json` carries `"razer3Button": null` for right/middle, and the exporter **refuses with a named missing capability rather than guessing**. That turns "get one more sample" from a discovered surprise into a scheduled task.

Two inferences that would produce files which import successfully and then play *backwards* are stored as typed per-exporter `Quirks` records deserialised from the capabilities JSON: Razer's `<State>` 0=down/1=up (inferred by symmetry, never observed) and MSMACRO's `Map_N` 132=down/4=up. A field report inverts one number, no rebuild.

## Never, and why

`redragon-msmacro` is blocked on one `DelayType=1` sample (with `DelayType=0` it can only emit evenly-spaced notes, which is musically dead) — but it is the **#1 sample ask**, because the OEM codebase is rebadged across several Chinese budget brands and cracking it once may unlock several. `corsair-cueprofile` is deferred with a stated re-entry condition (a real `.cueprofile` from the target iCUE version **and** a tester who has backed up): cereal's `polymorphic_id`/`ptr_wrapper` bookkeeping plus drifting `cereal_class_version` numbers mean a wrong file can corrupt a user's entire profile list — the one exporter whose failure mode harms the user rather than merely failing. **VIA / Vial / QMK / ZMK are never song exporters**: ~14–18 bytes per note against a ~500–900 byte macro buffer is 30–50 notes, and Vial silently truncates. **SteelSeries has no macro import at all** (GameSense is output-only). ASUS, MSI, Cooler Master, ROCCAT, Wooting, Glorious, Pulsar and the entire Chinese cluster (AULA / VGN / AJAZZ / Dareu / Rapoo / Attack Shark / KZZI / 机械师 / MIIIW / ThundeRobot) have **zero** verifiable formats, and two circulating "specs" (AULA `.ahap`, VGN `.vgncfg` with a 500-action limit) traced to AI-generated SEO farms. No speculative writers, ever.

## Validation without the vendor software — three real techniques, one dropped

**Dropped: "a reader for every writer, and round-trip equality substitutes for the vendor software."** Two judges independently showed this is a tautology on exactly the unknowns that matter: reader and writer share one mental model, so a `<State>` polarity inversion, a delay-before-vs-after-event error, or an unescaped brace round-trips perfectly and ships — and the team's confidence in an unverifiable surface goes *up* for no evidential reason. Readers are written only where the format is self-describing **and** the reader has an independent consumer: `hscore-json`, `hscore-csv`, `reduced-mid`, and `ahk-v2` (whose reader doubles as an interpreter).

What replaces it:

1. **Decode fixtures from the real samples.** `testdata/vendor-samples/` holds the corpus's verified artefacts with a `PROVENANCE.md` recording source URL and licence, and the tests assert we parse each one to a **hand-written expected timeline** — the Synapse 3 keyboard macro's `91/30/42/15/57` → `LWin(ext)/A/LShift/Tab/Space` is a genuine oracle, not a mirror. Where redistribution is doubtful we commit a re-typed minimal equivalent plus the original's SHA-256.
2. **Golden bytes, targeted not exhaustive.** Golden the **lowered event list** (post-`Deltaise`/`Clamp`/`Fold`/`MergeWaits`) for every fixture — that is where every real regression lives, it is small and reviewable — and golden actual exporter **bytes** for six deliberately chosen fixtures (short; chromatic; long modifier run; at the event budget; all-three-mouse-buttons; pathological). A serialiser bug is format-wide, not fixture-specific, so this still catches "I fixed Logitech and silently broke Razer" without committing 400+ files and tens of megabytes of generated XML that nobody will ever read.
3. **Executing the AHK on CI.** The `windows-2025` job runs the generated `.ahk` with `Send` stubbed to a log file and diffs the log against the `InputTimeline`. This is the difference between "reviewed by eye" and *verified* for the one exporter the whole generic tier depends on — and it catches a malformed `DllCall`, an unpaired `OnExit`, or a bad `{Blind}` prefix at parse time.

Also asserted: shared capability-lowering behaviour across every exporter; schema shape (UTF-8-with-BOM + CRLF for `.amc`, `<Guid>` format, Razer's 20-char cap, HID literals); and `CumulativeDriftMs < 1 ms` over a 5-minute piece.

## The contribute-a-sample pipeline (zero network)

1. **Where to look**, per vendor, from the capabilities JSON: `%APPDATA%`, `%LOCALAPPDATA%`, `C:\ProgramData`, the install dir, the vendor's own export dialog path.
2. **What to record** — the part that makes a sample decodable on sight:
   > 录制一个宏：按下 Z，等 137 毫秒，松开 Z，等 251 毫秒，按下 X，等 61 毫秒，松开 X。
   > 再录一个：按住**鼠标右键** 173 毫秒后松开，按住**鼠标中键** 89 毫秒后松开。
   > 录制时请打开「记录延时 / record delays」。

   Distinct prime-ish delays and distinct keys make every field individually identifiable; the mouse recipe unblocks Razer and Bloody; "record delays" unblocks `.MSMACRO` `DelayType=1`.
3. **Package, do not upload.** `hsc contrib package --vendor <id>` writes `contrib-<vendor>-<ts>.zip` (manifest with vendor + software version + OS + per-file SHA-256, the bytes verbatim, a 4 KiB hexdump for binary formats, `recipe.md`, and a `scrub-report.txt` listing every redaction) and opens the folder. The user attaches it themselves.
4. **Decode.** `hsc contrib inspect <zip>` runs the sample through every existing reader, reports which parses, and for unknown formats prints a field-alignment view against the known delays (137/251/61) so the delay encoding is usually visible on sight.
5. **Ship.** A new exporter is C# with a golden file and a decode fixture, released normally. **There is no template DSL and no drop-in plugin folder** — two judges independently found it granted an exemption from all three validation techniques to exactly the exporters nobody has ever seen, invented a mini-language nobody asked for, added a load-arbitrary-config-from-a-writable-directory vector to a tool already expecting AV heuristics, and solved the wrong bottleneck (new vendors are blocked on *sample acquisition*, not on authoring speed; a line-oriented writer is 80–150 lines).