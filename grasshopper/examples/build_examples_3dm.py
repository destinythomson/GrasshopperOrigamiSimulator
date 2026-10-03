"""Run inside Rhino 8 (ScriptEditor, Python 3). Reads patterns.json (from svg_to_segments.py) and
writes OrigamiExamples.3dm: one layer per crease type, one group + label per pattern, 5-column grid."""
import json, os
import Rhino
import Rhino.Geometry as rg
import System.Drawing as sd

try:
    doc = __rhino_doc__
except NameError:
    doc = Rhino.RhinoDoc.ActiveDoc

HERE = r"C:\Users\desti\Documents\Coding\GrasshopperOrigamiSimulator\grasshopper\examples"
PICK = ["Simple Vertex", "Map Fold", "Waterbomb Base", "Bird Base", "Traditional Crane",
        "Square Twist", "Hypar", "Miura-ori", "Waterbomb Tessellation", "Popup Simple"]
# colours = the SVG stroke convention in js/pattern.js typeForStroke
LAYERS = [("Border", "border", (0, 0, 0)), ("Mountain", "mountain", (255, 0, 0)),
          ("Valley", "valley", (0, 0, 255)), ("Facet", "facet", (255, 255, 0)),
          ("Cut", "cut", (0, 255, 0)), ("Hinge", "hinge", (255, 0, 255)),
          ("Labels", "labels", (90, 90, 90))]

for L in list(doc.Layers):  # drop the template's empty layers
    if L.Name and L.Name.startswith("Layer 0") and not L.IsDeleted:
        doc.Layers.Delete(L.Index, True)
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

data = {p["name"]: p for p in json.load(open(os.path.join(HERE, "patterns.json")))}
SP, COLS = 150.0, 5
for n, name in enumerate(PICK):
    p = data[name]
    ox, oy = (n % COLS) * SP, -(n // COLS) * SP * 1.2
    gi = doc.Groups.Add(name)
    for t, segs in p["segments"].items():
        for x1, y1, x2, y2, ang in segs:
            at = Rhino.DocObjects.ObjectAttributes()
            at.LayerIndex = idx[t]
            at.Name = "%s / %s" % (name, t)
            at.SetUserString("Pattern", name)
            at.SetUserString("CreaseType", t)
            at.SetUserString("TargetAngleDeg", str(ang))  # 180 * stroke opacity, mountain/valley only
            at.SetUserString("Source", p["source"])
            at.AddToGroup(gi)
            doc.Objects.AddLine(rg.Line(ox + x1, oy + y1, 0, ox + x2, oy + y2, 0), at)
    at = Rhino.DocObjects.ObjectAttributes()
    at.LayerIndex = idx["labels"]
    at.Name = name + " / label"
    at.AddToGroup(gi)
    org = rg.Point3d(ox, oy - 12, 0)
    tid = doc.Objects.AddText(name, rg.Plane(org, rg.Vector3d.ZAxis), 6.0, "Arial", False, False, at)
    # the template's annotation scale inflates text; rescale so the label is 8 units tall, top at org
    bb = doc.Objects.FindId(tid).Geometry.GetBoundingBox(True)
    k = 8.0 / (bb.Max.Y - bb.Min.Y)
    doc.Objects.Transform(tid, rg.Transform.Translation(org - rg.Point3d(bb.Min.X * k, bb.Max.Y * k, 0)) *
                          rg.Transform.Scale(rg.Point3d.Origin, k), True)

print("objects", doc.Objects.Count)
