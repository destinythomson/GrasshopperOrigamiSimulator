"""Parse Origami Simulator crease-pattern SVGs into line segments grouped by crease type.

Colour convention follows js/pattern.js typeForStroke: black border, red mountain, blue valley,
green cut, yellow facet (triangulation), magenta hinge. Stroke opacity scales the fold angle
(pattern.js getOpacity -> targetAngle = opacity * 180).

Usage: python svg_to_segments.py out.json name=path/to/file.svg [name=...]
"""
import json, math, re, sys
import xml.etree.ElementTree as ET

TYPES = {
    "border": {"#000000", "#000", "black", "rgb(0,0,0)"},
    "mountain": {"#ff0000", "#f00", "red", "rgb(255,0,0)"},
    "valley": {"#0000ff", "#00f", "blue", "rgb(0,0,255)"},
    "cut": {"#00ff00", "#0f0", "green", "lime", "rgb(0,255,0)"},
    "facet": {"#ffff00", "#ff0", "yellow", "rgb(255,255,0)"},
    "hinge": {"#ff00ff", "#f0f", "magenta", "fuchsia", "rgb(255,0,255)"},
}


def stroke_type(s):
    s = re.sub(r"\s", "", s or "").lower()
    for t, names in TYPES.items():
        if s in names:
            return t
    return None


def style_dict(el):
    d = {}
    for part in (el.get("style") or "").split(";"):
        if ":" in part:
            k, v = part.split(":", 1)
            d[k.strip()] = v.strip()
    for k in ("stroke", "opacity", "stroke-opacity"):
        if el.get(k) is not None and k not in d:
            d[k] = el.get(k)
    return d


def mat_mul(a, b):  # 2x3 affine [a b c d e f] as in SVG matrix()
    return [a[0]*b[0] + a[2]*b[1], a[1]*b[0] + a[3]*b[1],
            a[0]*b[2] + a[2]*b[3], a[1]*b[2] + a[3]*b[3],
            a[0]*b[4] + a[2]*b[5] + a[4], a[1]*b[4] + a[3]*b[5] + a[5]]


def parse_transform(t):
    m = [1, 0, 0, 1, 0, 0]
    for name, args in re.findall(r"(\w+)\s*\(([^)]*)\)", t or ""):
        v = [float(x) for x in re.findall(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", args)]
        if name == "matrix":
            n = v
        elif name == "translate":
            n = [1, 0, 0, 1, v[0], v[1] if len(v) > 1 else 0]
        elif name == "scale":
            n = [v[0], 0, 0, v[1] if len(v) > 1 else v[0], 0, 0]
        elif name == "rotate":
            r = math.radians(v[0]); c, s = math.cos(r), math.sin(r)
            n = [c, s, -s, c, 0, 0]
            if len(v) == 3:
                n = mat_mul(mat_mul([1, 0, 0, 1, v[1], v[2]], n), [1, 0, 0, 1, -v[1], -v[2]])
        else:
            raise ValueError("unsupported transform " + name)
        m = mat_mul(m, n)
    return m


def apply(m, p):
    return (m[0]*p[0] + m[2]*p[1] + m[4], m[1]*p[0] + m[3]*p[1] + m[5])


def path_segments(d):
    toks = re.findall(r"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", d)
    segs, i, cmd = [], 0, None
    cur = start = (0.0, 0.0)
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in "Zz":
                if cur != start:
                    segs.append((cur, start))
                cur = start
                continue
        if cmd in "Mm":
            x, y = float(toks[i]), float(toks[i+1]); i += 2
            cur = (cur[0] + x, cur[1] + y) if cmd == "m" else (x, y)
            start = cur
            cmd = "l" if cmd == "m" else "L"  # implicit lineto after moveto
        elif cmd in "Ll":
            x, y = float(toks[i]), float(toks[i+1]); i += 2
            nxt = (cur[0] + x, cur[1] + y) if cmd == "l" else (x, y)
            segs.append((cur, nxt)); cur = nxt
        elif cmd in "Hh":
            x = float(toks[i]); i += 1
            nxt = (cur[0] + x if cmd == "h" else x, cur[1])
            segs.append((cur, nxt)); cur = nxt
        elif cmd in "Vv":
            y = float(toks[i]); i += 1
            nxt = (cur[0], cur[1] + y if cmd == "v" else y)
            segs.append((cur, nxt)); cur = nxt
        else:
            raise ValueError("unsupported path command " + cmd)
    return segs


def num(el, k):
    return float(re.sub(r"[a-z%]+$", "", el.get(k, "0")))


def walk(el, m, inherited, out):
    tag = el.tag.split("}")[-1]
    st = style_dict(el)
    ctx = dict(inherited)
    if "stroke" in st:
        ctx["stroke"] = st["stroke"]
    op = 1.0
    for k in ("opacity", "stroke-opacity"):
        if k in st:
            op *= float(st[k])
    ctx["opacity"] = inherited.get("opacity", 1.0) * op
    m = mat_mul(m, parse_transform(el.get("transform")))
    segs = []
    if tag == "line":
        segs = [((num(el, "x1"), num(el, "y1")), (num(el, "x2"), num(el, "y2")))]
    elif tag == "path":
        segs = path_segments(el.get("d", ""))
    elif tag in ("polyline", "polygon"):
        v = [float(x) for x in re.findall(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", el.get("points", ""))]
        pts = list(zip(v[0::2], v[1::2]))
        segs = list(zip(pts, pts[1:]))
        if tag == "polygon" and len(pts) > 2:
            segs.append((pts[-1], pts[0]))
    elif tag == "rect":
        x, y, w, h = num(el, "x"), num(el, "y"), num(el, "width"), num(el, "height")
        p = [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]
        segs = [(p[k], p[(k + 1) % 4]) for k in range(4)]
    if segs:
        t = stroke_type(ctx.get("stroke"))
        if t is None:
            out.setdefault("_unknown", set()).add(ctx.get("stroke"))
        else:
            ang = 180.0 * ctx["opacity"] if t in ("mountain", "valley") else 0.0
            for a, b in segs:
                out.setdefault(t, []).append((apply(m, a), apply(m, b), ang))
    for ch in el:
        if ch.tag.split("}")[-1] not in ("defs", "namedview", "metadata"):
            walk(ch, m, ctx, out)


def parse(path):
    out = {}
    walk(ET.parse(path).getroot(), [1, 0, 0, 1, 0, 0], {}, out)
    unknown = out.pop("_unknown", set())
    if unknown:
        print("  warning: ignored strokes", unknown, file=sys.stderr)
    # normalise: largest side = 100, origin at lower-left, flip SVG y-down to Rhino y-up, dedupe
    pts = [p for segs in out.values() for s in segs for p in s[:2]]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    x0, y1 = min(xs), max(ys)
    sc = 100.0 / max(max(xs) - x0, y1 - min(ys))
    res = {}
    for t, segs in out.items():
        seen, lst = set(), []
        for a, b, ang in segs:
            a2 = (round((a[0] - x0) * sc, 6), round((y1 - a[1]) * sc, 6))
            b2 = (round((b[0] - x0) * sc, 6), round((y1 - b[1]) * sc, 6))
            if math.dist(a2, b2) < 1e-6:
                continue
            key = tuple(sorted([a2, b2]))
            if key in seen:
                continue
            seen.add(key)
            lst.append([a2[0], a2[1], b2[0], b2[1], round(ang, 3)])
        res[t] = lst
    return res


if __name__ == "__main__":
    result = []
    for arg in sys.argv[2:]:
        name, path = arg.split("=", 1)
        segs = parse(path)
        print(name, {t: len(v) for t, v in segs.items()})
        result.append({"name": name, "source": path, "segments": segs})
    with open(sys.argv[1], "w") as f:
        json.dump(result, f)
