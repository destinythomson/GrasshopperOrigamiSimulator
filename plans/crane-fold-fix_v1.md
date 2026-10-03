## crane-fold-fix

### Intent
The Traditional Crane in `grasshopper/examples/OrigamiExamples.3dm` tangles and self-intersects in both `OrigamiSim.gh` (dynamic solver) and `OrigamiSim_Kangaroo.gh`. Root-cause and fix both so the crane folds as in the web app. Suspected cause: both solvers give every M/V crease ±180° × Fold (`OrigamiSolver.cs:628`, `OrigamiKangaroo.cs:222`), whereas the web app uses opacity × 180 per crease (`js/pattern.js:75,85`). The crane's M/V creases are 45°×16, 90°×1, 135°×8, 180°×49 (`examples/patterns.json`); the angle is already stored on each example line as User Text `TargetAngleDeg`, but Geometry Pipelines pass only geometry.

Resolved assumptions: angle source = User Text `TargetAngleDeg` (missing → 180°); done = crane matches the web app at Fold = 1; live verification via the Rhino MCP (`mcp__rhino__*`, always pass `slot`; `g1_apply_graph`/`g1_connect_many` unreliable — see `plans/grasshopper-kangaroo-solver.md`).

### Criteria & scoring axes
- Criterion: root cause confirmed — test: tangle reproduced in both solvers on the crane alone (capture); all targets shown as ±180°; spike with per-crease targets removes the tangle before real code changes.
- Criterion: per-crease angles reach both solvers — test: Info target histogram for the crane = 45×16, 90×1, 135×8, 180×49; a line without the key gets 180°.
- Criterion: crane folds like the web app — test: Fold ramped 0→1 in both definitions; at 1: `finite=true`, mvSenseOk = all, maxThetaErrDeg ≤ 5°; viewport capture beside the web-app crane at 100% (repo `index.html` in browser pane).
- Criterion: no regressions — test: Miura demo + Simple Vertex, Map Fold, Square Twist, Miura-ori, Waterbomb Tessellation give identical Info at Fold 0.5 and 1 vs baseline; `OrigamiPrint.cs` unchanged; READMEs updated.
Axes: Goal-likelihood, Fidelity (to web app), Regression safety, Usability.

### Convergence
Converged at loop 5. Final scores: Goal-likelihood 9, Fidelity 9, Regression safety 9, Usability 9.

### Steps
1. Reference + baseline — web-app crane at 100% (capture, target histogram, face count); repro tangle in both .gh files (capture + targets); baseline Info for Miura demo + 5 full-angle patterns, both solvers, Fold 0.5 and 1, fixed solve count. Over fixing first: no measurable "like the web app" or "unchanged" otherwise. Divergence: the web-app crane may itself show interpenetration; "match" then tolerates it.
2. Spike in a throwaway slot (never saved) — minimal option-A patch: read `Component.Params.Input[i].VolatileData` → `GH_Curve.ReferenceID` → `RhinoDocument.Objects.FindId(id).Attributes.GetUserString("TargetAngleDeg")`. Over a separate patterns.json hack: no throwaway code. Contingency ladder if it still tangles, one targeted check each: (a) face/triangle count vs web app; (b) slow Fold ramp (small increments, several solves each); (c) Kangaroo flat-state branch / side windows. If ReferenceID is empty → option C (layer lookup + endpoint match). Divergence: scope grows if angles alone are insufficient.
3. Implement — `OrigamiSolver.cs` and `OrigamiKangaroo.cs`: per-curve angle lists from the SCRIPT region (default 180°); segments of a polyline inherit its angle; split sub-edges inherit; overlap winner carries its angle; target = sign × angle × Fold; Info gains `targets=` histogram. Archive previous files as `archive/OrigamiSim_v5.gh` and `archive/OrigamiSim_Kangaroo_v1.gh`; push sources into the components via MCP `run_csharp`; save. Over B (Query Model Objects rewiring): unreliable MCP wiring + canvas churn. Over C (endpoint match): fragile with overlaps. Divergence: internalised (non-referenced) curves fall back to 180°.
4. Verify — histogram, fold criteria and captures for both solvers; baseline re-run identical; `git diff` shows `OrigamiPrint.cs` untouched.
5. Docs — `grasshopper/README.md`: `TARGET_DEG` = default when a line has no `TargetAngleDeg`; User Text note; limits (internalised curves → 180°; editing only User Text may not refresh the pipeline). `grasshopper/examples/README.md`: drop the "fold to 180°" limitation.

### Execute at
low — mechanical edits and MCP runs; raise to medium only if the contingency ladder triggers.

### Execution checklist
- [ ] 1 Reference + baseline (web app crane; repro both solvers; baseline numbers)
- [ ] 2 Spike (option A in throwaway slot; crane folds)
- [ ] 3 Implement (both .cs; archive; update both .gh)
- [ ] 4 Verify (crane criteria; regression identical; Print untouched)
- [ ] 5 Docs (both READMEs)
