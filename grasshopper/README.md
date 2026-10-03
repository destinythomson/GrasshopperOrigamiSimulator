# Origami Simulator for Grasshopper

A Grasshopper (Rhino 8) definition that folds a crease pattern the way [origamisimulator.org](https://origamisimulator.org/) does. It is a CPU port of this repo's dynamic solver, described in *Fast, Interactive Origami Simulation using GPU Computation* (Ghassaei, Demaine, Gershenfeld, 7OSME).

Every vertex is a particle with mass 1. Every edge is a stiff spring (a "beam"). Every crease is an angular spring that pulls the fold angle toward `target × Fold`. Every triangle has springs that keep its corner angles flat. Each solve runs 100 explicit-Euler steps with the same time step rule as the web app.

## Files

| File | What it is |
|---|---|
| `OrigamiSim.gh` | The definition. Open this. |
| `OrigamiSolver.cs` | The solver source that lives inside the Origami Solver component, kept here so it can be read and diffed. |
| `StrainColour.cs` | The source of the Strain Colours component. |
| `OrigamiPrint.cs` | The source of the Origami Print component (the 3D Print group). |
| `OrigamiSim_Kangaroo.gh` | The Kangaroo2 version of the definition. See [Kangaroo version](#kangaroo-version). |
| `OrigamiKangaroo.cs`, `OrigamiKangarooReadout.cs` | The sources of its Origami Goals and Origami Readout components. |
| `examples/stl_groove_lines.py`, `examples/crane_print_reference.json` | Recover crease lines from a printable-origami STL; the crane reference used to check the 3D Print output. |
| `archive/` | Earlier versions of the definition. |

## Open and run

1. Open `OrigamiSim.gh` in Grasshopper (Rhino 8).
2. If the crease layers are empty or missing, the solver uses a built-in Miura-ori demo.
3. The **Animate** timer is disabled when the file opens. Select it and press `Ctrl+E` (or right-click it and choose *Enable*) to run the simulation continuously. Disable it again to stop.
4. Drag **Fold**: 0 is flat, 1 is fully folded, −1 is fully folded with mountains and valleys swapped.
5. Set **Reset** to true to snap back to the flat sheet; set it to false to fold again.

Without the timer, each change to an input runs one solve of 100 steps.

## Use your own crease pattern

The definition reads the crease pattern straight from Rhino layers. Anything on these layers is picked up automatically and updates live as you draw, move or delete it. The line types follow the web app's SVG import (`js/pattern.js`).

| Layer | Solver input | What it does in the simulation |
|---|---|---|
| `Border` | B | The outer edge of the paper. |
| `Mountain` | M | Folds toward −180° at Fold = 1. |
| `Valley` | V | Folds toward +180° at Fold = 1. |
| `Facet` | F | Flat crease: held at 0° with the facet stiffness. Use it to split faces into triangles your way. |
| `Cut` | C | The paper is cut along the line. The two sides become separate edges; the end of a slit inside the sheet stays connected. |
| `Hinge` | H | An edge with no fold spring: the panels on either side rotate freely. |
| `Labels` | — | Ignored. |
| `Anchors` (optional) | Anchors | Points on vertices that should stay still. |

1. Draw the pattern on these layers. Keep it planar. Lines should meet within the document's absolute tolerance; crossings and T-junctions are split automatically.
2. Layers can be top level or nested, for example `Origami::Mountain`: the filters are `*Mountain`, `*Valley` and so on, so any layer whose full path ends in that name is used. Avoid other layers ending in those words.
3. Polylines and rectangles are split into their straight segments. Curved segments are approximated by short straight lines, and the solver shows a warning, because curved creases are not simulated.
4. Hidden objects are ignored; locked objects are included. To change a layer name, double-click its pipeline in the **Pattern inputs** group and edit the layer filter.
5. If two lines overlap, the stronger type wins: Cut, then Mountain/Valley, then Facet, then Hinge, then Border. A Border line inside the sheet acts as a hinge, as in the web app.

With every crease layer empty, the solver falls back to the built-in demo.

Faces with more than three sides are triangulated, and the added diagonals act as Facet creases, as in the web app. Lines that dangle (end without meeting anything) are ignored.

## Constants

The stiffness settings are constants at the top of the `OrigamiSim` class inside the C# component. Double-click the component to edit them.

| Constant | Default | Web app slider | Range there | Effect |
|---|---|---|---|---|
| `AXIAL` | 20 | Axial Stiffness | 10–100 | Edge stretch resistance. Higher is stiffer but forces a smaller time step. |
| `CREASE` | 0.7 | Fold Stiffness | 0–3 | Strength of mountain and valley creases. |
| `FACET` | 0.7 | Facet Crease Stiffness | 0–3 | How strongly facets resist bending. |
| `FACE` | 0.2 | Face Stiffness | 0–5 | How strongly triangles keep their corner angles. |
| `DAMP` | 0.45 | Damping | 0.01–0.5 | Damping ratio of the edge springs. |
| `STEPS` | 100 | (fixed) | | Solver steps per solve. |
| `TARGET_DEG` | 180 | (per line opacity) | | Fold angle at Fold = 1. |
| `DEMO_WHEN_EMPTY` | 2 | | | Demo used when M, V and B are empty: 0 none, 1 single valley, 2 Miura 4×4, 3 Miura 12×12. |

## Reading the output

- **Mesh** is the plain folded mesh. Its preview is on when the file opens.
- **Strain Mesh** is a copy coloured by axial strain: blue is 0 %, red is 5 % or more. It comes from the separate **Strain Colours** component. Its preview is off when the file opens, because the two meshes sit in the same place. Select a parameter and press `Ctrl+Q` (or right-click it and choose *Preview*) to switch between them.
- **Strain** lists each vertex's strain in percent.
- **Info** reports counts and solver state:

| Field | Meaning |
|---|---|
| `dt` | Time step, `0.9 / (2π · ω_max)` over all edges. |
| `frame`, `steps` | Solves and total steps since the last rebuild. |
| `msPerSolve` | Time spent in the last solve. |
| `meanStrain%` | Average vertex strain. |
| `maxThetaErrDeg` | Largest gap between a mountain/valley crease's angle and its current target. |
| `mvSenseOk` | Mountain/valley creases bending the right way, out of the total. |
| `meanAbsV` | Average vertex speed. Near zero means the model has settled. |
| `finite` | False if the simulation blew up. |

## 3D print

The **3D Print** group turns the same crease layers into a flat, printable sheet of the unfolded pattern. Its structure copies a crane STL that printed and folded well:

- a **hinge layer** over the whole sheet, from z 0 to the Hinge Thickness. This thin layer is what bends;
- a **panel layer** on top of it, up to the Total Height, with a 1.2 mm gap centred on every Mountain, Valley and Hinge line;
- **through-holes** (Ø 5 mm) at crowded vertices.

The bottom is flat, so it prints on the bed with the grooves facing up.

| Slider | Default | What it does |
|---|---|---|
| Size | 158 | Length in mm of the sheet's longest side. The pattern is scaled to fit, so you can draw it at any size. |
| Total Height | 0.3 | Overall thickness in mm. |
| Hinge Thickness | 0.2 | Thickness of the hinge layer in mm. The panels are Total Height − Hinge Thickness. It must be smaller than Total Height. |
| Hole Degree | 6 | A hole goes at every interior vertex where at least this many grooved or cut lines meet. Raise it for fewer holes. |

What each layer becomes:

| Layer | Print |
|---|---|
| Mountain, Valley, Hinge | A 1.2 mm groove in the panel layer. All grooves are on the top face. |
| Cut | A 1.2 mm slot through the whole sheet. |
| Facet | Nothing: facets never fold, so they stay solid. |
| Border | The outline. A Border line inside the sheet stays solid. |

Constants at the top of `OrigamiPrint` (double-click the component): `GROOVE` 1.2 mm, `HOLE_D` 5 mm, `HOLE_SEGS` 48 sides per hole, `EPS` 0.01 mm. `EPS` is a clearance that keeps the panels just inside the outline, the holes and the slots, so the mesh can be built as one closed shell. It is far below what a slicer resolves.

**Printing it:** select **Print Mesh**, right-click and choose *Bake*. Then `_Export` it as STL with units set to millimetres. The mesh is in millimetres and placed to the right of the pattern. If the Rhino document is not in millimetres, the component warns you, because the numbers are millimetres regardless.

**Print Info** reports:
- the scale and the panel count;
- `insetFallbacks` and `facesDropped`: faces smaller than 1.2 × 1.2 mm after grooving get no panel;
- the hole count;
- whether the mesh is closed and manifold, plus how many small triangulation repairs were made;
- the volume, and the time per stage;
- `foldLimitDeg = 2·atan(1.2 / (2 × panel height))`: when a groove closes on the inside of a fold, its panel walls touch at this angle. Beyond it the hinge has to stretch. It is 161° at the default 0.1 mm panels and about 100° at 0.5 mm. If you raise Total Height a lot, expect stiffer folds.

**About the holes:** the reference crane has holes at its centre and at the 4 bird-base points. The bird-base points have 4 creases, the same as several unholed crossings in that pattern, so no degree threshold reproduces it exactly. At the default of 6, the crane gets only the centre hole; at 4 it gets 13.

With every crease layer empty, the group shows the same Miura demo as the solver.

## Kangaroo version

`OrigamiSim_Kangaroo.gh` folds the same crease layers with [Kangaroo2](https://www.food4rhino.com/en/app/kangaroo-physics), which ships with Rhino 8. It reads the same layers, Anchors, Fold and Reset as the main definition, and its Output and Strain colours groups work the same way. The 3D Print group is not included. The main definition is unchanged.

Kangaroo has no masses, time step or damping. Every constraint is a *goal*, a position the points would like to be in. The Solver moves each point to a weighted average of what its goals ask for, and repeats until the points stop moving.

| Group | What it does |
|---|---|
| Kangaroo goals | **Origami Goals** builds the same triangle mesh as the main solver. It outputs one *OrigamiCrease* goal per crease. Every mesh edge goes to Kangaroo's **Length(Line)** goal, which keeps the panels rigid. Anchors go to Kangaroo's **Anchor** goal, then through **Clean Tree**, because Anchor sends out an empty goal when there are no anchor points and the Solver fails on it. **Show** carries the mesh through the Solver so it comes out folded. |
| Kangaroo solver | Kangaroo's **Solver**. **On** = true keeps it iterating, so the model animates by itself and no timer is needed. Reset rebuilds it flat. |
| Readout | Takes the folded mesh out of the Solver. It measures strain and every fold angle from the geometry, the same way the main solver does. |

**Why creases are a custom goal.** Kangaroo's own Hinge goal measures the angle between −180° and 180°. When a crease nears a full fold, the Solver overshoots past 180°, the angle jumps to about −180° and the Hinge pushes the wrong way. In tests it flipped or stalled anywhere above about 150–165°. OrigamiCrease measures the angle with the web app's formula, inside a window on its own mountain or valley side. It moves the four points the way the web app's crease forces do. Those moves have no net force or twist, so a sheet whose creases can't all reach their targets settles instead of spinning. Edges, anchors, display and the Solver are stock Kangaroo.

| Slider | Default | What it does |
|---|---|---|
| Crease Strength | 3 | Weight of the mountain/valley goals, scaled by crease length. |
| Facet Strength | 3 | Weight of the flat-crease goals (Facet lines and triangulation diagonals). |
| Edge Strength | 100 | Weight of the Length goals. Higher means less stretch. |
| Anchor Strength | 1000 | Weight of the Anchor goals. |

Only the ratios between strengths matter. With Crease and Facet at 1 the edges stretch a little less, but a large pattern takes about twice as long to settle.

**Readout Info** reports `iterations`, `meanStrain%`, `maxStrain%`, `maxThetaErrDeg` (mountain/valley creases), `maxFacetDeg` (bending of flat creases), `mvSenseOk` and `finite`. `settled=true` means every mountain/valley crease is within 0.5° of its target. Partway through a fold, a pattern like the Miura cannot meet every target at once, so it can be still while `settled` is false.

### Compared with the main definition

Each case starts flat. "Settle" means no point moves more than 1e-6 × the sheet diagonal during one Solver pass. D is the largest difference, over all pairs of vertices, between their distances in the two results. It needs no alignment, and it is 0 when the shapes are identical.

| Pattern | Fold | D (% of diagonal) | Main settle | Kangaroo settle | Main strain | Kangaroo strain |
|---|---|---|---|---|---|---|
| Single crease | 0.5 | 0 % | 147 ms | 200 ms | 0 % | 0 % |
| Single crease | 1 | 0 % | 158 ms | 145 ms | 0 % | 0 % |
| Miura 4×4 | 0.5 | 7.4 % | 1.1 s | 0.33 s | 1.96 % | 0.37 % |
| Miura 4×4 | 1 | 0 % | 1.2 s | 0.20 s | 0 % | 0 % |
| Miura 12×12 | 0.5 | 7.1 % | 8.9 s | 1.8 s | 2.73 % | 0.54 % |
| Miura 12×12 | 1 | 0.004 % | 5.6 s | 0.45 s | 0 % | 0 % |

When every crease can reach its target (a single crease, or any full fold), both give the same shape. Partway through a Miura fold they differ by about 7 % of the sheet size. The main solver lets edges stretch about five times more, so its creases get closer to their targets. Kangaroo keeps the panels nearly rigid. Kangaroo settles 4–12 times faster.

![The main solver (left) and the Kangaroo version (right), Miura 12×12 at Fold 0.5, coloured by strain](captures/kangaroo-vs-v1-miura12-fold50.jpg)

**Behaviour to expect:**
- A free sheet that is unfolded back to Fold 0 is flat but may be tilted in space, because nothing holds it in place. Toggle Reset, or add an anchor.
- Unfolding a pattern from a full fold back to flat takes longer (about 3 s on Miura 12×12), because fully folded creases start with no leverage.
- The Kangaroo Solver merges points closer than its Tolerance, so the builder moves the extra vertex at each cut 0.1 × the document tolerance into its own panel. That keeps the two sides of a cut apart.

![The Kangaroo definition](captures/kangaroo-canvas.png)

## Troubleshooting

- **The mesh explodes or `finite=false`.** Toggle Reset. Then lower `AXIAL` or `CREASE`, or raise `DAMP`.
- **Nothing folds or `build failed: no closed faces`.** Lines probably do not meet, or the Border is missing. Check them with `_Intersect` or by raising the document tolerance.
- **My pattern is ignored and the demo shows.** Check the layer names (`Border`, `Mountain`, `Valley`, `Facet`, `Cut`, `Hinge`) and that the objects are not hidden.
- **It folds the wrong way.** Swap the M and V inputs, or set Fold negative.
- **It is slow.** Solve time grows with vertex count. Patterns with thousands of vertices run, but slowly.
- **The solver component is orange.** It shows warning CS1701, a harmless .NET assembly-version notice from Rhino 8. Wires from the layer pipelines are orange while those layers are empty.

## How it was built

The canvas was assembled headlessly through the Rhino MCP (`g1_*` tools for placement and wiring; `run_csharp` for the script code, groups and save). The build plan and verification results are in `plans/grasshopper-origami-solver.md`.

The 3D Print group was added the same way; its plan, design changes and test results are in `plans/grasshopper-print-export.md`.

The Kangaroo version was built the same way, starting from a copy of this definition. Its probes, design changes and test results are in `plans/grasshopper-kangaroo-solver.md`.
