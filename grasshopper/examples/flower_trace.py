"""Trace Mitani's "Artwork 14: Flower" (Curved-Folding Origami Design, CRC Press 2019) from a raster
of its crease pattern into flower.json for build_flower_3dm.py. PIL + numpy only.

Usage: python flower_trace.py pattern.png flower.json [overlay.png]

The raster is a square sheet drawn as a thin dark border, solid mountain curves and dashed valley
curves. The pattern is 4-fold rotationally symmetric, so the four rotations are merged, thinned to a
skeleton, and ONE sector (one arm, one centre side, one valley arc, one apex) is written in sheet
units (100 x 100, origin bottom-left). The builder rotates it four times. The valley arcs end free
inside the sheet, as in the artwork; the solver keeps such creases, as the web app does. The builder's
EXTEND_VALLEYS option extends each free end along its tangent to the mountain arm (see build_flower_3dm.py).
"""
import sys, math, json
import numpy as np
from PIL import Image, ImageDraw

SIDE = 100.0          # sheet size in model units
SAMPLE = 1.6          # mountain polyline spacing, units
RULE_DEG = 4.0        # valley vertex spacing, degrees about the apex
TARGET = 150.0        # TargetAngleDeg for every crease

src, dst = sys.argv[1], sys.argv[2]
ovl = sys.argv[3] if len(sys.argv) > 3 else None
a = np.array(Image.open(src).convert("L"))
dark = a < 140
H, W = dark.shape

# ---- border square: first two thin runs of rows that are >80 % dark
rows = np.where(dark.sum(1) > 0.8 * W)[0]
runs = []
for r in rows:
    if runs and r - runs[-1][-1] <= 1: runs[-1].append(r)
    else: runs.append([r])
thin = [ru for ru in runs if len(ru) <= 4]
top, bot = thin[0][0], thin[1][-1]
cols = np.where(dark[top:bot + 1].sum(0) > 0.8 * (bot - top))[0]
left, right = cols.min(), cols.max()
cx, cy = (left + right) / 2.0, (top + bot) / 2.0
sx, sy = SIDE / (right - left), SIDE / (bot - top)
print("border px x %d..%d y %d..%d" % (left, right, top, bot))

def to_sheet(p):  # pixel (x, y down) -> sheet (x, y up)
    return [(p[0] - left) * sx, (bot - p[1]) * sy]

# ---- interior pixels, components
m = 4
inner = np.zeros_like(dark)
inner[top + m:bot - m + 1, left + m:right - m + 1] = dark[top + m:bot - m + 1, left + m:right - m + 1]
ys, xs = np.where(inner)
pix = set(zip(xs.tolist(), ys.tolist()))
seen, comps = set(), []
for p in pix:
    if p in seen: continue
    stack, members = [p], []
    seen.add(p)
    while stack:
        x, y = stack.pop(); members.append((x, y))
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                q = (x + dx, y + dy)
                if q in pix and q not in seen: seen.add(q); stack.append(q)
    comps.append(members)
solid = np.array(sum([c for c in comps if len(c) > 400], []), float)
small = [np.array(c, float) for c in comps if 6 < len(c) <= 400]
print("solid px %d, dash components %d" % (len(solid), len(small)))

def rot(P, k):  # rotate pixel coords k*90 deg about the sheet centre
    u, v = P[:, 0] - cx, P[:, 1] - cy
    for _ in range(k): u, v = -v, u
    return np.c_[u + cx, v + cy]

# ---- skeleton of the solid lines (Zhang-Suen), then its graph
mask = np.zeros((H, W), bool)
R = np.rint(solid).astype(int); mask[R[:, 1], R[:, 0]] = True
def dil(M):
    out = M.copy()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1): out |= np.roll(np.roll(M, dy, 0), dx, 1)
    return out
def ero(M):
    out = M.copy()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1): out &= np.roll(np.roll(M, dy, 0), dx, 1)
    return out
mask = ero(dil(mask))

def nb8(I):
    P2 = np.roll(I, 1, 0); P3 = np.roll(P2, -1, 1); P4 = np.roll(I, -1, 1); P5 = np.roll(P4, -1, 0)
    P6 = np.roll(I, -1, 0); P7 = np.roll(P6, 1, 1); P8 = np.roll(I, 1, 1); P9 = np.roll(P8, 1, 0)
    return [P2, P3, P4, P5, P6, P7, P8, P9]
def crossing(I):
    N = nb8(I); seq = N + [N[0]]
    return sum(((~seq[i]) & seq[i + 1]).astype(int) for i in range(8))
def zhang_suen(img):
    img = img.copy()
    while True:
        changed = False
        for step in (0, 1):
            N = nb8(img)
            B = sum(n.astype(int) for n in N)
            seq = N + [N[0]]
            A = sum(((~seq[i]) & seq[i + 1]).astype(int) for i in range(8))
            if step == 0: c1 = ~(N[0] & N[2] & N[4]); c2 = ~(N[2] & N[4] & N[6])
            else:         c1 = ~(N[0] & N[2] & N[6]); c2 = ~(N[0] & N[4] & N[6])
            rem = img & (B >= 2) & (B <= 6) & (A == 1) & c1 & c2
            if rem.any(): img[rem] = False; changed = True
        if not changed: return img
skel = zhang_suen(mask)
print("skeleton px", int(skel.sum()))

def neighbours(p):
    x, y = p
    return [(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (dx or dy) and skel[y + dy, x + dx]]
def classify():
    X = crossing(skel)
    ys_, xs_ = np.where(skel)
    ends = [(int(x), int(y)) for x, y in zip(xs_, ys_) if X[y, x] == 1]
    junc = {(int(x), int(y)) for x, y in zip(xs_, ys_) if X[y, x] >= 3}
    return ends, junc
def walk(start, junc, seen0=()):
    """Follow the skeleton from start until a junction pixel or a dead end. All unvisited neighbours
    of the current pixel are consumed at each step, so staircase siblings cannot trap the walk."""
    path = [start]; seen = {start} | set(seen0); cur = start
    while True:
        cand = [q for q in neighbours(cur) if q not in seen]
        if not cand: return path
        seen.update(cand)
        hit = [q for q in cand if q in junc]
        if hit: path.append(hit[0]); return path
        q = max(cand, key=lambda c: len([r for r in neighbours(c) if r not in seen]))
        path.append(q); cur = q
for _ in range(5):
    ends, junc = classify()
    pruned = False
    for e in ends:
        p = walk(e, junc)
        if len(p) < 25 and p[-1] in junc:
            for q in p[:-1]: skel[q[1], q[0]] = False
            pruned = True
    if not pruned: break
ends, junc = classify()
jl = list(junc); jc = {}; clusters = []
for j in jl:
    if j in jc: continue
    cid = len(clusters); stack = [j]; jc[j] = cid; mem = []
    while stack:
        x, y = stack.pop(); mem.append((x, y))
        for dx in (-3, -2, -1, 0, 1, 2, 3):
            for dy in (-3, -2, -1, 0, 1, 2, 3):
                q = (x + dx, y + dy)
                if q in junc and q not in jc: jc[q] = cid; stack.append(q)
    clusters.append(np.mean(np.array(mem, float), 0))
print("endpoints %d, junction clusters %d: %s" % (len(ends), len(clusters), [np.round(c, 1).tolist() for c in clusters]))
assert len(clusters) == 4 and len(ends) == 4, "expected 4 corners and 4 border ends"
# cut a small disk out of the skeleton at every corner: the pieces left are 4 arms + 4 sides
cut = skel.copy()
for c in clusters:
    x0, y0 = int(round(c[0])), int(round(c[1]))
    cut[y0 - 4:y0 + 5, x0 - 4:x0 + 5] = False
cpix = set(zip(*np.where(cut)[::-1]))
def cnb(p):
    x, y = p
    return [(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (dx or dy) and (x + dx, y + dy) in cpix]
pieces, seen = [], set()
for p0 in cpix:
    if p0 in seen: continue
    stack, mem = [p0], []
    seen.add(p0)
    while stack:
        q = stack.pop(); mem.append(q)
        for r in cnb(q):
            if r not in seen: seen.add(r); stack.append(r)
    if len(mem) > 30: pieces.append(mem)
def order(piece):
    """Ordered pixel path through a 1-px-wide piece: walk from any pixel to a dead end, then walk
    back from that end (so pieces whose ends are staircase pairs still get traversed fully)."""
    ps = set(piece)
    def go(start):
        path = [start]; sn = {start}; cur = start
        while True:
            cand = [r for r in cnb(cur) if r in ps and r not in sn]
            if not cand: return path
            sn.update(cand)
            q = max(cand, key=lambda c_: len([r for r in cnb(c_) if r in ps and r not in sn]))
            path.append(q); cur = q
    return go(go(piece[0])[-1])
def near_cluster(q):
    d = [np.linalg.norm(np.array(q, float) - c) for c in clusters]
    i = int(np.argmin(d)); return i if d[i] <= 10 else None
def near_border(q): return min(q[1] - top, bot - q[1], q[0] - left, right - q[0]) <= 8
arms, sides = [], []
for piece in pieces:
    path = order(piece)
    h, t = path[0], path[-1]
    ch, ct = near_cluster(h), near_cluster(t)
    if ch is not None and ct is not None: sides.append((path, ch, ct))
    elif ct is not None and near_border(h): arms.append((path, ct))
    elif ch is not None and near_border(t): arms.append((path[::-1], ch))
    else: print("unclassified piece", len(path), h, t)
print("arm px", [len(p) for p, _ in arms], "side px", [len(p) for p, _, _ in sides])
assert len(arms) == 4 and len(sides) == 4, "expected 4 arms and 4 sides"

# ---- average the 4 arms and 4 sides into the sector of the top-left corner
corner_id = min(range(4), key=lambda i: clusters[i][0] + clusters[i][1])
def to_sector(P, cid):
    """rotation k that brings cluster cid onto the top-left corner, applied to P"""
    k = min(range(4), key=lambda k: np.linalg.norm(rot(np.array([clusters[cid]]), k)[0] - clusters[corner_id]))
    return rot(np.asarray(P, float), k), k
def resample_n(P, n):
    seg = np.hypot(*np.diff(P, axis=0).T); s = np.r_[0, np.cumsum(seg)]
    t = np.linspace(0, s[-1], n)
    return np.c_[np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])]
arm_avg = np.mean([resample_n(to_sector(p, cid)[0], 400) for p, cid in arms], 0)
# sides: rotate each so its start corner is the top-left one; all must then end at the same
# neighbouring corner, so flip the ones that run the other way round the square
TL = clusters[corner_id]
side_rot, k_side = [], None
for p, a, b in sides:
    Q, k = to_sector(p, a)
    k_this = min((1, 3), key=lambda kk: np.linalg.norm(Q[-1] - rot(np.array([TL]), kk)[0]))
    if k_side is None: k_side = k_this
    if k_this != k_side:
        Q, k = to_sector(p[::-1], b)
    side_rot.append(resample_n(Q, 200))
side_avg = np.mean(side_rot, 0)
C = (arm_avg[-1] + side_avg[0]) / 2.0
spread = max(np.linalg.norm(resample_n(to_sector(p, cid)[0], 400) - arm_avg, axis=1).max() for p, cid in arms)
print("corner px", np.round(C, 1), "side goes to rotation", k_side, "; max spread of the 4 arms about their mean %.1f px" % spread)

def smooth_resample(P, spacing_px, p0, p1):
    k = 7
    Ps = np.array([P[max(0, i - k):i + k + 1].mean(0) for i in range(len(P))])
    Ps[0], Ps[-1] = p0, p1
    seg = np.hypot(*np.diff(Ps, axis=0).T); s = np.r_[0, np.cumsum(seg)]
    n = max(2, int(round(s[-1] / spacing_px)))
    t = np.linspace(0, s[-1], n + 1)
    return np.c_[np.interp(t, s, Ps[:, 0]), np.interp(t, s, Ps[:, 1])]

spacing_px = SAMPLE / sx
e = arm_avg[0].copy()
dists = {"top": e[1] - top, "bot": bot - e[1], "left": e[0] - left, "right": right - e[0]}
edge = min(dists, key=dists.get)
if edge == "top": e[1] = top
elif edge == "bot": e[1] = bot
elif edge == "left": e[0] = left
else: e[0] = right
arm_pts = smooth_resample(arm_avg, spacing_px, e, C)
side_pts = smooth_resample(side_avg, spacing_px, C, rot(np.array([C]), k_side)[0])

# ---- valley: symmetrise all dashes into the sector of the top-left arc, fit apex, radius profile
cent = [c.mean(0) for c in small]
# group dashes into 4 curves (single linkage, centroid gap <= 18 px)
n = len(cent); parent = list(range(n))
def find(i):
    while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
    return i
for i in range(n):
    for j in range(i + 1, n):
        if np.hypot(*(cent[i] - cent[j])) <= 18: parent[find(i)] = find(j)
groups = {}
for i in range(n): groups.setdefault(find(i), []).append(i)
curves = [np.vstack([small[i] for i in g]) for g in groups.values() if sum(len(small[i]) for i in g) > 100]
assert len(curves) == 4, len(curves)
def kasa(P):
    x, y = P[:, 0], P[:, 1]
    A = np.c_[x, y, np.ones_like(x)]; b = x * x + y * y
    c = np.linalg.lstsq(A, b, rcond=None)[0]
    return np.array([c[0] / 2, c[1] / 2]), math.sqrt(c[2] + c[0] ** 2 / 4 + c[1] ** 2 / 4)
# bring every curve into the frame of the one whose apex is in the top-left quadrant
tl = min(range(4), key=lambda i: sum(kasa(curves[i])[0]))
merged = []
for i in range(4):
    best = min(range(4), key=lambda k: np.linalg.norm(kasa(rot(curves[i], k))[0] - kasa(curves[tl])[0]))
    merged.append(rot(curves[i], best))
V = np.vstack(merged)
P_apex, r0 = kasa(V)
ang = np.degrees(np.arctan2(V[:, 1] - P_apex[1], V[:, 0] - P_apex[0]))
rad = np.hypot(V[:, 0] - P_apex[0], V[:, 1] - P_apex[1])
# unwrap around the arc's mean direction
mean_dir = math.degrees(math.atan2(*(V.mean(0) - P_apex)[::-1]))
ang = (ang - mean_dir + 180) % 360 - 180 + mean_dir
# radius profile: 2-degree bins, smoothed
bins = np.arange(np.floor(ang.min()), np.ceil(ang.max()) + 2, 2.0)
bc, br = [], []
for b0, b1 in zip(bins[:-1], bins[1:]):
    s = (ang >= b0) & (ang < b1)
    if s.sum() >= 3: bc.append((b0 + b1) / 2); br.append(rad[s].mean())
bc, br = np.array(bc), np.array(br)
br_s = np.array([br[max(0, i - 3):i + 4].mean() for i in range(len(br))])
print("apex px", np.round(P_apex, 1), "r0 %.1f, angle span %.1f..%.1f, r range %.1f..%.1f" % (r0, bc[0], bc[-1], br_s.min(), br_s.max()))
def r_at(t): return np.interp(t, bc, br_s)
def pt(t): return P_apex + r_at(t) * np.array([math.cos(math.radians(t)), math.sin(math.radians(t))])
# which end touches the border?
def border_dist(p): return min(p[1] - top, bot - p[1], p[0] - left, right - p[0])
t_lo, t_hi = bc[0], bc[-1]
lo_is_border = border_dist(pt(t_lo)) < border_dist(pt(t_hi))
t_b, t_f = (t_lo, t_hi) if lo_is_border else (t_hi, t_lo)
# extend the border end radially-tangentially until it meets the border line
def border_hit(p, d):
    best = None
    for (axis, val) in ((1, top), (1, bot), (0, left), (0, right)):
        if abs(d[axis]) < 1e-9: continue
        lam = (val - p[axis]) / d[axis]
        if lam <= 0: continue
        q = p + lam * d
        if left - 0.5 <= q[0] <= right + 0.5 and top - 0.5 <= q[1] <= bot + 0.5:
            if best is None or lam < best[0]: best = (lam, q)
    return best[1]
step = 2.0 if t_b < t_f else -2.0   # sign of the angle change from border end toward free end
tang = pt(t_b) - pt(t_b + step)
tang /= np.linalg.norm(tang)
vb = border_hit(pt(t_b), tang)
print("valley border end extended by %.1f px" % np.linalg.norm(vb - pt(t_b)))
# valley vertices every RULE_DEG about the apex, from the border end to the free end
ts = list(np.arange(t_b, t_f, step * RULE_DEG / 2.0)) + [t_f]
valley_pts = [vb] + [pt(t) for t in ts[1:]]
print("valley vertices %d" % len(valley_pts))

# ---- write JSON in sheet units
def S(P): return [to_sheet(p) for p in P]
data = {
    "pattern": "Flower",
    "source": "Jun Mitani, Curved-Folding Origami Design (CRC Press, 2019), Artwork 14",
    "side": SIDE, "target_deg": TARGET,
    "note": "one 90-degree sector; the builder rotates it 4 times about the sheet centre",
    "arm": S(arm_pts), "centre_side": S(side_pts), "valley": S(valley_pts), "apex": to_sheet(P_apex),
}
json.dump(data, open(dst, "w"), indent=1)
print("wrote", dst)

# ---- overlay + fidelity metric (against the ORIGINAL, unsymmetrised pixels)
if ovl:
    im = Image.open(src).convert("RGB"); dr = ImageDraw.Draw(im)
    def from_sheet(p): return (p[0] / sx + left, bot - p[1] / sy)
    def rots(P):
        out = []
        for k in range(4):
            Q = np.array([from_sheet(p) for p in P]); out.append(rot(Q, k))
        return out
    segs = []
    for key, col in (("arm", (255, 0, 0)), ("centre_side", (255, 0, 0)), ("valley", (0, 0, 255))):
        for Q in rots(data[key]):
            dr.line([tuple(q) for q in Q], fill=col, width=1)
            segs += [(Q[i], Q[i + 1]) for i in range(len(Q) - 1)]
    im.save(ovl)
    A = np.array([s[0] for s in segs]); B = np.array([s[1] for s in segs])
    orig = np.vstack([solid] + curves)
    d_all = np.empty(len(orig))
    for i in range(0, len(orig), 400):
        P = orig[i:i + 400]
        AB = B - A; L2 = (AB ** 2).sum(1) + 1e-12
        t = ((P[:, None, 0] - A[None, :, 0]) * AB[None, :, 0] + (P[:, None, 1] - A[None, :, 1]) * AB[None, :, 1]) / L2[None, :]
        t = np.clip(t, 0, 1)
        qx = A[None, :, 0] + t * AB[None, :, 0]; qy = A[None, :, 1] + t * AB[None, :, 1]
        d_all[i:i + 400] = np.hypot(P[:, None, 0] - qx, P[:, None, 1] - qy).min(1)
    print("fidelity: mean %.2f  p95 %.2f  max %.2f px (1%% of side = %.1f px)" % (d_all.mean(), np.percentile(d_all, 95), d_all.max(), 0.01 * (right - left)))
