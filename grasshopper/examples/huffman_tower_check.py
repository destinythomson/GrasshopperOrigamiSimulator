"""Checks the Huffman Tower pattern from huffman_tower.py (CPython 3, no Rhino needed).

    python3 huffman_tower_check.py [--svg path/to/huffmanTower.svg] [--png out.png] [--cells 6]

1. Every valley vertex is 1.000 from a cusp; every curved-mountain vertex satisfies the focus-directrix
   condition of its arch (distance to the cusp focus = 4 - distance along the strip to the directrix).
2. Every Facet ruling ends on crease vertices and crosses no crease or other ruling.
3. No two vertices are closer than 0.09 units (the solver merges vertices within 0.5 % of the pattern radius,
   about 0.066 here; 0.09 leaves a third of that as margin).
4. With --svg: overlay on the web app's assets/Curved/huffmanTower.svg (72 px per unit, translation fitted on
   the cap creases): every crease vertex is within 2 px of that file's curve of the same colour, and vice versa
   (the Illustrator drawing is itself about 1.5 px off the analytic curves).
5. Uniform and paper target modes produce the same vertex set.
With --png (needs Pillow) it draws the pattern: black border, red mountains, blue valleys, magenta hinges,
olive rulings."""
import sys, os, math, re, argparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import huffman_tower as ht

EPS = 1e-9


def rnd(p): return (round(p[0], 6), round(p[1], 6))


def seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])


def proper_cross(a, b, c, d):
    """Segments ab and cd cross at interior points (shared endpoints or touching ends do not count)."""
    if len({rnd(a), rnd(b), rnd(c), rnd(d)}) < 4: return False
    d1, d2, d3, d4 = cross(c, d, a), cross(c, d, b), cross(a, b, c), cross(a, b, d)
    return (d1 * d2 < -EPS) and (d3 * d4 < -EPS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--svg"); ap.add_argument("--png"); ap.add_argument("--cells", type=int, default=6)
    args = ap.parse_args()
    out = ht.build(cells=args.cells)
    lay = out["layers"]; meta = out["meta"]
    N = args.cells; L = 4.0 * N
    fails = []

    # 1. analytic
    cusps = ht.cusp_points(N)
    verts = {}
    for name in ("border", "seam", "mountain", "valley", "hinge"):
        for pl in lay[name]:
            for p in pl["pts"]: verts.setdefault(rnd(p), name)
    bad = 0
    for pl in lay["valley"]:
        for p in pl["pts"]:
            if min(abs(math.hypot(p[0] - c[0], p[1] - c[1]) - 1.0) for c in cusps) > 1e-6: bad += 1
    if bad: fails.append("%d valley vertices not at radius 1 from a cusp" % bad)
    bad = 0
    for pl in lay["mountain"]:
        if abs(pl["pts"][0][0] - pl["pts"][-1][0]) < EPS: continue   # straight cap crease
        for p in pl["pts"]:
            ok = False
            for f in cusps:
                d = math.hypot(p[0] - f[0], p[1] - f[1])
                if abs(d - (4.0 - abs(p[0] - f[0]))) < 1e-6 and abs(p[0] - f[0]) <= 2.0 + 1e-6 and abs(p[1]) <= 4 + 1e-6:
                    ok = True; break
            if not ok: bad += 1
    if bad: fails.append("%d parabola vertices fail the focus-directrix test" % bad)

    # 2. rulings (an endpoint may also touch a crease between its vertices: the solver splits lines there)
    creases = [(pl["pts"][i], pl["pts"][i + 1]) for name in ("mountain", "valley", "hinge", "border", "seam")
               for pl in lay[name] for i in range(len(pl["pts"]) - 1)]
    facets = [(pl["pts"][0], pl["pts"][1]) for pl in lay["facet"]]
    def on_crease(p):
        return rnd(p) in verts or any(seg_dist(p, a, b) < 1e-9 for a, b in creases)
    loose = [f for f in facets if not (on_crease(f[0]) and on_crease(f[1]))]
    if loose: fails.append("%d rulings do not end on crease vertices, e.g. %s" % (len(loose), loose[0]))
    crossings = 0
    allsegs = creases + facets
    for i, (a, b) in enumerate(facets):
        for (c, d) in allsegs:
            if proper_cross(a, b, c, d): crossings += 1
    if crossings: fails.append("%d ruling crossings" % crossings)

    # 3. vertex spacing
    allv = sorted(set(rnd(p) for name in lay for pl in lay[name] for p in pl["pts"]))
    dmin = 1e9
    for i in range(len(allv)):
        for j in range(i + 1, len(allv)):
            if allv[j][0] - allv[i][0] > 0.2: break
            dmin = min(dmin, math.hypot(allv[j][0] - allv[i][0], allv[j][1] - allv[i][1]))
    if dmin < 0.09: fails.append("closest vertices %.4f apart (< 0.09)" % dmin)

    # 5. mode parity
    out_u = ht.build(cells=N, targets=(90, 77))
    allv_u = sorted(set(rnd(p) for name in out_u["layers"] for pl in out_u["layers"][name] for p in pl["pts"]))
    if allv_u != allv: fails.append("uniform mode vertex set differs from paper mode (%d vs %d)" % (len(allv_u), len(allv)))

    # 4. overlay on the web app's SVG
    if args.svg:
        items = parse_svg(args.svg)
        sx0, sy0, sc = 392.237, 2938.2, 72.0    # strip origin and px per unit in that file
        # the file's own origin is not documented: fit a translation on the straight 60-degree cap creases
        caps = [s for it in items if it["stroke"] == "#FF0000" and it["op"] == "0.3333" for s in it["segs"]]
        ours_caps = [pl["pts"] for pl in lay["mountain"] if abs(pl["pts"][0][0] - pl["pts"][-1][0]) < EPS]
        dx = sum(min(abs(sx0 + sc * o[0][0] - c[0][0]) for c in caps) * (1 if min(caps, key=lambda c: abs(sx0 + sc * o[0][0] - c[0][0]))[0][0] > sx0 + sc * o[0][0] else -1) for o in ours_caps) / len(ours_caps)
        tops = [c for c in caps if c[0][1] < sy0 and c[1][1] < sy0]
        dy = sum(max(c[0][1], c[1][1]) for c in tops) / len(tops) - (sy0 - sc * 4.0)
        sx0 += dx; sy0 += dy
        print("overlay: fitted translation %.2f, %.2f px" % (dx, dy))
        to_px = lambda p: (sx0 + sc * p[0], sy0 - sc * p[1])
        for colour, name in (("#FF0000", "mountain"), ("#0000FF", "valley")):
            segs = [s for it in items if it["stroke"] == colour and it["kind"] == "path" and it["op"] == "0.3" for s in it["segs"]]
            worst = 0.0
            for pl in lay[name]:
                if abs(pl["pts"][0][0] - pl["pts"][-1][0]) < EPS and name == "mountain": continue
                for p in pl["pts"]:
                    q = to_px(p)
                    worst = max(worst, min(seg_dist(q, a, b) for a, b in segs))
            # their vertices against the analytic curves (our polylines are coarse near the cusps)
            worst2 = 0.0
            for a, b in segs:
                for q in (a, b):
                    x, y = (q[0] - sx0) / sc, (sy0 - q[1]) / sc
                    if name == "valley":
                        r = min(abs(math.hypot(x - c[0], y - c[1]) - 1.0) for c in cusps)
                    else:
                        r = min(abs(math.hypot(x - c[0], y - c[1]) - (4.0 - abs(x - c[0]))) for c in cusps if abs(x - c[0]) <= 2.05)
                    worst2 = max(worst2, r * sc)
            print("overlay %s: our vertices -> svg chords max %.2f px, svg vertices -> analytic curve max %.2f px" % (name, worst, worst2))
            # 2 px: the Illustrator drawing itself differs from the analytic curves by up to about 1.5 px
            if max(worst, worst2) > 2.0: fails.append("%s overlay off by %.2f px" % (name, max(worst, worst2)))

    if args.png: draw(lay, args.png, L, 4.0 + meta["cap"])

    print("counts", meta["counts"], "vertices", len(allv), "rulings", len(facets), "min vertex gap %.3f" % dmin)
    print("s2(t_max) = %.4f (quarter circle ends at %.4f); parabola fold %.1f..%.1f deg, circle fold %.1f..%.1f deg" % (
        meta["s2_max"], math.pi / 2, min(meta["parabola_fold_deg"]), max(meta["parabola_fold_deg"]),
        min(meta["circle_fold_deg"]), max(meta["circle_fold_deg"])))
    if fails:
        print("FAIL"); [print(" -", f) for f in fails]; sys.exit(1)
    print("PASS")


def parse_svg(path):
    """Segments of the web app's huffmanTower.svg (Illustrator export: line elements and M/l/h/v paths)."""
    t = open(path).read()
    items = []
    for m in re.finditer(r'<line[^>]*>', t):
        s = m.group(0)
        g = lambda k: (re.search(r'\b' + k + r'="([^"]*)"', s) or [None, None])[1]
        items.append(dict(kind='line', stroke=g('stroke'), op=g('opacity'),
                          segs=[((float(g('x1')), float(g('y1'))), (float(g('x2')), float(g('y2'))))]))
    for m in re.finditer(r'<path[^>]*>', t, re.S):
        s = m.group(0)
        g = lambda k: (re.search(r'\b' + k + r'="([^"]*)"', s) or [None, None])[1]
        d = g('d').replace('\n', ' ').replace('\t', ' ')
        segs = []; cur = None
        for c, args in re.findall(r'([MLlmHhVvZz])\s*([-\d.,\s]*)', d):
            nums = [float(x) for x in re.findall(r'-?\d*\.?\d+', args)]
            if c in 'Mm':
                cur = (nums[0], nums[1]) if c == 'M' or cur is None else (cur[0] + nums[0], cur[1] + nums[1])
                for i in range(2, len(nums), 2):
                    nxt = (nums[i], nums[i + 1]) if c == 'M' else (cur[0] + nums[i], cur[1] + nums[i + 1]); segs.append((cur, nxt)); cur = nxt
            elif c in 'Ll':
                for i in range(0, len(nums), 2):
                    nxt = (nums[i], nums[i + 1]) if c == 'L' else (cur[0] + nums[i], cur[1] + nums[i + 1]); segs.append((cur, nxt)); cur = nxt
            elif c in 'Hh':
                for n in nums: nxt = (n if c == 'H' else cur[0] + n, cur[1]); segs.append((cur, nxt)); cur = nxt
            elif c in 'Vv':
                for n in nums: nxt = (cur[0], n if c == 'V' else cur[1] + n); segs.append((cur, nxt)); cur = nxt
        items.append(dict(kind='path', stroke=g('stroke'), op=g('opacity'), segs=segs))
    return items


def draw(lay, path, L, H, scale=60):
    from PIL import Image, ImageDraw
    W, Hh = int(L * scale) + 40, int(2 * H * scale) + 40
    im = Image.new("RGB", (W, Hh), "white"); dr = ImageDraw.Draw(im)
    col = {"border": (0, 0, 0), "seam": (0, 160, 0), "mountain": (230, 0, 0), "valley": (0, 0, 230),
           "hinge": (255, 0, 255), "facet": (190, 190, 0)}
    tp = lambda p: (20 + p[0] * scale, 20 + (H - p[1]) * scale)
    for name in ("facet", "hinge", "border", "seam", "mountain", "valley"):
        for pl in lay[name]:
            pts = [tp(p) for p in pl["pts"]]
            dr.line(pts, fill=col[name], width=1 if name == "facet" else 2)
    im.save(path)


if __name__ == "__main__":
    main()
