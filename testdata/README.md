# Test corpus

## The copyright rule, which is not negotiable

**Only public-domain material may be committed to `pd/`.** Bach, 欢乐颂, 小星星, IMSLP and
Mutopia scores, traditional 古风 melodies whose composer died over 70 years ago.

Do **not** helpfully add 起风了.mid, a Genshin OST rip, or anything else under copyright,
however widely it circulates. HarmonicaScript is bring-your-own-file by design: it ships no
song library, uploads nothing, and has no sharing feature. A committed pop MIDI would turn a
neutral converter into a distribution platform, which is the one thing we permanently avoid.

## Layout

| Path | What | Committed? |
|---|---|---|
| `pd/` | 25-30 public-domain MIDIs, the everyday regression corpus | yes |
| `generated/` | the procedural fixture generator (code + fixed seeds), not its output | code only |
| `corpus/manifest.json` | SHA-256 + provenance for a larger private set | manifest only |
| `vendor-samples/` | real exported vendor macro files, byte-exact, with `PROVENANCE.md` | yes |
| `snapshots/` | Verify golden files | yes |

`generated/` output is produced at test time from committed seeds, so the fixtures are
reproducible without committing megabytes of MIDI.
