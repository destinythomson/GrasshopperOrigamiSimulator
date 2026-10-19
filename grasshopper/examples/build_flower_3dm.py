"""Run inside Rhino 8 (ScriptEditor, Python 3, or through the Rhino MCP). Reads flower.json (from
flower_trace.py) and draws the Flower crease pattern: one 90-degree sector rotated four times about
the sheet centre, on the Border, Mountain and Valley layers OrigamiSim.gh reads. Set JSON_PATH before running."""
import json, math
import Rhino
import Rhino.Geometry as rg
import System.Drawing as sd

JSON_PATH = r"/path/to/grasshopper/examples/flower.json"

try:
    doc = __rhino_doc__
except NameError:
    doc = Rhino.RhinoDoc.ActiveDoc

LAYERS = [("Border", "border", (0, 0, 0)), ("Mountain", "mountain", (255, 0, 0)),
          ("Valley", "valley", (0, 0, 255))]
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

d = json.load(open(JSON_PATH))
S, name = d["side"], d["pattern"]
c = (S / 2.0, S / 2.0)
def rot(p, k):
    x, y = p[0] - c[0], p[1] - c[1]
    for _ in range(k): x, y = -y, x
    return rg.Point3d(x + c[0], y + c[1], 0)

gi = doc.Groups.Add(name)
count = {}
def add(kind, pts, angle=None):
    at = Rhino.DocObjects.ObjectAttributes()
    at.LayerIndex = idx[kind]
    at.Name = "%s / %s" % (name, kind)
    at.SetUserString("Pattern", name)
    at.SetUserString("CreaseType", kind)
    if angle is not None: at.SetUserString("TargetAngleDeg", str(angle))
    at.SetUserString("Source", d["source"])
    at.AddToGroup(gi)
    if len(pts) == 2: doc.Objects.AddLine(rg.Line(pts[0], pts[1]), at)
    else: doc.Objects.AddPolyline(pts, at)
    count[kind] = count.get(kind, 0) + 1

corners = [rg.Point3d(0, 0, 0), rg.Point3d(S, 0, 0), rg.Point3d(S, S, 0), rg.Point3d(0, S, 0)]
for i in range(4): add("border", [corners[i], corners[(i + 1) % 4]])
for k in range(4):
    add("mountain", [rot(p, k) for p in d["arm"]], d["target_deg"])
    add("mountain", [rot(p, k) for p in d["centre_side"]], d["target_deg"])
    add("valley", [rot(p, k) for p in d["valley"]], d["target_deg"])
verts = 4 + 4 * (len(d["arm"]) + len(d["centre_side"]) - 2 + len(d["valley"]))
print("objects per layer", count, "; approx. vertices", verts)
doc.Views.Redraw()
