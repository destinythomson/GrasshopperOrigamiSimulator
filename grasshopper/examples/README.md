# Example crease patterns

`OrigamiExamples.3dm` holds 10 flat crease patterns from the original web app's [`assets/`](https://github.com/amandaghassaei/OrigamiSimulator/tree/7855983a613c879c171b2b1557f8cd102d2640cf/assets) folder, drawn as Rhino lines. Each pattern is scaled so its longest side is 100 units and laid out on a 5-column grid.

## Layers

Each crease type has its own layer. The colours match the web app's SVG convention ([`js/pattern.js`](https://github.com/amandaghassaei/OrigamiSimulator/blob/7855983a613c879c171b2b1557f8cd102d2640cf/js/pattern.js), `typeForStroke`).

| Layer | Colour | Solver input |
|---|---|---|
| Mountain | red | `M` |
| Valley | blue | `V` |
| Border | black | `B` |
| Facet | yellow | `F` (a flat crease held at 0° with the facet stiffness) |
| Cut | green | `C` (the sheet is split along it) |
| Hinge | magenta | `H` (an edge with no fold spring) |
| Labels | grey | none (ignored) |

These match the layer table in [`grasshopper/README.md`](../README.md#use-your-own-crease-pattern).

Each pattern is a Rhino group named after the pattern. Every line carries these User Text keys: `Pattern`, `CreaseType`, `TargetAngleDeg` and `Source`. `TargetAngleDeg` is 180 × the SVG stroke opacity. Both definitions read it, so partial-angle creases (for example in the crane) fold to their own angle at Fold = 1. The crane folds in both `OrigamiSim.gh` and `OrigamiSim_Kangaroo.gh` when Fold is ramped up gradually; see [Kangaroo version](../README.md#kangaroo-version).

## Patterns

| Pattern | Source | Notes |
|---|---|---|
| Simple Vertex | `SimpleFolds/simpleVertex.svg` | |
| Map Fold | `SimpleFolds/mapfold.svg` | |
| Waterbomb Base | `Bases/waterbombBase.svg` | partial-angle creases |
| Bird Base | `Bases/birdBase.svg` | partial-angle creases |
| Traditional Crane | `Origami/traditionalCrane.svg` | facet lines; creases at 45°, 90°, 135° and 180° |
| Square Twist | `Origami/singlesquaretwist.svg` | |
| Hypar | `Origami/hypar.svg` | 94 hinge lines |
| Miura-ori | `Tessellations/miura-ori.svg` | |
| Waterbomb Tessellation | `Tessellations/waterbomb.svg` | |
| Popup Simple | `Popup/popupSimple.svg` | cut lines (kirigami) |

## Curved creases: Flower and Rotating Star

Both files need the solver's curved-folding import: set **Vertex Interval** to 2, switch **Curved** on, lower **Facet Strength** (0.1–0.3) and ramp **Fold** slowly (0.05 per step, about 30 solves per step). See [Curved creases](../README.md#curved-creases) in the main README. With the controls off these patterns tangle, as they do in the web app's standard import.

### Flower

`Flower.3dm` is "Artwork 14: Flower" from Jun Mitani's *Curved-Folding Origami Design* (CRC Press, 2019): four partial cones laid out toward the centre, their curves joined into a whirl. Solid lines are mountains, dashed are valleys; every crease carries `TargetAngleDeg` = 150, because a curved crease cannot fold flat.

It was traced from the book's diagram (not included) by `flower_trace.py` (PIL + numpy: skeletonise the lines, average the four 90° rotations, write one sector to `flower.json`), and `build_flower_3dm.py` draws the four rotated copies as polylines on the Border, Mountain and Valley layers. The traced lines sit within 1 % of the sheet side of the book's drawing. In the artwork the four valley arcs end free inside the sheet; the builder's `EXTEND_VALLEYS` option (on) adds a straight valley line from each free end along the arc's tangent to the mountain arm (21.65 units), which is how the pattern was made to fold in the web app's curved importer (that importer cannot mesh a face with a free-ended crease). `EXTEND_KIND = "hinge"` draws the extension on the Hinge layer instead; it folds about the same.

Folded with Vertex Interval 2, Curved on, Facet Strength 0.1 and Fold ramped 0 → 1 in 20 steps of 30 solves: four lobes rise around the centre square, every one of the 296 mountain/valley creases bends the right way, strain 1.2 %, and the creases settle about 39° short of their 150° targets (mean crease error 38.7°, down from about 50° with the standard triangulation, where the sheet crumpled). The triangulation is a set of cones with rulings radiating from each lobe's apex (`captures/flower-curved-triangulation.png`). The fold stops short because the cones have to bend sharply near their apexes, not because of a tangle: at Facet Strength 1 the error is 60° and at 0.3 it is 50°, all with every crease on the right side.

![Flower at Fold 1](../captures/flower-fold1.png)

![Flower at Fold 1, from above](../captures/flower-fold1-top.png)

### Rotating Star

`RotatingStar.3dm` is the web app's `assets/Curved/Rotating_Star.svg`: a 12-sided sheet with 12 mountain and 24 valley Bezier curves at full 180° targets. `svg_curves_to_polylines.py` flattens each cubic Bezier the way the web app's curved importer does (split at the midpoint until the chord is within 0.05 units of the curve and shorter than 2 units) and writes `rotating_star.json`; `build_polylines_3dm.py` draws the polylines. Folded with the same settings (Facet Strength 0.3) it turns into the rotating star: all 736 creases on the right side, mean crease error 26.5° against targets a curved crease cannot reach. With the standard triangulation it tangles (mean error 57°, nine creases folded the wrong way).

![Rotating Star at Fold 1](../captures/rotating-star-fold1.png)

## Using one with `OrigamiSim.gh`

Both definitions read the crease layers directly through Geometry Pipelines (`*Mountain`, `*Valley` and so on), so there is nothing to set by hand.

1. Open `OrigamiExamples.3dm` (or import it into your model) and open `OrigamiSim.gh`.
2. Hide every object that is not part of the pattern you want, for example select its group, invert the selection and run `Hide`. Pipelines ignore hidden objects, so only the visible pattern is simulated.

`OrigamiSim_Kangaroo.gh` reads the same layers, so the same steps work with it.

## Regenerating

The source SVGs are not in this repo. Download the ones you need from the original [`assets/`](https://github.com/amandaghassaei/OrigamiSimulator/tree/7855983a613c879c171b2b1557f8cd102d2640cf/assets) folder (for example `https://raw.githubusercontent.com/amandaghassaei/OrigamiSimulator/7855983a613c879c171b2b1557f8cd102d2640cf/assets/Origami/traditionalCrane.svg`), then run:

```bash
python grasshopper/examples/svg_to_segments.py grasshopper/examples/patterns.json "Name=path/to/pattern.svg" ...
```

Then run `build_examples_3dm.py` inside Rhino 8 with the ScriptEditor. Change `PICK` in that script to choose the patterns. The parser handles `line`, `path` (M/L/H/V/Z only), `polyline`, `polygon` and `rect`, along with group stroke inheritance and transforms. If a pattern uses curves (for example the upstream `assets/Curved` files), the parser raises an error; use `svg_curves_to_polylines.py` for those:

```bash
python grasshopper/examples/svg_curves_to_polylines.py grasshopper/examples/rotating_star.json "Rotating Star=path/to/Rotating_Star.svg"
```

Then run `build_polylines_3dm.py` inside Rhino 8 with `JSON_PATH` set to that file. Curves become polylines, which the solver treats as curved creases (a plain Line is not one), so draw curved creases as polylines or curves, never as chains of separate lines.
