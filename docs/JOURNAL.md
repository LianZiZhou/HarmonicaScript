# Engineering journal

What each milestone actually found, including where reality differed from the plan.
A project whose thesis is "record provenance honestly" should keep this.

---

## M0 — toolchain, skeleton, two build-path spikes

**Status: complete except the two items that need a GitHub remote.**

### Both spikes passed

| Spike | Result |
|---|---|
| (a) Avalonia 11.3.21 renders on net10.0 / macOS arm64 | **PASS** — `SMOKE OK: window rendered 900x600 on macOS 27.0.0 / Arm64 / runtime 10.0.12 / Avalonia 11.3.21.0`. Turned into a permanent repeatable check (`--smoke`) rather than a one-off, and CI runs it on Windows too. |
| (b) win-x64 self-contained exe cross-published **from macOS** | **PASS locally** — `hsc.exe` is a genuine `PE32+ executable (console) x86-64`, the GUI a `PE32+ executable (GUI) x86-64` with win-x64 Skia/HarfBuzz/ANGLE natives correctly resolved. **Zero DryWetMidi native blobs** in the output, confirming `ExcludeAssets="build"` does what decision D2 claims. The "launches on windows-2025" half is wired in `.github/workflows/windows.yml` and runs on first push. |

`dotnet build` and `dotnet test` are green on macOS in both Debug and Release, 0 warnings
with `TreatWarningsAsErrors=true`, all five suites discovered and passing.

### Deviations from the plan, and why

1. **.NET 10 SDK 10.0.401 is installed user-local at `~/.dotnet`, not system-wide.**
   `/usr/local/share/dotnet` is root-owned and installing there needs a password. The
   user-local install needs no admin rights and is fully under the project's control, but
   it is **not on PATH by default** — the system `dotnet` still resolves to 9.0.305, which
   `global.json` will reject. Add to your shell profile:
   ```bash
   export PATH="$HOME/.dotnet:$PATH"
   export DOTNET_ROOT="$HOME/.dotnet"
   ```
   Rider/VS Code need the same, or point them at `~/.dotnet` explicitly.

2. **`System.Text.Encoding.CodePages` removed from the pin set.** On .NET 10 it is part of
   the shared framework and an explicit `PackageReference` is a hard `NU1510` error.
   Verified by experiment that GB18030 still works without it:
   `codepage=54936`, `小星星` round-trips byte-exact. One fewer dependency than planned.

3. **`Avalonia.Controls.DataGrid` is pinned at 11.3.13, not 11.3.21.** Its 11.3.x line stops
   at 11.3.13 while core Avalonia is at 11.3.21. It is still MIT and only requires
   `Avalonia >= 11.3.13`, so the combination composes correctly.

4. **The `dotnet test` MTP opt-in lives in `global.json`, not `dotnet.config`.** On the .NET
   10 SDK the legacy VSTest bridge hard-errors for MTP projects. The MSBuild target that
   raises it gates on `_SupportsGlobalJsonTestRunner`, and the key that satisfies it is:
   ```json
   "test": { "runner": "Microsoft.Testing.Platform" }
   ```
   A `dotnet.config` with `[dotnet.test.runner]` does nothing — the string appears nowhere
   in the 10.0.401 SDK tree.

5. **Artefact size.** Self-contained non-trimmed GUI publish is **104 MB on disk / 45 MB
   zipped** (CLI: 35 MB zipped), dominated by the untrimmed BCL — `System.Private.CoreLib`
   15 MB, `libSkiaSharp` 9 MB, `System.Private.Xml` 7.4 MB. The zipped figure matches the
   plan's 40–50 MB estimate; the on-disk figure is roughly double what the plan implied.

### Confirmations worth keeping

- Every package pin resolved against live NuGet. `Verify.XunitV3` latest **stable** is
  indeed **32.0.0** (33.0.0-beta.8 exists but is prerelease) — the plan's instruction to
  verify rather than trust was right, and the guess held.
- `Avalonia.Diagnostics` still tops out at **11.3.21** with no 12.x, which is the whole
  basis of decision D1.
- Runner labels `ubuntu-24.04`, `macos-26` and `windows-2025` all exist.

### Not yet done (needs a git remote)

- CI green on all three runners.
- Spike (b) part 2: the macOS-built exe launching on `windows-2025`.

---

## M1 — instrument profiles, emission table, candidate table

**Status: complete.** `hsc profile candidates` prints every rendering for all 38 offsets and
matches both a reviewed listing and an exhaustive assertion against the closed formula.

Every pinned structural fact was confirmed **by code on the first run**: 6 modifier states with
deltas `[-12,-11,0,1,12,13]`; offsets `-12..+25` with zero interior gaps; 48 renderings;
multiplicity `{1:30, 2:6, 3:2}`; two-way offsets exactly `{-7,1,5,13,17,24}`; three-way exactly
`{0,12}`; and a modifier state uniquely determining the key.

### One real bug found

`TimingProfile.ModifierSwapCeilingNps` was **wrong** — it used the exclusive-swap settle time
alone and ignored that a modifier swap does not excuse the note keys from their own separation
requirement. This is exactly the "optimistic by up to 60%" failure the plan warned about. Fixed by
moving the three ceilings onto `ArticulationModel`, which can see the per-modifier lead times.

The corrected model then surfaced something the plan had not anticipated: **the worst exclusive
swap is a THREE-event transition** (`{降调+半音}` → `{升调}` — release two controls, press a
third), not the obvious two-event one. Under `df.reference` that costs 24 ms rather than 20, so
reporting the two-event figure would have overstated the ceiling by 10%.

### Deviations

- `data/games/df.game.json` renamed to `df.v1.json`: the loader resolves by id, so the filename
  has to match the id like every other profile.
- Chord-collapse width moved from the timing profile to `ConversionSettings`. It is a
  **notational** tolerance, not a physical one — the plan's own pipeline section argues this,
  while its data-model section had it in the timing file. Settings is the correct home, and the
  default is 45 ms rather than the reference tool's 25 ms.

## M2 — MIDI ingest, corpus, fixture generator

**Status: complete.** 44 fixtures, 184 KB total: 15 public-domain melodies, 23 procedural stress
cases (including the six calibration probes), and 6 deliberately malformed files.

The public-domain corpus is **typed out as source**, not downloaded — a deliberate copyright
posture, and it makes the melodies reviewable and diffable rather than opaque binaries of
uncertain origin.

All six malformed files load rather than throwing, including `KeySignature scale=255`, a
truncated final chunk, an orphaned note-on and raw GBK track-name bytes.

**Bug found:** the fixture *writer* dropped Chinese track names to `???` because it used the
default encoding while the reader used GB18030. The raw-GBK malformed fixture passing while the
round-trip one failed is what localised it to the writer.

## M3–M7 — the conversion engine

**Status: complete.** 363 tests across five suites, all green, in both Debug and Release.

### M4's central gate passed on the first run

The modifier-planning DP matches an exhaustive brute-force optimum on **2000 random instances at
exact int64 equality** — not an epsilon. The generator deliberately over-samples the eight
multi-way offsets, because a uniform generator is nearly useless here: 30 of the 38 offsets have
exactly one feasible state, so a uniform instance is mostly forced and the DP has no choice to
get wrong.

Also asserted: self-consistency against an independent rescore, monotonicity in the event weight,
and **soundness of the pessimistic availability bound** — every edge the DP calls feasible is
realisable by truncating the previous note alone. That last one is what buys the single-pass
design with no iteration and no convergence assumption.

### M5's degeneracy suite passed on the first run

All twelve keys resolve to key-distance zero; an all-sharp piece is **not** transposed down a
semitone; and hard infeasibility is never traded against musical loss. On real music the tiering
is visibly doing work: converting Für Elise, candidate `+1` is *mechanically cheaper* (mech 10 vs
15) yet correctly loses to transpose 0 because its key distance is 1.

### Two real bugs in the lowering, both caught by the validator

1. **Press emitted before its own release.** The modifier block used per-event
   `down - lead - stagger*order` arithmetic, which inverts whenever the stagger exceeds the
   difference between one control's release lead and another's press lead. Under `df.conservative`
   (stagger 6, leads 16/12) it produced a double-down followed by an unpaired release. Fixed by
   laying the block out forwards from a computed start rather than backwards per event.
2. **The first fix reintroduced it.** Clamping an individual press back to `down - pressLead`
   pulled it in front of a release again. Fixed properly by *sizing* the block so the last event
   still lands within its lead, with no per-event clamping.

Neither would have been found without the validator being a logical state machine.

### An int-overflow bug caught before it could ship

The lowering originally carried microseconds in `InputEvent.TimeMs` (an `int`), which overflows
silently at about **36 minutes** of music. Introduced a separate `RawEvent` with `long TimeUs`;
`Quantise` is the only bridge between the two.

### A structural finding worth recording

`minSurvivingDurationMs` is derived as `noteHoldMinMs + keyChangeGapMs`, which is *exactly* the
minimum spacing the articulation ladder could ever satisfy. So for monophonic input the
reduction's `dropShort` pass catches impossible density **before** the ladder sees it, and the
ladder's merge/drop rungs fire only when modifier setup pushes the requirement above that floor.
Either way the loss is reported rather than silently emitted.

## M8, M13, M14 — CLI, playback, exporters

**Status: complete.**

Ten export targets ship. Six generic (AutoHotkey v2, JSON, CSV, jianpu, key tape, reduced MIDI)
plus Logitech Lua, which is the **only** vendor exporter that ships ungated because its mouse API
is publicly documented. Razer, Bloody and Redragon are **registered but refuse**, with a named
missing capability and the exact recipe that would unblock them — a guessed mouse encoding would
import cleanly and then press the wrong button, which on an instrument whose three modifiers are
the mouse buttons means holding FIRE in an online shooter.

The whole playback engine is tested on macOS via `VirtualClock` + `TraceBackend`, including a
fault-injection theory over **six abort modes × every 17th event** asserting the trace always
ends in `RELEASEALL` with an empty held set.

The Windows `SendInput` path is asserted **from macOS**: the `INPUT` union layout (40 bytes on
x64), `wVk = 0` under `KEYEVENTF_SCANCODE`, key-up carrying `0x000A` rather than `0x0002`, the
extended flag, every mouse flag constant, that `MOUSEEVENTF_MOVE` never appears, and one
`SendInput` call per batch. This was made possible by a design correction: the platform gate
belongs on the *seam* (`RealSendInputApi`), not on the backend class, because everything else in
it is struct arithmetic.

## M9–M12, M15 — CI, weights, GUI, release

**Status: complete.** Avalonia GUI with seven tabs, runtime-switchable zh-Hans/en, a stacked
penalty bar (never a bare score), the transposition candidate table, timing values badged with
their provenance, a filterable note grid defaulting to 「只看有问题的音」, and a rehearsal view
driven by the *same* `InstrumentSimulator` the round-trip test asserts against — so it renders a
verified invariant rather than being a second implementation that can drift.

`Policy.Tests` enforces with Mono.Cecil, not prose: no `System.Net` in **any** assembly, no
`System.IO` in Core, no DryWetMidi Multimedia in Midi, **no floating-point opcodes in the
optimiser**, no SharpHook, no paid Avalonia component, no conversion algorithm in the GUI, no
`WH_KEYBOARD_LL`, and no `OpenProcess`/`ReadProcessMemory` anywhere. Localisation tests enforce
key parity in both directions, that Chinese strings actually contain Chinese, and that
防封/过检测/undetectable/更安全 appear nowhere in any shipped string.

### What the golden files revealed

The `exclusive-swap-storm` golden is the clearest demonstration of why the DP earns its place.
Offset 24 has two renderings, and the planner chooses `{升调+半音} + degree 7` over
`{升调} + degree 8` — so 半音 is pressed **once at t=4 ms and held for the entire piece**, paying
only for the Left↔Right swap on each note. That is 63 modifier toggles saved that the flat
one-key-per-pitch map every surveyed prior tool uses would have paid.

### Still unverifiable, as planned

The five items in `docs/CALIBRATION.md`. Every one is a JSON value with a provenance badge, and
`hsc profile dump --effective` shows which layer supplied each. Nothing about the running game has
been measured, and the product says so — in the report header, in the GUI's amber badge, and in
the blocking first-run disclaimer.

### A test-infrastructure race, found by running the suite as a whole

Every suite passed individually while the combined `dotnet test` run failed intermittently with
15 errors. Cause: three test assemblies each regenerate the fixture corpus, and `dotnet test` runs
them in **parallel processes**, so they raced on the same files. Fixed with a named cross-process
mutex plus temp-file-and-atomic-move writes, verified over three consecutive full runs.

Worth recording because the failure mode is instructive: a per-suite green board said everything
was fine, and only running the whole thing together exposed it. It would have shown up in CI as
a flaky build that "just needs a re-run".

---

## Final state

| | |
|---|---|
| Projects | 16 (11 source, 5 test) |
| C# | ~12,900 lines across 101 files |
| Tests | **363, all green**, Debug and Release, three consecutive clean runs |
| Fixtures | 44 MIDI files, generated deterministically from source |
| Golden files | 6 |
| Export targets | 10 (7 usable, 3 refusing pending a real vendor sample) |
| Warnings | 0, with `TreatWarningsAsErrors` |
| Artefacts | win-x64 GUI and CLI cross-published from macOS, 0 native blob leaks |

---

## Release workflow

`.github/workflows/release.yml` builds all six OS/architecture targets on every push and
publishes them to a GitHub Release versioned by the commit's short hash.

### Verified rather than assumed

- **All six RIDs publish with their native assets** (Skia, HarfBuzz, and ANGLE on Windows) -
  checked before writing the workflow, because Avalonia's native payload is per-RID and a missing
  one would only surface as a runtime crash on a machine nobody has.
- **The GUI and CLI share one output folder.** Publishing both into the same directory works:
  the runtime assemblies are byte-identical and each app keeps its own `.deps.json`. This costs
  ~1 MB over the GUI alone instead of ~35 MB for a second copy of the runtime.
- **The produced artifact actually runs.** Extracted the osx-arm64 zip and ran it with `PATH` and
  `DOTNET_ROOT` unset: it resolved `data/` from its own folder
  (`layers: embedded -> app:/…/HarmonicaScript-a1b2c3d-osx-arm64/data`), converted a MIDI, and
  exported an AutoHotkey script. The executable bit survives the zip, and `--version` reports the
  stamped `0.9.0+a1b2c3d`.
- **The whole build and publish shell was dry-run locally** against a copy of the tree, not just
  eyeballed.

### Bugs found and fixed while writing it

1. **Stale action versions.** Checked against the registry rather than memory: `checkout` is at
   **v7** (not v5), `setup-dotnet` **v6**, `upload-artifact` **v7**, `download-artifact` **v8**.
   Confirmed every input still in use (`compression-level`, `merge-multiple`, `global-json-file`)
   exists on the new majors, then bumped all three workflows.
2. **Invalid YAML.** The per-platform `RUNNING.txt` files were written with heredocs nested inside
   a `run: |` block scalar. Their content starts at column 0, which terminates the block - the
   file did not parse. Fixed properly by moving those notes to `packaging/RUNNING-*.txt`, where
   they are also reviewable and diffable instead of being trapped in YAML.

### Deliberate choices

- **Tag is `build-<sha7>`, not the bare short hash.** Git treats a tag that looks like an object
  id as ambiguous and warns on every checkout. The short hash is still the version everywhere a
  user sees it: the release title and every filename.
- **Prerelease.** These are per-commit CI builds; full releases would repoint the repository's
  "Latest release" badge on every push, including pushes to throwaway branches.
- **Gated on tests.** Publishing an artefact from a commit whose tests fail would put a broken
  build behind a permanent download link.
- **Old releases are never deleted.** Deleting is irreversible and was not asked for; if the
  releases page gets noisy, prune with `gh release delete`.
