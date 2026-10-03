# grasshopper-print-export

### Intent
Add a **3D Print** section (canvas group) to `grasshopper/OrigamiSim.gh` that turns the crease pattern on the existing layers into a flat, closed, printable mesh of the unfolded sheet. The structure copies `C:\Users\desti\Downloads\origami+crane.stl`, which the user printed and folded successfully: a hinge layer over the whole sheet, a panel layer on top with grooves along the creases, and through-holes at chosen vertices. It has its own legacy C# component, fed from the existing layer pipelines. The solver component is untouched.

Decisions taken (do not relitigate):
- Sliders: **Size** (longest sheet side in mm, default 158), **Total Height** (default 0.3), **Hinge Thickness** (default 0.2; panels = Total Height − Hinge), **Hole Degree N** (int, default 6).
- Constants in code: groove width `GROOVE` = 1.2 mm, hole diameter `HOLE_D` = 5.0 mm.
- M, V and Hinge lines are grooved, Cut lines become full-depth slots of groove width, and Facet lines are ignored. Border is the outline. All grooves are on top and the bottom is flat.
- Holes go automatically at interior vertices of degree ≥ N (counting grooved and cut segments). The user accepted that at N = 6 the crane gets 1 hole, not 5.
- Output is a mesh only (`Print Mesh` param). The user bakes it and exports STL.
- Plan high, execute medium.

### Criteria & scoring axes
- **C1, reproduces the crane's structure.** Test: run the reference crane lines (chunk 1) at Size 158.358, H 0.3, Hinge 0.2, N 6.
  - The bounding box equals the STL's within 0.01 mm (±79.179 in XY after centring, z 0 to 0.3).
  - Horizontal faces exist only at z 0, 0.2 and 0.3.
  - Rasterize both top surfaces at 0.1 mm into height classes {0.3, 0.2, void}. Outside r = 3 mm disks around the STL's 5 hole centres, under 1 % of pixels may differ.
- **C2, printable solid.** Test:
  - `IsClosed` and `IsManifold(true, out _, out _)` both pass.
  - No faces with area under 1e-9.
  - `Volume()` = A_base·Hinge + ΣA_panel·(H − Hinge), within 0.5 %.
  - Write a binary STL from the harness, re-read it, weld it: still closed.
- **C3, parameters behave.** Test:
  - H 0.3 → 0.6: the top face is at 0.6 and the floor stays at 0.2.
  - Size 100 → 200: longest side 200 ± 0.01.
  - N 6 → 4: hole count equals the number of interior vertices with degree ≥ 4.
  - Hinge ≥ H: Error message and null output.
- **C4, robust and integrated.** Test:
  - All 13 patterns in `grasshopper/examples/patterns.json` build in under 2 s each, closed and manifold.
  - The 3D Print group overlaps nothing.
  - `OrigamiSim.gh` reopens in a fresh slot with 0 Error messages.
  - The README has a "3D print" section.
- The final test is the user's: print and fold.

Axes: goal-likelihood, fidelity, robustness, executability.

### Convergence
Converged at loop 6/20. Final scores: goal 9 · fidelity 9 · robustness 9 · executability 9.

---

### Reference STL facts (measured 2026-10-02 by parsing the binary STL)
- 8,812 triangles in one closed shell. 158.358 × 158.358 mm square, centred on the origin, z 0 to 0.300.
- Horizontal faces only: z 0 (down, 24,979 mm²), z 0.2 (up, groove floors, 1,693 mm²) and z 0.3 (up, panel tops, 23,286 mm²). Every other face is a vertical wall; there are no chamfers.
- Grooves are **1.2 mm** wide (74 paired boundary edges, all at 1.20), the same for every crease. They run to the sheet edge, and panels are flush with the border (no inset there). Where grooves meet, their outlines merge and the panel corners are sharp miters.
- 5 through-holes of **r 2.5** (180-gon) at (0,0), (0,46.44), (−45.74,−0.09), (0,−46.00) and (45.85,−0.09). Degrees: the centre is 6, the other 4 are 4. Several unholed vertices also have degree 4.
- The total crease length is about 1,411 mm. That is not the repo's `traditionalCrane.svg` (2,759 mm M+V).
- The analysis scripts are in this session's scratchpad. Chunk 1 rewrites what it needs into the repo.

### Environment facts
- In this session the Rhino MCP server is named **`rhino`** (`mcp__rhino__spawn_slot`, `g1_*`, `run_csharp`, `get_viewport_image`). Its tool set is the 0.3.0 one. The old record's note "do not use `mcp__rhino__*`" is stale: the config now points `rhino` at 0.3.0. Always pass `slot`.
- `doc.Import(stl)` in `run_csharp` **hung for 300 s**, probably on an import-options dialog. Read STLs with a hand-written binary parser instead (header 80 bytes, uint32 count, 50 bytes per triangle).
- `get_viewport_image` results above about 480×270 overflow the tool-result limit and are saved to a file. Decode them with `json.load(...)["content"][i]["data"]` (base64 JPEG).
- No numpy, PIL or matplotlib on this machine. Python 3.12 has only the standard library; a PNG can be written by hand with `zlib` (see `raster.py` in chunk 1).
- The `file://` URLs in the browser pane are refused.
- All the GH and legacy-C# component facts in `plans/grasshopper-origami-solver.md` (*Toolchain probe results*, *Execution notes*) apply unchanged: component Guid `a9a8ebd2-…`, `dynamic` ScriptSource, clear `<ScriptAssembly>k__BackingField` before recompiling, `g1_connect_many` by instance Guid, outputs to plain params, `GH_DocumentIO.SaveQuiet`.

---

### Steps
1. **Reference crane lines.** Extract the groove centrelines from the STL into `grasshopper/examples/crane_print_reference.json`, then check them by rasterizing.
   - Over: using `traditionalCrane.svg`, which is a different crease set.
   - Divergence: none (test only).
2. **`grasshopper/OrigamiPrint.cs`.** A static core runs: scale → planar arrangement → face walk → mitered per-edge inset → validity check plus local boolean fallback → holes → extrude → single-shell union with a touching-shells fallback → mesh.
   - Over: a global curve-boolean union, which fails on coincident collinear strips.
   - Divergence: N = 6 gives the crane 1 hole, not 5. The fixed 1.2 groove collides above about 0.5 mm of panel height, and Info reports the angle.
3. **Headless test harness** (`run_csharp`, the same `.cs` plus a test runner) for C1–C3.
   - Over: iterating inside the GH component, which needs a recompile and cache-clear every time.
4. **Canvas.** Archive the current file as v4, add the 3D Print group, wire it to the pipelines, save.
   - Divergence: the mesh is placed to the right of the pattern, not on top of it.
5. **Robustness sweep and reopen** (C4).
6. **README "3D print" section and capture.**

### Execute at
medium. The geometry and the API calls are fixed here, and the test loop catches the remaining API detail.

---

### Chunk specs

#### Chunk 1: reference crane lines
`grasshopper/examples/stl_groove_lines.py` (standard library only):
1. Parse the STL. Take the up-facing triangles at z 0.2 (the groove floors). Collect the floor's boundary edges: edges used once, after rounding the xy to 1e-3.
2. Pair edges that are parallel (|cross of unit directions| < 0.02), 1.2 ± 0.05 apart, and that overlap in projection. For each pair, the midline over the overlap is a centreline piece.
3. Merge pieces that are collinear (direction within 0.5°, perpendicular offset < 0.05) and touching or overlapping (gap < 2.0) into maximal segments.
4. Close the gaps at junctions. Extend each segment end by up to 3.5 mm until it hits another segment's line, the border square (±79.179) or a hole centre (the 5 above), and snap it there. Ends that hit nothing stay as they are.
5. Write `{"name":"STL Crane","source":"origami+crane.stl","segments":{"border":[4 square edges],"mountain":[all centrelines],"valley":[],"facet":[]}}` in the `patterns.json` segment format `[x1,y1,x2,y2,angle]` with angle 180. Mountain vs valley doesn't matter for printing.
6. **Verify:** rasterize at 0.1 mm the STL's groove-floor triangles, and separately the centrelines stroked to width 1.2 with mitered joins (approximate: each segment becomes a rectangle 1.2 wide extended 0.6 at both ends). Mask r = 3 disks at the 5 holes. Under 1 % mismatched pixels. Print the counts.

#### Chunk 2: `grasshopper/OrigamiPrint.cs`
Layout mirrors `OrigamiSolver.cs`: a static class `OrigamiPrint` in *AdditionalCode*, with a short *ScriptCode* that calls it.

```
Inputs (legacy C# params, all optional):
  B, M, V, C, H : List<Curve> (GH_CurveHint, list access)
  Size : double, Height : double, Hinge : double, HoleDeg : int
Outputs: PrintMesh (Mesh), Info (string)
Constants: GROOVE = 1.2, HOLE_D = 5.0, HOLE_SEGS = 64, MIN_AREA_FACTOR = 1.0 (a piece needs area ≥ GROOVE²),
           UNION_MAX_PANELS = 400, GAP_X = 0.2 (output offset to the right, × pattern width), DEMO_WHEN_EMPTY = true
public static Result Run(List<Curve> B, M, V, C, H, double size, double height, double hinge, int holeDeg, double docTol)
Result { Mesh Mesh; string Info; List<string> Errors; List<string> Warnings; }
```

Algorithm (all in the XY plane):
1. **Validate.** Error with null mesh if `hinge <= 0`, `hinge >= height` or `size <= 0`. Warn if any curve point has |z − z0| > tol (not planar in XY), and project to z0.
2. **Lines.** Split curves into segments: copy `ToLines`/`AddPolyline` from `OrigamiSolver.cs`, approximating curved segments with a warning. Kinds: B = 0, groove (M, V, H) = 1, cut C = 2. If M, V and H are all empty and `DEMO_WHEN_EMPTY` is set, use the solver's Miura 4×4 demo (copy `Demo(2,…)` and `MiuraPt`).
3. **Scale.** bbox = the bbox of the B lines, or of all lines if there are no B lines (then warn "no Border: using the bounding rectangle" and add its 4 edges as B). s = Size / max(dx, dy). Transform every point to `p' = (p − bbox.Min)·s`, so the sheet sits at [0, dx·s] × [0, dy·s] in mm and z = 0. The final placement translation is step 11. Tolerance: `tol = max(docTol·s, 1e-4)`.
4. **Arrangement.** Split all segments at mutual crossings and T-junctions, using the solver's `AddLines` / `EndpointCuts` / `VertexId` logic. Keep the kind of each split piece; overlapping pieces resolve by priority cut > groove > border. Drop zero-length pieces. Build vertices V[] and undirected edges E[] with a kind.
5. **Faces.** Turn each undirected edge into 2 half-edges. At each vertex, sort the outgoing half-edges by `atan2`. For a half-edge u→v, `next` = the outgoing half-edge at v that comes immediately clockwise from v→u (this gives CCW faces). Walk the cycles. Faces with signed area > 0 are panels; the single largest-magnitude negative cycle per connected component is the outer boundary.
   - **Dangling edges** (a vertex of degree 1 that isn't on the border) appear twice in the same cycle (u→v and v→u). Remove them from the face polygon (collapse the spike), but keep them in a `dangling[face]` list for step 7.
6. **Inset (mitered).** For a face polygon P[0..n−1] (CCW), edge i = P[i]→P[i+1] gets offset d_i = 0 if its kind is B, else GROOVE/2.
   - The offset line of edge i is `P[i] + d_i·nL_i + t·u_i`, where `u_i` is the unit direction and `nL_i = (−u_i.y, u_i.x)`, the left normal (inward for CCW).
   - The new vertex Q[i+1] is the intersection of offset lines i and i+1. Solve the 2×2 system `A + t·u_i = Bp + w·u_{i+1}` with `det = u_i.x·u_{i+1}.y − u_i.y·u_{i+1}.x`.
   - If |det| < 1e-9 (collinear): if d_i == d_{i+1}, Q = P[i+1] + d_i·nL_i; otherwise use the intersection with the perpendicular at P[i+1] (step).
   - **Valid** if every inset edge Q[i]→Q[i+1] has `dot(Q[i+1] − Q[i], u_i) > 0`, the polygon doesn't self-intersect (O(n²) segment test, n is small), and its area ≥ MIN_AREA.
   - **Fallback** if invalid or the face has dangling edges: `Curve.CreateBooleanDifference(facePolylineCurve, strips, tol)`. Strips are stadiums (`Curve.CreateBooleanUnion` is not needed: pass all strips as subtractors) for the face's non-B edges plus its dangling edges, each a rectangle of width GROOVE along the edge, extended GROOVE/2 at both ends. Keep the result pieces with area ≥ MIN_AREA. If it fails, drop the face and count `facesDropped`.
7. Done in step 6 (the dangling edges are handled by the fallback).
8. **Holes.**
   - Interior vertex: not on any B edge.
   - Degree: the number of incident kind-1 and kind-2 edges.
   - Hole if degree ≥ HoleDeg: a circle of r = HOLE_D/2 → `PolylineCurve` with HOLE_SEGS segments.
   - Union overlapping circles with `Curve.CreateBooleanUnion`.
   - For each panel whose bbox meets a hole's bbox: `Curve.CreateBooleanDifference(panel, hole)`. Keep the pieces with area ≥ MIN_AREA. A hole fully inside a panel without touching its edge can't happen at a vertex, because there is always a groove there; if it does, the result is an outer loop and an inner loop, and `Brep.CreatePlanarBreps` takes both.
9. **Base outline.** `Curve.CreateBooleanDifference(outerBoundary, holes ∪ cutStrips)`. Cut strips are kind-2 edges as rectangles GROOVE wide, extended GROOVE/2. Then `Brep.CreatePlanarBreps(resultCurves, tol)`, which handles the inner loops.
10. **Solids and mesh.**
    - Base: each planar brep face → `face.CreateExtrusion(LineCurve((0,0,0)→(0,0,hinge)), true)`.
    - Panels: each panel curve → `Extrusion.Create(curve, height − hinge, true)` moved to z = hinge. Check the extrusion direction sign: the solid must span z hinge → height.
    - If panelCount ≤ UNION_MAX_PANELS, run `Brep.CreateBooleanUnion(all, tol)`. Accept it if it returns exactly 1 solid with `IsSolid`; else keep the separate solids and add the note `shells=<n> (touching; slicers merge them)`.
    - Mesh: `MeshingParameters mp = MeshingParameters.FastRenderMesh` with `SimplePlanes = true`, then `Mesh.CreateFromBrep` per brep, `Append` into one mesh, `Vertices.CombineIdentical(true, true)`, `Faces.CullDegenerateFaces()`, `Normals.ComputeNormals()`, `Compact()`.
11. **Placement.** Translate by `(bbox.Max.x + GAP_X·dx, bbox.Min.y, z0)`, in document units, with no scale back: the mesh is in mm from step 3.
    - Document units ≠ mm: warn "document units are X; Print Mesh is in mm". The user exports with STL units = mm.
12. **Info** (one `key=value` per line):
    - counts: `size`, `scale`, `segments`, `vertices`, `faces`, `panels`, `insetFallbacks`, `facesDropped`, `holes`, `cuts`
    - solid: `shells`, `closed`, `manifold`, `volume`
    - `foldLimitDeg = 2·atan(GROOVE / (2·(height − hinge)))` in degrees, with the note "folds past this need the hinge to stretch"
    - `ms`

#### Chunk 3: harness tests (slot)
- `spawn_slot {version:"8"}`. Every `run_csharp` call is the full text of `OrigamiPrint.cs`'s class plus a `Test` driver that prints one JSON line. Read the `.cs` from disk in Bash and splice it into the script string, so the tested source is the file.
- C1 driver: load `crane_print_reference.json` → Lines → `Run(..., size 158.358, height 0.3, hinge 0.2, holeDeg 6)`. Print the bbox, the distinct z of horizontal faces, and the hole count. Write the mesh's up-facing triangles as JSON to the scratchpad; Python rasterizes and compares with the STL (reuse the chunk 1 rasterizer) → mismatch % outside the hole disks.
- C2 driver: `IsClosed`, `IsManifold`, degenerate count, `Volume` vs the analytic value (from the region areas computed inside `Run`, exposed in `Result`), and the STL write/read round trip with the same hand parser.
- C3 driver: the 4 sweeps from the criteria.

#### Chunk 4: canvas (slot)
1. `copy grasshopper/OrigamiSim.gh → grasshopper/archive/OrigamiSim_v4.gh`.
2. Open `OrigamiSim.gh` in the slot (`Instances.DocumentServer.AddDocument(path, true)`). `g1_get_canvas_graph` → the pipeline instance ids for Border, Mountain, Valley, Cut, Hinge, and every object's bounds.
3. `g1_apply_graph` (`replace:false` if supported, else place with `g1_place_component` / `g1_place_slider`) **below** the lowest existing object + 80 px:
   - sliders Size (10…500, 158, float), Total Height (0.1…5, 0.3, float), Hinge Thickness (0.05…2, 0.2, float), Hole Degree (2…16, 6, int);
   - the legacy C# `a9a8ebd2-fff5-4c44-a8f5-739736d129ba` named "Origami Print";
   - a Mesh param "Print Mesh" and a Text param "Print Info".
4. `run_csharp`: configure the params. Inputs B, M, V, C, H use `GH_CurveHint` with list access; Size, Height, Hinge use `GH_DoubleHint_CS`; HoleDeg uses `GH_IntegerHint_CS`. Outputs are PrintMesh and Info. Set the ScriptSource from the `.cs` and clear the cached assembly. Same pattern as the old record's chunk 3 step 5.
5. `g1_connect_many` (by Guid): 5 pipelines → B, M, V, C, H; 4 sliders → inputs; outputs → params.
6. `run_csharp`: create a `GH_Group` "3D Print" around the new objects in a distinct colour. Hide the component's preview and turn on the Print Mesh preview. Overlap check on all bounds, with 0 overlaps. `SaveQuiet` to `grasshopper/OrigamiSim.gh`.
7. Verify: `g1_get_canvas_graph` shows the Origami Print component with 0 errors, Info `closed=True` on the demo, and wires 13/13 OK.

#### Chunk 5: sweep and reopen
- Harness: loop over the 13 entries of `patterns.json` (segment kinds: border → B, mountain/valley → groove, facet → ignored, cut/hinge if present). Print per pattern: ms, closed, manifold, panels, fallbacks, dropped, holes. Pass: no exception, closed, manifold, ms < 2000.
- Reopen `OrigamiSim.gh` in a fresh slot: 0 Error messages, the group exists, wires intact.

#### Chunk 6: docs and capture
- README: a "3D print" section covering the parameters table, what is grooved, the hole rule (with the crane caveat), the fold-limit note, and baking and exporting STL in mm. Add `OrigamiPrint.cs` to the Files table.
- Capture: bake the crane print mesh in the slot, `get_viewport_image` top and perspective at 480×480, decode to `grasshopper/captures/print-crane.jpg`, delete the baked object.

### Execution notes (new findings)
- **Running a `.cs` file from disk:** `run_command "_-ScriptEditor _Run <path>"` works. Results must be written to a file: `run_csharp` returned an empty payload for every script in this session, and Console output does not come back through `run_command`.
- **A compile error is silent:** no output file appears. Suspect a local name clashing with a `Build` parameter (`B`, `M`, `V`, `C`, `H`); that was the one compile error in this run.
- The harness is assembled by `scratchpad/mkharness.sh`: `using` line + driver + the ADDITIONAL section of `OrigamiPrint.cs`. A sliced `.cs` compiles as a C# script with top-level statements followed by class declarations.
- `RhinoDoc.Import` of an STL blocks on an options dialog in a background slot (300 s timeout). Parse binary STL by hand.

### Execution checklist
- [x] 1 · reference crane lines extracted and raster-verified: `grasshopper/examples/stl_groove_lines.py` → `crane_print_reference.json` (24 centrelines, 1,487.7 mm, 5 holes r 2.5). Groove raster vs STL at 0.1 mm outside the hole disks: **0.009 %** mismatch. Fix during the run: the outer square loop (4 equidistant corners) was misread as a hole, so a hole loop now needs ≥ 8 points.
- [x] 2 · `grasshopper/OrigamiPrint.cs` written. **Steps 9–10 of the spec were replaced during execution.** The Brep extrude-and-union approach failed C4: 88 s on Waterbomb Tessellation, plus open meshes on 2 patterns. Each change below is listed with the measurement that forced it:
  1. **The mesh is built directly; there are no Breps.**
     - The bottom (z 0), the groove floor (z = hinge: the hinge outline with every panel as a hole) and the panel tops are triangulated with an inline C# port of mapbox/earcut 2.2.4. The walls are quads on the same loop points, so every edge is shared by construction.
     - Before this: the Brep union took 16.5 s with 13 holes. Joining planar faces took 46–75 s on dense patterns and failed (naked edges) on 4 of 13.
  2. **`EPS` = 0.01 mm** inset on border edges, cut edges (GROOVE/2 + EPS) and holes (r + EPS for panels). It keeps every panel strictly inside the hinge layer, so the floor is one region whose holes are the panels. Cut slots extend GROOVE/2 − EPS past the cut ends; before that, the Popup Simple slot ends touched the panels across the next groove.
  3. **Earcut keeps every input vertex** (all nodes are marked steiner). Stock earcut drops collinear vertices, and panel edges along a straight border are collinear, which left T-junctions against the walls. Rotating the frame (tried) made float slivers instead.
  4. **Safety-net repairs:**
     - T-junction fan split: a triangle edge used once, with wall vertices on it, is split into a fan.
     - Zero-area sliver removal: the neighbour across the long edge is split at the third vertex.
     Both are incremental, and Info reports their counts. C2 now gives 0 faces under 1e-9 mm².
  5. **`LongHash` comparer** for every edge-keyed dictionary. `long.GetHashCode()` of `(lo << 32) | hi` is `lo ^ hi`, which collides for nearby ids; a single edge count took 1.7 s on 38k faces. `OrigamiSolver.cs` uses the same key pattern on smaller meshes.
  6. **Holes:**
     - a hole that clips one panel corner replaces that corner with an arc (`CutCorner`, no boolean); `Curve.CreateBooleanDifference` is used only for anything else. Waterbomb Tessellation holes went from 4.4 s to 0.08 s;
     - the hinge-layer holes are inner loops, and only cutters crossing the outline need a boolean (`MergeOverlapping` unions overlapping cutters);
     - holes are 48-gons (`HOLE_SEGS`).
  7. **Convex faces are inset by half-plane clipping** (Sutherland–Hodgman, `ClipHalfPlanes`), which is exact for convex faces. The miter path with its local boolean fallback is kept only for non-convex faces: Lang Honeycomb went from 326 fallbacks to 55, and from 3.1 s to 1.8 s.
  Info reports the ms per stage, plus `insetFallbacks`, `facesDropped` and the repair counts.
- [x] 3 · harness passes, re-run on the final code. The `.cs` is run from disk with `run_command "_-ScriptEditor _Run <file>"`, and results are written to a file; see *Execution notes*.
  - **C1:** crane at 158.358 / 0.3 / 0.2 / N 6:
    - bbox 158.358 × 158.358 × 0.300;
    - horizontal faces only at z {0, 0.2, 0.3};
    - 1 hole (the centre);
    - raster against the STL at 0.1 mm, outside the 5 hole disks: **0.027 %** mismatch (303 + 373 of 2,491,754 pixels);
    - 24 panels, 1 shell, 49 ms.
  - **C2:**
    - closed and manifold, 0 faces under 1e-9 mm²;
    - volume equals base area × hinge + panel area × panel height to 1e-14 %;
    - binary STL round trip: 664 triangles, every edge shared by exactly 2 triangles, every directed edge paired.
  - **C3:**
    - H 0.6: horizontal faces {0, 0.2, 0.6};
    - Size 200: 200.000 × 200.000; Size 100: 100.000;
    - N 4: 13 holes, matching an independent count with tolerance-merged endpoints (13); 36 ms;
    - Hinge = H: Error and null mesh;
    - empty inputs: Miura demo (closed), plus the units warning when units are not mm.
  - Fixture fix: the reference lines now end exactly on hole centres. Before, the ends could be up to 0.1 mm off, so the centre was never a shared vertex and the crane got 0 holes.
- [x] 5a · 13-pattern sweep (harness, Size 158, H 0.3, Hinge 0.2, N 6): **all 13 closed, manifold, single shell; max 1.83 s** (Lang Honeycomb: 1,314 creases, 539 panels, 197 holes).
  - Others: Huffman 0.78 s, Waterbomb Tessellation 0.62 s, Hypar 0.28 s, Traditional Crane 0.16 s; the rest under 0.1 s.
  - On dense patterns at 158 mm many faces are smaller than GROOVE² and are dropped (Lang 364 of 867); this is reported in Info.
- [x] 4 · canvas, 2026-10-03, slot `aardvark`. The MCP server now registers as `Rhino_MCP_Platform`, and `run_csharp` output comes back again.
  - Archived the old definition as `grasshopper/archive/OrigamiSim_v4.gh`.
  - `g1_apply_graph` (`replace:false`, `includeDeprecated:true`) placed 4 sliders (x 70; y 900/930/960/990), the legacy C# "Origami Print" (centre 520, 960), and the Print Mesh / Print Info params (centre 900, 930/990); `placeErrors` was empty.
  - `run_csharp` set 9 inputs and 2 outputs, and read the source regions from the `.cs` file inside the script, so no string embedding was needed.
  - Fix: the legacy component's default usings do **not** include `System.Linq`, so the USING region now adds it (the first compile gave 18 CS1061 errors).
  - `g1_connect_many` connected 11/11 wires.
  - Group "3D Print" (colour 150,170,235,230); 0 object and 0 group overlaps.
  - End to end, with the reference crane drawn on the `Border` and `Mountain` layers at Size 158.358: 158.358 × 158.358 × 0.300, closed and manifold, 1 shell, 1 hole, 24 ms, 0 errors.
  - Saved `OrigamiSim.gh`: 36,633 bytes, 29 objects.
- [x] 5b · reopened in fresh slot `armadillo`: 29 objects, 6 groups including 3D Print (7), 0 Error messages; Print component 9/9 inputs wired, 2/2 outputs; solver 9/9; the demo print is 1 closed, manifold shell.
- [x] 6 · README: a "3D print" section (sliders, layer mapping, constants, export in mm, Print Info fields, fold limit, hole-rule caveat) and two Files table rows. Capture: `grasshopper/captures/print-crane.jpg` (perspective, baked crane print).
