# Provenance and the clean-room boundary

**This file is maintained continuously, from the first commit.** A boundary document
written after all the code exists is retroactive rationalisation and protects nobody.
The process is the protection; this file is the record of it.

## The rule

Algorithms, numeric constants and file-format structures derived from **public
documentation, published README text, or a program's observable behaviour** are facts,
and facts are not copyrightable. **Source expression never is a fact.** Where a prior-art
project is copyleft, we may read its documentation and observe its behaviour; we may not
transcribe its code, and for the most aggressively licensed we do not read the source at all.

Every pull request carries a clean-room checkbox. Every entry below records: what we
derived, from which artefact, at what depth of reading.

## Register

| Project | Licence | Depth of reading | What was derived | Expression copied |
|---|---|---|---|---|
| `ChickenD233/harmonica-auto-player` | **none (all rights reserved)** | README + the bundled `口琴钢琴测试台.html` simulator page | The instrument mapping (`deg = {z:0,x:2,c:4,v:5,b:7,n:9,m:11,',':12}`, left/middle/right mouse = −12/+1/+12, 38-semitone span) and the timing constants shipped as the `df.reference` profile. These are **facts about a third-party program's observable behaviour**, recorded with their exact source lines in `data/timing/df.reference.json` via each value's `source` field. | **None.** |
| `BardMusicPlayer` / `LightAmp` | GPL-3.0 | README and observable behaviour only | Articulation-constraint *shape* (that a minimum inter-attack interval and a release gap must exist at all). Our numbers are not theirs. | **None.** |
| `MidiBard2` | AGPL-3.0, plus a per-file "must prominently credit akira0245" clause | **Not read. Treated as radioactive.** | Nothing. | **None.** |
| `clxTools` | LGPL-2.1 | Published pass names only | The *concept* of a named pass pipeline emitting per-pass counters. **Our pass names are deliberately project-native** (`reduce.chords`, `fit.search`, `emit.quantise`) rather than mirroring theirs, so that we do not manufacture a written record of a structural resemblance. | **None.** |
| `sabihoshi/GenshinLyreMidiPlayer` | **MIT** | Source | The only project we may borrow expression from. Any borrowing is marked at the call site and listed in `NOTICE.md`. | Permitted; none yet. |
| Razer Synapse 3 macro XML | n/a (file format) | A real exported `.xml` published by a third party | Element names, `Type`/`Delay`/`Makecode`/`IsExtended`/`State` structure, and that `Makecode` is PS/2 Set-1 (verified: `91/30/42/15/57` decode as LWin(ext)/A/LShift/Tab/Space). | File formats are not expression. |

## Standing bans, enforced as tests not prose

`HarmonicaScript.Policy.Tests` (Mono.Cecil) plus `BannedApiAnalyzers` enforce:

- `HarmonicaScript.Core` references no `System.IO` and no `System.Net.*` — it is pure computation.
- `HarmonicaScript.Midi` references no `Melanchall.DryWetMidi.Multimedia` — decision D2.
- No assembly anywhere references `System.Net.*` — the application makes no outbound
  network connection, ever (decision D12). This is a first-party invariant precisely so a
  user can corroborate it with a firewall in ten seconds.
- From M5: no floating-point opcodes in the optimiser (decision D8).
