# Design record

These are the working documents the implementation was built from, kept as a record rather than
as current documentation. Where they disagree with the code, **the code is right** — the
deviations, and why each happened, are recorded in [`../JOURNAL.md`](../JOURNAL.md).

| file | what it covers |
|---|---|
| `PLAN.md` | the approved plan: settled facts, decisions D1–D15, milestones M0–M15 |
| `thesis.md` | the organising idea in one page |
| `dataModel.md` | the four layers: source song → score → timeline, plus the profile schema |
| `pipeline.md` | the named pass pipeline and the joint transposition + modifier DP |
| `branchA.md` | live input: backend seam, scheduler, safety invariants, the six-rung diagnosis |
| `branchB.md` | macro export: the IR, the exporter contract, the sample gate |
| `gui.md` | screens, in build order |
| `verification.md` | how any of this is testable from a machine that cannot run the game |
| `decisions.md` | every contested call, as "chose X because Y" |
| `milestones.md` | the milestone list with DONE criteria |
| `cut.md` | what was deliberately left out, and why |
| `releaseAndRisk.md` | licence, clean-room boundary, risk posture |
| `solutionLayout.md` | project layout and package pins |

The single most load-bearing conclusion, if you read only one thing: because the eight key
degrees have pairwise-distinct semitone offsets, **a modifier state uniquely determines the
physical key**. That collapses fingering into a plain Viterbi over six states, and it is why this
project can do something a lookup table cannot. See `pipeline.md`.
