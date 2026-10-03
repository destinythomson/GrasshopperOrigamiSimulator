# grasshopper-origami-solver

### Intent
Port OrigamiSimulator's dynamic solver (nodes with mass 1, beams on every edge, angular springs on creases driven to `targetAngle × fold`, face-corner-angle springs, explicit Euler with auto `dt`, N sub-steps per frame, strain colouring) into one Grasshopper (Rhino 8) C# script component, and deliver a ready `grasshopper/OrigamiSim.gh`. Inputs are three lists of Rhino lines (mountain / valley / boundary-or-flat) plus a fold slider, reset toggle and optional anchor points; output is the folding mesh, per-node strain and an info string. The canvas is built **headlessly** in a spawned Rhino slot through the McNeel Rhino MCP (`g1_*` tools for placement/wiring; `run_csharp` through the same MCP for the four things `g1_*` cannot do: script code + params, groups, overlap check, save). Nothing is clicked in the user's own Grasshopper window. Excluded: curved folding, static/rigid solvers, Verlet, SVG import, VR, video.

Decisions taken (do not relitigate): port the repo solver, not Kangaroo2 · C# · 6-input component, constants in code · headless fully automated · 4 canvas groups · no `claude-in-rhino` conventions merge · plan high, execute medium.

### Criteria & scoring axes
- Folds correctly — test: built-in single-crease demo reaches |θ − θ_target| ≤ 2° at Fold = 1 and ≤ 2° from flat at Fold = 0; built-in Miura 4×4 demo runs 20 solves (2000 steps) with no NaN, mean strain < 5 %, every crease has the right M/V sense (sign of θ matches sign of target).
- Source-faithful — test: every force term, constant and the step order in the `.cs` carries a `js/` file:line or shader-name comment; defaults equal the repo's (axial 20, crease 0.7, facet 0.7, face 0.2, damping 0.45, 100 steps, mass 1). Ticked in this record before chunk 1 closes.
- Interactive — test: one solve (100 steps) on the Miura 12×12 demo (~340 nodes) completes in < 1 s (measure with `Stopwatch` inside `Info`); auto-animation re-solves at ≥ 10 fps on Miura 4×4; Reset returns to flat.
- Reproducible setup — test: `OrigamiSim.gh` saved from the slot reopens in a fresh slot with zero Error-level messages in `g1_get_canvas_graph`; `grasshopper/README.md` tells the user how to reference their own lines without editing code.
- Executable by a lower model — every chunk below is buildable from this file alone: formulas written out, data layouts fixed, tool calls enumerated with their JSON shapes, one verification per chunk.

Axes: goal-likelihood, fidelity, interactivity, reproducibility, executability.

### Convergence
Converged at loop 5/20. Final scores: goal 9 · fidelity 9 · interactivity 9 · reproducibility 9 · executability 9.
Post-convergence user changes folded in at Phase 4: 6-input variant; headless build; groups + alignment + no overlap. These moved code/params/groups/save from "user pastes" to `run_csharp` via the MCP (the user's explicit choice when told `g1_*` cannot do them).

---

### Environment facts (verified 2026-10-02)
- Rhino 8.34.26223.11001 at `C:\Program Files\Rhino 8`. Kangaroo2 and ScriptComponents.gha present. No .NET SDK, no numpy on this machine → the `.cs` cannot be compiled locally; first compile happens in Grasshopper.
- Rhino-MCP-Platform **0.3.0** installed (`%APPDATA%\McNeel\Rhinoceros\packages\8.0\Rhino-MCP-Platform\0.3.0`); `~/.claude.json` `mcpServers.rhino` still points at the 0.1.5 router. **Prerequisite for chunks 3+:** user runs `MCPConnect` in Rhino, pastes the config, starts a new Claude Code session in this folder; the `rhino` server must show in `session_connectors_status`.
- 0.3.0 tool names (string-scanned from the binaries): `spawn_slot`, `close_slot`, `list_slots`, `g1_start`, `g1_search_components`, `g1_describe_component`, `g1_place_component`, `g1_place_slider`, `g1_apply_graph`, `g1_connect`, `g1_connect_many`, `g1_delete_component`, `g1_solve_graph`, `g1_get_canvas_graph`, `g1_clear_canvas`, `run_csharp`, `run_python`, `run_command`, `get_viewport_image`, `list_objects`, `open_doc`, `save_doc` (Rhino doc, **not** `.gh`), `set_camera`, `ask_user`, `get_context`. Re-list with the real tool schema before relying on argument names (tool list can lag).
- `g1_apply_graph` (upstream source `rhino/plugin/Tools/GH1/GH1_ApplyGraphTool.cs`): `sliders[{Key,Min,Value,Max,Type('float'|'int'),Name?,X,Y}]`, `components[{Key,Selector(Guid preferred),X,Y}]`, `wires[{SrcKey,Src,DstKey,Dst}]`, `solve`, `includeDeprecated`. Wire selectors: component port = index or Name/NickName; pure param (slider/toggle/panel/Line param) = `""` or `"0"`. Returns `{Placed[{Key,Id,Kind}], PlaceErrors[], Wires[{Index,Ok,Error}], WiresOk}` — read per step.
- `run_csharp` runs a C# file via `_-ScriptEditor _Run`; `__rhino_doc__` (RhinoDoc) is injected; **only `Console.WriteLine`/`RhinoApp.WriteLine` output comes back**; `error` is populated only by sniffing "Compile Error" / "error CS" / "Exception:" → a failure that prints none of those reports success. Always print a JSON line and re-query.
- Legacy C# component API (reflected from installed ScriptComponents.gha 8.34): type `ScriptComponents.Component_CSNET_Script`; `ScriptSource.UsingCode / ScriptCode / AdditionalCode` are settable strings; params are `Grasshopper.Kernel.Parameters.Param_ScriptVariable` with settable `TypeHint` (hints: `GH_LineHint`, `GH_Point3dHint`, `GH_DoubleHint_CS`, `GH_BooleanHint_CS`, `GH_IntegerHint_CS`, `GH_StringHint_CS`, `GH_MeshHint`), `Access`, `Optional`; `CreateParameter(GH_ParameterSide, int)`, `Params.RegisterInputParam(p, idx)`, `Params.RegisterOutputParam(p, idx)`, `Params.UnregisterInputParameter(p)`, `VariableParameterMaintenance()`. The legacy component is hidden in Rhino 8 → place with `includeDeprecated=true` or by Guid from `g1_search_components("C# Script", includeDeprecated=true)`.
- Grasshopper API (reflected 8.34): `GH_DocumentIO(GH_Document).SaveQuiet(string path)`; `GH_Group` (`Grasshopper.Kernel.Special`) with `CreateAttributes()`, `AddObject(Guid)`, `NickName`, `Colour`; `IGH_Attributes.Bounds : RectangleF`, `.Pivot : PointF`; `GH_Document.AddObject(obj, false)`, `NewSolution(false)`, `ScheduleSolution(ms, callback)`; `Instances.ActiveCanvas.Document`.

### Toolchain probe results (verified live 2026-10-02 in spawned slot `armadillo`, Rhino 8.34, plugin 0.3.0)
Use these over anything above that disagrees.
- **Server name:** tools are `mcp__Rhino_MCP_Platform__*` (0.3.0). A second server `mcp__rhino__*` (stale 0.1.5 router entry in `~/.claude.json`) is also connected — do not use it; the user may delete that entry.
- **Always pass `slot`.** With no `slot` and no registered Rhino the router auto-spawns one. The user's own Rhino is not adopted (`list_slots` was empty) unless they run `MCPStart` in it.
- `spawn_slot {version:"8"}` → `{slotId, port, pid}`; `g1_start {slot}` → "Opened Grasshopper"; `close_slot {slot}` discards everything.
- `g1_apply_graph` works in the slot (upstream #98 not reproduced). Extra arg `replace` (default true). Per-step result `{placed[{key,id,kind}], placeErrors[], wires[], wiresOk}`.
- **`g1_connect_many` wires are `{SrcId, Src, DstId, Dst}` with instance Guids**, not keys. Pure param selector `""`, component port by name. Result includes `solve.statuses[]` with each component's messages — usable directly as the compile-loop readout.
- `g1_describe_component {name}` describes a **library** component by name; it cannot inspect a placed instance. Verify instances with `g1_get_canvas_graph` (returns inputs/outputs by name, `messages[]`, `sources`, data samples).
- **Panels cannot be wire destinations** via `g1_*` ("destination is a value source"). Outputs go to plain params instead: Mesh → `Mesh` param, Strain → `Number` param, Info → `Text` param.
- **Component Guids:** legacy C# Script (type `ScriptComponents.Component_CSNET_Script`) = `a9a8ebd2-fff5-4c44-a8f5-739736d129ba` (listed obsolete/hidden; place with `includeDeprecated=true`). Do **not** use `88c3f2b5-…` ("DotNET C# Script (LEGACY)", type `ScriptComponents.Legacy.ComponentLegacyCsScript`, reports "No code supplied") or `b6ba1144-…` (Rhino 8 `RhinoCodePluginGH.Components.CSharpComponent`). Params: Line `8529dbdf-9b6f-42e9-8e1f-c7a2bde56a70`, Point `fbac3e32-f100-4292-8692-77240a42fd1a`, Mesh `1e936df3-0eea-4246-8549-514cb8862b7a`, Number `3e8ca6be-fda8-4aaf-b5c0-3c54c8bb7312`, Text `3ede854e-c753-40eb-84cb-b48008f14fd4`, Boolean Toggle `2e78987b-9dfb-42a2-8b76-3923ac8bd91a`.
- **`run_csharp` cannot `using ScriptComponents;`** (assembly not referenced at compile time). Find the component by `obj.GetType().FullName == "ScriptComponents.Component_CSNET_Script"`, cast to `IGH_Component` / `IGH_VariableParameterComponent` for params, and use `dynamic` for `ScriptSource` (`dynamic src = ((dynamic)obj).ScriptSource; src.UsingCode = …; src.ScriptCode = …; src.AdditionalCode = …`). Verified end to end: unregister defaults, `CreateParameter` + `RegisterInputParam` with `Param_ScriptVariable.TypeHint`/`Access`/`Optional`, `VariableParameterMaintenance()`, `Params.OnParametersChanged()`, `ExpireSolution(false)`, `doc.NewSolution(false)` → compiled, ran, `Component.InstanceGuid` and `GrasshopperDocument` available inside the script, a `static class` in AdditionalCode keeps state across solves, output named `Mesh` coexists with `Rhino.Geometry.Mesh` when the type is fully qualified. Duplicate `using Rhino.Geometry;` in UsingCode only warns (CS0105) — omit it.
- `GH_Group` (`CreateAttributes`, `NickName`, `Colour`, `AddObject(guid)`, `doc.AddObject(g,false)`, `ExpireCaches()`) works. `Attributes.Bounds` works after `ExpireLayout()`.
- **Pivot semantics:** slider pivot = top-left (default size 160×20); component pivot = centre (legacy C# with 6 in / 3 out ≈ 119×124; with 2 in / 2 out ≈ 72×44). Use the bounds check, not assumptions.
- `new GH_DocumentIO(doc).SaveQuiet(path)` → true; `Instances.DocumentServer.AddDocument(path, true)` reopens and becomes the active canvas (object count readable).
- **Self-scheduled animation stalls in a background slot:** `GrasshopperDocument.ScheduleSolution(30, …)` advanced the solve counter from 1 to 2 and stopped. **A `GH_Timer` works:** `new GH_Timer(){Interval=100}`, `CreateAttributes()`, `doc.AddObject`, `AddTarget(solverGuid)`, `Locked=false` → counter 2 → 120. Design change: animation = Timer component named "Animate" (saved `Locked=true` so the file opens still; the user unlocks it to animate), not self-scheduling.
- `get_viewport_image` metadata counts **Rhino document objects only**; Grasshopper preview is invisible to it (`visibleObjectCount 0`). For a visual check, bake a copy of the output mesh into the slot's Rhino doc via `run_csharp` (`__rhino_doc__.Objects.AddMesh(((GH_Mesh)goo).Value)`), capture with `boxMin/boxMax`, then delete the baked object.

### Repo facts the port must match
| Fact | Source |
|---|---|
| Node mass = 1 | `js/node.js:205` |
| Beam `k = axialStiffness / L0`, `d = ζ·2·√(k·m_min)`, `ω = √(k/m_min)` | `js/beam.js:63-77` |
| `dt = 0.9 / (2π · ω_max)` over all beams | `js/dynamic/dynamicSolver.js:244-250` |
| Crease `k = (type==0 ? panelStiffness : creaseStiffness) · L`; crease damping **disabled** | `js/crease.js:44-52`; `dynamicSolver.js:490` (commented) and `velocityCalcShader` (`// + creaseMeta[1]*thetas[1]`) |
| Mountain target = −angle, valley = +angle (degrees → radians at build) | `js/pattern.js:75,85`; `js/model.js:271` |
| Crease type 0 (panel/facet, angle 0) vs 1 (M/V, angle ≠ 0) | `js/model.js:265` |
| Crease node roles: node1 = far vertex of face1, node2 = far vertex of face2, node3 = edge.nodes[0], node4 = edge.nodes[1] | `js/crease.js:103-108`; `dynamicSolver.js:614-619` |
| Face normal = normalize((b−a)×(c−a)) | `normalCalc` shader |
| θ = atan2( (n1 × ĉ)·n2 , n1·n2 ), ĉ = normalize(node4 − node3); unwrap: if θ−θ_last < −5 add 2π, if > 5 subtract 2π | `thetaCalcShader` |
| Crease geo: h1 = ⟂ distance of node1 from crease line, h2 likewise node2; c1 = proj(node1−node3)·ĉ / L, c2 likewise; disable crease if L < 1e-6 or h < 1e-6 | `updateCreaseGeo` shader |
| Crease force: `angF = k·(target·fold − θ)`; node1: `+angF/h1 · n1`; node2: `+angF/h2 · n2`; node4: `−angF·(c1/h1·n1 + c2/h2·n2)`; node3: same with c→(1−c) | `velocityCalcShader` crease loop |
| Beam force on node i from neighbour j: `Δ = x_j − x_i`; `F += k·(Δ − Δ̂·L0) + d·(v_j − v_i)` | `velocityCalcShader` beam loop |
| Face force (per triangle a,b,c, per corner, `faceStiffness`): angles via acos of unit edge dots; `Δα = faceStiffness·(α_nominal − α)`; corner a: `F −= Δα_a·(n×âc/|ac| − n×âb/|ab|)`, `F −= Δα_b·(n×âb/|ab|)`, `F += Δα_c·(n×âc/|ac|)`; corner b: `F −= Δα_a·(n×âb/|ab|)`, `F += Δα_b·(n×âb/|ab| + n×b̂c/|bc|)`, `F −= Δα_c·(n×b̂c/|bc|)`; corner c: `F += Δα_a·(n×âc/|ac|)`, `F −= Δα_b·(n×b̂c/|bc|)`, `F += Δα_c·(n×b̂c/|bc| − n×âc/|ac|)`; skip triangle if any edge < 1e-7 | `velocityCalcShader` face loop; `dynamicSolver.js:548-566` (nominal angles) |
| Integrate: fixed nodes get v = 0 and do not move; else `v += F·dt/m`, `x += v·dt` | `velocityCalcShader` head, `positionCalcShader` |
| Step order per sub-step: normals → θ → crease geo → velocities (all nodes, from last positions) → positions | `dynamicSolver.js:131-170` |
| 100 sub-steps per frame | `js/globals.js:90` |
| Strain per node = mean over its beams of |L/L0 − 1| × 100 (%), clip at 5; colour HSL(h = (1 − e/5)·0.7, s = 1, l = 0.5) → blue 0 %, red ≥ 5 % | `velocityCalcShader` nodeError; `dynamicSolver.js:205-215` |
| Slider ranges: fold −1…1 (shown −100…100 %), axial 10–100, face 0–5, crease 0–3, panel 0–3, damping 0.01–0.5 | `js/controls.js:445-470` |
| Triangulation: quads split on the shorter diagonal; larger polygons ear-clipped; new edges = flat (type 0, angle 0) creases | `js/pattern.js:984-1135` |

---

### Steps

1. **Write `grasshopper/OrigamiSolver.cs`** (one file; the whole thing is the component's *AdditionalCode*; a 3-line *ScriptCode* body calls it). *Over:* Kangaroo2 goals (different math) or a compiled `.gha` (no SDK; cannot be injected). *Divergence:* ear clipping may pick different diagonals than the repo's earcut on concave n-gons; fold result unaffected.
2. **Write `grasshopper/README.md`** — what the definition does, the 6 inputs, the constants block, how to reference lines (`Set Multiple Lines` on each Line param, points on Anchors), the demo-when-empty behaviour, troubleshooting (stiffness blow-up, non-manifold pattern). *Over:* comments only. *Why:* criterion 4.
3. **Headless canvas build** — `spawn_slot` → `g1_start` → `g1_search_components` (Guids) → one `g1_apply_graph` with all objects at pinned pivots, **no wires**, `solve=false` → `run_csharp` configures the script component (params + source) → `g1_connect_many` → `g1_solve_graph`. *Over:* a single `run_csharp` that builds everything (faster, but the user asked for the MCP graph tools where they work). *Divergence:* if `g1_apply_graph` returns the #98 "Could not get GH document" error, fall back to `run_csharp` for placement too and say so.
4. **Compile loop** — `g1_get_canvas_graph` → `Messages[]` on the script component → fix `.cs` → re-inject → re-solve until no Error-level messages. *Why:* the first compile is in Grasshopper.
5. **Groups, alignment check, save** — `run_csharp`: create 4 `GH_Group`s, read every object's `Attributes.Bounds`, assert no pairwise intersection (nudge by +40 px in Y and re-check if any), `GH_DocumentIO.SaveQuiet(<repo>/grasshopper/OrigamiSim.gh)`; verify the file exists and size > 0 from this side. *Over:* `save_doc` (saves the Rhino document, not the canvas).
6. **Verify criteria 1–3 in the slot** — numeric, via `Info` output read from `g1_get_canvas_graph` (sample size 1) or `run_csharp` reading the output param's volatile data. Single-crease test by temporarily switching `DEMO_WHEN_EMPTY` to 1 through source replace + re-solve, then restoring to 2.
7. **Reopen check** — `close_slot`, fresh `spawn_slot`, `run_csharp`: `Instances.DocumentServer.AddDocument(path, true)`; `g1_get_canvas_graph` shows zero Error messages (criterion 4). Optional, **only with user's OK**: open the `.gh` in the user's visible Rhino for a screenshot (`run_csharp` without `slot`, same `AddDocument` call) — this touches their window.

### Execute at
medium — every formula, layout coordinate and tool call is pinned below; only the compile loop and the first `run_csharp` param-configuration need judgement. Escalate if `Component_CSNET_Script` refuses the programmatic source (then switch to building the whole canvas via `run_csharp` with the same code, or to the Rhino 8 script component via computer-use paste, and record which).

---

### Chunk specs (Phase 5 builds from these)

#### Chunk 1 — `grasshopper/OrigamiSolver.cs`

File layout (plain C#, no namespace, so it drops into the legacy component's AdditionalCode as-is). Mark the three regions with comments `// ===== USING =====`, `// ===== SCRIPT (RunScript body) =====`, `// ===== ADDITIONAL =====`; chunk 3 splits the file on those markers.

**USING region**
```
using System; using System.Collections.Generic; using System.Linq; using System.Diagnostics;
using Rhino; using Rhino.Geometry; using Rhino.Geometry.Intersect;
using Grasshopper; using Grasshopper.Kernel;
```
(The legacy component already provides most of these; `UsingCode` is appended, duplicates are harmless.)

**SCRIPT region** (becomes `ScriptSource.ScriptCode`; the component generates `RunScript(List<Line> M, List<Line> V, List<Line> B, double Fold, bool Reset, List<Point3d> Anchors, ref object Mesh, ref object Strain, ref object Info)` from the params configured in chunk 3):
```
var r = OrigamiSim.Run(Component.InstanceGuid, GrasshopperDocument, Component, M, V, B, Fold, Reset, Anchors, RhinoDocument.ModelAbsoluteTolerance);
Mesh = r.Mesh; Strain = r.Strain; Info = r.Info;
```

**ADDITIONAL region** — classes and constants:

```
static class OrigamiSim {
  // ---- constants (repo defaults; sources in plan table) ----
  const double AXIAL = 20, CREASE = 0.7, FACET = 0.7, FACE = 0.2, DAMP = 0.45;
  const int STEPS = 100;                 // globals.js:90
  const double TARGET_DEG = 180;         // M → −180°, V → +180°  (pattern.js:75,85)
  const int DEMO_WHEN_EMPTY = 2;         // 0 none, 1 single valley, 2 Miura 4x4, 3 Miura 12x12 (used only when M,V,B all empty)
  const double STRAIN_CLIP = 5.0;        // % (globals.js:60)
  // Animation is driven by the canvas Timer ("Animate", chunk 3), not by ScheduleSolution (stalls in background slots — probe 2026-10-02).
  static readonly Dictionary<Guid, SimState> States = new Dictionary<Guid, SimState>();

  public static Result Run(Guid id, GH_Document doc, IGH_Component comp, List<Line> M, List<Line> V, List<Line> B,
                           double fold, bool reset, List<Point3d> anchors, double tol)
  // 1. key = hash of (M,V,B endpoints rounded to tol, anchors, DEMO_WHEN_EMPTY); if no state, key changed, or reset → Build().
  // 2. state.Fold = clamp(fold, -1, 1). If fold changed → state.Frames = 0.
  // 3. Stopwatch; for s in 0..STEPS: Step(state).  state.Frames++.
  // 4. Mesh = BuildMesh(state) (positions = orig + disp; vertex colours from strain).
  // 5. Info = $"nodes={N} beams={E} creases={C} faces={F} dt={dt:E3} frame={Frames} msPerSolve={ms} meanStrain%={..:F3} maxThetaErrDeg={..:F2} meanAbsV={..:E2} demo={demoUsed}".
  //    (no self-scheduling; the Timer re-expires the component)
}
class SimState {  // Structure-of-arrays, all indices int
  Point3d[] Orig; Vector3d[] Disp, Vel; bool[] Fixed;
  int[] BeamA, BeamB; double[] BeamL0, BeamK, BeamD;
  int[] FaceA, FaceB, FaceC; double[] NomAngA, NomAngB, NomAngC; Vector3d[] Normal;
  // creases: parallel arrays
  int[] CrN1, CrN2, CrN3, CrN4, CrF1, CrF2; double[] CrK, CrTarget, CrTheta, CrLastTheta, CrH1, CrH2, CrC1, CrC2; bool[] CrOn;
  List<int>[] NodeBeams, NodeFaces;              // adjacency for beam/face loops
  List<(int crease, int role)>[] NodeCreases;    // role 1..4
  double Dt, Fold; int Frames; double[] StrainPct; string Key; int Demo;
}
```
`USING` region: only `using System.Diagnostics;` (everything else is already provided by the legacy component; a duplicate `using Rhino.Geometry;` produces warning CS0105).

**Build(lines M, V, B, anchors, tol) → SimState** (pattern → mesh; replaces pattern.js cleanup + triangulation):
1. Tag lines: M → kind 'M', V → 'V', B → 'B'. If all three lists are empty → `Demo(DEMO_WHEN_EMPTY)` supplies the lists (see demos).
2. **Split at intersections:** for every pair (i<j) `Intersection.LineLine(li, lj, out a, out b, tol, true)`; if hit and parameter strictly inside both (tol/len < a < 1−tol/len), record split params per line. Cut each line at its sorted params → segments inherit the kind. Drop segments shorter than tol.
3. **Merge vertices:** quantise endpoints to a grid of cell = tol (`Math.Round(x/tol)`); dictionary cell → vertex index; also check the 26 neighbouring cells to merge points straddling a cell edge. Vertex position = first point seen. Build edge list (u,v,kind) with u<v; drop u==v; dedupe (u,v) keeping priority M/V over B.
4. **Planar face walk** (all input is planar, use the plane of the first three non-collinear vertices; work in 2D (u,v) coordinates in that plane):
   - For each vertex sort outgoing half-edges by angle `atan2`.
   - Half-edge h = (u→v). `next(h)`: at v, take the outgoing half-edge that is the **previous** one in CCW order relative to (v→u) (i.e. turn as far right as possible… use the standard "next = rotate clockwise from the reverse edge"). Walk until back at start → one face loop. Mark visited.
   - Compute signed area; drop the one loop with negative (outer) area; drop loops with |area| < tol².
   - Result: polygon faces as vertex index lists (CCW).
5. **Triangulate:** 3-gon as is; 4-gon: split on the shorter diagonal (pattern.js rule); n>4: ear clipping in 2D (standard: repeatedly remove a convex vertex whose triangle contains no other polygon vertex). Every new diagonal → edge of kind 'F'.
6. **Beams:** one per unique edge (input edges + diagonals). `L0 = |orig_v − orig_u|`, `k = AXIAL / L0`, `d = DAMP·2·√(k·1)`.
7. **Edge→faces map.** Interior edges (exactly 2 faces) become creases: kind M → target = −TARGET_DEG·π/180, type 1, `k = CREASE·L0`; V → +TARGET_DEG·π/180, type 1; B or F → target 0, `k = FACET·L0`. Boundary edges (1 face) → no crease. `node3 = u, node4 = v` (the edge's stored order), `node1` = the third vertex of face1, `node2` = third vertex of face2, `F1 = face1`, `F2 = face2`. Which face is "1" is arbitrary **but the sign convention must be consistent**: order faces so that face1 is on the left of u→v in the pattern plane (cross((v−u),(n1−u)).z > 0). Then θ > 0 is valley (paper folds toward +Z normal side), matching the web app where valley = positive.
8. **Nominal angles** per face: `ab = (b−a)̂, ac = (c−a)̂, bc = (c−b)̂; angA = acos(ab·ac), angB = acos(−ab·bc), angC = acos(ac·bc)`; warn in Info if |sum − π| > 0.1.
9. **Fixed:** vertex fixed iff within tol of any anchor point. (No anchors → none fixed, like the web app.)
10. `Dt = 0.9 / (2π · max_beams √(k/1))`. Disp = Vel = 0. CrLastTheta = 0; CrOn = true.

**Step(state)** — exactly the shader order:
```
a) normals: for each face f: n = normalize((P[b]−P[a])×(P[c]−P[a]))            // P = Orig+Disp
b) theta:   for each crease c with CrOn: n1=Normal[F1], n2=Normal[F2]; ĉ = normalize(P[n4]−P[n3]);
            x = clamp(n1·n2,−1,1); y = (n1×ĉ)·n2; th = atan2(y,x); d = th − CrLastTheta;
            if d<−5 d+=2π else if d>5 d−=2π; CrTheta = CrLastTheta + d;
c) geo:     cv = P[n4]−P[n3]; L=|cv|; if L<1e-6 → CrOn=false, continue; ĉ=cv/L;
            v1=P[n1]−P[n3]; v2=P[n2]−P[n3]; p1=ĉ·v1; p2=ĉ·v2;
            h1=√|v1·v1−p1²|; h2=√|v2·v2−p2²|; if h1<1e-6||h2<1e-6 → CrOn=false, continue;
            CrH1=h1; CrH2=h2; CrC1=p1/L; CrC2=p2/L;
d) forces & velocity, for each node i (read Disp/Vel of the *previous* step for every node — compute all forces into F[] first, then update Vel):
            if Fixed[i] { Vel[i]=0; continue; }
            F=0; err=0;
            beams: for each beam (i,j): Δ = P[j]−P[i]; len=|Δ|; Δ −= Δ·(L0/len); err += |len/L0 − 1|;
                   F += k·Δ + d·(Vel[j]−Vel[i]);      err /= nBeams(i)
            creases: for each (c,role) of node i with CrOn[c]:
                   angF = CrK·(CrTarget·Fold − CrTheta);
                   role 1: F += angF/CrH1 · Normal[F1];  role 2: F += angF/CrH2 · Normal[F2];
                   role 3: F −= angF·((1−CrC1)/CrH1·Normal[F1] + (1−CrC2)/CrH2·Normal[F2]);
                   role 4: F −= angF·(CrC1/CrH1·Normal[F1] + CrC2/CrH2·Normal[F2]);
            faces: for each face containing i (as corner a, b or c): face-force formulas from the table above with FACE stiffness.
            NewVel[i] = Vel[i] + F·Dt/1;  StrainPct[i] = err·100
e) positions: Vel = NewVel; for non-fixed i: Disp[i] += Vel[i]·Dt;  CrLastTheta = CrTheta
```

**BuildMesh(state):** `Mesh m; m.Vertices.AddVertices(P); m.Faces.AddFace(a,b,c)`; colours: `e = min(StrainPct, STRAIN_CLIP); hue = (1 − e/STRAIN_CLIP)·0.7; colour = HSL→RGB(hue, 1, 0.5)` (implement HSL→RGB inline, hue in [0,1]); `m.VertexColors.SetColors(colours)`; `m.Normals.ComputeNormals()`. Return.

**Demos** (all in the XY plane, unit cells, generated as `List<Line>` M/V/B):
- Demo 1 single valley: square (0,0)-(2,1)… use rectangle 2×1 with the crease x=1 from (1,0) to (1,1) as **V**; four boundary lines as B. 4 nodes, 2 triangles (the rectangle halves are already triangles? no — each half is a 1×1 square → shorter-diagonal split → 4 faces, 6 nodes; that is fine and mirrors the lessons' running example only loosely; Info reports the V crease angle).
- Demo 2 Miura 4×4 (nx = 4, ny = 4, a = 1, zig = 0.25): vertices `x = i·a + (j odd ? zig : 0)`, `y = j·a`; horizontal zigzag rows j = 1..ny−1 alternate M/V by row parity; vertical lines i = 1..nx−1 alternate by column parity *and* flip at each row (standard Miura assignment: vertical segments between rows j and j+1 have kind = ((i + j) even ? M : V)); outer rectangle lines = B.
- Demo 3 = Miura 12×12, same generator.

**Fidelity checklist for chunk 1 (tick in this record):** mass 1 · beam k,d · dt · crease k by type · crease damping off · M/V sign · theta unwrap · geo disable · crease role forces · beam force · face force · integrate · step order · 100 steps · strain colour.

#### Chunk 2 — `grasshopper/README.md`
Sections: What it is (one paragraph + link to the web app and the 7OSME paper) · Open and run (open `OrigamiSim.gh`; the Miura demo folds immediately; drag Fold) · Use your own pattern (draw lines in Rhino on the XY plane; right-click each Line param → Set Multiple Lines; M = mountain, V = valley, B = boundary and flat helper lines; Anchors optional; Reset toggle) · Constants (table of the constants block, what each does, repo slider ranges) · Reading the output (colour = axial strain %, blue 0 → red ≥ 5; `Info` fields) · Troubleshooting (exploding mesh → lower AXIAL or raise DAMP; pattern won't fold → check lines meet within document tolerance; empty inputs → demo) · How it was built (MCP, headless; file locations).

#### Chunk 3 — Headless canvas build (session with `Rhino_MCP_Platform` connected)
0. `session_connectors_status` shows `Rhino_MCP_Platform` connected; load its tool schemas with ToolSearch (`select:mcp__Rhino_MCP_Platform__spawn_slot,…`).
1. `spawn_slot {version:"8"}` → `slot`. **Every later call passes this `slot`.**
2. `g1_start {slot}` → expect "Opened Grasshopper".
3. Guids are pinned in *Toolchain probe results* above; `g1_search_components` only if one fails to place.
4. `g1_apply_graph {slot, solve:false, includeDeprecated:true}` with these keys and pivots (canvas px). Remember: slider pivot = top-left, component/param pivot = centre.
   | Key | Kind | Selector / spec | X | Y |
   |---|---|---|---|---|
   | `inM` | Line param | `8529dbdf-9b6f-42e9-8e1f-c7a2bde56a70` | 150 | 100 |
   | `inV` | Line param | same | 150 | 160 |
   | `inB` | Line param | same | 150 | 220 |
   | `inAnchors` | Point param | `fbac3e32-f100-4292-8692-77240a42fd1a` | 150 | 280 |
   | `fold` | slider | Min −1, Value 0.6, Max 1, Type "float", Name "Fold" | 70 | 420 |
   | `reset` | Boolean Toggle | `2e78987b-9dfb-42a2-8b76-3923ac8bd91a` | 150 | 480 |
   | `solver` | C# Script (legacy) | `a9a8ebd2-fff5-4c44-a8f5-739736d129ba` | 520 | 290 |
   | `outMesh` | Mesh param | `1e936df3-0eea-4246-8549-514cb8862b7a` | 900 | 200 |
   | `outStrain` | Number param | `3e8ca6be-fda8-4aaf-b5c0-3c54c8bb7312` | 900 | 290 |
   | `outInfo` | Text param | `3ede854e-c753-40eb-84cb-b48008f14fd4` | 900 | 380 |
   Read `placeErrors` (must be empty) and keep the key→id map.
5. `run_csharp {slot}` — configure the solver component (verified pattern, prints one JSON line):
   ```
   using System; using System.Linq; using System.Collections.Generic;
   using Grasshopper; using Grasshopper.Kernel; using Grasshopper.Kernel.Parameters; using Grasshopper.Kernel.Parameters.Hints;
   var doc = Instances.ActiveCanvas.Document;
   var obj = doc.FindObject(new Guid("<solver id>"), true);
   if (obj.GetType().FullName != "ScriptComponents.Component_CSNET_Script") throw new Exception("wrong component type: " + obj.GetType().FullName);
   var comp = (IGH_Component)obj; var vp = (IGH_VariableParameterComponent)obj; dynamic d = obj;
   while (comp.Params.Input.Count > 0) comp.Params.UnregisterInputParameter(comp.Params.Input[0]);
   while (comp.Params.Output.Count > 0) comp.Params.UnregisterOutputParameter(comp.Params.Output[0]);
   Action<string, IGH_TypeHint, GH_ParamAccess, bool> addIn = (name, hint, access, opt) => {
     var p = (Param_ScriptVariable)vp.CreateParameter(GH_ParameterSide.Input, comp.Params.Input.Count);
     p.Name = name; p.NickName = name; p.TypeHint = hint; p.Access = access; p.Optional = opt;
     comp.Params.RegisterInputParam(p, comp.Params.Input.Count); };
   Action<string> addOut = (name) => { var p = vp.CreateParameter(GH_ParameterSide.Output, comp.Params.Output.Count);
     p.Name = name; p.NickName = name; comp.Params.RegisterOutputParam(p, comp.Params.Output.Count); };
   addIn("M", new GH_LineHint(), GH_ParamAccess.list, true); addIn("V", new GH_LineHint(), GH_ParamAccess.list, true);
   addIn("B", new GH_LineHint(), GH_ParamAccess.list, true); addIn("Fold", new GH_DoubleHint_CS(), GH_ParamAccess.item, false);
   addIn("Reset", new GH_BooleanHint_CS(), GH_ParamAccess.item, false); addIn("Anchors", new GH_Point3dHint(), GH_ParamAccess.list, true);
   addOut("Mesh"); addOut("Strain"); addOut("Info");
   vp.VariableParameterMaintenance(); comp.Params.OnParametersChanged();
   dynamic src = d.ScriptSource;
   src.UsingCode = @"<USING region>"; src.ScriptCode = @"<SCRIPT region>"; src.AdditionalCode = @"<ADDITIONAL region>";
   comp.ExpireSolution(false); doc.NewSolution(false);
   var msgs = comp.RuntimeMessages(GH_RuntimeMessageLevel.Error).Cast<string>().ToList();
   Console.WriteLine("{\"inputs\":" + comp.Params.Input.Count + ",\"outputs\":" + comp.Params.Output.Count + ",\"errors\":" + msgs.Count + "}");
   ```
   The three regions are read from `grasshopper/OrigamiSolver.cs` **by the executing session**: read the file, split on the `// ===== ` markers, embed each as a C# verbatim string (`@"…"`, with every `"` doubled). `using ScriptComponents;` does not compile in `run_csharp` — the `dynamic` route above is the verified one.
   Also place the **Timer** in this same script: `var t = new Grasshopper.Kernel.Special.GH_Timer(); t.CreateAttributes(); t.NickName = "Animate"; t.Interval = 40; t.Attributes.Pivot = new System.Drawing.PointF(520, 460); doc.AddObject(t, false); t.AddTarget(comp.InstanceGuid); t.Locked = true;` (locked = not animating; the user unlocks it in the saved file).
   Re-query with `g1_get_canvas_graph {slot, include_data:false}`: the solver must list inputs `M,V,B,Fold,Reset,Anchors`, outputs `Mesh,Strain,Info`, and no Error-level messages.
6. `g1_connect_many {slot, solve:true}` with instance Guids from step 4: `{SrcId:inM, Src:"", DstId:solver, Dst:"M"}`, same for V, B, Anchors, `fold→"Fold"`, `reset→"Reset"`, then `{SrcId:solver, Src:"Mesh", DstId:outMesh, Dst:""}`, `Strain→outStrain`, `Info→outInfo`. Expect `okCount == 9`; read `solve.statuses[]` for component messages.
7. Chunk 4 compile loop until the solver has no Error messages. `Component` and `GrasshopperDocument` are verified available inside the legacy script. Output name `Mesh` is kept; the type is written `Rhino.Geometry.Mesh` everywhere (verified to compile). Re-inject after each `.cs` fix by re-running the step-5 script's `src.*` lines only.

#### Chunk 5 — Groups, overlap check, save (`run_csharp`, slot)
```
groups: ("Pattern inputs", [inM,inV,inB,inAnchors], Color.FromArgb(255,220,235,255))
        ("Controls", [fold,reset], (255,235,255,220)) ("Solver", [solver,timer], (255,255,240,200)) ("Output", [outMesh,outStrain,outInfo], (255,225,255,225))
foreach: var g = new GH_Group(); g.CreateAttributes(); g.NickName = name; g.Colour = c; foreach id: g.AddObject(id); doc.AddObject(g,false); g.ExpireCaches();
overlap: var objs = doc.Objects.Where(o => !(o is GH_Group)).ToList(); bounds = o.Attributes.Bounds (call o.Attributes.ExpireLayout(); first);
         for all pairs: if (a.Bounds.IntersectsWith(b.Bounds)) → list; if any: move the later one down by 40 px (Pivot.Y += 40, ExpireLayout) and re-check (max 5 passes); print JSON {overlaps:[...], passes}
save:    var io = new GH_DocumentIO(doc); bool ok = io.SaveQuiet(@"C:\Users\desti\Documents\Coding\GrasshopperOrigamiSimulator\grasshopper\OrigamiSim.gh"); print {saved: ok}
```
Then from this side: `Test-Path` + size of the `.gh`.

#### Chunk 6 — Verification (slot)
- Read `Info` after 20 solves on the Miura demo: either 20 × `g1_solve_graph` or unlock the Timer via `run_csharp` (`timer.Locked = false`), wait one call, lock it again; then `g1_get_canvas_graph include_data=true sample_size=1`; assert `maxThetaErrDeg ≤ 2`, `meanStrain% < 5`, no `NaN` in Info; `msPerSolve` recorded. Visual check: bake a copy of the mesh into the slot doc, `get_viewport_image {slot, boxMin, boxMax, displayMode:"Shaded"}`, delete the baked object.
- Switch `DEMO_WHEN_EMPTY` 2→1 by `run_csharp` string-replace on `comp.ScriptSource.AdditionalCode`, solve ×20, read Info (angle ≤ 2° at Fold 1; set `fold` slider to 0 via `run_csharp` `slider.SetSliderValue(0)` or re-place, solve, ≤ 2° from flat), then restore to 2 and re-solve.
- Demo 3 timing: set constant to 3, solve once, read `msPerSolve` < 1000; restore to 2.
- Record every number in the checklist below.

#### Chunk 7 — Reopen check
`close_slot`; `spawn_slot`; `g1_start`; `run_csharp`: `Grasshopper.Instances.DocumentServer.AddDocument(path, true)` (verified: becomes the active canvas); `g1_get_canvas_graph` → zero Error messages; object count = 15 (10 placed + Timer + 4 groups). Opening the file in the user's own Rhino needs that Rhino adopted by the router (`MCPStart` there) and the user's OK.

### Execution checklist
- [x] 0 · toolchain probe (2026-10-02): every API call in chunks 3–7 exercised live in a spawned slot; results folded into *Toolchain probe results*. Changes: legacy C# Guid, `connect_many` by Guid, params not panels for outputs, Timer instead of self-scheduling, bake-for-capture.
- [x] 1 · `grasshopper/OrigamiSolver.cs` written; fidelity checklist ticked: mass 1 ✓ · beam k,d ✓ · dt ✓ · crease k by type ✓ · crease damping off ✓ · M/V sign ✓ · theta unwrap ✓ · geo disable (per step, as the shader) ✓ · crease role forces ✓ · beam force ✓ · face force ✓ · integrate ✓ · step order ✓ · 100 steps ✓ · strain colour ✓
  - Deviations from the chunk 1 spec: (a) Demo 1 is the lessons' exact example (unit square, valley diagonal, 4 nodes, 2 triangles). (b) Miura assignment corrected: zigzag columns are one type each (even i M, odd i V); a row segment between columns i and i+1 takes column i+1's type on even rows and column i's on odd rows (big-little-big rule). The spec's "(i+j) even" rule is not flat-foldable. Verified: Miura folds fully flat at Fold 1 with 0.0002 % strain. (c) USING region is comments only; everything is fully qualified. (d) acos arguments clamped to [-1, 1] (the shader relies on GPU clamping). (e) Crease "on/off" recomputed every step, as `updateCreaseGeo` does, not latched. (f) Dangling lines pruned; quads fall back to the other diagonal if the shorter one makes a zero-area triangle; ear clipping skips straight-angle vertices. (g) While Reset is true the sheet is held flat (no steps).
- [x] 2 · `grasshopper/README.md`
- [x] — `Rhino_MCP_Platform` (0.3.0) server connected in the session (chunks 3–7 need this)
- [x] 3 · headless canvas built in slot `aardvark` (2026-10-02): `g1_apply_graph` placed 10 objects, `placeErrors` empty; `run_csharp` configured 6 inputs / 3 outputs, injected source read from the `.cs`, placed the Animate timer; `g1_connect_many` okCount 9/9.
- [x] 4 · compiled clean on the first injection: 0 errors, 1 warning (CS1701 System.Drawing.Primitives version unification, environmental, cannot be suppressed from source).
- [x] 5 · 4 groups (Pattern inputs, Controls, Solver, Output); left column left-aligned at x = 70, timer left-aligned under the solver, outputs centre-aligned at x = 900; 0 object/object and 0 group/group overlaps; `grasshopper/OrigamiSim.gh` saved, 13 676 bytes. Layout capture: `grasshopper/captures/canvas-layout.png`.
- [x] 6 · criteria numbers (all in the slot, 100 steps per solve):
  | Test | Result | Bar |
  |---|---|---|
  | Single valley (Demo 1), Fold 1, 20 solves | maxThetaErrDeg 0.00, strain 0.0000 %, sense 1/1 | ≤ 2° ✓ |
  | Single valley, back to Fold 0, 20 solves | maxThetaErrDeg 0.00 | ≤ 2° ✓ |
  | Miura 4×4, Fold 0.6, 20 solves | finite, meanStrain 1.67 %, sense 24/24, settled (|v| 4e-4); crease angles up to 38° from target (Miura creases are coupled) | no NaN, < 5 % ✓ |
  | Miura 4×4, Fold 1, 20 more solves | maxThetaErrDeg 1.62, strain 0.0002 % | (extra) ✓ |
  | Miura 12×12 (169 nodes), Fold 0.6 | 18.9 ms per solve, sense 264/264 | < 1 s ✓ |
  | Animate timer on Miura 4×4 | 68 frames in 4.1 s ≈ 16 fps | ≥ 10 fps ✓ |
  | User lines (2×2 square, crossing V diagonals, M midline with T-junctions) | 7 nodes, 6 faces, 6 creases, sense 6/6, strain 0.46 % | splits work ✓ |
  Visual: `grasshopper/captures/miura4-fold60-perspective.jpg` (baked copy, deleted after capture).
- [x] 7 · reopened in a fresh slot via `DocumentServer.AddDocument`: 15 objects, 4 groups, 0 Error messages, timer locked with 1 target, solver output identical to the pre-save run.

### Execution notes (new findings)
- **Changing `ScriptSource` on an already-compiled legacy component does not recompile it.** Clear the cached assembly first: `obj.GetType().BaseType.GetField("<ScriptAssembly>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, null)`, then `ExpireSolution(false)`.
- **The Timer only starts ticking after a solution completes** while it is unlocked. Unlock, then expire the solver and `NewSolution(false)`. In the Grasshopper UI, enabling the timer triggers that solve.
- `GH_PersistentParam<T>.SetPersistentData` is ambiguous with an array argument; cast to `IEnumerable<GH_Line>`.
- Canvas image: `canvas.GenerateHiResImageTile(viewport, Color.White)` with `Viewport.Width/Height/Zoom/Tx/Ty` set by hand (`Viewport.Focus` over-zoomed). `GetCanvasScreenshot` does not exist.

### Change 2026-10-02 — strain colours as a separate node (user request)
- [x] 8 · Solver's `Mesh` output is now the plain mesh (colouring removed from `OrigamiSolver.cs`). New legacy C# component **Strain Colours** (`grasshopper/StrainColour.cs`, inputs Mesh + Strain, output StrainMesh) in its own purple group "Strain colours", feeding a new **Strain Mesh** param in the Output group. Placed with `g1_apply_graph`, wired with `g1_connect_many` (3/3), configured with `run_csharp`.
  - Previews: both script components hidden; Mesh param preview on, Strain Mesh param preview off (the meshes coincide).
  - Verified: plain mesh 0 vertex colours, strain mesh 25 colours (15 distinct); 0 errors; 0 overlaps; reopened in a fresh slot: 18 objects, 5 groups, 0 errors. Previous file kept as `grasshopper/archive/OrigamiSim_v1.gh`. Capture: `grasshopper/captures/mesh-vs-strain-mesh.png`.

### Change 2026-10-02 — inputs read from Rhino layers (user request)
- [x] 9 · Replaced the M, V, B Line params and the Anchors Point param with four `GH_GeometryPipeline` objects (deleted with `g1_delete_component`, placed with `g1_apply_graph`, wired with `g1_connect_many` 4/4). Filters: layer `*Mountain` / `*Valley` / `*Boundary` (type Curve) and `*Anchors` (type Point); `IncludeHidden=false`, `IncludeLocked=true`; pipeline previews hidden. Solver M/V/B type hints changed to `GH_CurveHint`; `OrigamiSolver.cs` now takes `List<Curve>` and splits polylines/polycurves into segments, approximating curved segments with a warning.
  - Findings: an exact layer filter (`Mountain`) matches the top-level layer only; `*Mountain` also matches `Parent::Mountain` (filters match the full layer path). Pipelines refresh on Rhino document changes without a forced solve, even in a background slot. Pipelines are 156 × 80 px with a top-left pivot: column respaced to a 90 px pitch, Controls moved down to y 460.
  - Verified: (a) 2×2 test pattern on top-level layers, boundary as one closed rectangle, decoy line on Default ignored: 7 nodes, 6 faces, 6 creases, 1 fixed node. (b) Live update: a new Mountain line raised the pipeline count 2 → 3 with no forced solve. (c) Reopened file in a fresh slot with a Miura 4×4 drawn on nested `Origami::` layers (boundary one polyline, zigzags polylines): 25 nodes, 32 faces, sense 24/24, meanStrain 1.6694 % at frame 21 vs 1.6704 % for the built-in demo; filters persisted; 0 errors. 0 overlaps. Previous file kept as `grasshopper/archive/OrigamiSim_v2.gh`.

### Change 2026-10-02 — the user's layer names and line types (user request)
- [x] 10 · Layers: Border, Mountain, Valley, Facet, Cut, Hinge, Labels (ignored); Anchors kept as optional. Semantics taken from the web app's SVG import (`js/pattern.js:136-153, 497-531, 580-596, 826`): Facet = F (flat crease, 0 deg, facet stiffness); Hinge = U (beam, no angular spring); Cut = C (split, `splitCuts`); interior Border = no crease (U-like), which corrects the earlier "interior B = flat crease" behaviour.
  - Solver: new inputs F, C, H (Curve, list, optional) inserted after B; kinds B/M/V/F/H/C with overlap priority C > M/V > F > H > B; cut unzipping after triangulation (per vertex on a cut, the triangle fan is grouped across non-cut edges; each extra group gets a vertex copy); creases only on interior M/V/F edges and diagonals; notes `cutEdges`, `cutVertexCopies`, `freeHinges`.
  - Canvas: Boundary pipeline renamed Border (`*Border`); Facet, Cut, Hinge pipelines added (`g1_apply_graph`, `g1_connect_many` 3/3); column of 7 pipelines at 90 px pitch in solver-input order; solver re-centred at y 370; outputs and Strain colours moved down; 0 overlaps.
  - Verified: Miura demo unchanged after the edit (identical frame-1 numbers). Layer test with the exact names: 2×2 sheet, Border rectangle, Cut slit (0,1)-(1,1), Valley below, Mountain above, one Facet, one Hinge, text + line on Labels: pipelines 1/1/1/1/1/1, Labels ignored; 9 nodes, 15 beams, 7 faces, 5 creases (2 M/V), cutVertexCopies 1, freeHinges 1; slit edges 1.636 apart after folding (flaps fold opposite ways); strain 0.015 %, crease error 1.02°. Reopened in a fresh slot: 21 objects, 5 groups, 0 errors, all 9 inputs wired, filters persisted. Previous file: `grasshopper/archive/OrigamiSim_v3.gh`.

