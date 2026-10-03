# Example crease patterns

`OrigamiExamples.3dm` holds 10 flat crease patterns from this repo's `assets/` folder, drawn as Rhino lines. Each pattern is scaled so its longest side is 100 units and laid out on a 5-column grid.

## Layers

Each crease type has its own layer. The colours match the web app's SVG convention (`js/pattern.js`, `typeForStroke`).

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

## Using one with `OrigamiSim.gh`

Both definitions read the crease layers directly through Geometry Pipelines (`*Mountain`, `*Valley` and so on), so there is nothing to set by hand.

1. Open `OrigamiExamples.3dm` (or import it into your model) and open `OrigamiSim.gh`.
2. Hide every object that is not part of the pattern you want, for example select its group, invert the selection and run `Hide`. Pipelines ignore hidden objects, so only the visible pattern is simulated.

`OrigamiSim_Kangaroo.gh` reads the same layers, so the same steps work with it.

## Regenerating

```bash
python grasshopper/examples/svg_to_segments.py grasshopper/examples/patterns.json "Name=assets/path.svg" ...
```

Then run `build_examples_3dm.py` inside Rhino 8 with the ScriptEditor. Change `PICK` in that script to choose the patterns. The parser handles `line`, `path` (M/L/H/V/Z only), `polyline`, `polygon` and `rect`, along with group stroke inheritance and transforms. If a pattern uses curves (for example the `assets/Curved` files), the parser raises an error.
