## crane-fold-fix (v2 — supersedes crane-fold-fix_v1.md after the chunk-2 spike)

### Intent
The Traditional Crane in `grasshopper/examples/OrigamiExamples.3dm` tangles and self-intersects in both `OrigamiSim.gh` (dynamic solver) and `OrigamiSim_Kangaroo.gh`. Root-cause and fix both so the crane folds as in the web app.

Spike findings (2026-10-03), three causes:
1. Per-crease target angles ignored: every M/V crease gets ±180° × Fold (`OrigamiSolver.cs:628`, `OrigamiKangaroo.cs:222`); the web app uses opacity × 180 (`js/pattern.js:75,85`). The angle is on each example line as User Text `TargetAngleDeg`.
2. Vertex merge distance = document tolerance. The crane's endpoints miss by 0.016–0.045 (sheet = 100 units) → 72 nodes, 7 crease edges pruned as dangling, print mesh not closed. The web app merges within vertTol = 3 px of a 588 px sheet (`js/globals.js:63`) → 60 nodes, 104 faces, 163 edges, 149 creases.
3. Scale dependence. The web app centres the pattern and scales it to bounding radius 1 (`js/model.js:351`); our solver simulates in raw model units, so the force balance (face vs crease vs beam) changes with drawing size.
With 1–3 applied in the spike (tol 0.5, crane scaled to radius 1), our solver matches the web app run for run under the same schedule (sense 89/111 at 0.2 both; max error at fold 1 + hold 86.6° ours vs 87.1° web).

Resolved assumptions: angle source = User Text `TargetAngleDeg` (missing → 180°); done = crane matches the web app at Fold = 1; fix causes 2–3 inside both solvers (option A); live verification via the Rhino MCP (`mcp__rhino__*`, always pass `slot`; harness in the session scratchpad drives the Fold ramp with `_-ScriptEditor _Run`). Web-app reference must be driven with `globals.model.step(100)` (the hidden browser pane pauses requestAnimationFrame, so timed ramps and position reads are stale).

### Criteria & scoring axes
- Criterion: root cause confirmed — test: tangle reproduced (done: strain 14–20 %, maxErr 468°, sense 108/121); spike shows the fix (done, see Intent).
- Criterion: per-crease angles reach both solvers — test: Info `targets=` for the crane = `-180x50,-135x4,-45x10,45x8,90x2,135x4,180x33` (web app's signed histogram); a line without the key gets 180°.
- Criterion: crane folds like the web app — test: both definitions, crane at its original 100-unit size in the example file, Fold ramped 0→1 (50 increments × 18 solves, hold 100): `finite=true`, mvSenseOk = all, topology 60 nodes / 104 faces, max and mean crease error within 10 % of the web app's (87.1°, 10.9°); captures beside the web-app crane.
- Criterion: no regressions — test: Miura demo + Simple Vertex, Map Fold, Square Twist, Miura-ori, Waterbomb Tessellation still fold correctly (finite, mvSenseOk all, strain no worse than before at Fold 0.5 and 1); `OrigamiPrint.cs` unchanged; READMEs updated.
Axes: Goal-likelihood, Fidelity (to web app), Regression safety, Usability.

### Convergence
Converged at loop 5 (v1). Revised after the spike by user decision: fix option A, criteria above.

### Steps
1. Reference + repro — done: web-app reference (60 nodes, 104 faces, signed histogram, step-driven error profile), repro in the dynamic solver.
2. Spike — done: causes 1–3 identified; angle plumbing via `Component.Params.Input[i].VolatileData` → `IGH_GeometricGoo.ReferenceID` → User Text works.
3. Implement — `OrigamiSolver.cs`: per-curve angles (done in spike); Build normalises to the web app's frame (centre = bounding-box centre, scale = 1 / max distance to centre) and merges within max(tol × scale, MERGE_REL); output mesh mapped back to model units. Same changes in `OrigamiKangaroo.cs` (builder shared by copy). Archive previous files as `archive/OrigamiSim_v5.gh` and `archive/OrigamiSim_Kangaroo_v1.gh`; push sources into the components; save. Divergence: internalised curves fall back to 180°; MERGE_REL can merge genuinely distinct points closer than ~0.5 % of the pattern radius (same as the web app).
4. Verify — crane criteria for both solvers with captures; regression patterns before (HEAD source) vs after; `git diff` shows `OrigamiPrint.cs` untouched.
5. Docs — `grasshopper/README.md`: `TARGET_DEG` = default; User Text; normalisation + merge distance; limits. `grasshopper/examples/README.md`: drop the "fold to 180°" limitation.

### Execute at
low — mechanical edits and MCP runs.

### Execution checklist
- [x] 1 Reference + repro (web app crane; repro dynamic solver)
- [x] 2 Spike (three causes found; angle plumbing works)
- [x] 3a Implement dynamic solver (angles, normalisation, merge tolerance; Info gains `meanThetaErrDeg`, signed `targets=`)
- [x] 3b Implement Kangaroo builder (angles, merge tolerance; stays in model units — goals already scale with edge length)
- [x] 3c Archive (`archive/OrigamiSim_v5.gh`, `archive/OrigamiSim_Kangaroo_v1.gh`) and save both .gh files (`GH_DocumentIO.SaveQuiet`)
- [x] 4 Verify — results 2026-10-03, saved files, no injection, crane at 100 units / tol 0.01:
  - Dynamic: 60 nodes / 104 faces, `targets=-180x50,-135x4,-45x10,45x8,90x2,135x4,180x33`, mvSenseOk 111/111, finite, settled (meanAbsV 7e-5), maxErr 86.63° (web 87.1°), meanErr 10.88° (web 10.9°). PASS. Captures: `captures/crane-fixed-dynamic-fold100.png`, `crane-before-dynamic-fold100.png`, `webapp-crane-fold100.jpg`.
  - Kangaroo: same topology and targets, but mvSenseOk 84/111, maxErr 226–266°, facets bent to 162°. FAIL — fourth cause: quasi-static minimisation picks a different branch from flat (Facet Strength 30 → 71/111; 5× slower ramp → identical). User chose K1: ship shared fixes, document, spin off. Capture: `captures/crane-kangaroo-fold100.png`.
  - Regression (Miura demo, Simple Vertex, Map Fold, Square Twist, Miura-ori, Waterbomb Tessellation; Fold 0.5 and 1): dynamic — same topology, all finite, mvSenseOk all, strain equal or lower everywhere; Kangaroo — identical to before. PASS.
  - `OrigamiPrint.cs` untouched (git diff). PASS.
- [x] 5 Docs (`grasshopper/README.md`: partial folds, scale, MERGE_REL, Info fields, Kangaroo crane limitation, troubleshooting; comparison-table caveat. `examples/README.md`: limitation replaced)
