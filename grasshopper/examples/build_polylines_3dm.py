"""Run inside Rhino 8 (ScriptEditor, Python 3, or through the Rhino MCP). Reads a JSON written by
svg_curves_to_polylines.py and draws every pattern in it as polylines on the layers OrigamiSim.gh reads,
with the same User Text keys as build_examples_3dm.py (Pattern, CreaseType, TargetAngleDeg, Source).
Set JSON_PATH (or define JSON_PATH_OVERRIDE before exec-ing this file). Several patterns are laid out
on a grid; keep one per file for use with the simulator, or hide the others."""
import json
import Rhino
import Rhino.Geometry as rg
import System.Drawing as sd

JSON_PATH = globals().get("JSON_PATH_OVERRIDE", r"/path/to/pattern_polylines.json")
SPACING, COLS = 150.0, 5

try:
    doc = __rhino_doc__
except NameError:
    doc = Rhino.RhinoDoc.ActiveDoc

LAYERS = [("Border", "border", (0, 0, 0)), ("Mountain", "mountain", (255, 0, 0)),
          ("Valley", "valley", (0, 0, 255)), ("Facet", "facet", (255, 255, 0)),
          ("Cut", "cut", (0, 255, 0)), ("Hinge", "hinge", (255, 0, 255))]
idx = {}
for name, key, c in LAYERS:
    i = doc.Layers.FindByFullPath(name, -1)
    if i < 0:
        L = Rhino.DocObjects.Layer()
        L.Name = name
        L.Color = sd.Color.FromArgb(*c)
        L.PlotColor = L.Color
        i = doc.Layers.Add(L)
    idx[key] = i

count = {}
for n, pat in enumerate(json.load(open(JSON_PATH))):
    name = pat["name"]
    ox, oy = (n % COLS) * SPACING, -(n // COLS) * SPACING * 1.2
    gi = doc.Groups.Add(name)
    for kind, polys in pat["polylines"].items():
        for pl in polys:
            at = Rhino.DocObjects.ObjectAttributes()
            at.LayerIndex = idx[kind]
            at.Name = "%s / %s" % (name, kind)
            at.SetUserString("Pattern", name)
            at.SetUserString("CreaseType", kind)
            if kind in ("mountain", "valley"): at.SetUserString("TargetAngleDeg", str(pl["angle"]))
            at.SetUserString("Source", pat["source"])
            at.AddToGroup(gi)
            pts = [rg.Point3d(ox + x, oy + y, 0) for x, y in pl["pts"]]
            if len(pts) == 2: doc.Objects.AddLine(rg.Line(pts[0], pts[1]), at)
            else: doc.Objects.AddPolyline(pts, at)
            count[kind] = count.get(kind, 0) + 1
            count["vertices"] = count.get("vertices", 0) + len(pts)
print("objects per layer", count)
doc.Views.Redraw()
