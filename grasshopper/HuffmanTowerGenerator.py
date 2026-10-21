#! python 3
"""Huffman Tower generator: the Python 3 script component in the "Huffman Tower generator" group of
OrigamiSim.gh (this file is a copy of its code, kept for reading and diffing).

Imports examples/huffman_tower.py from the folder next to the .gh file and turns its polylines into curves
for the Origami Solver, with the per-segment target angles as MA / VA lists. Inputs: Cells, Cap, P, Paper
(True: fold angles from Mundilova et al. 2024, each segment its own line; False: one polyline per curve
with ParDeg / CirDeg), ParDeg, CirDeg, Seam (strip ends on the Seam output instead of Border), Hinge
(cap fold lines as Hinge; False: Valley at CapLineDeg), CapLineDeg.
Outputs: M, V, B, F, H, S (curves), MA, VA (degrees), Info."""
import os, sys, importlib
import Rhino.Geometry as rg

def _examples_dir():
    try:
        path = ghenv.Component.OnPingDocument().FilePath
    except Exception:
        path = None
    if path:
        d = os.path.join(os.path.dirname(path), "examples")
        if os.path.isdir(d): return d
    return None

d = _examples_dir()
if d and d not in sys.path: sys.path.insert(0, d)
import huffman_tower
importlib.reload(huffman_tower)

cells = int(Cells) if Cells is not None else 6
cap = float(Cap) if Cap is not None else 1.6
P = int(P) if P is not None else 12
paper = bool(Paper) if Paper is not None else False
targets = "paper" if paper else (float(ParDeg) if ParDeg is not None else 54.0, float(CirDeg) if CirDeg is not None else 54.0)
cap_line = "hinge" if (Hinge if Hinge is not None else True) else "valley"
out = huffman_tower.build(cells=cells, cap=cap, P=P, targets=targets, cap_line=cap_line,
                          cap_line_angle=(float(CapLineDeg) if CapLineDeg is not None else None),
                          seam=bool(Seam) if Seam is not None else True)

def curves(key):
    res, angs = [], []
    for pl in out["layers"][key]:
        pts = [rg.Point3d(x, y, 0.0) for x, y in pl["pts"]]
        c = rg.LineCurve(pts[0], pts[1]) if len(pts) == 2 else rg.PolylineCurve(pts)
        res.append(c)
        angs.append(pl["angle"] if pl["angle"] is not None else 180.0)
    return res, angs

M, MA = curves("mountain")
V, VA = curves("valley")
B, _ = curves("border")
F, _ = curves("facet")
H, _ = curves("hinge")
S, _ = curves("seam")
m = out["meta"]
Info = "cells=%d cap=%.2f P=%d targets=%s s2(t_max)=%.4f counts=%s parabola_deg=%s circle_deg=%s" % (
    cells, cap, P, m["targets"], m["s2_max"], m["counts"], m["parabola_fold_deg"], m["circle_fold_deg"])
