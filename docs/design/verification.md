## The premise

Three things are unverifiable by this developer. Everything else is made verifiable on a Mac or on a free GitHub Windows runner, and the residue is labelled inside the product rather than quietly assumed.

## 1. The instrument algebra is a unit test, not an assumption (M1)

The emission table's properties are exact integers, independently reproducible in ten minutes with a calculator — 38 covered offsets in `[-12,+25]`, 48 renderings, popcount `{1:30, 2:6, 3:2}`, 2-way exactly `{-7,1,5,13,17,24}`, 3-way exactly `{0,12}`, and `pitch(DegreeOf(st,o), st) == P0 + o` for all 48. *(I re-derived every one of these by hand while writing this plan; they are correct, and the corollary — that a state uniquely determines the key, hence every alternative spelling costs a modifier transition — is what killed the seam-alternation feature.)* These live in a **fixture test pinned to `df.harmonica.v1.json`**, while the loader's generic validator asserts only derived invariants, so a game patch to a 9-key row is a JSON edit rather than a red test suite.

## 2. The DP is proved against exhaustive search, with the generator fixed (M4)

2,000 randomised instances with `N ≤ 6` (plus `N ≤ 8 × 100` nightly), enumerating all `6^N` plans and asserting **exact int64 equality** of the optimum. Two corrections the judges forced: costs are integer micro-units so the comparison cannot flake on float summation order, and the generator is **biased hard toward the multi-way offsets and toward adjacent pairs forcing exclusive swaps** — a uniform offset generator produces mostly trivial instances, because 30 of the 38 offsets have exactly one feasible state, collapsing the effective search space far below `6^N`. Plus monotonicity, self-consistency, and a **soundness** test asserting every edge the DP calls feasible is realisable by truncating the previous note alone (which is what makes the single-pass guarantee real rather than hoped for).

The honest scope of this test, stated because the winning plan overclaimed it: it proves the Viterbi implements *its own recursion*, which for a chain with pairwise costs is the part least likely to be wrong. It says nothing about whether the objective is the right objective. The objective is validated by the tiered-degeneracy regression suite (M5), the corpus, and the ear set (M11).

## 3. The round-trip is the ear you do not have (M7) — the highest-value test in the project

`InstrumentSimulator` replays the finished `InputTimeline` through the profile's own pitch algebra — tracking active modifiers, computing `P0 + Σ SemitoneDelta + DegreeSemitone` — and asserts three things, all of which are amendments the judges required:

1. the pitch sequence equals the intended audible score, onsets within `maxOnsetShiftMs`;
2. **sounded pitch is CONSTANT across each note's `[down, up)` span**, not merely correct at onset — onset-only sampling is blind to the `ModifiersRepitchSustainedNotes = true` failure mode the flag exists to guard against;
3. **`EffectiveMidiNote == SourceMidiNote + t`** for every note not flagged `OctaveFolded`/`Dropped`/`ChordSibling` — the non-circular assertion, because comparing the simulator against `EffectiveMidiNote` alone is a round trip through the same formula on both sides and cannot catch a wrong band chosen by the planner.

The simulator computes from `DegreeSemitones` + `ModifierSpec` directly and **never consults `EmissionTable`**, so a table bug cannot cancel out. It runs across both timing presets and both lowering modes. The negative test — a planner that arms a modifier one note early must make it fail — ships alongside. And the same `Simulate` drives the rehearsal view, so the animated jianpu display is a rendering of a verified invariant rather than a second implementation that can drift.

## 4. Golden files, six suites, reviewably scoped

(a) MIDI → `SourceSong`; (b) `SourceSong` → `HarmonicaScore` + report; (c) `HarmonicaScore` → `InputTimeline` (two variants: hold and toggle); (d) `InputTimeline` → the **lowered event list** for every fixture, plus exporter **bytes** for six chosen fixtures; (e) `InputTimeline` → `SimulatedNote[]`; (f) the full `ConversionReport` including every pass counter. `*.verified.* -text` in `.gitattributes`. Suite (f) is what makes weight changes reviewable — and `hsc goldens review` emits an aggregate delta table that must be pasted into the commit, with a CI check rejecting any `*.verified.*` change lacking a `GOLDEN-CHANGE:` trailer, because a raw diff across dozens of fixtures is a rubber stamp.

## 5. Property tests (CsCheck)

Strict monophony of the audible projection; emitted pitches within range after legalisation; every fingering reproduces its `EffectiveMidiNote`; timeline pairing and logical-state termination under **both** lowerings; exclusion-group members never simultaneously active; lead times satisfied; hold caps satisfied; edits idempotent under replay. Adversarial fuzz: 1 ms inter-onsets, alternating extreme octaves, zero-length notes, 300 nps bursts, 4-second sustains inside long modifier runs.

## 6. The corpus is in the repo

25–30 public-domain `.mid` files **committed**, plus a **procedural fixture generator** (code + fixed seeds, license-free by construction) covering exactly the edge cases harvested pop songs would not reliably contain: chromatic runs, band-seam crossings at offsets 0 and 12, 4-second sustains, dense sixteenths at the retrigger floor, forced L↔R swaps, >38-semitone range, 40–50 ms rolled chords, multi-tempo, malformed. A larger private corpus exists by manifest for weight tuning, but **no milestone gate depends on it** — the backbone must not live on one laptop, and an outside contributor to a public repo must be able to reproduce every golden diff.

## 7. `VirtualClock` + `TraceBackend` make the whole playback engine testable on macOS

The scheduler takes an injected `IClock` and `IInputBackend`. Under `VirtualClock` a three-minute song runs in microseconds, and the entire engine is deterministically covered: countdown, release-all-at-start, guard intervals, exclusion serialisation, min-hold, retrigger gap, panic hotkey, mid-song focus loss, seek, speed change, exception paths, signal paths. A fault-injection theory (every fixture × every 17th event × six abort modes, including the out-of-process watchdog path) asserts the trace always terminates in `RELEASEALL` with an empty held set.

## 8. The Windows backend's marshalling is verified without Windows — and then verified *on* Windows for free

`WindowsSendInputBackend` depends on `ISendInputApi`; `RecordingSendInputApi` captures the exact `INPUT[]` and the tests assert byte by byte: `wVk == 0`, `wScan` matches the Set-1 column, **every key-up carries `dwFlags == 0x000A`**, mouse events never set `MOUSEEVENTF_MOVE`, all events sharing a `TimeMs` arrive in exactly one call, `sizeof(INPUT) == 40` on x64.

Then the **free `windows-2025` runner** — the single largest asset all three source plans under-used — closes most of the rest on every commit: launch the macOS-cross-published exe; drive SendInput into a real Win32 window and assert the characters arrive (validating the whole Set-1/extended/key-up/batching chain end to end on genuine Windows); execute the generated `.ahk` with `Send` stubbed to a log and diff it against the `InputTimeline`; run the `RegisterHotKey` pump thread; measure real scheduler p99 deadline error. None of this involves Delta Force.

## 9. The trace log IS the golden file

One canonical writer produces both, with scheduled times in the event column and realised lateness confined to the summary line, under a documented canonicalisation rule for the header. So a user's emailed log is **directly diffable** against CI, and `hsc replay hs-diag-….zip --backend trace` re-runs their exact settings and profile snapshots locally and diffs. Traces match ⇒ the engine is right and the environment is wrong. That is the entire remote-debugging story, with no network and no Windows machine.

## 10. Policy and hygiene are tests

`Policy.Tests` uses Mono.Cecil over **first-party assembly IL** to assert: no `System.Net.*` member reference reachable from the app (stated honestly in the README as a first-party check, not a runtime guarantee — a self-contained publish always ships the BCL); `Core` references no `System.IO`, no Avalonia, no DryWetMidi, no P/Invoke; `Midi` never touches `Melanchall.DryWetMidi.Multimedia`; `App` references no algorithm type; `InstrumentSimulator` never references `EmissionTable`; no `System.Double` or `System.Single` in the optimiser namespace. Plus a determinism test (whole pipeline twice, byte-identical) and a culture matrix (`tr-TR`, `zh-CN`, `en-US`) that catches the `ToLower`/number-format bugs that would otherwise only surface on a Chinese user's machine.

---

## What remains genuinely unverifiable — the honest list, ranked

1. **Does Delta Force accept injected input at all.** Binary; project-killing for Branch A alone. No first-hand report exists. Mitigation: the AHK path ships first and reaches the game through a different (and widely validated) injection engine, so the question is asked with a 150-line text file at day ~30 rather than after building six days of C#; rung 6 of the diagnostic ladder names this cause specifically rather than reporting a generic failure; and if the answer is no, the editor + report + rehearsal + export product is complete without it.
2. **Every timing constant.** Minimum registerable hold, same-key retrigger floor, modifier settle time, whether a modifier re-pitches a sounding note, whether a rate limit or 判定粘连 threshold exists. Mitigation: three shipped presets defaulting to the evidence-backed one, per-value `Provenance` rendered as a dotted underline in the UI, `docs/CALIBRATION.md` with six scripted experiments (each a golden-filed timeline plus one yes/no question, so the *harness* is verifiable even though its results are not), and a profile-import path with a validating field diff so the first user with the game and thirty minutes fixes it for everyone.
3. **Hold versus toggle, and modifier stacking.** Both behaviours implemented and independently golden-filed from M6; the choice is one JSON word. Experiment C5 answers it definitively with one user and one question.
4. **Whether any vendor macro file imports, and whether vendor engines quantise delays.** If Razer/Logitech/Bloody quantise to 10 ms or the 15.6 ms Windows tick, that vendor is musically dead regardless of format correctness — nobody has ever measured this. Mitigation: the generic AHK tier has no such question; vendor exporters ship with an explicit unmeasured-granularity warning and a two-minute user test (export a scale in sixteenths, listen) in their `ExtraInstructions`.
5. **Which game modes the instrument works in, and whether the eight keys and three buttons are exclusively captured while it is equipped.** The single biggest input to the risk framing, and the corpus could not close it. Handled as a checklist question and a `GameProfile` field.

Nothing on that list requires a code change once the answer arrives. That is the point of the architecture — and it is stated here without the two overclaims the judges caught: the round-trip proves the emitter is self-consistent **with a model of the instrument**, and the model is the unverified part; and "provably correct" applies to the DP's implementation of its objective, not to the objective itself.