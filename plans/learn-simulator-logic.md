# learn-simulator-logic

### Intent
Teach the user how and why OrigamiSimulator works — physical model (particles + beams + crease angular springs + face angle constraints driven toward fold-percent target angles), the numerical solver (force sum → integrate → damp, run as GPU passes), and the surrounding pipeline (import, triangulation, curved folding, inactive static/rigid solvers). Pseudocode over real code. Format: Markdown lessons in `learn/`, each ending in quiz questions the user answers in chat for grading. User level: calculus + basic physics. Goal: general understanding.

### Criteria & scoring axes
- Predict behavior — test: ≥80% on per-lesson predict questions without looking back; predictions checkable in the live app (origamisimulator.org).
- Source-faithful — test: every claim/pseudocode block cites `js/` file:line, `index.html` shader, or the 7OSME paper, verified before marking done.
- Bite-sized — test: ~20–30 min/lesson, ≤3 new concepts, prerequisites only earlier lessons.
Axes: goal-likelihood, fidelity, digestibility.

### Convergence
Converged at loop 5. Final scores: goal 9, fidelity 9, digest 9.

### Lesson format
**Changed in Phase 5 (user request): lessons are HTML, not Markdown.** `learn/NN-slug.html`, shared `learn/lesson.css` (light/dark tokens, dataviz reference palette), `learn/index.html` TOC, `learn/glossary.html`. Each lesson: inline SVG diagrams + at least one interactive demo/chart where it teaches (with hover tooltip, legend). Preview: `.claude/launch.json` "lessons" (python http.server :8765). Pacing: one lesson at a time, next written after the previous quiz is graded.
Intuition → Model → Pseudocode → Try it (live app) → Code vs ideal → Quiz (2 recall + 2 predict).
**Changed after L2 quiz (user: "too much physics"): from L3 on, concepts first.** Plain words + visuals + demos lead; formulas and pseudocode go in collapsed `<details class="math">` "Show the math" boxes; quizzes test ideas/predictions only, never formula recall. L1–L2 left as-is (user choice). Running example: single valley fold (1 crease, 2 triangles, 4 nodes) through L1–L7.

### Lessons
0 Big picture · 1 Newton/Euler · 2 Beams · 3 dt & stability · 4 Dihedral angle · 5a Crease angular spring · 5b Torque → node forces · 6 Face angle constraints · 7 Full step + strain · 8 GPU parallelism · 9 Euler vs Verlet · 10 Pattern → mesh · 11 Curved folding (capped) · 12 Why not solve directly (inactive static/rigid) · 13 Capstone

### Key source facts (verified)
- Node mass = 1 (`js/node.js:205`). Beam k = axialStiffness/L; d = ζ·2√(k·m_min) (`js/beam.js`).
- dt = 0.9/(2π·ω_max) over beams (`js/dynamic/dynamicSolver.js:244`).
- Crease k = (creaseStiffness | panelStiffness)·L (`js/crease.js`); target = targetTheta·creasePercent; crease damping commented out in shader.
- solveStep order: normalCalc → thetaCalc → updateCreaseGeo → velocityCalc → positionCalc (Euler default) (`dynamicSolver.js:131`); 100 steps/frame (`js/globals.js`).
- Static/rigid solvers disabled (`js/main.js:58-60`).
- Strain colour: HSL hue (1−err/strainClip)·0.7 → blue = 0 strain, red = clip (`dynamicSolver.js:212`). Crease slider label "Fold Stiffness", facet "Facet Crease Stiffness".
- Import (agent summary, re-verify per claim when writing L10/L11): stroke colour → type (`pattern.js:136-145`); opacity → angle (`:75,:85`); cleanup = merge verts (vertTol) → split intersections (`:1160`) → remove stray/redundant verts; faces via FOLD lib; cuts split (`:671`); triangulate: quads shorter diagonal, earcut otherwise, new edges = F angle 0 (`:984-1135`); creaseParams only for M/V/F (`:820`). Curved: Bézier subdivision by apprCurve/vertInt, cdt2d + greedy edge flips scored by orthogonality to curved creases (`curvedFolding.js:2551-2725`); output = ordinary short straight M/V creases.

### Steps
1. Glossary + L0 — fixes vocabulary and running example; over per-lesson definitions (drift). Risk: abstract until L1–3.
2. L1–L7 physics arcs, verified vs shader math — core how/why; over file-by-file tour. Risk: L5b shows code formula + intuition, flagged as not a full derivation.
3. L8–L9 GPU + integrators — explains 100 steps/frame and stiffness blow-ups; over skipping. Risk: concepts only.
4. L10–L11 from verified agent summary — over full curved-folding coverage (budget). Risk: L11 may feel thin.
5. L12 design-rationale lesson — over omitting dead code. Risk: label intent vs implemented.
6. L13 capstone.
7. Quiz loop in chat per lesson.

### Execute at
medium — content is pinned by plan + source; only L5b/L6 derivations need care.

### Execution checklist
- [x] glossary + L0
- [x] L1 Newton/Euler
  - L0 quiz (2026-10-02): Q1 2/4 (unclear on beam = length only, crease = drives angle), Q2 ✓, Q3 half (strain meaning), Q4 right answer/wrong reason (ΣF=0 → mass drops out). Predict ≈ 1.25/2. Revisit strain in L7; dt∝√m invariance in L3.
  - L1 quiz: 4/4 (Q2 wording: v,a aren't forces). Predict 2/2 ✓. Key insight reinforced: forces travel only via positions.
- [x] L2 Beams
  - L2 quiz: Q1 skipped (too mathy), Q2 ✓, Q3 ✗ (thought dampers stop uniform motion — they only see relative velocity), Q4 ✓. Predict 1/2.
- [x] L3 dt & stability (first concepts-first lesson; ~44 snapshots/wiggle; damping slider range 0.01–0.5; crease sliders capped at 3)
  - L3 quiz: Q1 ✓, Q2 ✓ (missing snowball link), Q3 half (right "slower", reasoning inverted: stiffer → smaller dt → less sim time/frame; skipped shape), Q4 ✓. Predict 1.5/2.
- [x] L4 Dihedral angle
  - L4 quiz: Q1 ✓, Q2 ✗ (thought raw reading "inaccurate"; it's ambiguous ±180), Q3 half (normals DO rotate; relative angle doesn't), Q4 ✗ (didn't reason spring's reaction to wrapped reading). Predict ~0.5/2. Theme: how spring responds to measurement → recap box in L5a.
- [x] L5a Crease angular spring
  - L5a quiz: Q1 ✓, Q2 half (missed stiffness factor), Q3 half (k=0 → twist 0 right, but missed consequence: panels bend freely), Q4 ✓. Predict 1.5/2. Pattern: computes right, misses "removing a spring frees motion".
- [x] L5b Torque → node forces (forces = exact dihedral-angle gradient, Schenk&Guest/Tachi; added --accent-4 violet token)
- [ ] L6 Face constraints
- [ ] L7 Full step + strain
- [ ] L8 GPU
- [ ] L9 Euler vs Verlet
- [ ] L10 Pattern → mesh
- [ ] L11 Curved folding
- [ ] L12 Why not solve directly
- [ ] L13 Capstone
