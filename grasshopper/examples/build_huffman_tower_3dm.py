"""Run inside Rhino 8 (ScriptEditor, Python 3, or through the Rhino MCP). Draws David Huffman's Hexagonal
Column with Cusps from huffman_tower.py (same folder) on the layers OrigamiSim.gh reads, with the User Text
keys of the other examples (Pattern, CreaseType, TargetAngleDeg, Source). Fold it with Vertex Interval 0 and
Curved off: the pattern carries its own rulings as Facet lines.

Parameters can be overridden by defining *_OVERRIDE names before exec-ing this file (as the MCP runs do):
CELLS (6), CAP (1.6), P (12), TARGETS ("paper" or (parabola_deg, circle_deg)), RULINGS ("ode"|"linear"),
CAP_LINE ("hinge"|"valley"|"mountain"), CAP_LINE_ANGLE, SEAM (False), MODULE_DIR (folder of huffman_tower.py)."""
import os, sys
import Rhino
import Rhino.Geometry as rg
import System.Drawing as sd

g = globals()
CELLS = g.get("CELLS_OVERRIDE", 6)
CAP = g.get("CAP_OVERRIDE", 1.6)
P = g.get("P_OVERRIDE", 12)
TARGETS = g.get("TARGETS_OVERRIDE", "paper")
RULINGS = g.get("RULINGS_OVERRIDE", "ode")
CAP_LINE = g.get("CAP_LINE_OVERRIDE", "hinge")
CAP_LINE_ANGLE = g.get("CAP_LINE_ANGLE_OVERRIDE", None)
SEAM = g.get("SEAM_OVERRIDE", False)
MODULE_DIR = g.get("MODULE_DIR_OVERRIDE", os.path.dirname(os.path.abspath(__file__)) if "__file__" in g else None)

if MODULE_DIR and MODULE_DIR not in sys.path: sys.path.insert(0, MODULE_DIR)
import huffman_tower
try:
    import importlib; importlib.reload(huffman_tower)
except Exception:
    pass

try:
    doc = __rhino_doc__
except NameError:
    doc = Rhino.RhinoDoc.ActiveDoc

NAME = "Huffman Tower"
SOURCE = ("D. Huffman, Hexagonal Column with Cusps (1978); geometry after Mundilova, Demaine, Lang, Tachi, "
          "Analysis of Huffman's Hexagonal Column with Cusps, Origami^8 (2024)")
LAYERS = [("Border", "border", (0, 0, 0)), ("Mountain", "mountain", (255, 0, 0)),
          ("Valley", "valley", (0, 0, 255)), ("Facet", "facet", (255, 255, 0)),
          ("Hinge", "hinge", (255, 0, 255)), ("Seam", "seam", (0, 160, 0))]
out = huffman_tower.build(cells=CELLS, cap=CAP, P=P, targets=TARGETS, rulings=RULINGS,
                          cap_line=CAP_LINE, cap_line_angle=CAP_LINE_ANGLE, seam=SEAM)
idx = {}
for name, key, c in LAYERS:
    if not out["layers"][key]: continue
    i = doc.Layers.FindByFullPath(name, -1)
    if i < 0:
        L = Rhino.DocObjects.Layer()
        L.Name = name
        L.Color = sd.Color.FromArgb(*c)
        L.PlotColor = L.Color
        i = doc.Layers.Add(L)
    idx[key] = i

gi = doc.Groups.Add(NAME)
count = {}
for key, polys in out["layers"].items():
    for pl in polys:
        at = Rhino.DocObjects.ObjectAttributes()
        at.LayerIndex = idx[key]
        at.Name = "%s / %s" % (NAME, key)
        at.SetUserString("Pattern", NAME)
        at.SetUserString("CreaseType", key)
        if pl["angle"] is not None: at.SetUserString("TargetAngleDeg", "%.4f" % pl["angle"])
        at.SetUserString("Source", SOURCE)
        at.AddToGroup(gi)
        pts = [rg.Point3d(x, y, 0) for x, y in pl["pts"]]
        if len(pts) == 2: doc.Objects.AddLine(rg.Line(pts[0], pts[1]), at)
        else: doc.Objects.AddPolyline(pts, at)
        count[key] = count.get(key, 0) + 1
        count["vertices"] = count.get("vertices", 0) + len(pts)
doc.Views.Redraw()
RESULT = {"objects": count, "meta": out["meta"]}
sys.stderr.write("huffman tower: %s\n" % count)
