"""Parse an Origami Simulator crease-pattern SVG that contains cubic Bezier paths (the upstream assets/Curved files)
into polylines grouped by crease type, for build_polylines_3dm.py.

Colour convention as in svg_to_segments.py (js/pattern.js typeForStroke); stroke opacity scales the fold angle.
Each path is one polyline: M starts it, L/H/V add straight vertices, C/S (cubic) and Q/T (quadratic) Beziers are
flattened the way the web app's curved-folding importer does (js/curvedFolding.js divPathC: split at the midpoint
until the chord deviates from the curve by less than CURVE_TOL and is shorter than CHORD). Lines, polylines,
polygons and rects become polylines too. The sheet is scaled so its longest side is SIDE units, y up.

Usage: python svg_curves_to_polylines.py out.json "Name=path/to/pattern.svg" [...]
"""
import json, math, re, sys
import xml.etree.ElementTree as ET
from svg_to_segments import stroke_type, style_dict, mat_mul, parse_transform, apply, num

SIDE = 100.0        # output sheet size (longest side), model units
CHORD = 2.0         # longest chord of a flattened curve, model units (the web app: globals.vertInt = 20 px on ~600-800 px sheets)
CURVE_TOL = 0.05    # largest chord-to-curve deviation, model units (the web app: globals.apprCurve = 0.2 px)
NUM = r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?"


def bez3(p0, p1, p2, p3, t):
    u = 1 - t
    return (u*u*u*p0[0] + 3*t*u*u*p1[0] + 3*t*t*u*p2[0] + t*t*t*p3[0],
            u*u*u*p0[1] + 3*t*u*u*p1[1] + 3*t*t*u*p2[1] + t*t*t*p3[1])


def flatten_cubic(p0, p1, p2, p3, out, scale):
    """Append the interior vertices and the end point of the cubic to out (divPathC in sheet units)."""
    mid = bez3(p0, p1, p2, p3, 0.5)
    dx, dy = p3[0] - p0[0], p3[1] - p0[1]
    chord = math.hypot(dx, dy)
    dev = abs(dx * (p0[1] - mid[1]) - dy * (p0[0] - mid[0])) / chord if chord > 0 else math.dist(p0, mid)
    if dev * scale > CURVE_TOL or chord * scale > CHORD:
        q0 = ((p0[0]+p1[0])/2, (p0[1]+p1[1])/2); q1 = ((p1[0]+p2[0])/2, (p1[1]+p2[1])/2); q2 = ((p2[0]+p3[0])/2, (p2[1]+p3[1])/2)
        r0 = ((q0[0]+q1[0])/2, (q0[1]+q1[1])/2); r1 = ((q1[0]+q2[0])/2, (q1[1]+q2[1])/2)
        flatten_cubic(p0, q0, r0, mid, out, scale)
        flatten_cubic(mid, r1, q2, p3, out, scale)
    else:
        out.append(p3)


def path_polylines(d, scale):
    toks = re.findall(r"[A-Za-z]|" + NUM, d)
    polys, i, cmd = [], 0, None
    cur = start = (0.0, 0.0)
    pts = []
    last_c2 = last_q1 = None
    def rd(n):
        nonlocal i
        v = [float(x) for x in toks[i:i + n]]; i += n
        return v
    def flush():
        nonlocal pts
        if len(pts) > 1: polys.append(pts)
        pts = []
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in "Zz":
                if pts and math.dist(cur, start) > 1e-9: pts.append(start)
                cur = start
                flush(); pts = [cur]
                continue
        rel = cmd.islower()
        if cmd in "Mm":
            x, y = rd(2)
            cur = (cur[0] + x, cur[1] + y) if rel else (x, y)
            start = cur
            flush(); pts = [cur]
            cmd = "l" if rel else "L"
            last_c2 = last_q1 = None
        elif cmd in "Ll":
            x, y = rd(2); cur = (cur[0] + x, cur[1] + y) if rel else (x, y); pts.append(cur); last_c2 = last_q1 = None
        elif cmd in "Hh":
            x = rd(1)[0]; cur = (cur[0] + x if rel else x, cur[1]); pts.append(cur); last_c2 = last_q1 = None
        elif cmd in "Vv":
            y = rd(1)[0]; cur = (cur[0], cur[1] + y if rel else y); pts.append(cur); last_c2 = last_q1 = None
        elif cmd in "CcSs":
            if cmd in "Cc":
                v = rd(6); c1 = (v[0], v[1]); c2 = (v[2], v[3]); p3 = (v[4], v[5])
            else:
                v = rd(4); c1 = (2*cur[0] - last_c2[0], 2*cur[1] - last_c2[1]) if last_c2 else cur; c2 = (v[0], v[1]); p3 = (v[2], v[3])
            if rel:
                if cmd in "Cc": c1 = (cur[0] + c1[0], cur[1] + c1[1])
                c2 = (cur[0] + c2[0], cur[1] + c2[1]); p3 = (cur[0] + p3[0], cur[1] + p3[1])
            flatten_cubic(cur, c1, c2, p3, pts, scale)
            cur = p3; last_c2 = c2; last_q1 = None
        elif cmd in "QqTt":
            if cmd in "Qq":
                v = rd(4); q1 = (v[0], v[1]); p2 = (v[2], v[3])
                if rel: q1 = (cur[0] + q1[0], cur[1] + q1[1]); p2 = (cur[0] + p2[0], cur[1] + p2[1])
            else:
                v = rd(2); q1 = (2*cur[0] - last_q1[0], 2*cur[1] - last_q1[1]) if last_q1 else cur; p2 = (v[0], v[1])
                if rel: p2 = (cur[0] + p2[0], cur[1] + p2[1])
            # quadratic -> cubic
            c1 = (cur[0] + 2/3*(q1[0]-cur[0]), cur[1] + 2/3*(q1[1]-cur[1])); c2 = (p2[0] + 2/3*(q1[0]-p2[0]), p2[1] + 2/3*(q1[1]-p2[1]))
            flatten_cubic(cur, c1, c2, p2, pts, scale)
            cur = p2; last_q1 = q1; last_c2 = None
        else:
            raise ValueError("unsupported path command " + cmd)
    flush()
    return polys


def walk(el, m, inherited, out, scale):
    tag = el.tag.split("}")[-1]
    st = style_dict(el)
    ctx = dict(inherited)
    if "stroke" in st: ctx["stroke"] = st["stroke"]
    op = 1.0
    for k in ("opacity", "stroke-opacity"):
        if k in st: op *= float(st[k])
    ctx["opacity"] = inherited.get("opacity", 1.0) * op
    m = mat_mul(m, parse_transform(el.get("transform")))
    polys = []
    if tag == "line":
        polys = [[(num(el, "x1"), num(el, "y1")), (num(el, "x2"), num(el, "y2"))]]
    elif tag == "path":
        polys = path_polylines(el.get("d", ""), scale)
    elif tag in ("polyline", "polygon"):
        v = [float(x) for x in re.findall(NUM, el.get("points", ""))]
        pts = list(zip(v[0::2], v[1::2]))
        if tag == "polygon" and len(pts) > 2: pts.append(pts[0])
        polys = [pts]
    elif tag == "rect":
        x, y, w, h = num(el, "x"), num(el, "y"), num(el, "width"), num(el, "height")
        polys = [[(x, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y)]]
    if polys:
        t = stroke_type(ctx.get("stroke"))
        if t is None:
            out.setdefault("_unknown", set()).add(ctx.get("stroke"))
        else:
            ang = 180.0 * ctx["opacity"] if t in ("mountain", "valley") else 0.0
            for p in polys:
                if len(p) > 1: out.setdefault(t, []).append(([apply(m, q) for q in p], ang))
    for ch in el:
        if ch.tag.split("}")[-1] not in ("defs", "namedview", "metadata"):
            walk(ch, m, ctx, out, scale)


def parse(path):
    root = ET.parse(path).getroot()
    # first pass at scale 1 to find the extent, second pass with the real scale so the flattening works in sheet units
    out = {}
    walk(root, [1, 0, 0, 1, 0, 0], {}, out, 1.0)
    pts = [q for t, lst in out.items() if t != "_unknown" for p, a in lst for q in p]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    x0, y1 = min(xs), max(ys)
    sc = SIDE / max(max(xs) - x0, y1 - min(ys))
    out = {}
    walk(root, [1, 0, 0, 1, 0, 0], {}, out, sc)
    unknown = out.pop("_unknown", set())
    if unknown: print("  warning: ignored strokes", unknown, file=sys.stderr)
    res = {}
    for t, lst in out.items():
        res[t] = [{"pts": [[round((q[0] - x0) * sc, 6), round((y1 - q[1]) * sc, 6)] for q in p], "angle": round(a, 3)} for p, a in lst]
    return res


if __name__ == "__main__":
    result = []
    for arg in sys.argv[2:]:
        name, path = arg.split("=", 1)
        polys = parse(path)
        print(name, {t: (len(v), sum(len(p["pts"]) for p in v)) for t, v in polys.items()})
        result.append({"name": name, "source": path, "side": SIDE, "polylines": polys})
    with open(sys.argv[1], "w") as f:
        json.dump(result, f)
