# In-game calibration

The developer has no Windows machine, and ACE lists VMWare/KVM/VirtualBox as banned, so a
VM is not an option either. Five facts therefore cannot be established here. Each is a JSON
value with a `source` badge of `invented`, `inferred` or `thirdPartyTool` — never `measured`
— and each is fixable by editing a profile rather than rebuilding.

Anyone with Windows and the game can close four of the five in about half an hour. Each
experiment below is a single yes/no question with a pre-generated timeline to play.

| # | Question | How to answer | Fixes |
|---|---|---|---|
| C1 | Does `SendInput` reach the game at all? | Run `hsc selftest --notepad` first (must pass). Then equip the harmonica in a safe zone, run `hsc play --fixture c1-single-note`, and say whether you heard one note. | The one genuinely project-killing unknown for Branch A. |
| C2 | Are the three mouse modifiers hold or toggle? | Play `c2-modifier-probe`: it holds 升调 across four notes, releases, then clicks it four times. Say which pass sounded like an ascending octave row. | `instrumentProfile.modifiers[].behavior` |
| C3 | Is `降调`/`升调` exactly ±12 semitones? | Play `c3-octave-probe` (1 with 升调 held, then 1̇ with nothing held). Say whether the two pitches were identical. | `instrumentProfile.modifiers[].semitoneDelta` |
| C4 | What is the real same-key retrigger floor? | Play `c4-retrigger-ladder`: the same note repeated at 8/12/16/20/25/30 ms gaps. Say the first rung at which you heard every repetition. | `timing.sameKeyRetriggerMs` |
| C5 | How long does a modifier take to settle? | Play `c5-modifier-lead-ladder`: an exclusive 降调→升调 swap at 8/12/16/20/25 ms of lead. Say the first rung that sounded correct. | `timing.exclusiveSwapMs`, `octavePressLeadMs`, `octaveReleaseLeadMs` |
| C6 | Does the game run elevated? | Task Manager → Details → add the "Elevated" column → find the game process. | Decides whether UIPI silently blocks a normal-privilege process (diagnosis rung 4). |

**C1 is the one that cannot be worked around.** If the answer is no, Branch A is dead and the
product is the converter, the editor, the rehearsal view and the exporters — which is why
M13 is scheduled last and why M12 is a complete product on its own.

Report results via the `calibration-result` issue template. A profile field only moves from
`inferred` to `measured` once independent reports agree.
