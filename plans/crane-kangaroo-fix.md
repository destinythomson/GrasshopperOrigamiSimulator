## crane-kangaroo-fix — the Traditional Crane in OrigamiSim_Kangaroo.gh (follow-up to crane-fold-fix.md, K1)

### Intent
After crane-fold-fix, `OrigamiSim_Kangaroo.gh` built the same crane mesh as the dynamic solver (60 nodes, 104 faces, `targets=-180x50,-135x4,-45x10,45x8,90x2,135x4,180x33`). But ramping Fold 0→1 (50 × 18 solves, hold 100) ended at mvSenseOk 84/111, maxErr 226–266° and facets 162°. Make it fold the crane without changing the Miura demo, Simple Vertex, Map Fold, Square Twist, Miura-ori or Waterbomb Tessellation results (mvSenseOk all, settled at Fold 1).

### Root cause (2026-10-03, slot `aardvark`)
- **Not the stepping scheme.** A direct-stepping harness (`PhysicalSystem` driven with the builder's goals, solver component locked) reproduced the canvas exactly: 84/111, 225.71°, 162.53°. `MomentumStep(0.98)` lands on the same state, so BouncySolver-style momentum is not the lever.
- **Not stretchiness alone.** Edge Strength 30 / 10 / 3 → 85 / 92 / 85 of 111.
- **Not angle unwrapping.** θ history shared through the builder cache (v1's unwrap) → 79–82/111.
- **The energy differs from v1's.** Per-crease angle dumps at Fold 0.1–1 (dynamic vs Kangaroo, same vertex order):
  - at Fold 0.1 the dynamic creases are already near their targets while Kangaroo's sit at half;
  - creases flip sign from 0.3;
  - facets snap to 110°+ by 0.4.

  A projection goal with weight w is a spring of stiffness w / |∇r|² on its error r. OrigamiCrease had w = CreaseK · L / L̄, so its stiffness was ∝ L · h² (h = wing heights). Thin-winged creases went limp. The ±30° per-iteration cap also flattened large errors. The dynamic solver additionally has face corner-angle springs (FACE = 0.2), which Kangaroo lacked.

### Fix (`grasshopper/OrigamiKangaroo.cs`)
- Every goal weight = v1 stiffness × |∇r|² × Unit, with Unit = EDGE_REF / mean(2 · AXIAL / L0):
  - edges: Length(Line) strength `EdgeW` = EdgeK / EDGE_REF · 2 · AXIAL / L0 · Unit (|∇r|² = 2 for a Length goal);
  - creases: k = CreaseK · CREASE · L0 · Unit (FacetK · FACET for flat creases); the goal sets `Weighting = k · gg` each iteration;
  - corners: new `OrigamiCorner` goal (3 per triangle) with k = FACE · radius · Unit. v1's face term has no length factor, and v1 works at radius 1.
- `MAXROT_DEG` 15 → 90 (a ±180° cap).
- Scale-free: every weight is invariant under scaling the pattern.
- Canvas:
  - builder gains input `EdgeK` (← Edge Strength) and output `EdgeW` (→ Length(Line).Strength);
  - Crease / Facet Strength defaults 3 → 1, now multipliers of the web app's stiffness;
  - old file archived as `archive/OrigamiSim_Kangaroo_v2.gh`.

Ablation (crane, direct harness, Readout rule):

| Variant | Sense | maxErr | meanErr |
|---|---|---|---|
| all | 93/111 | 90.41° | 11.09° |
| + θ history | 93/111 | 90.41° | 11.09° |
| cap 15° | 84/111 | 212° | 24.9° |
| no gg weights | 82/111 | 270° | 88.6° |
| no corner goals | 66/111 | 256° | 61.0° |
| uniform edges | 94/111 | 87.5° | 12.49° |

θ history was dropped as unnecessary.

### Verification (2026-10-03, canvas harness: real Kangaroo Solver, ramp 50 × 18, hold 100)
- **Crane:**
  - 60 nodes / 104 faces, same `targets=`, finite;
  - maxErr 90.41° (web 87.1°, +3.8 %), meanErr 11.09° (web 10.9°, +1.7 %), facets ≤ 40°;
  - rigidly aligned to the dynamic crane: RMS 1.45 % of the diagonal, max per-crease |Δθ| 6.1°, mean 1.5°;
  - capture `grasshopper/captures/crane-dynamic-vs-kangaroo-fold100.png`.
- **mvSenseOk = 93/111 is not all.** The Readout fails creases folded more than 5° past flat (review fix #3). All 18 failures are correct-side overshoots of 185–197°. The dynamic crane scores 95/111 under the same rule (its own Info says 111/111 because it checks only the side). Left as is: changing SENSE_SLACK_DEG would undo review fix #3.
- **Regression:** Miura demo, Simple Vertex, Map Fold, Square Twist, Miura-ori, Waterbomb Tessellation all reach mvSenseOk all, maxErr 0.00°, settled=true at Fold 1. Mid-fold strain rose from 0.2–0.5 % to 0.7–1.7 %, closer to v1's 1.8–4 %.
- **C2 (Miura 12×12):**
  - 0→0.8 settles in 517 / 469 ms (old 371 / 278 ms);
  - 0.8→1 takes 498 ms;
  - 1→0 takes 1,635 ms and returns flat (0.00°);
  - Reset holds the start position exactly.
- **C3:** reopened in fresh slot `armadillo`:
  - 0 errors (CS1701 only), 33 objects, 7 groups;
  - sliders 0.6 / 1 / 1 / 100 / 1000;
  - embedded source = `.cs`; KangarooSolver.dll reference kept; EdgeK / EdgeW wired.
- **C4 table re-measured** (README): D mid-fold 2.5 % (Miura 4×4) and 1.8 % (12×12), was 7.4 % / 7.1 %.

### Not done
- `captures/kangaroo-canvas.png` predates the EdgeK / EdgeW ports.
- `captures/kangaroo-vs-v1-miura12-fold50.jpg` uses the old weights; the README says so.
