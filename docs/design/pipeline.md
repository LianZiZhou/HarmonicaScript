## The named pass pipeline

`IScorePass { string Id; string DisplayNameKey; PassResult Run(PipelineContext ctx); }` where `PassResult(bool Changed, IReadOnlyList<PassCounter> Counters, IReadOnlyList<Diagnostic> Diagnostics)` and **every `Diagnostic` carries a `NoteId`**. IDs are **stable semantic strings with no ordinal** (`reduce.dropShort`, not `P08.DropTooShort`) — inserting a pass in v1.1 must not churn 30 golden reports. Order comes from the registration list.

The pipeline is **two-level**, because a flat list provably cannot express the real control flow (the maintainability judge's flaw): `IScorePass` for the linear parts, and one named composite `CandidateEvaluator` that is an ordinary class, not a pass, for the per-transposition subsequence.

```
load.read            tolerant ReadingSettings + GB18030 + Sanitize        → sourceNotes, repairedEvents
load.inventory       per (track,channel) stats + MelodyScore              → tracks, autoSelectedTrack
track.select         drop channel 9 by default, drop deselected           → notesAfterSelect
time.absolute        ticks→µs via TempoMap (keep BOTH)                    → tempoChanges
time.trim            leading silence (default ON)                         → trimmedMs
─── reduction fixpoint group, repeat until Changed==false, max 4 iterations ───
  reduce.chords      window ChordWindowMs=45, MUSICAL time (pre-speed);
                     policy Highest|Lowest|Loudest|TrackPriority;
                     tie-break velocity↓, length↓; onset = EARLIEST of the group
                     (a rolled chord must not land late); siblings KEPT + flagged
  reduce.monophony   forward sweep note[i].end = min(end, note[i+1].onset)
  reduce.dropShort   drop < minSurvivingDurationMs (DERIVED = noteHoldMin + keyChangeGap)
  reduce.smoothMelody  a kept note ≥12 semitones above BOTH neighbours and <90 ms
                     → PROMOTE its next-highest chord sibling if one exists, else drop
──────────────────────────────────────────────────────────────────────────────
time.speed           µs /= speed. Articulation floors do NOT scale (physical).
fit.search           ← the composite. See §2.
fit.commit           materialise HarmonicaScore
edit.apply           journal replay, anchored to SourceIndex               → editsApplied, editsOrphaned
fit.replan           t pinned, edited notes pinned, re-run legalise+plan
sched.articulate     the resolution ladder + drift guard
sched.breath         breath rest (default ON: 90 ms after 8 s continuous sound)
emit.lower           LowerHoldSpans | LowerToggleTransitions (profile-driven, both golden-filed)
emit.holdCaps        maxKeyHoldMs / maxModifierHoldMs — a COMPILE-TIME property of the IR
emit.quantise        the ONE rounding point: µs → absolute int ms, round-half-up
emit.validate        the hard invariant. A failing timeline is neither playable nor exportable.
```

**Why the fixpoint group:** `reduce.smoothMelody` removes notes, which reopens gaps that `reduce.monophony` had closed and can un-strand a truncated predecessor. Running once leaves notes artificially staccato against silence and mis-prices the DP's edges (the judge's "never re-converges" flaw). Bounded at 4 with a `ReductionNotConverged` counter.

**Why chord collapse is pre-speed and 45 ms:** it is a *notational* tolerance, not a physical one. At 2× speed a 30 ms passage would be misread as a chord; at 0.5× a real chord would escape. 45 ms catches DAW-humanised and lightly-rolled chords that a 25 ms window misses (BMP deliberately spaces chord members at 50 ms). `RollDetected` is counted so under-collapse is visible rather than silent.

---

## 2. The joint transposition + modifier optimisation

### 2.1 Candidates — exact bounds, **no pruning**

```
LO = EmissionTable.LoOffset (−12);  HI = EmissionTable.HiOffset (+25)
T = [ (P0+LO) − maxSourcePitch , (P0+HI) − minSourcePitch ] ∩ [−36, +36]
```
~60–120 candidates. **No top-K prefilter.** Both runner-up judges independently showed a prefilter (a) saves nothing (73 × 2000 × 36 ≈ 5.3M cheap integer edge evals, single-digit ms) and (b) prunes on the wrong axis — out-of-range is nearly flat across neighbouring `t` while modifier churn is spiky, so the churn-optimal candidate can be discarded before it is ever scored. Deleting the prefilter also deletes an entire class of "the prune doesn't preserve the argmin" bug and the "best 25 with ties" determinism hole. `TranspositionMode`: `PreserveKey` (T restricted to multiples of 12), `Balanced` (default), `Free`, `Manual`.

### 2.2 The objective — **tiered lexicographic, all integer**

This is the single biggest change from the winning plan, and it fixes four judge-found flaws at once (inverted degeneracy argument; the t≠0 hole; hard infeasibility normalised by N so it got cheaper as songs got longer; two inconsistent tie definitions).

```
For candidate t, after legalise + plan, compute:
  T1  hardInfeasible : int      # transitions the ladder must MERGE or DROP. A count. Never normalised.
  T2  musicalLoss    : int ppm  # (droppedDurLow + 4*droppedDurHigh + 0.35*foldedDur) / totalDur × 1e6
  T3  keyDistance    : (|signedMod12(t)|, |t|)     # lexicographic pair
  T4  mech           : int µu   # W_EVENT*events + W_EXCL*swaps + W_DUTY*heldSeconds + W_SOFTDEF*deficitMs

compare(a,b):
    if a.T1 != b.T1                      → by T1 ascending          (exact, no epsilon)
    if |a.T2 − b.T2| > EPS_LOSS (5000)   → by T2 ascending          (0.5% of duration)
    if a.T3 != b.T3                      → by T3 ascending          (exact)
    → by T4 ascending; final tie-break (|t|, t, state-sequence)     (fully deterministic)
```

Three consequences worth naming. (i) **A piece can never be detuned for a mechanical crumb**: `t=−1` with sharp-always-held sounds identical to `t=0` — same `T1`, same `T2` — and then loses at `T3` because `|signedMod12(−1)| = 1 > 0`, *before* mechanics is consulted. Likewise `t=+4` versus `t=+3`: if musical loss ties within 0.5%, `|3| < |4|` wins outright. (ii) **Hard infeasibility can never be traded against a folded note**, because it is tier 1 and is a raw count. (iii) The 5% relative hysteresis and the 0.02 absolute tie window — which the judge showed were mutually inconsistent — are both gone; there is one epsilon, on one tier, in one unit.

Weights (`data/weights/objective.v1.json`, each carrying a `Provenance` badge exactly like the timing constants): `W_EVENT = 1_000_000 µu`, `W_EXCL = 1_500_000`, `W_SOFTDEF = 300_000 /ms`, `W_DUTY = 40_000 /s × modifier.dutyWeightMilli/1000`, `HARD_PENALTY = 1_000_000_000`. **All costs are int64 micro-units.** No floats anywhere in the optimiser: this makes the brute-force equivalence test an exact integer comparison rather than a flaky float one, makes tie-breaking deterministic, and makes golden files stable.

### 2.3 The DP — exact, integer, `O(k²·N)` with `k` derived from the profile

`ModifierPlanner` loops `for st in 0..StateCount-1`; the 6-state space is **derived from `ModifierSpec[]` + exclusion groups + `MaxSimultaneousModifiers`**, never typed. For this instrument `k = 6`, asserted by a pinned fixture test.

```
precompute once per (profile, timing) — a 6×6 table, not a per-note branch:
  evCount[a][b]  = popcount(a^b)
  exclSwap[a][b] = ∃ m1 ∈ (a\b), m2 ∈ (b\a) with the same nonzero ExclusionGroup
  setup[a][b]    = max( max_{m ∈ a\b} m.ReleaseLeadMs , max_{m ∈ b\a} m.PressLeadMs )
                   |> if exclSwap then max(·, ExclusiveSwapMs)
                   |> + InterModifierStaggerMs × (evCount − 1)
  reqSil[a][b][sameKey] = ModifiersRepitchSustainedNotes
                          ? max(sameKey ? SameKeyRetriggerMs : KeyChangeGapMs, setup[a][b])
                          : (sameKey ? SameKeyRetriggerMs : KeyChangeGapMs)

per note i:
  off[i]  = pitch[i] + t − P0                        (in [LO,HI]; legalise ran first)
  mask[i] = EmissionTable.StateMask(off[i])
  span[i] = onset[i+1] − onset[i]   (i < N−1);  span[N−1] = dur[N−1]

  # PESSIMISTIC availability — this is what removes the fixpoint entirely
  availSil[i] = max(0, span[i] − NoteHoldMinMs)      # achievable by truncating i−1 ALONE
  baseSil[i]  = min over (a,b) ∈ mask[i−1]×mask[i] of reqSil[a][b][sameKey(a,b)]

nodeCost(i, st) = Σ_{m ∈ st} W_DUTY × m.dutyWeightMilli × span[i] / 1000
      # charged over the INTER-ONSET span, not the note duration — this is exactly the
      # hold time the maximal-run lowering will produce, so the DP minimises the real quantity

edgeCost(i−1, a → i, b):
    sameKey = EmissionTable.DegreeOf(a, off[i−1]) == EmissionTable.DegreeOf(b, off[i])
    req     = reqSil[a][b][sameKey]
    # bill ONLY the marginal deficit attributable to the modifier plan
    deficit = max(0, req − availSil[i]) − max(0, baseSil[i] − availSil[i])
    hard    = deficit > 0
    return W_EVENT*evCount[a][b] + W_EXCL*exclSwap[a][b]
         + W_SOFTDEF*deficit + (hard ? HARD_PENALTY : 0)

forward pass over a rolling long[k]; back[] is byte[k*N]; zero allocation in the loop
cost[0][st]   = mask[0].Has(st) ? edgeCost(Neutral→0,st) + nodeCost(0,st) : LONG_MAX
cost[i][st]   = nodeCost(i,st) + min_{a ∈ mask[i−1]} ( cost[i−1][a] + edgeCost(i−1,a,i,st) )
answer        = min_st ( cost[N−1][st] + exitCost(st→Neutral) ) ; backtrack
hardInfeasible = re-walk the chosen path and count edges with hard==true
```

**Why the pessimistic `availSil` is the right call.** The winning plan used `availSil = interOnset − NoteHoldMin` unclamped *and* implicitly assumed the scheduler's ±15 ms shift budget, then patched the resulting mismatch with `SolverIterations = 2` and an acceptance criterion ("iteration 2 changes ≤1% of notes") that was hoped for rather than argued. Clamping to `max(0, …)` and *excluding* the shift budget makes the DP's feasibility judgement a **sound lower bound**: any edge the DP believes feasible is realisable by truncating the previous note alone, which is a purely local operation that cannot cascade. The scheduler's shift budget then only ever *recovers rhythm*, never enables a plan. **One pass. No iteration. No convergence assumption.** The cost is mild conservatism (an occasional respell where a 5 ms shift would have sufficed), reported as `ConservativeRespells`.

Subtracting `baseSil` is what stops pure note-density infeasibility — 15 ms inter-onsets against a 20 ms hold floor — from being billed to the modifier plan and then amplified at `W_SOFTDEF`. Density infeasibility surfaces separately as `DensityDeficitCount`, computed once per candidate.

**Complexity.** 36 edge evaluations per note per candidate, each three array reads and integer arithmetic; ≤120 candidates. `N = 2000` ⇒ ~8.6M evaluations, low tens of milliseconds — fast enough to re-run on every settings change interactively. `long` bounds: `2000 × 1e9 = 2e12`, far inside int64.

**What falls out for free, and what does not.** Natural spellings win ties automatically (the duty node cost is nonzero, so `4` beats `#3` and `1̇` beats `#7` with no hand-written rule). Infeasible exclusive swaps are *priced in tier 1* rather than patched downstream. **Seam alternation does not fall out and is not claimed** — I verified that within a fixed state at most one key renders any offset, so every alternative spelling of a repeated note requires a modifier transition; under `df.reference` alternating (`20 + max(20,12) = 40 ms`) is *worse* than retriggering (`20 + 12 = 32 ms`). The DP will discover it in the rare case it helps; there is no feature, no claim, and no milestone gate attached to it.

### 2.4 Out-of-range legalisation (runs **inside** the candidate loop, before the DP)

Ordering matters: legalise → re-run monophony → plan. Folding changes the band, hence the state sequence, so folding after the DP would invalidate the plan the judge correctly flagged.

Default `FoldThenDrop`: drop if `durationMs < 120` (an ornament in the wrong octave is more distracting than its absence); else fold by at most `MaxFoldOctaves = 1`; contour guard rejects the fold if it creates an interval > 14 semitones against **both** neighbours *and* neither adjacent gap is a phrase boundary (rest ≥ 250 ms), in which case drop. **Clamp-to-edge is not offered as a default** — it breaks pitch class and produces unison plateaus that read as a stuck note. `DropOnly` and `FoldUnbounded` are settings. The `4×` asymmetric penalty for dropping HIGH notes lives in the *transposition objective's tier 2*, not in the per-note policy, because it answers "which transposition", not "what to do with the residue".

### 2.5 Articulation ladder — now purely about density

Because the plan is already realisable by construction, rungs (c) and (d) fire only on genuine density infeasibility:

```
for i: down = onset[i] + carriedShift ; up = max(down + NoteHoldMin, down + musicalDur[i])
    req = reqSil[state[i−1]][state[i]][sameKey]          # the SAME table the DP used
    if up[i−1] + req > down[i]:
      (a) truncate i−1 toward NoteHoldMin                          → DurationClamped
      (b) shift i later ≤ MaxOnsetShiftMs, cumulative ≤ MaxDriftMs → OnsetShifted
      (c) else if sameKey: merge i into i−1                        → MergedWithPrev
      (d) else drop i + a Blocker diagnostic carrying the exact ms → Dropped
```

Truncate before shift (staccato is less perceptible than rubato); merge before drop (a merged repeat reads as a tie, a dropped one as a hole); drop always produces a clickable diagnostic saying 「缺 38 毫秒」, never 「失败」. **`DeficitEventCount` is now well-defined** — it is `count(c) + count(d)` — which resolves the judge's finding that the winning plan's `DeficitEventCount` was simultaneously asserted by `Validate()` to be zero and used as a search predicate. `MaxSustainableSpeedFactor` bisects the speed factor over ~8 re-runs for the largest speed with `DeficitEventCount == 0`.

### 2.6 Reported ceilings — three numbers, not one

`MaxSupportedNotesPerSecond` was optimistic by up to 60% in the winning plan because it ignored same-key retrigger and modifier setup. Report all three, plus the one that matters: `SameKeyCeiling = 1000/(hold+retrigger)`, `KeyChangeCeiling = 1000/(hold+keyChangeGap)`, `ModifierSwapCeiling = 1000/(hold+exclusiveSwap+leads)`, and **`EffectiveCeiling`** computed from *this score's actual transition mix*. `P50/P95/Peak` notes-per-second alongside; P95 drives the grade so one 32nd-note flourish does not tank an otherwise fine song.

### 2.7 `ConversionReport` — a projection, not a parallel record

The winning plan had ~40 typed fields duplicating the `PassCounter` bag, both golden-snapshotted — two sources of truth for the same number. Here `ConversionReport` holds `IReadOnlyList<PassCounter>` plus `IReadOnlyList<Diagnostic>` plus the transposition candidate list, and every headline metric is a **typed accessor projecting over the bag**. Adding a metric is adding a counter. The stacked penalty bar (`丢音 −12 / 八度折叠 −7 / 速度 −3`) is computed from the projection; clicking a segment filters the note grid. **There is no "out of scale" metric** — it is structurally always zero on a fully chromatic instrument, and reporting it would be theatre.