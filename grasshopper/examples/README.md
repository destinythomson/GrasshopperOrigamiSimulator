# Example crease patterns

`OrigamiExamples.3dm` holds 10 flat crease patterns from this repo's `assets/` folder, drawn as Rhino lines. Each pattern is scaled so its longest side is 100 units and laid out on a 5-column grid.

## Layers

Each crease type has its own layer. The colours match the web app's SVG convention (`js/pattern.js`, `typeForStroke`).

| Layer | Colour | Solver input |
|---|---|---|
| Mountain | red | `M` |
| Valley | blue | `V` |
| Border | black | `B` |
| Facet | yellow | `B` (a flat crease) |
| Cut | green | not supported by the solver |
| Hinge | magenta | not supported (an undriven crease) |
| Labels | grey | none |

Each pattern is a Rhino group named after the pattern. Every line carries these User Text keys: `Pattern`, `CreaseType`, `TargetAngleDeg` and `Source`. `TargetAngleDeg` is 180 × the SVG stroke opacity. The solver currently uses one fixed angle, so partial-angle creases (for example in the crane) fold to 180°.

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

1. Turn off every layer except **Mountain**.
2. Right-click the `M` Line parameter, choose **Set Multiple Lines**, and window-select the pattern you want.
3. Repeat with only **Valley** on for `V`, then with **Border** and **Facet** on for `B`.

## Regenerating

```bash
python grasshopper/examples/svg_to_segments.py grasshopper/examples/patterns.json "Name=assets/path.svg" ...
```

Then run `build_examples_3dm.py` inside Rhino 8 with the ScriptEditor. Change `PICK` in that script to choose the patterns. The parser handles `line`, `path` (M/L/H/V/Z only), `polyline`, `polygon` and `rect`, along with group stroke inheritance and transforms. If a pattern uses curves (for example the `assets/Curved` files), the parser raises an error.
