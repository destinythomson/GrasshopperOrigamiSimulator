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

## Flower (curved creases)

`Flower.3dm` is "Artwork 14: Flower" from Jun Mitani's *Curved-Folding Origami Design* (CRC Press, 2019): four partial cones laid out toward the centre, their curves joined into a whirl. Solid lines are mountains, dashed are valleys; every crease carries `TargetAngleDeg` = 150, because a curved crease cannot fold flat. The four valley arcs end free inside the sheet, as drawn.

It was traced from the book's diagram (not included) by `flower_trace.py` (PIL + numpy: skeletonise the lines, average the four 90° rotations, write one sector to `flower.json`), and `build_flower_3dm.py` draws the four rotated copies as polylines on the Border, Mountain and Valley layers. The traced lines sit within 1 % of the sheet side of the book's drawing.

To fold it: open `Flower.3dm`, open `OrigamiSim.gh`, lower **Facet Strength** and ramp **Fold** up gradually. With this definition it does not reach its targets (mean crease error about 50° at Fold 1) and crumples. The reason is not the solver physics but the triangulation: the web app folds its curved examples (Comet, Rotating Star, this Flower) only through its curved-folding importer, which subdivides every line and re-triangulates each face toward ruling-like diagonals; this definition uses the web app's standard ear clipping, and so do the same files in the web app's standard import, where Comet stays flat. For the web app's curved importer the Flower also needs its valley ends extended to the mountain arms, because free-ended creases break that importer. See `plans/flower.md`.

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

Then run `build_examples_3dm.py` inside Rhino 8 with the ScriptEditor. Change `PICK` in that script to choose the patterns. The parser handles `line`, `path` (M/L/H/V/Z only), `polyline`, `polygon` and `rect`, along with group stroke inheritance and transforms. If a pattern uses curves (for example the upstream `assets/Curved` files), the parser raises an error.
