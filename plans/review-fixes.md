# review-fixes

### Intent
Apply code-review Spec findings #1, #3, #8, #9, #10 from `plans/handoff-review-fixes.md` on branch `grasshopper-solver`: report `cutOffset` in builder Info; make the Readout's mountain/valley sense check able to fail; fix stale text in the Kangaroo record, builder header and examples README. Re-inject into `grasshopper/OrigamiSim_Kangaroo.gh` via Rhino MCP, verify headlessly, re-save, record, one commit, push to PR #1.

Deviation from handoff (user-approved): #3 derives each crease's side from `sign(target)` in the Readout instead of adding a `Sides` builder output + Readout input + wire. Builder `side == sign(target)` whenever `target != 0` (both are `(M ? -1 : 1) * sign(Fold)`); at `target == 0` the sense test is skipped.

### Criteria & scoring axes
- C1 sense check can fail — test: demo 1 with vertex 3 rotated to the wrong side gives `mvSenseOk=0/1`; the mirrored (correct-side) rotation gives 1/1.
- C2 no regressions — test: demo 1 @ Fold 1 → 1/1, err ≤ 2°; demo 2 @ Fold 1 → 24/24; demo 3 @ Fold 0.8 → 264/264; cut-layer test → 13 nodes, `cutVertexCopies:1`, (0,1) copies ~2.0 apart.
- C3 fixes in the saved file — test: fresh-slot reopen shows `cutOffset:0.001` on cut test, injected source == `.cs` sections, 0 errors, 7 groups, sliders 0.6/3/3/100/1000, DEMO=2; `git status` shows `OrigamiSim.gh` unmodified.
- C4 text correct — test: grep finds none of "Defaults stay at 1", "wrapped to [-180, 180]", "Set Multiple Lines", "not supported by the solver"; README Readout wording matches.
Axes: goal-likelihood, verification strength, .gh risk

### Convergence
Converged at loop 3. Final scores: goal 9, verification 9, .gh risk 9

### Steps
1. Text fixes #8, #9, #10 + README Readout line — no dependencies; direct edits.
2. #1 builder ADDITIONAL: `if (copies > 0) notes.Add("cutOffset:"…)` after the offset loop, inside the cut block — no offset noise on uncut patterns.
3. #3 Readout ADDITIONAL: side = sign(target); M/V θ into the goal's window (valley (−π/2, 3π/2], mountain [−3π/2, π/2)); error `|target − θu|`; sense OK iff `target == 0 || 0 < side·θu ≤ π + 5°`; flat creases keep the wrapped error. Header comment updated. Diverges from the goal only at Fold 0 (M/V error uses ±180 wrap, not side window).
4. Main slot: open .gh, re-inject both components (source only, no param edits → wires kept), clear compiled cache; headless demos 1/2/3; cut-layer test then delete geometry; restore DEMO=2, Fold 0.6; solve; save.
5. Scratch slot: reopen saved .gh; C3 checks; ± wrong-side test on demo 1 (expect pass/fail pair — proves the harness, not just the code); close without saving.
6. Kangaroo record checklist "Review fixes" entry with measured numbers; `git status` (v1 untouched); one commit listing 5 items + this record; push.

### Execute at
Current effort — mechanical steps, each with a concrete check.

### Execution checklist
- [x] 1. Text fixes #8, #9, #10 (+ README Readout line)
- [x] 2. #1 builder cutOffset note
- [x] 3. #3 Readout side-window sense check
- [x] 4. Main slot: inject, demos 1/2/3, cut test, restore, save
- [x] 5. Scratch slot: reopen checks + wrong-side test
- [x] 6. Record entry, commit, push
