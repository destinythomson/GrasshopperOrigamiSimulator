// OrigamiKangaroo.cs
// Grasshopper (Rhino 8) legacy "C# Script" component source: the "Origami Goals" builder of the Kangaroo2
// version of the origami simulator (grasshopper/OrigamiSim_Kangaroo.gh). It turns the crease-pattern layers
// into Kangaroo goals; the native Kangaroo Solver does the physics.
//
// The pattern builder (lines -> planar graph -> faces -> triangles -> cuts -> creases) is copied from
// OrigamiSolver.cs (v1) so both versions see the same mesh with the same vertex order. Changes from v1:
//   - cut-vertex copies are offset 0.1 x document tolerance into their own triangles, because Kangaroo merges
//     particles that sit closer than its Tolerance (v1 copies share one position);
//   - edge-keyed dictionaries use the LongHash comparer from OrigamiPrint.cs;
//   - instead of simulating, the component outputs geometry and OrigamiCrease goals.
//
// Why a custom crease goal: Kangaroo's stock Hinge goal wraps its angle at +-180 deg and flips or stalls
// above ~150-165 deg (probed 2026-10-03, see plans/grasshopper-kangaroo-solver.md). OrigamiCrease uses v1's
// fold-angle formula (thetaCalcShader) read in a window on its own side (valley (-90, 270], mountain [-270, 90)
// deg; flat creases wrap to +-180) so it never pushes the wrong way near a full fold, and v1's crease forces.
//
// The component's ScriptSource.References must include
//   C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\KangarooSolver.dll
//
// Split on the "// ===== " markers like OrigamiSolver.cs:
//   USING -> UsingCode, SCRIPT -> ScriptCode (RunScript body), ADDITIONAL -> AdditionalCode.
//
// Component inputs : M, V, B, F, C, H (List<Curve>), Anchors (List<Point3d>), Fold (double),
//                    CreaseK (double, M/V crease strength), FacetK (double, flat crease strength)
// Component outputs: Edges (List<Line>, every mesh edge, flat)  -> Kangaroo Length(Line)
//                    Creases (List of OrigamiCrease goals)        -> Kangaroo Solver
//                    Flat (Mesh, flat triangle mesh)              -> Kangaroo Show, Readout
//                    AnchorPts (List<Point3d>, snapped to nodes)  -> Kangaroo Anchor
//                    Quads (List<int>, 4 per crease: lo, hi, wing1, wing2), Targets (List<double>, rad),
//                    Types (List<int>, 1 = M/V, 0 = flat)         -> Readout
//                    Tol (double)  -> Solver Tolerance;  Thr (double) -> Solver Threshold;  Info (string)

// ===== USING =====
using System.Linq;

// ===== SCRIPT (RunScript body) =====
double docTol = (RhinoDocument != null) ? RhinoDocument.ModelAbsoluteTolerance : 0.001;
OrigamiK.Result res = OrigamiK.Run(Component.InstanceGuid, M, V, B, F, C, H, Anchors, Fold, CreaseK, FacetK, docTol);
if (res.Warning != null) Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, res.Warning);
Edges = res.Edges;
Creases = res.Creases;
Flat = res.Flat;
AnchorPts = res.AnchorPts;
Quads = res.Quads;
Targets = res.Targets;
Types = res.Types;
Tol = res.Tol;
Thr = res.Thr;
Info = res.Info;

// ===== ADDITIONAL =====
static class OrigamiK
{
  // ---------------------------------------------------------------------------------------------
  // Constants
  // ---------------------------------------------------------------------------------------------
  const double TARGET_DEG = 180.0;  // full fold; M = -180, V = +180   js/pattern.js:75,85
  const double MAXROT_DEG = 15.0;   // one crease goal asks for at most 2 x this fold-angle change per iteration
  const int DEMO_WHEN_EMPTY = 2;    // used only when every crease layer is empty:
                                    // 0 none, 1 single valley (2 triangles), 2 Miura 4x4, 3 Miura 12x12

  // line kinds = index into the groups array. Triangulation diagonals are not in the input and count as F.
  const int KIND_B = 0, KIND_M = 1, KIND_V = 2, KIND_F = 3, KIND_H = 4, KIND_C = 5, NKINDS = 6;
  static int Priority(int kind)   // which kind wins when two input lines overlap
  {
    switch (kind) { case KIND_C: return 5; case KIND_M: case KIND_V: return 4; case KIND_F: return 3; case KIND_H: return 2; default: return 1; }
  }

  // ---------------------------------------------------------------------------------------------
  // The crease goal. Particles: 0 = node3 (crease start, lo), 1 = node4 (crease end, hi),
  // 2 = wing tip of face 1 (CCW lo -> hi), 3 = wing tip of face 2. Same roles as js/crease.js:103-108.
  // ---------------------------------------------------------------------------------------------
  public class OrigamiCrease : KangarooSolver.GoalObject
  {
    public double Target;   // radians, + valley, - mountain
    public int Side;        // +1 folds on the valley side, -1 on the mountain side, 0 = flat crease
    readonly double maxRot;

    public OrigamiCrease(Point3d e3, Point3d e4, Point3d w1, Point3d w2, double target, int side, double k)
    {
      PPos = new Point3d[] { e3, e4, w1, w2 };
      Move = new Vector3d[4];
      Weighting = new double[] { k, k, k, k };
      Target = target;
      Side = side;
      maxRot = MAXROT_DEG * Math.PI / 180.0;
    }

    public override void Calculate(List<KangarooSolver.Particle> p)
    {
      Point3d x3 = p[PIndex[0]].Position, x4 = p[PIndex[1]].Position;
      Point3d x1 = p[PIndex[2]].Position, x2 = p[PIndex[3]].Position;
      for (int i = 0; i < 4; i++) Move[i] = Vector3d.Zero;

      Vector3d c = x4 - x3;
      if (c.Length < 1e-12) return;
      c.Unitize();
      // face normals with v1's vertex order (normalCalc shader): face 1 = (lo, hi, w1), face 2 = (hi, lo, w2)
      Vector3d n1 = Vector3d.CrossProduct(x4 - x3, x1 - x3);
      Vector3d n2 = Vector3d.CrossProduct(x3 - x4, x2 - x4);
      if (n1.Length < 1e-12 || n2.Length < 1e-12) return;
      n1.Unitize(); n2.Unitize();
      double theta = Math.Atan2(Vector3d.CrossProduct(n1, c) * n2, n1 * n2);   // thetaCalcShader

      // atan2 wraps at +-180 deg, where a fully folded crease sits. v1 unwraps with the previous angle;
      // this goal keeps no history (Kangaroo may clone goals), so it reads theta in a window centred on
      // the crease's own side instead: valley (-90, 270] deg, mountain [-270, 90) deg. A mountain that
      // overshoots to +179.9 then reads -180.1 and unfolds back the way it came, not through the other face.
      double d;
      if (Side > 0) { if (theta <= -0.5 * Math.PI) theta += 2.0 * Math.PI; d = Target - theta; }
      else if (Side < 0) { if (theta >= 0.5 * Math.PI) theta -= 2.0 * Math.PI; d = Target - theta; }
      else { d = Target - theta; if (d > Math.PI) d -= 2.0 * Math.PI; else if (d < -Math.PI) d += 2.0 * Math.PI; }
      d = Math.Max(-2.0 * maxRot, Math.Min(2.0 * maxRot, d));

      // gradient of theta with respect to the four points: the directions of v1's crease forces
      // (velocityCalcShader crease loop; h = moment arm, c = position along the crease, updateCreaseGeo shader).
      // It comes from a rotation-invariant energy, so the moves have zero net force and zero net torque:
      // a sheet that cannot reach every target (Miura mid-fold) settles instead of spinning.
      double L = (x4 - x3).Length;
      Vector3d v1 = x1 - x3, v2 = x2 - x3;
      double p1 = c * v1, p2 = c * v2;
      double h1 = Math.Sqrt(Math.Abs(v1.SquareLength - p1 * p1)), h2 = Math.Sqrt(Math.Abs(v2.SquareLength - p2 * p2));
      if (h1 < 1e-9 || h2 < 1e-9) return;
      double c1 = p1 / L, c2 = p2 / L;
      Vector3d g1 = n1 / h1, g2 = n2 / h2;
      Vector3d g3 = -(g1 * (1.0 - c1) + g2 * (1.0 - c2));
      Vector3d g4 = -(g1 * c1 + g2 * c2);
      double gg = g1.SquareLength + g2.SquareLength + g3.SquareLength + g4.SquareLength;
      if (gg < 1e-30) return;
      // smallest move that changes theta by d to first order (one Gauss-Newton step)
      double s = d / gg;
      Move[0] = g3 * s; Move[1] = g4 * s; Move[2] = g1 * s; Move[3] = g2 * s;
    }
  }

  public class Result
  {
    public List<Line> Edges = new List<Line>();
    public List<object> Creases = new List<object>();
    public Rhino.Geometry.Mesh Flat;
    public List<Point3d> AnchorPts = new List<Point3d>();
    public List<int> Quads = new List<int>();
    public List<double> Targets = new List<double>();
    public List<int> Types = new List<int>();
    public double Tol, Thr;
    public string Info, Warning;
  }

  // flat topology, cached per component until the input lines change
  class Topo
  {
    public ulong Key; public int Demo; public string Note;
    public Point3d[] Pos; public List<int[]> Tris = new List<int[]>();
    public List<Line> Edges = new List<Line>();
    public List<int> Quads = new List<int>(); public List<int> Kind = new List<int>(); public List<double> L0 = new List<double>();
    public double LMean; public List<Point3d> AnchorPts = new List<Point3d>(); public int Hinges;
    public double Diag;
    public int FoldSign = 1;   // sign of the last nonzero Fold: which side the creases folded toward
  }

  static readonly Dictionary<Guid, Topo> Cache = new Dictionary<Guid, Topo>();

  // ---------------------------------------------------------------------------------------------
  // Entry point (one call per Grasshopper solve)
  // ---------------------------------------------------------------------------------------------
  public static Result Run(Guid id, List<Curve> M, List<Curve> V, List<Curve> B, List<Curve> F, List<Curve> C,
                           List<Curve> H, List<Point3d> anchors, double fold, double creaseK, double facetK, double tol)
  {
    if (!(tol > 0)) tol = 1e-6;
    Result r = new Result();
    r.Tol = 0.01 * tol;
    int curved = 0;
    List<Line>[] groups = new List<Line>[NKINDS];
    groups[KIND_M] = ToLines(M, tol, ref curved);
    groups[KIND_V] = ToLines(V, tol, ref curved);
    groups[KIND_B] = ToLines(B, tol, ref curved);
    groups[KIND_F] = ToLines(F, tol, ref curved);
    groups[KIND_C] = ToLines(C, tol, ref curved);
    groups[KIND_H] = ToLines(H, tol, ref curved);
    int nLines = 0;
    foreach (List<Line> g in groups) nLines += g.Count;
    List<Point3d> an = anchors != null ? new List<Point3d>(anchors) : new List<Point3d>();

    int demo = 0;
    if (nLines == 0 && DEMO_WHEN_EMPTY > 0)
    {
      demo = DEMO_WHEN_EMPTY;
      Demo(demo, groups[KIND_M], groups[KIND_V], groups[KIND_B]);
    }

    ulong key = Hash(groups, an, tol, demo);
    Topo t;
    Cache.TryGetValue(id, out t);
    if (t == null || t.Key != key)
    {
      try { t = Build(groups, an, tol); }
      catch (Exception ex)
      {
        Cache.Remove(id);
        r.Info = "build failed: " + ex.Message;
        r.Warning = r.Info;
        return r;
      }
      t.Key = key;
      t.Demo = demo;
      Cache[id] = t;
    }

    if (double.IsNaN(fold)) fold = 0;
    fold = Math.Max(-1.0, Math.Min(1.0, fold));
    if (fold > 0) t.FoldSign = 1; else if (fold < 0) t.FoldSign = -1;
    if (double.IsNaN(creaseK) || creaseK < 0) creaseK = 0;
    if (double.IsNaN(facetK) || facetK < 0) facetK = 0;

    // goals are rebuilt every solve (cheap); Kangaroo matches them to its particles by flat position
    int nc = t.Kind.Count, mv = 0;
    for (int c = 0; c < nc; c++)
    {
      int kind = t.Kind[c];
      bool isMV = kind == KIND_M || kind == KIND_V;
      double target = isMV ? (kind == KIND_M ? -1.0 : 1.0) * TARGET_DEG * Math.PI / 180.0 * fold : 0.0;
      double k = (isMV ? creaseK : facetK) * t.L0[c] / t.LMean;   // v1: k = stiffness x length (js/crease.js:44-48)
      int i3 = t.Quads[4 * c], i4 = t.Quads[4 * c + 1], i1 = t.Quads[4 * c + 2], i2 = t.Quads[4 * c + 3];
      int side = isMV ? (kind == KIND_M ? -1 : 1) * t.FoldSign : 0;
      r.Creases.Add(new OrigamiCrease(t.Pos[i3], t.Pos[i4], t.Pos[i1], t.Pos[i2], target, side, k));
      r.Targets.Add(target);
      r.Types.Add(isMV ? 1 : 0);
      if (isMV) mv++;
    }

    Rhino.Geometry.Mesh flat = new Rhino.Geometry.Mesh();
    foreach (Point3d p in t.Pos) flat.Vertices.Add(p);
    foreach (int[] tr in t.Tris) flat.Faces.AddFace(tr[0], tr[1], tr[2]);
    flat.Normals.ComputeNormals();
    r.Flat = flat;
    r.Edges = new List<Line>(t.Edges);
    r.AnchorPts = new List<Point3d>(t.AnchorPts);
    r.Quads = new List<int>(t.Quads);
    r.Thr = 1e-12 * t.Diag;

    System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
    r.Info = string.Format(ci,
      "nodes={0} edges={1} faces={2} creases={3} mvCreases={4} anchors={5} fold={6:F2} creaseK={7:G4} facetK={8:G4} tol={9:E2} thr={10:E2} demo={11}{12}",
      t.Pos.Length, t.Edges.Count, t.Tris.Count, nc, mv, t.AnchorPts.Count, fold, creaseK, facetK, r.Tol, r.Thr, t.Demo,
      string.IsNullOrEmpty(t.Note) ? "" : " note=" + t.Note);
    if (curved > 0)
      r.Warning = curved + " curved segment(s) were approximated with straight lines (curved creases are not simulated as curves).";
    return r;
  }

  // Curves from Rhino (lines, polylines, rectangles, polycurves) -> straight line segments.
  // Non-linear segments are approximated by a polyline and counted.
  static List<Line> ToLines(List<Curve> curves, double tol, ref int approximated)
  {
    List<Line> lines = new List<Line>();
    if (curves == null) return lines;
    foreach (Curve c in curves)
    {
      if (c == null || !c.IsValid) continue;
      Polyline pl;
      if (c.TryGetPolyline(out pl)) { AddPolyline(lines, pl); continue; }
      Curve[] segs = c.DuplicateSegments();
      if (segs == null || segs.Length == 0) segs = new Curve[] { c };
      foreach (Curve sg in segs)
      {
        if (sg.IsLinear(tol)) { lines.Add(new Line(sg.PointAtStart, sg.PointAtEnd)); continue; }
        approximated++;
        PolylineCurve pc = sg.ToPolyline(0, 0, 0.1, 0, 0, tol, 0, 0, true);
        Polyline p2;
        if (pc != null && pc.TryGetPolyline(out p2)) AddPolyline(lines, p2);
        else lines.Add(new Line(sg.PointAtStart, sg.PointAtEnd));
      }
    }
    return lines;
  }

  static void AddPolyline(List<Line> lines, Polyline pl)
  {
    for (int i = 0; i + 1 < pl.Count; i++) lines.Add(new Line(pl[i], pl[i + 1]));
  }

  // ---------------------------------------------------------------------------------------------
  // Build: lines -> planar graph -> faces -> triangles -> nodes, beams, creases
  // (replaces pattern.js cleanup + triangulation and model.js sync). Copied from OrigamiSolver.cs.
  // ---------------------------------------------------------------------------------------------
  static Topo Build(List<Line>[] groups, List<Point3d> anchors, double tol)
  {
    List<string> notes = new List<string>();

    // 1. tagged input lines
    List<Line> lines = new List<Line>();
    List<int> kinds = new List<int>();
    for (int g = 0; g < NKINDS; g++) AddLines(lines, kinds, groups[g], g, tol);
    int n = lines.Count;
    if (n == 0) throw new Exception("no input lines longer than the document tolerance");

    // 2. split every line where another line crosses or touches it (pattern.js splits intersections)
    List<double>[] cuts = new List<double>[n];
    BoundingBox[] boxes = new BoundingBox[n];
    for (int i = 0; i < n; i++)
    {
      cuts[i] = new List<double>();
      cuts[i].Add(0.0); cuts[i].Add(1.0);
      boxes[i] = lines[i].BoundingBox;
      boxes[i].Inflate(tol);
    }
    for (int i = 0; i < n; i++)
    {
      for (int j = i + 1; j < n; j++)
      {
        if (!Overlap(boxes[i], boxes[j])) continue;
        Line la = lines[i], lb = lines[j];
        double ta, tb;
        if (Rhino.Geometry.Intersect.Intersection.LineLine(la, lb, out ta, out tb, tol, true))
        {
          AddCut(cuts[i], ta, la.Length, tol);
          AddCut(cuts[j], tb, lb.Length, tol);
        }
        EndpointCuts(la, lb, cuts[i], tol);   // T-junctions and collinear overlaps
        EndpointCuts(lb, la, cuts[j], tol);
      }
    }

    // 3. merge vertices within tolerance, collect unique edges (M/V wins over B on duplicates)
    List<Point3d> verts = new List<Point3d>();
    Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    Dictionary<long, int> edgeKind = new Dictionary<long, int>(new LongHash());
    for (int i = 0; i < n; i++)
    {
      List<double> ts = cuts[i];
      ts.Sort();
      Line ln = lines[i];
      double len = ln.Length;
      for (int k = 0; k + 1 < ts.Count; k++)
      {
        if ((ts[k + 1] - ts[k]) * len <= tol) continue;
        int u = VertexId(ln.PointAt(ts[k]), verts, grid, tol);
        int w = VertexId(ln.PointAt(ts[k + 1]), verts, grid, tol);
        if (u == w) continue;
        long ek = EdgeKey(u, w);
        int old;
        if (!edgeKind.TryGetValue(ek, out old) || Priority(kinds[i]) > Priority(old)) edgeKind[ek] = kinds[i];
      }
    }
    int NV = verts.Count;

    // 4. drop dangling lines (vertices of degree 1), as pattern.js removes stray vertices
    List<HashSet<int>> nbr = new List<HashSet<int>>(NV);
    for (int k = 0; k < NV; k++) nbr.Add(new HashSet<int>());
    foreach (long ek in edgeKind.Keys) { int lo = (int)(ek >> 32), hi = (int)(ek & 0xffffffffL); nbr[lo].Add(hi); nbr[hi].Add(lo); }
    Stack<int> stack = new Stack<int>();
    for (int k = 0; k < NV; k++) if (nbr[k].Count == 1) stack.Push(k);
    int pruned = 0;
    while (stack.Count > 0)
    {
      int k = stack.Pop();
      if (nbr[k].Count != 1) continue;
      int o = -1;
      foreach (int x in nbr[k]) { o = x; break; }
      nbr[k].Clear(); nbr[o].Remove(k);
      edgeKind.Remove(EdgeKey(k, o));
      pruned++;
      if (nbr[o].Count == 1) stack.Push(o);
    }
    if (pruned > 0) notes.Add("prunedDanglingEdges:" + pruned);

    // 5. pattern plane and 2D coordinates (normal oriented toward +Z when possible)
    List<Point3d> used = new List<Point3d>();
    for (int k = 0; k < NV; k++) if (nbr[k].Count > 0) used.Add(verts[k]);
    if (used.Count < 3) throw new Exception("no closed faces: fewer than 3 connected vertices");
    Plane plane;
    if (Plane.FitPlaneToPoints(used, out plane) == PlaneFitResult.Failure) throw new Exception("could not fit a plane to the pattern");
    if (plane.ZAxis.Z < 0) plane.Flip();
    double dev = 0;
    foreach (Point3d p in used) dev = Math.Max(dev, Math.Abs(plane.DistanceTo(p)));
    if (dev > 10 * tol) notes.Add("notPlanar:maxDev=" + dev.ToString("G3", System.Globalization.CultureInfo.InvariantCulture));
    double[] X = new double[NV], Y = new double[NV];
    for (int k = 0; k < NV; k++) { double s, t; plane.ClosestParameter(verts[k], out s, out t); X[k] = s; Y[k] = t; }

    // 6. planar face walk: neighbours sorted CCW; next half-edge = clockwise neighbour of the way back
    int[][] around = new int[NV][];
    for (int k = 0; k < NV; k++)
    {
      List<int> lst = new List<int>(nbr[k]);
      int kk = k;
      lst.Sort((p, q) => Math.Atan2(Y[p] - Y[kk], X[p] - X[kk]).CompareTo(Math.Atan2(Y[q] - Y[kk], X[q] - X[kk])));
      around[k] = lst.ToArray();
    }
    HashSet<long> visited = new HashSet<long>(new LongHash());
    List<List<int>> faces = new List<List<int>>();
    double areaEps = tol * tol;
    for (int u = 0; u < NV; u++)
    {
      foreach (int w0 in around[u])
      {
        if (visited.Contains(HalfKey(u, w0))) continue;
        List<int> loop = new List<int>();
        int cu = u, cw = w0, guard = 0;
        bool ok = true;
        while (true)
        {
          visited.Add(HalfKey(cu, cw));
          loop.Add(cu);
          int[] ar = around[cw];
          int idx = Array.IndexOf(ar, cu);
          int nx = ar[(idx - 1 + ar.Length) % ar.Length];
          cu = cw; cw = nx;
          if (cu == u && cw == w0) break;
          if (++guard > 4 * NV + 8) { ok = false; break; }
        }
        if (ok && SignedArea(loop, X, Y) > areaEps) faces.Add(loop);   // outer boundaries are CW: dropped
      }
    }
    if (faces.Count == 0) throw new Exception("no closed faces found: check that lines meet within document tolerance");

    // 7. triangulate: triangles kept, quads split on the shorter diagonal, larger polygons ear-clipped
    //    (pattern.js:984-1135); new diagonals become flat creases
    List<int[]> tris = new List<int[]>();
    foreach (List<int> f in faces) Triangulate(f, verts, X, Y, tris, areaEps);
    if (tris.Count == 0) throw new Exception("triangulation produced no triangles");

    // 7b. cuts (pattern.js splitCuts): around every vertex on a cut, the triangle fan is split into groups
    //     that are connected only across non-cut edges; each extra group gets its own copy of the vertex.
    //     A cut edge then becomes two separate boundary edges. origOf maps copies back to input vertices.
    List<int> origOf = new List<int>(NV);
    for (int k = 0; k < NV; k++) origOf.Add(k);
    HashSet<long> cutKeys = new HashSet<long>(new LongHash());
    foreach (KeyValuePair<long, int> kv in edgeKind) if (kv.Value == KIND_C) cutKeys.Add(kv.Key);
    if (cutKeys.Count > 0)
    {
      List<int>[] fan = new List<int>[NV];
      for (int t = 0; t < tris.Count; t++) foreach (int k in tris[t]) { if (fan[k] == null) fan[k] = new List<int>(); fan[k].Add(t); }
      HashSet<int> onCut = new HashSet<int>();
      foreach (long ck in cutKeys) { onCut.Add((int)(ck >> 32)); onCut.Add((int)(ck & 0xffffffffL)); }
      int copies = 0;
      foreach (int vtx in onCut)
      {
        if (fan[vtx] == null) continue;
        List<int> ft = fan[vtx];
        int[] parent = new int[ft.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        // triangles of the fan that share a non-cut edge (vtx, w) are joined
        Dictionary<int, int> firstWithW = new Dictionary<int, int>();
        for (int i = 0; i < ft.Count; i++)
          foreach (int w in tris[ft[i]])
          {
            if (w == vtx || cutKeys.Contains(EdgeKey(vtx, origOf[w]))) continue;   // w may already be a copy
            int j;
            if (firstWithW.TryGetValue(w, out j)) Union(parent, i, j); else firstWithW[w] = i;
          }
        Dictionary<int, int> rootToVertex = new Dictionary<int, int>();
        for (int i = 0; i < ft.Count; i++)
        {
          int root = Find(parent, i);
          int nv;
          if (!rootToVertex.TryGetValue(root, out nv))
          {
            if (rootToVertex.Count == 0) nv = vtx;
            else { verts.Add(verts[vtx]); nv = verts.Count - 1; origOf.Add(vtx); copies++; }
            rootToVertex[root] = nv;
          }
          if (nv != vtx) { int[] tr = tris[ft[i]]; for (int c = 0; c < 3; c++) if (tr[c] == vtx) tr[c] = nv; }
        }
      }
      notes.Add("cutEdges:" + cutKeys.Count + ",cutVertexCopies:" + copies);
      // Kangaroo merges particles closer than its Tolerance (0.01 x tol). Move each copy 0.1 x tol toward the
      // centroid of its own triangles so it stays a separate particle; v1 keeps copies at the same position.
      double cutOffset = 0.1 * tol;
      for (int vc = NV; vc < verts.Count; vc++)
      {
        Point3d cen = Point3d.Origin; int cnt = 0;
        foreach (int[] tr in tris)
          if (tr[0] == vc || tr[1] == vc || tr[2] == vc) { cen += (verts[tr[0]] + verts[tr[1]] + verts[tr[2]]) / 3.0; cnt++; }
        if (cnt == 0) continue;
        Vector3d dir = cen / cnt - verts[vc];
        if (dir.Unitize()) verts[vc] = verts[vc] + dir * cutOffset;
      }
      if (copies > 0) notes.Add("cutOffset:" + cutOffset.ToString("G3", System.Globalization.CultureInfo.InvariantCulture));
    }
    NV = verts.Count;

    // 8. edges of the triangle mesh. face "left" of lo->hi = the triangle whose CCW order runs lo->hi
    Dictionary<long, int[]> einfo = new Dictionary<long, int[]>(new LongHash());   // [leftFace, rightFace]
    int nonManifold = 0;
    for (int t = 0; t < tris.Count; t++)
    {
      int[] tr = tris[t];
      for (int e = 0; e < 3; e++)
      {
        int p = tr[e], q = tr[(e + 1) % 3];
        long ek = EdgeKey(p, q);
        int[] ei;
        if (!einfo.TryGetValue(ek, out ei)) { ei = new int[] { -1, -1 }; einfo[ek] = ei; }
        int side = p < q ? 0 : 1;
        if (ei[side] >= 0) nonManifold++;
        ei[side] = t;
      }
    }
    if (nonManifold > 0) notes.Add("nonManifoldEdges:" + nonManifold);

    // 9. nodes = vertices used by triangles (same order as v1)
    int[] map = new int[NV];
    for (int k = 0; k < NV; k++) map[k] = -1;
    List<int> nodeVerts = new List<int>();
    foreach (int[] tr in tris) foreach (int k in tr) if (map[k] < 0) { map[k] = nodeVerts.Count; nodeVerts.Add(k); }

    Topo topo = new Topo();
    topo.Note = null;
    int N = nodeVerts.Count;
    topo.Pos = new Point3d[N];
    for (int i = 0; i < N; i++) topo.Pos[i] = verts[nodeVerts[i]];
    foreach (int[] tr in tris) topo.Tris.Add(new int[] { map[tr[0]], map[tr[1]], map[tr[2]] });
    BoundingBox bb = new BoundingBox(topo.Pos);
    topo.Diag = bb.Diagonal.Length;

    // anchors snap to every node within 10 x tolerance (v1 fixes the same nodes)
    double anchorTol = Math.Max(10 * tol, 1e-9);
    for (int i = 0; i < N; i++)
      foreach (Point3d a in anchors)
        if (topo.Pos[i].DistanceTo(a) <= anchorTol) { topo.AnchorPts.Add(topo.Pos[i]); break; }
    if (anchors.Count > 0) notes.Add("fixedNodes:" + topo.AnchorPts.Count);

    // edges (Kangaroo Length goals) and creases on interior M, V, F edges and triangulation diagonals (pattern.js:826)
    List<long> creaseKeys = new List<long>();
    foreach (KeyValuePair<long, int[]> kv in einfo)
    {
      int lo = (int)(kv.Key >> 32), hi = (int)(kv.Key & 0xffffffffL);
      topo.Edges.Add(new Line(topo.Pos[map[lo]], topo.Pos[map[hi]]));
      if (kv.Value[0] >= 0 && kv.Value[1] >= 0)
      {
        int k = KindOf(kv.Key, edgeKind, origOf);
        if (k == KIND_M || k == KIND_V || k == KIND_F) creaseKeys.Add(kv.Key);
        else topo.Hinges++;
      }
    }
    if (topo.Hinges > 0) notes.Add("freeHinges:" + topo.Hinges);

    double sumL = 0;
    foreach (long ek in creaseKeys)
    {
      int lo = (int)(ek >> 32), hi = (int)(ek & 0xffffffffL);
      int[] ei = einfo[ek];
      int f1 = ei[0], f2 = ei[1];                  // face 1 is left of node3 -> node4 (pattern.js creaseParams order)
      topo.Quads.Add(map[lo]); topo.Quads.Add(map[hi]);
      topo.Quads.Add(map[Third(tris[f1], lo, hi)]);
      topo.Quads.Add(map[Third(tris[f2], lo, hi)]);
      topo.Kind.Add(KindOf(ek, edgeKind, origOf));
      double L0 = topo.Pos[map[lo]].DistanceTo(topo.Pos[map[hi]]);
      topo.L0.Add(L0);
      sumL += L0;
    }
    topo.LMean = creaseKeys.Count > 0 ? sumL / creaseKeys.Count : 1.0;

    topo.Note = notes.Count > 0 ? string.Join(",", notes.ToArray()) : null;
    return topo;
  }

  // ---------------------------------------------------------------------------------------------
  // Demo patterns (XY plane)
  // ---------------------------------------------------------------------------------------------
  static void Demo(int which, List<Line> m, List<Line> v, List<Line> b)
  {
    if (which == 1)
    {
      // the lessons' running example: unit square, one valley crease on the diagonal (4 nodes, 2 triangles)
      Point3d p0 = new Point3d(0, 0, 0), p1 = new Point3d(1, 0, 0), p2 = new Point3d(1, 1, 0), p3 = new Point3d(0, 1, 0);
      b.Add(new Line(p0, p1)); b.Add(new Line(p1, p2)); b.Add(new Line(p2, p3)); b.Add(new Line(p3, p0));
      v.Add(new Line(p0, p2));
      return;
    }
    // Miura-ori. Vertex (i, j) at x = i + (j odd ? s : 0), y = j: columns zigzag, rows are straight.
    // Zigzag column i is all one type (even i mountain, odd i valley). A row segment between columns
    // i and i+1 takes the type of column i+1 on even rows and of column i on odd rows; this gives
    // 3:1 mountain/valley vertices that satisfy the big-little-big rule.
    int nx = which == 3 ? 12 : 4, ny = nx;
    double s = 0.25;
    for (int i = 0; i <= nx; i++)
      for (int j = 0; j < ny; j++)
      {
        Line ln = new Line(MiuraPt(i, j, s), MiuraPt(i, j + 1, s));
        if (i == 0 || i == nx) b.Add(ln); else if (i % 2 == 0) m.Add(ln); else v.Add(ln);
      }
    for (int j = 0; j <= ny; j++)
      for (int i = 0; i < nx; i++)
      {
        Line ln = new Line(MiuraPt(i, j, s), MiuraPt(i + 1, j, s));
        if (j == 0 || j == ny) { b.Add(ln); continue; }
        int col = (j % 2 == 0) ? i + 1 : i;
        if (col % 2 == 0) m.Add(ln); else v.Add(ln);
      }
  }

  static Point3d MiuraPt(int i, int j, double s) { return new Point3d(i + (j % 2 == 1 ? s : 0.0), j, 0); }

  // ---------------------------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------------------------
  // Edge keys pack two vertex ids as (lo << 32) | hi. long.GetHashCode() is lo ^ hi, which collides for
  // nearby ids; this comparer mixes the bits first (copied from OrigamiPrint.cs).
  class LongHash : IEqualityComparer<long>
  {
    public bool Equals(long a, long b) { return a == b; }
    public int GetHashCode(long x) { unchecked { ulong z = (ulong)x * 0x9E3779B97F4A7C15UL; z ^= z >> 31; return (int)(z ^ (z >> 32)); } }
  }

  static double Clamp(double x) { return x < -1.0 ? -1.0 : (x > 1.0 ? 1.0 : x); }

  static long EdgeKey(int p, int q) { int lo = Math.Min(p, q), hi = Math.Max(p, q); return ((long)lo << 32) | (uint)hi; }

  static long HalfKey(int from, int to) { return ((long)from << 32) | (uint)to; }

  // kind of a mesh edge, looked up through vertex copies; edges not in the input are triangulation diagonals (F)
  static int KindOf(long meshKey, Dictionary<long, int> edgeKind, List<int> origOf)
  {
    int lo = (int)(meshKey >> 32), hi = (int)(meshKey & 0xffffffffL);
    int kind;
    return edgeKind.TryGetValue(EdgeKey(origOf[lo], origOf[hi]), out kind) ? kind : KIND_F;
  }

  static int Find(int[] parent, int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
  static void Union(int[] parent, int a, int b) { a = Find(parent, a); b = Find(parent, b); if (a != b) parent[b] = a; }

  static int Third(int[] tr, int a, int b)
  {
    for (int k = 0; k < 3; k++) if (tr[k] != a && tr[k] != b) return tr[k];
    throw new Exception("degenerate triangle");
  }

  static void AddLines(List<Line> lines, List<int> kinds, List<Line> src, int kind, double tol)
  {
    foreach (Line ln in src)
    {
      if (!ln.IsValid || ln.Length <= tol) continue;
      lines.Add(ln); kinds.Add(kind);
    }
  }

  static bool Overlap(BoundingBox a, BoundingBox b)
  {
    return a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;
  }

  static void AddCut(List<double> cuts, double t, double len, double tol)
  {
    if (t * len > tol && (1.0 - t) * len > tol) cuts.Add(t);   // interior points only
  }

  static void EndpointCuts(Line target, Line other, List<double> cuts, double tol)
  {
    Point3d[] ends = { other.From, other.To };
    foreach (Point3d p in ends)
    {
      double t = target.ClosestParameter(p);
      if (t <= 0 || t >= 1) continue;
      if (target.PointAt(t).DistanceTo(p) <= tol) AddCut(cuts, t, target.Length, tol);
    }
  }

  static long CellKey(long x, long y, long z) { unchecked { return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L); } }

  static int VertexId(Point3d p, List<Point3d> verts, Dictionary<long, List<int>> grid, double tol)
  {
    long ix = (long)Math.Floor(p.X / tol), iy = (long)Math.Floor(p.Y / tol), iz = (long)Math.Floor(p.Z / tol);
    for (long dx = -1; dx <= 1; dx++)
      for (long dy = -1; dy <= 1; dy++)
        for (long dz = -1; dz <= 1; dz++)
        {
          List<int> cell;
          if (!grid.TryGetValue(CellKey(ix + dx, iy + dy, iz + dz), out cell)) continue;
          foreach (int k in cell) if (verts[k].DistanceTo(p) <= tol) return k;
        }
    verts.Add(p);
    int id = verts.Count - 1;
    long key = CellKey(ix, iy, iz);
    List<int> own;
    if (!grid.TryGetValue(key, out own)) { own = new List<int>(); grid[key] = own; }
    own.Add(id);
    return id;
  }

  static double SignedArea(List<int> loop, double[] X, double[] Y)
  {
    double a = 0;
    for (int k = 0; k < loop.Count; k++)
    {
      int p = loop[k], q = loop[(k + 1) % loop.Count];
      a += X[p] * Y[q] - X[q] * Y[p];
    }
    return 0.5 * a;
  }

  // twice the signed area of triangle (a, b, c); > 0 when counter-clockwise
  static double Area2(int a, int b, int c, double[] X, double[] Y)
  {
    return (X[b] - X[a]) * (Y[c] - Y[a]) - (X[c] - X[a]) * (Y[b] - Y[a]);
  }

  static void Triangulate(List<int> poly, List<Point3d> P3, double[] X, double[] Y, List<int[]> tris, double eps)
  {
    int n = poly.Count;
    if (n < 3) return;
    if (n == 3)
    {
      if (Area2(poly[0], poly[1], poly[2], X, Y) > eps) tris.Add(new int[] { poly[0], poly[1], poly[2] });
      return;
    }
    if (n == 4)
    {
      int a = poly[0], b = poly[1], c = poly[2], d = poly[3];
      bool okAC = Area2(a, b, c, X, Y) > eps && Area2(a, c, d, X, Y) > eps;
      bool okBD = Area2(b, c, d, X, Y) > eps && Area2(b, d, a, X, Y) > eps;
      double lAC = P3[a].DistanceTo(P3[c]), lBD = P3[b].DistanceTo(P3[d]);
      if (okAC && (!okBD || lAC <= lBD)) { tris.Add(new int[] { a, b, c }); tris.Add(new int[] { a, c, d }); return; }
      if (okBD) { tris.Add(new int[] { b, c, d }); tris.Add(new int[] { b, d, a }); return; }
    }
    EarClip(poly, X, Y, tris, eps);
  }

  static void EarClip(List<int> poly, double[] X, double[] Y, List<int[]> tris, double eps)
  {
    List<int> idx = new List<int>(poly);
    int guard = 0;
    while (idx.Count > 3 && guard++ < 100000)
    {
      int n = idx.Count;
      bool clipped = false;
      for (int k = 0; k < n; k++)
      {
        int ip = idx[(k - 1 + n) % n], ic = idx[k], inx = idx[(k + 1) % n];
        if (Area2(ip, ic, inx, X, Y) <= eps) continue;          // reflex or straight: not an ear tip
        bool blocked = false;
        foreach (int q in idx)
        {
          if (q == ip || q == ic || q == inx) continue;
          if (Area2(ip, ic, q, X, Y) >= -eps && Area2(ic, inx, q, X, Y) >= -eps && Area2(inx, ip, q, X, Y) >= -eps) { blocked = true; break; }
        }
        if (blocked) continue;
        tris.Add(new int[] { ip, ic, inx });
        idx.RemoveAt(k);
        clipped = true;
        break;
      }
      if (!clipped) break;
    }
    if (idx.Count == 3)
    {
      if (Area2(idx[0], idx[1], idx[2], X, Y) > eps) tris.Add(new int[] { idx[0], idx[1], idx[2] });
      return;
    }
    // fallback for numerically awkward polygons: fan from the first vertex, skipping degenerate triangles
    for (int k = 1; k + 1 < idx.Count; k++)
      if (Area2(idx[0], idx[k], idx[k + 1], X, Y) > eps) tris.Add(new int[] { idx[0], idx[k], idx[k + 1] });
  }

  static ulong Hash(List<Line>[] groups, List<Point3d> an, double tol, int demo)
  {
    ulong h = 14695981039346656037UL;
    h = Mix(h, demo);
    h = Mix(h, BitConverter.DoubleToInt64Bits(tol));
    for (int g = 0; g < groups.Length; g++)
    {
      h = Mix(h, 1000 + g);
      h = Mix(h, groups[g].Count);
      foreach (Line ln in groups[g]) { h = MixPt(h, ln.From, tol); h = MixPt(h, ln.To, tol); }
    }
    h = Mix(h, 2000 + an.Count);
    foreach (Point3d p in an) h = MixPt(h, p, tol);
    return h;
  }

  static ulong MixPt(ulong h, Point3d p, double tol)
  {
    h = Mix(h, (long)Math.Round(p.X / tol));
    h = Mix(h, (long)Math.Round(p.Y / tol));
    return Mix(h, (long)Math.Round(p.Z / tol));
  }

  static ulong Mix(ulong h, long x)   // FNV-1a over the 8 bytes of x
  {
    unchecked
    {
      for (int k = 0; k < 8; k++) { h ^= (ulong)((x >> (8 * k)) & 0xff); h *= 1099511628211UL; }
    }
    return h;
  }
}
