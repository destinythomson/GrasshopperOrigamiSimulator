"""Flat crease pattern of David Huffman's Hexagonal Column with Cusps (1978), drawn for OrigamiSim.gh.

Pure Python (no numpy, no Rhino), so the same code runs in CPython, in Rhino 8's ScriptEditor and inside a
Grasshopper Python component. `build_huffman_tower_3dm.py` and the generator group on the OrigamiSim canvas
both import it.

Geometry follows Mundilova, Demaine, Lang and Tachi, "Analysis of Huffman's Hexagonal Column with Cusps"
(Origami^8, 2024, https://erikdemaine.org/papers/HuffmanColumn_OSME2024/), section 4. One module is the
2 x 4 rectangle with corner o = (0, 0): a circle of radius 1 about o from b = (1, 0) to b' = (0, 1), and a
parabola with focus o from its vertex a = (2, 0) to a' = (0, 4). Here the strip runs along x and is 8 wide
(y in [-4, 4]) plus two flat cap bands; one cell is 4 long and a hexagon is 6 cells. In cell k, centred on
c = 4k + 2: the upper arch (mountain) has its apex at (c, 4) and ends in cusps at (c +/- 2, 0); the valley
semicircle about the lower cusp (c, 0) bulges up; the lower half is the upper half rotated 180 degrees about
(c + 1, 0), as the paper's transformation T. Cusps are where two arches meet with a common vertical tangent.

Rulings (the paper's observed and computed structure) are drawn as Facet lines so the solver's triangulation
follows them, as the web app's own huffmanTower.svg does with its 2017 fan rulings:
- cylinder: between two adjacent arches, lines along the strip at every parabola sample level;
- cone: inside each semicircle, a fan from its cusp (the circle's "left" patch, rulings -x2/|x2|);
- central patch (inside an arch, above the semicircle): rulings from parabola point x1(t) to circle point
  x2(s2(t)) from the paper's differential equations (18)-(20) with its initial values, plus the start cone
  from the cusp a and the end cone from the apex a' (section 4.3.2). `rulings="linear"` replaces the ODE by a
  linear correspondence between the two arcs.

Crease targets: `targets="paper"` writes the fold angle 2 phi(t) of the same solution to every segment (each
segment is then its own line with its own TargetAngleDeg); `targets=(parabola_deg, circle_deg)` writes one
angle per curve and keeps each branch and semicircle a single polyline, which the solver treats as a curved
crease (needed by its Curved mode). Straight cap creases are 60-degree mountains (the hexagon's dihedral),
the cap fold lines are Hinge by default.

Output of build(): dict with "layers" -> {"border"|"seam"|"mountain"|"valley"|"facet"|"hinge": [polyline]},
polyline = {"pts": [(x, y), ...], "angle": degrees or None}, and "meta" with the ODE results and counts.
"""
import math

# Paper section 4.4: initial values of s2, phi1, phi2 that close the hexagon (errors 1e-9 .. 1e-15).
S2_0 = 0.6015913936768009
PHI1_0 = -0.7753374025984491
PHI2_0 = 0.6754673354175101
T_MAX = 4.59117  # arc length of the parabola from a to a' (footnote 4); recomputed below

PI = math.pi


# ---------------------------------------------------------------- module geometry (one cell, local frame)

def parabola_xy(y):
    """Point of the module parabola at height y: focus (0,0), vertex (2,0), through (0,4)."""
    return (2.0 - y * y / 8.0, y)


def parabola_speed(y):
    """ds/dy along the parabola."""
    return math.sqrt(1.0 + y * y / 16.0)


def parabola_curvature(y):
    """Signed curvature (left turn, toward the focus) as a function of y."""
    return 0.25 / (1.0 + y * y / 16.0) ** 1.5


def parabola_arclength(y, n=200):
    """Arc length from the vertex a to height y (Simpson)."""
    if y <= 0: return 0.0
    h = y / n
    s = parabola_speed(0) + parabola_speed(y)
    for i in range(1, n):
        s += (4 if i % 2 else 2) * parabola_speed(i * h)
    return s * h / 3.0


def _frame_parabola(y):
    """Point, unit tangent (toward a') and left normal of the parabola at height y."""
    p = parabola_xy(y)
    tx, ty = -y / 4.0, 1.0
    n = math.hypot(tx, ty)
    tx, ty = tx / n, ty / n
    return p, (tx, ty), (-ty, tx)


def _frame_circle(s):
    """Point, unit tangent (counter-clockwise) and left normal (toward the centre) of the unit circle."""
    return (math.cos(s), math.sin(s)), (-math.sin(s), math.cos(s)), (-math.cos(s), -math.sin(s))


def _angle_in_frame(v, t, n):
    """Ruling angle theta in (0, pi): angle from the tangent t to the vector v, measured toward the left normal."""
    return math.atan2(v[0] * n[0] + v[1] * n[1], v[0] * t[0] + v[1] * t[1])


def _unit(ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    d = math.hypot(dx, dy)
    return (dx / d, dy / d)


def solve_central_patch(steps=4000):
    """Integrate the paper's equations (18)-(20) along the parabola (t = arc length from a, s1(t) = t).

    State: s2 (circle angle paired with x1(t)), phi1, phi2 (half fold angles), y (height on the parabola).
    Returns a table of rows (t, y, s2, phi1, phi2) and t_max. RK4 with fixed step."""
    def f(state):
        s2, phi1, phi2, y = state
        x1, t1, n1 = _frame_parabola(y)
        k1 = parabola_curvature(y)
        x2, t2, n2 = _frame_circle(s2)
        r = _unit(x1[0], x1[1], x2[0], x2[1])        # central ruling, parabola -> circle (eq. 12)
        th1L = _angle_in_frame(r, t1, n1)            # eq. 13
        th2R = _angle_in_frame(r, t2, n2)            # eq. 14
        th1R = _angle_in_frame((-1.0, 0.0), t1, n1)  # cylinder rulings along the strip
        th2L = PI / 2                                # cone rulings toward the centre
        ds2 = -k1 * math.sin(th2R) * math.tan(phi1) / (1.0 * math.sin(th1L) * math.tan(phi2))   # eq. 18
        dphi1 = 0.5 * k1 * (1.0 / math.tan(th1L) + 1.0 / math.tan(th1R)) * math.tan(phi1)       # eq. 19
        dphi2 = -0.5 * k1 * math.sin(th2L + th2R) / (math.sin(th1L) * math.sin(th2L)) * math.tan(phi1)  # eq. 20
        dy = 1.0 / parabola_speed(y)
        return (ds2, dphi1, dphi2, dy)

    t_max = parabola_arclength(4.0)
    h = t_max / steps
    state = (S2_0, PHI1_0, PHI2_0, 0.0)
    rows = [(0.0,) + (state[3], state[0], state[1], state[2])]
    t = 0.0
    for i in range(steps):
        k1 = f(state)
        k2 = f(tuple(s + 0.5 * h * k for s, k in zip(state, k1)))
        k3 = f(tuple(s + 0.5 * h * k for s, k in zip(state, k2)))
        k4 = f(tuple(s + h * k for s, k in zip(state, k3)))
        state = tuple(s + h / 6.0 * (a + 2 * b + 2 * c + d) for s, a, b, c, d in zip(state, k1, k2, k3, k4))
        t += h
        if i == steps - 1: state = (state[0], state[1], state[2], 4.0)   # land exactly on a'
        rows.append((t, state[3], state[0], state[1], state[2]))
    return rows, t_max


def solve_cone_segment(s_from, s_to, phi_from, apex, steps=400):
    """Half fold angle along a circular arc whose left patch is the cone from the centre and whose right patch is
    the cone from `apex` (section 2.2.2, equation 9 with theta_L = pi/2). Integrates from s_from to s_to.
    Returns rows (s, phi)."""
    def dphi(s, phi):
        x2, t2, n2 = _frame_circle(s)
        r = _unit(apex[0], apex[1], x2[0], x2[1])
        thR = _angle_in_frame(r, t2, n2)
        return 0.5 * (1.0 / math.tan(thR)) * math.tan(phi)
    h = (s_to - s_from) / steps
    s, phi = s_from, phi_from
    rows = [(s, phi)]
    for _ in range(steps):
        a = dphi(s, phi)
        b = dphi(s + 0.5 * h, phi + 0.5 * h * a)
        c = dphi(s + 0.5 * h, phi + 0.5 * h * b)
        d = dphi(s + h, phi + h * c)
        phi += h / 6.0 * (a + 2 * b + 2 * c + d)
        s += h
        rows.append((s, phi))
    return rows


def _interp(rows, key, x, col):
    """Linear interpolation in a monotone table: rows[i][key] -> rows[i][col]."""
    lo, hi = 0, len(rows) - 1
    if x <= rows[0][key]: return rows[0][col]
    if x >= rows[-1][key]: return rows[-1][col]
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if rows[mid][key] <= x: lo = mid
        else: hi = mid
    a, b = rows[lo], rows[hi]
    w = (x - a[key]) / (b[key] - a[key]) if b[key] != a[key] else 0.0
    return a[col] + w * (b[col] - a[col])


# ---------------------------------------------------------------- pattern assembly

def _level_heights(P, power=0.6):
    """Parabola sample heights y_1 < ... < y_P = 4. Power < 1 keeps the first sample away from the cusp, where the
    two arches are only y^2/4 apart (and y^2/8 from a strip end) and the solver would merge vertices closer than
    0.5 % of the pattern radius (0.066 for 6 cells): with P = 12 the first level is y = 0.90, x = 0.10 off the cusp."""
    return [4.0 * (j / float(P)) ** power for j in range(P + 1)]


class _Collector(object):
    def __init__(self):
        self.layers = {"border": [], "seam": [], "mountain": [], "valley": [], "facet": [], "hinge": []}
        self.seen = set()

    def add(self, layer, pts, angle=None):
        pts = [(float(x), float(y)) for x, y in pts]
        if len(pts) < 2: return
        key = tuple((round(x, 6), round(y, 6)) for x, y in pts)
        rkey = tuple(reversed(key))
        if key in self.seen or rkey in self.seen: return
        if layer == "facet" and len(pts) == 2 and key[0] == key[1]: return
        self.seen.add(key)
        self.layers[layer].append({"pts": pts, "angle": angle})


def build(cells=6, cap=1.6, P=12, fan=(3, 1), ruling_every=2, targets="paper", rulings="ode", cap_line="hinge",
          cap_line_angle=None, cap_angle=60.0, seam=False):
    """Build the pattern. See the module docstring.

    cells: cells along the strip (6 = hexagon). cap: height of each flat cap band (units; strip half-width is 4).
    P: parabola segments per branch. fan: subdivisions of the start cone (cusp) and end cone (apex) arcs.
    ruling_every: a circle vertex of its own at every n-th parabola sample (one per sample would put circle
    vertices closer than the solver's vertex-merge tolerance; circle ends closer than 0.1 are thinned further);
    the samples in between share the previous circle vertex.
    targets: "paper" (per-segment fold angles from the solution) or (parabola_deg, circle_deg).
    rulings: "ode" (paper) or "linear". cap_line: "hinge", "valley" or "mountain" for the lines y = +/-4.
    seam: put the two strip ends on the Seam layer instead of Border."""
    N = int(cells)
    L = 4.0 * N
    H = 4.0 + cap
    ys = _level_heights(P)
    ts = [parabola_arclength(y) for y in ys]
    t_max = ts[-1]

    # --- the paper's solution: s2(t), phi1(t), phi2(t) on the parabola, and phi on the two cone arcs
    rows, _ = solve_central_patch()
    if rulings == "ode":
        s2_of_t = lambda t: _interp(rows, 0, t, 2)
    else:
        s2_end_lin = _interp(rows, 0, t_max, 2)
        s2_of_t = lambda t: S2_0 + (s2_end_lin - S2_0) * t / t_max
    s2_max = s2_of_t(t_max)
    if not (S2_0 < s2_max < PI / 2):
        raise ValueError("central patch does not fit the quarter circle: s2(t_max) = %.4f" % s2_max)
    start_rows = solve_cone_segment(S2_0, 0.0, PHI2_0, (2.0, 0.0))              # cusp a -> circle, backwards
    end_rows = solve_cone_segment(s2_max, PI / 2, _interp(rows, 0, t_max, 4), (0.0, 4.0))   # apex a' -> circle
    start_rows.reverse()

    def phi_parabola(t): return _interp(rows, 0, t, 3)

    def phi_circle(s):
        if s <= S2_0: return _interp(start_rows, 0, s, 1)
        if s >= s2_max: return _interp(end_rows, 0, s, 1)
        t = _interp(rows, 2, s, 0)
        return _interp(rows, 0, t, 4)

    def deg(phi): return abs(2.0 * phi) * 180.0 / PI

    # --- circle vertex angles on the quarter from b (0) to b' (pi/2): start fan, central rulings, end fan
    nS, nE = max(1, int(fan[0])), max(1, int(fan[1]))
    step = max(1, int(ruling_every))
    rj = [j for j in range(0, P + 1, step)]
    if rj[-1] != P: rj.append(P)
    # thin the rulings whose circle ends would be closer than 0.1 (the solver merges vertices within about 0.066)
    thinned = [rj[0]]
    for j in rj[1:-1]:
        if s2_of_t(ts[j]) - s2_of_t(ts[thinned[-1]]) >= 0.1 and s2_max - s2_of_t(ts[j]) >= 0.1: thinned.append(j)
    rj = thinned + [P]
    quarter = [S2_0 * i / float(nS) for i in range(nS)]
    quarter += [s2_of_t(ts[j]) for j in rj]
    quarter += [s2_max + (PI / 2 - s2_max) * i / float(nE) for i in range(1, nE + 1)]
    semi = quarter + [PI - a for a in reversed(quarter[:-1])]   # angles 0 .. pi, symmetric
    circle_angle_deg = [deg(phi_circle(0.5 * (semi[i] + semi[i + 1]))) if semi[i + 1] <= PI / 2 + 1e-12
                        else deg(phi_circle(PI - 0.5 * (semi[i] + semi[i + 1]))) for i in range(len(semi) - 1)]
    parabola_angle_deg = [deg(phi_parabola(0.5 * (ts[j] + ts[j + 1]))) for j in range(P)]
    uniform = targets != "paper"
    if uniform:
        par_deg, cir_deg = float(targets[0]), float(targets[1])

    col = _Collector()

    def place(x, y, c, mirror, lower):
        """Module frame -> strip. mirror: reflect in the apex line x = c. lower: rotate 180 deg about (c+1, 0)."""
        X = c - x if mirror else c + x
        Y = y
        if lower: X, Y = 2 * c + 2 - X, -Y
        return (X, Y)

    def inside(pts):
        return all(-1e-9 <= x <= L + 1e-9 for x, y in pts)

    def on_end(pts):
        return all(abs(x) < 1e-9 for x, y in pts) or all(abs(x - L) < 1e-9 for x, y in pts)

    def emit(layer, pts, angle=None):
        if not inside(pts): return
        if layer == "facet" and on_end(pts): return
        col.add(layer, pts, angle)

    def cell(c, lower):
        for mirror in (False, True):
            tr = lambda p: place(p[0], p[1], c, mirror, lower)
            # parabola branch a -> a' (mountain)
            bpts = [parabola_xy(y) for y in ys]
            if uniform:
                emit("mountain", [tr(p) for p in bpts], par_deg)
            else:
                for j in range(P):
                    emit("mountain", [tr(bpts[j]), tr(bpts[j + 1])], parabola_angle_deg[j])
            # central rulings parabola -> circle, start cone from a, end cone from a'
            # every parabola sample gets a ruling; a sample without a circle vertex of its own shares the
            # vertex of the previous kept ruling (no polygon between rulings is larger than a quad, so the
            # solver never ear-clips a sliver triangle there and drops it as degenerate)
            for j in range(P + 1):
                jj = max(q for q in rj if q <= j)
                s = s2_of_t(ts[jj])
                emit("facet", [tr(bpts[j]), tr((math.cos(s), math.sin(s)))])
            for a in quarter[:nS]:
                emit("facet", [tr((2.0, 0.0)), tr((math.cos(a), math.sin(a)))])
            for a in quarter[nS + len(rj):]:
                emit("facet", [tr((0.0, 4.0)), tr((math.cos(a), math.sin(a)))])
            # cone fan from the upper cusp a into its own (lower) semicircle: point reflection of the circle about b
            for a in semi:
                emit("facet", [tr((2.0, 0.0)), tr((2.0 - math.cos(a), -math.sin(a)))])
        # valley semicircle about o (one object)
        cpts = [place(math.cos(a), math.sin(a), c, False, lower) for a in semi]
        if uniform:
            # clip to the strip for the end cells (the semicircle about an end cusp is half outside)
            keep = [p for p in cpts if -1e-9 <= p[0] <= L + 1e-9]
            if len(keep) >= 2: col.add("valley", keep, cir_deg)
        else:
            for i in range(len(cpts) - 1):
                emit("valley", [cpts[i], cpts[i + 1]], circle_angle_deg[i])

    for k in range(N):
        cell(4 * k + 2, False)
    for k in range(-1, N):
        cell(4 * k + 2, True)

    # cylinder rulings between adjacent arches (upper: cusps at 4k; lower: cusps at 4k+2) and at the strip ends;
    # a ruling shorter than 0.1 (the first level at a strip end) is left out so the solver cannot merge its ends
    for j in range(1, P):
        y = ys[j]
        d = y * y / 8.0
        for k in range(N + 1):
            x = 4.0 * k
            a, b = (max(0.0, x - d), y), (min(L, x + d), y)
            if b[0] - a[0] >= 0.1: emit("facet", [a, b])
        for k in range(N):
            x = 4.0 * k + 2.0
            if 2 * d >= 0.1: emit("facet", [(x - d, -y), (x + d, -y)])

    # caps: straight hexagon edges, fold lines, border
    for k in range(N):
        x = 4.0 * k + 2.0
        col.add("mountain", [(x, 4.0), (x, H)], cap_angle)
    for k in range(1, N):
        x = 4.0 * k
        col.add("mountain", [(x, -4.0), (x, -H)], cap_angle)
    cl_layer = {"hinge": "hinge", "valley": "valley", "mountain": "mountain"}[cap_line]
    cl_angle = None if cl_layer == "hinge" else cap_line_angle
    col.add(cl_layer, [(0.0, 4.0), (L, 4.0)], cl_angle)
    col.add(cl_layer, [(0.0, -4.0), (L, -4.0)], cl_angle)
    col.add("border", [(0.0, H), (L, H)])
    col.add("border", [(0.0, -H), (L, -H)])
    col.add("seam" if seam else "border", [(0.0, -H), (0.0, H)])
    col.add("seam" if seam else "border", [(L, -H), (L, H)])

    meta = {
        "cells": N, "cap": cap, "P": P, "fan": [nS, nE], "ruling_every": step, "targets": targets, "rulings": rulings,
        "t_max": t_max, "s2_max": s2_max, "s2_0": S2_0,
        "parabola_fold_deg": [round(a, 2) for a in parabola_angle_deg],
        "circle_fold_deg": [round(a, 2) for a in circle_angle_deg[:len(quarter)]],
        "phi1_end": _interp(rows, 0, t_max, 3), "phi2_end": _interp(rows, 0, t_max, 4),
        "counts": dict((k, len(v)) for k, v in col.layers.items()),
        "levels": ys,
    }
    return {"layers": col.layers, "meta": meta}


def cusp_points(cells):
    """All cusp (= focus) positions on the centreline: upper cusps at 4k, lower cusps at 4k+2."""
    return [(2.0 * i, 0.0) for i in range(2 * int(cells) + 1)]


if __name__ == "__main__":
    import json, sys
    out = build()
    m = out["meta"]
    sys.stdout.write(json.dumps(m, indent=1) + "\n")
