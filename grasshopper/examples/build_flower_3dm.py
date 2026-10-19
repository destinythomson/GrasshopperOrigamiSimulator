"""Run inside Rhino 8 (ScriptEditor, Python 3, or through the Rhino MCP). Reads flower.json (from
flower_trace.py) and draws the Flower crease pattern: one 90-degree sector rotated four times about
the sheet centre, on the Border, Mountain and Valley layers OrigamiSim.gh reads. Set JSON_PATH before running
(or define JSON_PATH_OVERRIDE before exec-ing this file).

EXTEND_VALLEYS: in the artwork each valley arc ends free inside the sheet. With this on, a straight line
continues the free end along the arc's end tangent until it meets a mountain line (about 21.7 units), so every
crease ends on another crease. The web app's curved-folding importer cannot mesh a face with a free-ended
crease, and the Flower folded there only with this extension; OrigamiSim.gh's Curved mode works either way,
but folds closer to the book with it. EXTEND_KIND is the layer of the extension ("valley" or "hinge")."""
import json, math
import Rhino
import Rhino.Geometry as rg
import System.Drawing as sd

JSON_PATH = globals().get("JSON_PATH_OVERRIDE", r"/path/to/grasshopper/examples/flower.json")
EXTEND_VALLEYS = globals().get("EXTEND_VALLEYS_OVERRIDE", True)
EXTEND_KIND = globals().get("EXTEND_KIND_OVERRIDE", "valley")

try:
    doc = __rhino_doc__
except NameError:
    doc = Rhino.RhinoDoc.ActiveDoc

LAYERS = [("Border", "border", (0, 0, 0)), ("Mountain", "mountain", (255, 0, 0)),
          ("Valley", "valley", (0, 0, 255)), ("Hinge", "hinge", (255, 0, 255))]
idx = {}
for name, key, c in LAYERS:
    if key == "hinge" and not (EXTEND_VALLEYS and EXTEND_KIND == "hinge"): continue
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

def extend_free_end(pts, mountains):
    """Ray from the last valley vertex along the last segment; return the nearest hit on a mountain polyline."""
    a, b = pts[-2], pts[-1]
    d_ = b - a
    d_.Unitize()
    best = None
    for m in mountains:
        for i in range(len(m) - 1):
            p, q = m[i], m[i + 1]
            e = q - p
            den = d_.X * e.Y - d_.Y * e.X
            if abs(den) < 1e-12: continue
            w = p - b
            t = (w.X * e.Y - w.Y * e.X) / den     # along the ray
            u = (w.X * d_.Y - w.Y * d_.X) / den   # along the segment
            if t > 1e-6 and -1e-9 <= u <= 1 + 1e-9 and (best is None or t < best[0]):
                best = (t, b + d_ * t)
    return best

corners = [rg.Point3d(0, 0, 0), rg.Point3d(S, 0, 0), rg.Point3d(S, S, 0), rg.Point3d(0, S, 0)]
for i in range(4): add("border", [corners[i], corners[(i + 1) % 4]])
mountains = []
for k in range(4):
    mountains.append([rot(p, k) for p in d["arm"]])
    mountains.append([rot(p, k) for p in d["centre_side"]])
for m in mountains: add("mountain", m, d["target_deg"])
ext_len = None
for k in range(4):
    v = [rot(p, k) for p in d["valley"]]
    if EXTEND_VALLEYS:
        hit = extend_free_end(v, mountains)
        if hit is None: raise Exception("valley end tangent misses every mountain line")
        ext_len = hit[0]
        # a separate straight line, as in the web app test (one uncut edge): the solver cuts curved polylines at
        # the Vertex Interval but leaves straight lines whole
        add(EXTEND_KIND, [v[-1], hit[1]], d["target_deg"] if EXTEND_KIND == "valley" else None)
    add("valley", v, d["target_deg"])
if EXTEND_VALLEYS: print("valley free ends extended %.2f units along their tangent (%s)" % (ext_len, EXTEND_KIND))
verts = 4 + 4 * (len(d["arm"]) + len(d["centre_side"]) - 2 + len(d["valley"]) + (1 if EXTEND_VALLEYS else 0))
print("objects per layer", count, "; approx. vertices", verts)
doc.Views.Redraw()
