"""Recover crease centrelines from a printable-origami STL (grooved panel layer on a flat hinge layer).

Used to build the reference pattern for the 3D Print section's fidelity test
(plans/grasshopper-print-export.md, chunk 1). Standard library only.

    python stl_groove_lines.py <in.stl> <out.json> [--floor-z 0.2] [--groove 1.2] [--png check.png]

Every groove floor (up-facing triangles at --floor-z) is bounded by pairs of parallel edges --groove apart;
the midline of each pair is a centreline piece. Collinear pieces are merged, and segment ends are extended
(up to EXTEND mm) to the nearest other line, the sheet border or a hole centre.
"""
import json, math, struct, sys, zlib, collections

EXTEND = 3.5


def read_stl(path):
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[80:84])[0]
    return [struct.unpack("<12f", b[84 + 50 * i:84 + 50 * i + 48]) for i in range(n)]


def key(x, y):
    return (round(x, 3), round(y, 3))


def boundary_edges(tris):
    count = collections.Counter()
    for t in tris:
        vs = [key(t[3 + 3 * k], t[4 + 3 * k]) for k in range(3)]
        for a, b in ((0, 1), (1, 2), (2, 0)):
            if vs[a] != vs[b]:
                count[tuple(sorted((vs[a], vs[b])))] += 1
    return [e for e, c in count.items() if c == 1]


def holes(tris):
    """Bottom-face boundary loops other than the outer one -> (cx, cy, r)."""
    bot = [t for t in tris if t[2] < -0.9]
    adj = collections.defaultdict(list)
    for a, b in boundary_edges(bot):
        adj[a].append(b)
        adj[b].append(a)
    seen, out = set(), []
    for s in adj:
        if s in seen:
            continue
        stack, loop = [s], []
        while stack:
            p = stack.pop()
            if p in seen:
                continue
            seen.add(p)
            loop.append(p)
            stack += adj[p]
        cx = sum(p[0] for p in loop) / len(loop)
        cy = sum(p[1] for p in loop) / len(loop)
        r = [math.hypot(p[0] - cx, p[1] - cy) for p in loop]
        if len(loop) >= 8 and max(r) - min(r) < 0.05:  # round loop = hole (the 4-point outer square is not)
            out.append((cx, cy, sum(r) / len(r)))
    return out


def unit(a, b):
    L = math.dist(a, b)
    return ((b[0] - a[0]) / L, (b[1] - a[1]) / L), L


def midlines(edges, groove):
    pieces = []
    E = [(e, *unit(*e)) for e in edges if math.dist(*e) > 0.05]
    for i, (e, u, L) in enumerate(E):
        for j in range(i + 1, len(E)):
            f, v, L2 = E[j]
            if abs(u[0] * v[1] - u[1] * v[0]) > 0.02:
                continue
            d = -(f[0][0] - e[0][0]) * u[1] + (f[0][1] - e[0][1]) * u[0]
            if abs(abs(d) - groove) > 0.05:
                continue
            # overlap of f's projection on e's line
            t = sorted([(f[0][0] - e[0][0]) * u[0] + (f[0][1] - e[0][1]) * u[1],
                        (f[1][0] - e[0][0]) * u[0] + (f[1][1] - e[0][1]) * u[1]])
            lo, hi = max(0.0, t[0]), min(L, t[1])
            if hi - lo < 0.05:
                continue
            nx, ny = -u[1] * d / 2, u[0] * d / 2
            p = (e[0][0] + u[0] * lo + nx, e[0][1] + u[1] * lo + ny)
            q = (e[0][0] + u[0] * hi + nx, e[0][1] + u[1] * hi + ny)
            pieces.append((p, q))
    return pieces


def merge(pieces):
    """Merge collinear pieces that touch or overlap (gap < 2.0) into maximal segments."""
    groups = []  # (u, offset, [(t0, t1)])
    for p, q in pieces:
        u, L = unit(p, q)
        if u[0] < -1e-9 or (abs(u[0]) < 1e-9 and u[1] < 0):
            u = (-u[0], -u[1])
        off = -p[0] * u[1] + p[1] * u[0]
        t0 = p[0] * u[0] + p[1] * u[1]
        t1 = q[0] * u[0] + q[1] * u[1]
        for g in groups:
            if abs(g[0][0] * u[1] - g[0][1] * u[0]) < math.sin(math.radians(0.5)) and abs(g[1] - off) < 0.05:
                g[2].append((min(t0, t1), max(t0, t1)))
                break
        else:
            groups.append((u, off, [(min(t0, t1), max(t0, t1))]))
    segs = []
    for u, off, iv in groups:
        iv.sort()
        cur = list(iv[0])
        for a, b in iv[1:]:
            if a <= cur[1] + 2.0:
                cur[1] = max(cur[1], b)
            else:
                segs.append((u, off, cur))
                cur = [a, b]
        segs.append((u, off, cur))
    out = []
    for u, off, (a, b) in segs:
        n = (-u[1], u[0])
        out.append(((u[0] * a + n[0] * off, u[1] * a + n[1] * off), (u[0] * b + n[0] * off, u[1] * b + n[1] * off)))
    return out


def ray_hit(p, d, a, b):
    """Distance along ray p + s*d to segment ab (or None)."""
    ex, ey = b[0] - a[0], b[1] - a[1]
    det = d[0] * (-ey) - d[1] * (-ex)
    if abs(det) < 1e-12:
        return None
    rx, ry = a[0] - p[0], a[1] - p[1]
    s = (rx * (-ey) - ry * (-ex)) / det
    w = (d[0] * ry - d[1] * rx) / det
    if -1e-6 <= w <= 1 + 1e-6 and s > -1e-6:
        return s
    return None


def extend(segs, half, hole_list):
    border = [((-half, -half), (half, -half)), ((half, -half), (half, half)),
              ((half, half), (-half, half)), ((-half, half), (-half, -half))]
    out = []
    for i, (p, q) in enumerate(segs):
        ends = []
        for a, b in ((p, q), (q, p)):
            u, _ = unit(b, a)  # outward direction at end a
            best, snap = None, None
            for j, s in enumerate(segs):
                if j == i:
                    continue
                h = ray_hit(a, u, *s)
                if h is not None and h <= EXTEND and (best is None or h < best):
                    best = h
            for s in border:
                h = ray_hit(a, u, *s)
                if h is not None and h <= EXTEND and (best is None or h < best):
                    best = h
            for cx, cy, r in hole_list:
                t = (cx - a[0]) * u[0] + (cy - a[1]) * u[1]
                perp = abs(-(cx - a[0]) * u[1] + (cy - a[1]) * u[0])
                if 0 <= t <= EXTEND and perp < 0.1 and (best is None or t < best):
                    best, snap = t, (cx, cy)   # end exactly on the centre so the lines share one vertex
            ends.append(snap if snap else ((a[0] + u[0] * best, a[1] + u[1] * best) if best else a))
        out.append((ends[0], ends[1]))
    return out


def raster_check(tris, segs, hole_list, groove, floor_z, half, png):
    res = 0.1
    W = int(2 * half / res)
    def rect_poly(p, q):
        u, L = unit(p, q)
        n = (-u[1] * groove / 2, u[0] * groove / 2)
        a = (p[0] - u[0] * groove / 2, p[1] - u[1] * groove / 2)
        b = (q[0] + u[0] * groove / 2, q[1] + u[1] * groove / 2)
        return [(a[0] + n[0], a[1] + n[1]), (b[0] + n[0], b[1] + n[1]), (b[0] - n[0], b[1] - n[1]), (a[0] - n[0], a[1] - n[1])]
    def fill(grid, poly):
        xs = [(x + half) / res for x, _ in poly]
        ys = [(y + half) / res for _, y in poly]
        for yy in range(max(0, int(min(ys))), min(W - 1, int(max(ys)) + 1) + 1):
            py = yy + 0.5
            xi = []
            for k in range(len(poly)):
                x1, y1, x2, y2 = xs[k], ys[k], xs[(k + 1) % len(poly)], ys[(k + 1) % len(poly)]
                if (y1 <= py) != (y2 <= py):
                    xi.append(x1 + (py - y1) * (x2 - x1) / (y2 - y1))
            xi.sort()
            for k in range(0, len(xi) - 1, 2):
                for xx in range(max(0, int(math.ceil(xi[k] - 0.5))), min(W - 1, int(math.floor(xi[k + 1] - 0.5))) + 1):
                    grid[yy][xx] = 1
    A = [bytearray(W) for _ in range(W)]
    Bg = [bytearray(W) for _ in range(W)]
    for t in tris:
        if t[2] > 0.9 and abs(t[5] - floor_z) < 1e-3:
            fill(A, [(t[3], t[4]), (t[6], t[7]), (t[9], t[10])])
    for p, q in segs:
        fill(Bg, rect_poly(p, q))
    mism = tot = 0
    img = []
    for yy in range(W):
        row = bytearray()
        for xx in range(W):
            x, y = (xx + 0.5) * res - half, (yy + 0.5) * res - half
            masked = any(math.hypot(x - cx, y - cy) < 3.0 for cx, cy, _ in hole_list)
            a, b = A[yy][xx], Bg[yy][xx]
            if not masked:
                tot += 1
                mism += a != b
            row += bytes((255, 255, 255) if masked else ((200, 200, 200) if not (a or b) else ((40, 60, 200) if a and b else ((230, 30, 30) if a else (30, 180, 30)))))
        img.append(row)
    if png:
        raw = b"".join(b"\x00" + bytes(r) for r in reversed(img))
        ch = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
        open(png, "wb").write(b"\x89PNG\r\n\x1a\n" + ch(b"IHDR", struct.pack(">IIBBBBB", W, W, 8, 2, 0, 0, 0)) + ch(b"IDAT", zlib.compress(raw)) + ch(b"IEND", b""))
    return mism, tot


def main():
    args = sys.argv[1:]
    opt = lambda name, d: type(d)(args[args.index(name) + 1]) if name in args else d
    src, dst = args[0], args[1]
    floor_z, groove, png = opt("--floor-z", 0.2), opt("--groove", 1.2), opt("--png", "")
    tris = read_stl(src)
    xs = [t[3 + 3 * k] for t in tris for k in range(3)]
    half = (max(xs) - min(xs)) / 2
    hole_list = holes(tris)
    floor = [t for t in tris if t[2] > 0.9 and abs(t[5] - floor_z) < 1e-3]
    pieces = midlines(boundary_edges(floor), groove)
    segs = extend(merge(pieces), half, hole_list)
    border = [[-half, -half, half, -half, 0.0], [half, -half, half, half, 0.0], [half, half, -half, half, 0.0], [-half, half, -half, -half, 0.0]]
    out = {"name": "STL Crane", "source": src.replace("\\", "/").split("/")[-1],
           "holes": [[round(c, 4) for c in h] for h in hole_list],
           "segments": {"border": border, "mountain": [[p[0], p[1], q[0], q[1], 180.0] for p, q in segs], "valley": [], "facet": []}}
    json.dump(out, open(dst, "w"), indent=1)
    total = sum(math.dist(p, q) for p, q in segs)
    mism, tot = raster_check(tris, segs, hole_list, groove, floor_z, half, png)
    print(json.dumps({"holes": len(hole_list), "pieces": len(pieces), "segments": len(segs), "lengthMm": round(total, 1),
                      "mismatchPct": round(100 * mism / tot, 3)}))


if __name__ == "__main__":
    main()
