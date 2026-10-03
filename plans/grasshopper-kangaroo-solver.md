# grasshopper-kangaroo-solver

### Intent
Build a second Grasshopper definition, `grasshopper/OrigamiSim_Kangaroo.gh`, that folds the same layer-driven crease patterns as `grasshopper/OrigamiSim.gh` but uses Kangaroo2 for the physics instead of the hand-ported explicit-Euler solver.
- **Inputs:** the same as v1: `Border`/`Mountain`/`Valley`/`Facet`/`Cut`/`Hinge` layers, `Anchors`, Fold, Reset.
- **Pipeline:** a legacy C# builder reuses v1's pattern builder and emits geometry plus custom `OrigamiCrease` goals. Native Kangaroo2 components (Length(Line), Anchor, Show, Solver) do the rest. A C# Readout independently measures strain and fold angles.
- **Build:** headless via the Rhino MCP, as in v1. Nothing is committed to git.
- **Out of scope:** the 3D Print group.

Decisions taken (do not relitigate):
- plan high, execute medium;
- C# builder → native K2 goals → native Solver;
- new file, not a group in v1;
- judged by v1's fold tests;
- **custom crease goal (option B)**, because the stock Hinge fails near 180° (probe below);
- assumptions 1–5 from Phase 1: same layers and builder, headless MCP build, edges = Length, M/V/F = crease goal, Hinge layer = no goal, Print excluded, StrainColour reused.

### Criteria & scoring axes
- **C1 Folds correctly.** Tests:
  - single-crease demo (DEMO 1) at Fold = 1 settles with |θ − θ_target| ≤ 2°;
  - at Fold = 0, within 2° of flat;
  - Miura 4×4 (DEMO 2) at Fold = 1: no NaN, mean strain < 5 %, every M/V crease has the sign of its target;
  - θ comes from the Readout (v1 formula on the output mesh), not from the goals.
- **C2 Interactive.** Tests:
  - Miura 12×12 (DEMO 3) settles in < 1 s after a Fold change from 0 to 0.8. Settled = Readout max M/V θ error ≤ 0.5° and mean strain < 0.5 %. Stopwatch over the harness solve loop;
  - Reset → mesh flat (max |z − z_flat| < 1e-6).
- **C3 Reproducible setup.** Tests:
  - `OrigamiSim_Kangaroo.gh` reopens in a fresh slot with 0 Error-level messages;
  - the same layer filters as v1;
  - the README has a "Kangaroo version" section (inputs, strengths, how to compare with v1).
- **C4 v1 vs v2 comparison (informational).** README table per demo (1, 2, 3) at Fold 0.5 and 1:
  - D = max over vertex pairs |d_ij(v1) − d_ij(v2)|, alignment-free; the vertex order is identical because Build() is shared;
  - settle time of each.

Axes: goal-likelihood, correctness, interactivity, executability.

### Convergence
Converged at loop 6. Final scores: goal 9 · correctness 9 · interactivity 9 · executability 9.

---

### Probe results (verified 2026-10-03, slot `aardvark`, Rhino 8.34, Kangaroo 2.5.3)
- **Files:**
  - `C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\KangarooSolver.dll` (v2.5.3.0);
  - `Kangaroo2Component.gha`, loaded into the AppDomain once Grasshopper starts.
- **The K2 "Length" goal class is `KangarooSolver.Goals.Spring`.** Ctors:
  - `Spring(int s, int e, double l, double k)`;
  - `Spring(Point3d s, Point3d e, double l, double k)`.
  - `Hinge(int|Point3d P0..P3, double RA, double k)`: P0, P1 are the hinge edge and P2, P3 the wing tips.
  - `Anchor(Point3d P, double k)`.
- **The stock Hinge fails near flat-folded.**

  | Change | Result |
  |---|---|
  | Jump from flat to ≤ 150° | Exact (~14 ms per 1,000 momentum `Step`s, 4 particles) |
  | Jump from flat to ≥ 155° | Wrong rest or flip, ~16 s per 1,000 steps |
  | Ramp 140→160°, 150→165° | Exact |
  | Ramp 150→178°, 170→180° | Fails |

  Cause: the angle wraps at ±180° and momentum overshoots past it. **This is why the crease is a custom goal.**
- **Particle matching:** `PhysicalSystem.AssignPIndex` matches goal `PPos` to particle **start** positions (`FindParticleIndex(Pos, tol, ByCurrent)`). A goal rebuilt from flat positions maps onto the moved particle, so a Fold change continues from the current state and only Reset rebuilds.
- **`KangarooSolver.GoalObject`** (abstract):
  - properties `PPos : Point3d[]`, `PIndex : int[]`, `Move : Vector3d[]`, `Weighting : double[]`, `Torque`, `TorqueWeighting`, `InitialOrientation`, `Name`;
  - virtual `Calculate(List<KangarooSolver.Particle>)`, `Output(List<Particle>) : object`, `Clone()`;
  - `Particle.Position` is the current position.
- **Legacy C# component** (`ScriptComponents.Component_CSNET_Script`): `ScriptSource` has a public field **`List<string> References`** (assembly paths). Also `Menu_DestroyAssemblyCaches` and `<ScriptAssembly>k__BackingField` (clear it to recompile).
- **`#r` does not work in `_-ScriptEditor _Run` / `run_csharp` scripts.** Both `#r "C:\…"` and `#r "C:/…"` gave CS0246. Harness scripts reach K2 types by reflection: `Assembly.LoadFrom` + `Activator.CreateInstance` + `dynamic`, and `List<IGoal>` via `typeof(List<>).MakeGenericType(...)`.
- **Component Guids** (Kangaroo2 category, from `g1_search_components`):

  | Component | Guid | Ports |
  |---|---|---|
  | Solver | `313490f5-8e38-4dde-9e9a-05e4d739b35d` | in GoalObjects (tree), Reset, Threshold, Tolerance, On; out I, V, O |
  | Length(Line) | `091bae84-8fa9-4b35-8aad-b25b859055f6` | in Line, Length (optional), Strength; out S |
  | Anchor | `3c30b1a1-4473-4ad4-a700-ea9770726c03` | in P, T, Strength; out A |
  | BouncySolver | `0febdb68-…` | fallback only |

  Show: find it with `g1_search_components("Show", category "Kangaroo2")` in chunk 3.
- **Testing loop that works:** write a `.cs` file in the scratchpad, `run_command "_-ScriptEditor _Run \"<path>\""` (the slot needs `g1_start` first for GH types), and write the results to a `.txt` the script creates. A failed long run times out the MCP call at 300 s but keeps running; poll for the `.txt`.
- **All v1 toolchain facts still apply:** `plans/grasshopper-origami-solver.md` "Toolchain probe results" and "Execution notes", and `plans/grasshopper-print-export.md` "Execution notes". In particular:
  - `dynamic` ScriptSource;
  - clear `<ScriptAssembly>k__BackingField` before recompiling;
  - `g1_connect_many` by instance Guid;
  - outputs go to plain params;
  - `GH_DocumentIO(doc).SaveQuiet(path)`;
  - a legacy C# component needs an explicit `using System.Linq;`;
  - edge-keyed dictionaries copied from `OrigamiSolver.cs` use `(lo << 32) | hi` keys, whose default `long.GetHashCode()` is `lo ^ hi`. That is a performance issue (collisions), not a correctness one. The builder passes `new LongHash()` (copy the class from `OrigamiPrint.cs:1445`) to every such Dictionary/HashSet.
- **MCP server:** `mcp__rhino__*` (the router now serves slots and `g1_*`). Always pass `slot`.

### Crease goal spec (`OrigamiCrease : KangarooSolver.GoalObject`)
```
ctor(Point3d e3, Point3d e4, Point3d w1, Point3d w2, double target, int side, double k)   // side: +1 valley side, -1 mountain side, 0 flat
  PPos = {e3, e4, w1, w2}            // e3 = node3 = lo, e4 = node4 = hi, w1 = third vertex of face f1 (CCW lo->hi), w2 = third of f2
  Move = new Vector3d[4]; Weighting = {k, k, k, k}; Target = target (radians)
Calculate(List<Particle> p):
  x3,x4,x1,x2 = p[PIndex[0..3]].Position
  a = x4 - x3; L = |a|; if L < 1e-12 -> all Move = 0, return; c = a / L
  n1 = (x4 - x3) x (x1 - x3); n2 = (x3 - x4) x (x2 - x4); unitize both (if either < 1e-12 -> zero moves)
  theta = atan2( (n1 x c) . n2 , n1 . n2 )            // v1 thetaCalcShader convention
  // (final, after execution notes 2-3)
  Side > 0: if theta <= -PI/2: theta += 2PI     Side < 0: if theta >= PI/2: theta -= 2PI
  Side = 0: d wrapped to [-PI, PI]; else d = Target - theta
  d = clamp(d, -2*MAXROT, 2*MAXROT)                    // MAXROT = 15 deg
  h1, h2 = moment arms of x1, x2 from the crease line; c1 = ((x1-x3).c)/L, c2 = ((x2-x3).c)/L
  g1 = n1/h1, g2 = n2/h2, g3 = -[(1-c1) g1 + (1-c2) g2], g4 = -[c1 g1 + c2 g2]
  s = d / (|g1|^2 + |g2|^2 + |g3|^2 + |g4|^2);  Move = {g3 s, g4 s, g1 s, g2 s}
Output -> null
```
The sense was verified correct as written (Miura 264/264, single crease 1/1).
Weights: M/V `k = CreaseK · L0 / L_mean`; F and triangulation diagonals `k = FacetK · L0 / L_mean`; hinge-layer and border-inside edges get no crease goal (as in v1). Target = (M ? −1 : +1) · TARGET_DEG · Fold in radians (TARGET_DEG = 180); F target = 0.

### Builder spec (`grasshopper/OrigamiKangaroo.cs`, same USING / SCRIPT / ADDITIONAL layout as `OrigamiSolver.cs`)
- **Inputs:**

  | Input | Type |
  |---|---|
  | M, V, B, F, C, H | `List<Curve>`, `GH_CurveHint`, list, optional |
  | Anchors | `List<Point3d>`, optional |
  | Fold | double |
  | CreaseK | double |
  | FacetK | double |

  Reset is not a builder input; it goes straight to the Solver.
- **Outputs:**

  | Output | Content |
  |---|---|
  | Edges | `List<Line>`: every mesh edge, flat positions |
  | Creases | `List<object>`: OrigamiCrease goals |
  | Flat | Mesh: flat triangle mesh, vertices in v1 node order |
  | AnchorPts | `List<Point3d>`: anchors snapped to node positions |
  | Quads | `List<int>`: 4 per crease: lo, hi, w1, w2 as Flat vertex indices |
  | Targets | `List<double>`: radians per crease |
  | Types | `List<int>`: 1 = M/V, 0 = F |
  | Tol | double = 0.01 × doc tolerance |
  | Thr | double = 1e-12 × bbox diagonal |
  | Info | string: counts, v1 notes |

- **Body:** copy v1's `ToLines`, `AddPolyline`, `Build` steps 1–8 (stop before the SimState physics arrays), `Demo`, `MiuraPt`, helpers, `Hash`, `Mix*`. Constants: `TARGET_DEG = 180`, `MAXROT_DEG = 15`, `DEMO_WHEN_EMPTY = 2`.
- **Cut copies:** each copy is offset 0.1 × doc tolerance toward the centroid of its own fan triangles, so K2 does not merge it back. Note `cutOffset` in Info.
- **Caching:** cache the topology by `Hash` in a static dictionary keyed by `Component.InstanceGuid`. A Fold change only rebuilds the goal objects (cheap); matching to particles is by flat position (see probe).
- **References:** `ScriptSource.References.Add(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\KangarooSolver.dll")`. The goal classes live in ADDITIONAL (nested in Script_Instance).

### Readout spec (`grasshopper/OrigamiKangarooReadout.cs`)
- **Inputs:** O (tree, generic), Flat (Mesh), Quads (`List<int>`), Targets (`List<double>`), Types (`List<int>`), I (int).
- **Outputs:** Mesh, Strain (`List<double>`), Info (string).
- **Mesh:** the first `GH_Mesh` in O whose vertex count equals Flat's.
- **Strain:** per vertex, mean over its edges of |L/L0 − 1| × 100, as in v1.
- **θ per crease:** v1 formula with the face normals as in the crease spec.
- **Info keys:** `iterations`, `meanStrain%`, `maxStrain%`, `maxThetaErrDeg` (M/V only), `maxFacetDeg`, `mvSenseOk n/N`, `finite`. (Review fix #3: M/V θ is read in the goal's side window, side = sign(target); see checklist 7.)

### Canvas spec
Start from `grasshopper/OrigamiSim.gh` opened in the slot (`Instances.DocumentServer.AddDocument(path, true)`). v1 on disk is never overwritten.
- **Delete:** the Origami Solver component, the Animate timer, every object in the 3D Print group, and the group itself.
- **Keep:** the 7 layer pipelines, Fold slider, Reset toggle, Mesh / Strain / Info / Strain Mesh params, and the Strain Colours component with its group.
- **Add:**
  - builder "Origami Goals" (legacy C# `a9a8ebd2-fff5-4c44-a8f5-739736d129ba`, `includeDeprecated=true`);
  - Length(Line);
  - Anchor;
  - Show;
  - Solver;
  - sliders: Edge Strength (1–1000, default 100), Crease Strength (0–10, default 1), Facet Strength (0–10, default 1), Anchor Strength (1–10000, default 1000);
  - Boolean toggle "On" (true);
  - "Origami Readout" (legacy C#).
- **Wires:**
  - pipelines → builder M/V/B/F/C/H/Anchors; Fold → builder; Crease Strength → CreaseK; Facet Strength → FacetK;
  - Edges → Length.Line; Edge Strength → Length.Strength;
  - AnchorPts → Anchor.P; Anchor Strength → Anchor.Strength; Flat → Show;
  - Solver.GoalObjects ← Creases, Length.S, Anchor.A, Show output (4 wires); Reset → Solver.Reset; Tol → Solver.Tolerance; Thr → Solver.Threshold; On → Solver.On;
  - Readout ← Solver.O, Solver.I, builder Flat / Quads / Targets / Types;
  - Readout Mesh / Strain / Info → the existing params; the existing Strain Colours wiring is kept.
- **Groups:** Pattern inputs (existing), Kangaroo goals (builder, Length, Anchor, Show, 4 sliders), Kangaroo solver (Solver, On), Readout (Readout + output params), Strain colours (existing).
- **Layout and save:** 0 bounding-box overlaps. Save to `grasshopper/OrigamiSim_Kangaroo.gh`.

### Test harness
`run_csharp`-style `.cs` files run through `_-ScriptEditor _Run`, writing to scratchpad `.txt` files.
- **Settle loop:** `for r < 300: solver.ExpireSolution(false); doc.NewSolution(false); read the Readout Info; stop when settled`. Stopwatch around the loop.
- **Demo switch:** edit `DEMO_WHEN_EMPTY = N` in the builder's AdditionalCode, clear `<ScriptAssembly>k__BackingField`, and expire.
- **Sliders:** `GH_NumberSlider.SetSliderValue((decimal)x)`.

---

### Steps
1. **Spike.** A legacy C# component with a `KangarooSolver.dll` reference and a one-point `GoalObject` subclass feeds the native Solver.
   - Over: writing the builder first, since everything depends on this.
   - Divergence: none. On fail, stop and ask: the Rhino 8 C# component (`b6ba1144-…`) or stock Hinge capped at 150°.
2. **Builder and Readout sources.** v1 Build is reused, plus the custom crease goal.
   - Over: stock Hinge, which fails above 150–165°; and a shared source file, which adds a build step for no gain.
   - **Divergence:** creases are a custom goal, not a stock component. K2 strengths are relative weights, so mid-fold shapes can differ from v1; C4 quantifies this.
3. **Minimal canvas plus the C2 speed check.** Built on a copy of v1's canvas.
   - Over: a blank canvas, which would lose the tuned layer filters.
   - Divergence: no Timer; the K2 Solver animates itself while On.
   - **Tuning order if > 1 s:** Crease and Facet ×3, then MAXROT 30°, then BouncySolver. Stop after each and re-measure.
4. **C1 fold tests, Reset, and the v1 layer test with a Cut slit.**
5. **Groups, overlap check, save, reopen (C3).**
6. **C4 table, README "Kangaroo version" section, canvas and fold captures.**

### Execute at
medium — the formulas, Guids, ports and harness are fixed here, and each chunk has one verification.

### Execution checklist
- [x] 1 · Spike: custom GoalObject in the legacy C# component drives the native Solver. Verified 2026-10-03: `ScriptSource.References.Add(<KangarooSolver.dll>)` plus a `PullGoal : KangarooSolver.GoalObject` in AdditionalCode → 0 errors and 0 warnings. Output A is a `GH_ObjectWrapper` around `Script_Instance+PullGoal`. After 20 × {ExpireSolution(solver); NewSolution}: Solver I = 200 (10 iterations per forced solve) and V = {1,2,3}, i.e. the point reached its target. The Solver's Reset, Threshold, Tolerance and On have usable defaults when unwired. O is empty for a goal whose Output returns null.
- [x] 2 · `OrigamiKangaroo.cs` + `OrigamiKangarooReadout.cs` written, and both compile in the slot with 0 errors. Verified 2026-10-03 in a scratch canvas (harness `scratchpad/klib.cs` + `kscratch_main.cs`), with the builder, Readout, Length(Line), Anchor → Clean Tree, Show and Solver all wired:
  - 0 errors and 0 warnings on the builder, Readout and Solver;
  - the Readout finds the Show mesh in O (O holds GH_Mesh and GH_Line);
  - Miura 4×4: 25 nodes, 56 edges, 32 faces, 40 creases (24 M/V).
  - Three spec changes were forced during the run; see notes 1–3 below.
- [x] 3 · Canvas built on a copy of v1 in slot `aardvark`.
  - **Removed:** Origami Solver, Animate, the Solver group and the 3D Print group (7 objects).
  - **Placed** with `g1_place_component` / `g1_place_slider`: builder `75eef7fd…`, Readout `0019caa7…`, Length(Line), Anchor, Clean Tree, Show, Solver `587a56ba…`, On toggle, and sliders Crease / Facet / Edge / Anchor Strength.
  - **Configured** by script (`Configure`, legacy `out` params removed, script previews hidden). **Wired** with `g1_connect`, except the 4 GoalObjects sources (see the toolchain note below).
  - **Groups:** Kangaroo goals, Kangaroo solver, Readout (plus v1's Pattern inputs, Controls, Output, Strain colours). 0 overlaps, 33 objects.
  - **C2 speed,** Miura 12×12 0→0.8:
    - defaults 1/1: 364 ms to 1e-4, 1,073 ms to 1e-6, so **> 1 s**;
    - per the tuning order, **Crease = Facet = 3 are now the defaults**: 410 / 478 ms to 1e-6 (2 runs), mean strain 0.22 %;
    - 0.8→1 takes 482 ms; 1→0 takes 3,072 ms and comes back flat.
  - Toolchain: this `mcp__rhino` server's **`g1_apply_graph` and `g1_connect_many` fail** with an opaque "An error occurred invoking …", even for a single wire. `g1_place_component`, `g1_place_slider` and `g1_connect` work. **`g1_connect` replaces existing sources**, so multi-source inputs are wired by script with `AddSource`.
- [x] 4 · C1 on the real canvas (Crease = Facet = 3):
  - DEMO 1: Fold 1 error 0.00°, Fold 0 error 0.00°;
  - DEMO 2: Fold 1 sense 24/24, strain 0.0000 %, finite;
  - Reset held: bbox z 0, 0 iterations.
  - **Layer test** (Border 2×2, Cut (0,1)-(1,1), Valley (1,0)-(1,1), Mountain (1,1)-(1,2), Facet x = 1.5, Hinge (1,1)-(2,1), line + text on Labels):
    - pipelines 1/1/1/1/1/1, Labels ignored;
    - 13 nodes, 24 edges, 12 faces, 10 creases (2 M/V), `cutVertexCopies:1`, `freeHinges:2`;
    - the two (0,1) vertices start 0.001 apart (the cut offset) and are **1.999 apart at Fold 0.5**, the flaps folding to ±z;
    - at Fold 1 both flaps lie flat on the right half and meet again (0.001), as expected;
    - 0 errors. The geometry was removed afterwards.
  - A first layer design (slit inside one rigid band) correctly gave no separation; it is kept as a note, not a failure.
- [x] 5 · Saved `grasshopper/OrigamiSim_Kangaroo.gh` (21,743 bytes) with `SaveQuiet`; v1's `OrigamiSim.gh` mtime unchanged (2026-10-03T00:03:36).
  - **Reopened in fresh slot `armadillo`:** 33 objects; 7 groups (Pattern inputs 7, Controls 2, Output 4, Strain colours 1, Kangaroo goals 9, Kangaroo solver 2, Readout 1); **0 errors** (only v1's known CS1701 warning on Strain Colours).
  - **Persisted:** sliders Fold 0.6, Crease 3, Facet 3, Edge 100, Anchor 1000; Reset false, On true; all 7 layer filters; the KangarooSolver.dll reference; GoalObjects with 4 sources.
  - **Anchor test:** a point on an `Anchors` layer at (0,0,0) → `anchors=1`. That Miura node stayed within 1e-7 of the origin while Fold 1 reached 0.00° and 24/24.
  - Re-saved afterwards with the Clean Tree preview hidden; only the Mesh param previews.
- [x] 6 · C4 table (README "Kangaroo version"):
  - D = 0 for the single crease and all Fold 1 cases (12×12: 0.0007);
  - Miura mid-fold D = 7.4 % (4×4) and 7.1 % (12×12) of the diagonal;
  - v1 mean strain 1.96 % / 2.73 % vs v2 0.37 % / 0.54 %;
  - v2 settles 4–12× faster.
  - Captures: `grasshopper/captures/kangaroo-canvas.png`, `kangaroo-vs-v1-miura12-fold50.jpg` (strain-coloured, Grasshopper preview disabled during capture).
  - README: Files table, "Kangaroo version" section, "How it was built" line.
- [x] 7 · Review fixes (code-review Spec #1, #3, #8, #9, #10; record `plans/review-fixes.md`). Verified 2026-10-03, main slot `aardvark`, reopen slot `armadillo`:
  - **#1:** builder Info adds `cutOffset:` when a cut makes copies. Cut-layer test (doc tol 0.01, Fold 0.5): `nodes=13`, `cutVertexCopies:1`, `cutOffset:0.001`; the (0,1) copies are 0.001 apart flat and 1.9985 apart folded; 2/2 sense, settled.
  - **#3:** the Readout reads M/V θ in the goal's side window, with the side = sign(target) (equal to the goal's `Side` whenever target ≠ 0), so no `Sides` param was added. Sense OK iff target = 0 or 0 < side·θu ≤ 185°. Demos still pass: 1 @ Fold 1 → 1/1, err 0.00°; 2 @ Fold 1 → 24/24; 3 @ Fold 0.8 → 264/264. Hand-made demo-1 meshes, new vs old Readout:

    | θ | Target | New | Old |
    |---|---|---|---|
    | −30° | +180° | 0/1 | 1/1 |
    | +30° | +180° | 1/1 | 1/1 |
    | −170° (= 190°) | +180° | 0/1 | 1/1 |
    | +170° | +180° | 1/1 | 1/1 |
    | −120° | +90° | 0/1 | 1/1 |
    | +120° | +90° | 1/1 | 1/1 |
  - **#8, #9, #10:** Execution note 5 defaults line; builder header (side windows); `examples/README.md` layer table and pipeline usage.
  - Re-saved `OrigamiSim_Kangaroo.gh` (22,080 bytes) with DEMO 2 and Fold 0.6. Reopened: 0 errors (CS1701 only), 7 groups, sliders 0.6/3/3/100/1000, injected sources equal the `.cs` sections, O wired. v1 `OrigamiSim.gh` unmodified.

### Execution notes (new findings)
1. **The Anchor component outputs a null goal when it has no points**, and the Solver throws "Offset and length were out of bounds".
   - Worse, the crash leaves the shared goal instances half-indexed, so every other Solver fed the same goals then fails with "Index was out of range".
   - **Canvas change:** Anchor.A → native **Clean Tree** (`071c3940-a12d-4b77-bb23-42b5d3314a0d`; defaults N = true, X = true, E = false) → Solver.GoalObjects.
2. **Exact wing rotation was replaced with v1's crease-force directions** (Gauss-Newton step `Δx_i = d·g_i / Σ|g_j|²`, g = ∇θ from `velocityCalcShader`: node1 n1/h1, node2 n2/h2, node3 −[(1−c1)n1/h1 + (1−c2)n2/h2], node4 −[c1 n1/h1 + c2 n2/h2]).
   - Why: the rotation moves summed to zero force but not zero torque. At mid-fold (Miura 0.5, where targets conflict) the sheet spun rigidly forever: 3.1 units moved per solve on a 4-unit sheet, never settling.
   - With gradient moves: Miura 4×4 0→0.5 settles in 340 ms.
3. **The stateless wrap was replaced with side windows.** Each M/V crease reads θ in a window on its own side: valley (−90°, 270°], mountain [−270°, 90°); flat creases keep the [−180°, 180°] wrap. The side = kind sign × the sign of the last nonzero Fold, stored per component in the builder's `Topo.FoldSign` (not in the goals, which the Solver may clone).
   - Why: unfolding Miura 12×12 from Fold 1 to 0 got stuck at θ error 134° and facets 98°. Creases that overshot ±180° read the wrong side and opened through the other face.
   - After the fix: 1→0 returns flat (0.01° error, facets 0.00°).
4. **C2's "settled" was redefined as stillness:** the max vertex move during one Solver solve < 1e-6 × the sheet diagonal (time to 1e-4 is reported too).
   - Why: the θ-error definition can't be met mid-fold. Miura creases can't all sit at ±(Fold × 180°) at once, so at equilibrium Fold 0.8 the error is 27° (Fold 0.5: 59°). v1 has the same property by construction.
   - C1 still uses θ error, at Fold 0 and 1, where every target is reachable.
5. **Speed (scratch canvas, defaults Edge 100, Crease 1, Facet 1).** Times are to 1e-4 / 1e-6 × diagonal.

   | Case | Time | Result |
   |---|---|---|
   | Miura 12×12 0→0.8 | 345 / **944 ms** | C2 passes, barely |
   | 0.8→1 | 237 / 703 ms | |
   | 1→0 | 675 / 3,429 ms | |
   | 0→−1 | 372 / 699 ms | |
   | −1→0 | 729 / 3,711 ms | |
   | Single crease 0→1 | 70 / 182 ms | |
   | Single crease 1→0 | 33 / 141 ms | |
   | Crease = Facet = 3 | 470 ms | mean strain 0.22 % |
   | Crease = Facet = 10 | 570 ms | mean strain 0.73 % |

   Defaults were later raised to 3: see checklist 3 (the canvas run at 1/1 took 1,073 ms).
6. A free sheet that unfolds back to Fold 0 is flat (θ ≤ 0.01°) but may sit **rotated in space** (bbox z 0.14–0.24 on Miura 12×12). Nothing pins it; v1 behaves the same way. Reset restores the original placement.
7. Reset held true keeps the sheet flat at its start position (bbox z = 0, iterations 0). Releasing it at Fold 1 refolds Miura 12×12 in 679 ms.
