// OrigamiPrint.cs
// Grasshopper (Rhino 8) legacy "C# Script" component source: turns a flat crease pattern into a printable
// solid of the unfolded sheet, copying the structure of a crane STL that printed and folded well
// (plans/grasshopper-print-export.md):
//   - a hinge layer over the whole sheet, z 0 .. Hinge (the part that bends)
//   - a panel layer on top, z Hinge .. Height, with GROOVE-wide gaps centred on every grooved line
//     (panels stay flush with the border, corners are sharp miters)
//   - through-holes of HOLE_D at interior vertices where at least HoleDeg grooved/cut lines meet
//
// This file is NOT compiled on its own. The build script splits it on the "// ===== " markers and
// copies each region into the component's ScriptSource (same layout as OrigamiSolver.cs):
//   USING      -> UsingCode
//   SCRIPT     -> ScriptCode   (the body of RunScript)
//   ADDITIONAL -> AdditionalCode (members of the generated Script_Instance class)
//
// Component inputs : B (border), M (mountain), V (valley), C (cut), H (hinge): List<Curve>;
//                    Size (longest sheet side, mm), Height (total, mm), Hinge (hinge layer, mm): double;
//                    HoleDeg: int
// Line types       : M, V and H are grooved; C is a full-depth slot GROOVE wide; B is the outline
//                    (a Border line inside the sheet stays solid). Facet lines are not used.
// Component outputs: PrintMesh (closed mesh, in mm, placed to the right of the pattern), Info (string)

// ===== USING =====
// The legacy component imports System, System.Collections.Generic, Rhino, Rhino.Geometry, Grasshopper and
// Grasshopper.Kernel, but not System.Linq.
using System.Linq;

// ===== SCRIPT (RunScript body) =====
double tol = (RhinoDocument != null) ? RhinoDocument.ModelAbsoluteTolerance : 0.001;
string units = (RhinoDocument != null) ? RhinoDocument.ModelUnitSystem.ToString() : "Millimeters";
OrigamiPrint.Result r = OrigamiPrint.Run(B, M, V, C, H, Size, Height, Hinge, HoleDeg, tol, units);
foreach (string e in r.Errors) Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e);
foreach (string w in r.Warnings) Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
PrintMesh = r.Mesh;
Info = r.Info;

// ===== ADDITIONAL =====
static class OrigamiPrint
{
  // ---------------------------------------------------------------------------------------------
  // Constants (mm). Values measured from the reference crane STL.
  // ---------------------------------------------------------------------------------------------
  const double GROOVE = 1.2;            // gap in the panel layer, centred on each grooved line
  const double HOLE_D = 5.0;            // through-hole diameter
  const int HOLE_SEGS = 48;             // sides of the hole polygon
  const double EPS = 0.01;              // panels stay this far inside the hinge layer's outline, holes and slots
  const double MIN_AREA = GROOVE * GROOVE;   // panel pieces smaller than this are dropped
  const double GAP_X = 0.2;             // output sits right of the pattern, this fraction of its width away
  const bool DEMO_WHEN_EMPTY = true;    // no lines at all -> the solver's Miura 4x4 demo

  const int KB = 0, KG = 1, KC = 2;     // border, groove (M/V/H), cut
  static int Priority(int k) { return k == KC ? 3 : (k == KG ? 2 : 1); }

  static readonly System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

  public class Result
  {
    public Mesh Mesh;
    public string Info = "";
    public List<string> Errors = new List<string>();
    public List<string> Warnings = new List<string>();
    // numbers for tests and Info
    public double Scale, BaseArea, PanelArea, Volume, Ms;
    public string Stages = "";   // ms per build stage
    public int TJunctions, Slivers;
    public int Segments, Vertices, Faces, Panels, InsetFallbacks, FacesDropped, Holes, Cuts, Shells;
    public bool Closed, Manifold;
    public List<Point3d> HoleCentres = new List<Point3d>();   // in output coordinates
  }

  // ---------------------------------------------------------------------------------------------
  // Entry point
  // ---------------------------------------------------------------------------------------------
  public static Result Run(List<Curve> B, List<Curve> M, List<Curve> V, List<Curve> C, List<Curve> H,
                           double size, double height, double hinge, int holeDeg, double docTol, string units)
  {
    Result res = new Result();
    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
    if (!(size > 0)) res.Errors.Add("Size must be > 0.");
    if (!(hinge > 0)) res.Errors.Add("Hinge Thickness must be > 0.");
    if (!(height > hinge)) res.Errors.Add("Total Height must be greater than Hinge Thickness (panels = Total Height - Hinge).");
    if (res.Errors.Count > 0) { res.Info = "error: " + string.Join(" ", res.Errors); return res; }
    if (!(docTol > 0)) docTol = 0.001;

    try { Build(res, B, M, V, C, H, size, height, hinge, holeDeg, docTol, units); }
    catch (Exception ex) { res.Errors.Add("build failed: " + ex.Message); res.Mesh = null; }
    sw.Stop();
    res.Ms = sw.Elapsed.TotalMilliseconds;
    res.Info = InfoText(res, height, hinge);
    return res;
  }

  static void Build(Result res, List<Curve> B, List<Curve> M, List<Curve> V, List<Curve> C, List<Curve> H,
                    double size, double height, double hinge, int holeDeg, double docTol, string units)
  {
    System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    double last = 0;
    Action<string> stage = name => { double t = clock.Elapsed.TotalMilliseconds; res.Stages += name + ":" + (t - last).ToString("F0", IC) + " "; last = t; };

    // 1. input lines by kind
    int curved = 0;
    List<Line>[] groups = { ToLines(B, docTol, ref curved), new List<Line>(), ToLines(C, docTol, ref curved) };
    groups[KG].AddRange(ToLines(M, docTol, ref curved));
    groups[KG].AddRange(ToLines(V, docTol, ref curved));
    groups[KG].AddRange(ToLines(H, docTol, ref curved));
    if (curved > 0) res.Warnings.Add(curved + " curved segment(s) were approximated with straight lines.");
    if (groups[KB].Count + groups[KG].Count + groups[KC].Count == 0)
    {
      if (!DEMO_WHEN_EMPTY) throw new Exception("no input lines");
      List<Line> dm = new List<Line>(), dv = new List<Line>();
      Demo(dm, dv, groups[KB]);
      groups[KG].AddRange(dm); groups[KG].AddRange(dv);
      res.Warnings.Add("No pattern lines: showing the Miura 4x4 demo.");
    }

    // 2. sheet extent (doc units) and planarity
    BoundingBox bb = BoundingBox.Empty;
    foreach (Line ln in (groups[KB].Count > 0 ? groups[KB] : groups[KG].Concat(groups[KC]).ToList()))
    { bb.Union(ln.From); bb.Union(ln.To); }
    double z0 = bb.Min.Z, zDev = 0;
    foreach (List<Line> g in groups) foreach (Line ln in g) zDev = Math.Max(zDev, Math.Max(Math.Abs(ln.From.Z - z0), Math.Abs(ln.To.Z - z0)));
    if (zDev > 10 * docTol) res.Warnings.Add("Pattern is not flat in the XY plane (max z offset " + zDev.ToString("G3", IC) + "); it was projected.");
    double dx = bb.Max.X - bb.Min.X, dy = bb.Max.Y - bb.Min.Y;
    if (!(Math.Max(dx, dy) > docTol)) throw new Exception("pattern has no extent");
    if (groups[KB].Count == 0)
    {
      res.Warnings.Add("No Border lines: using the bounding rectangle of the creases as the sheet outline.");
      Point3d a = new Point3d(bb.Min.X, bb.Min.Y, z0), b = new Point3d(bb.Max.X, bb.Min.Y, z0),
              c = new Point3d(bb.Max.X, bb.Max.Y, z0), d = new Point3d(bb.Min.X, bb.Max.Y, z0);
      groups[KB].Add(new Line(a, b)); groups[KB].Add(new Line(b, c)); groups[KB].Add(new Line(c, d)); groups[KB].Add(new Line(d, a));
    }

    // 3. scale to mm: longest side = size, sheet min corner at the origin, z = 0
    double s = size / Math.Max(dx, dy);
    res.Scale = s;
    double tol = Math.Max(docTol * s, 1e-4);
    List<Line> lines = new List<Line>();
    List<int> kinds = new List<int>();
    for (int k = 0; k < 3; k++)
      foreach (Line ln in groups[k])
      {
        Line t = new Line(new Point3d((ln.From.X - bb.Min.X) * s, (ln.From.Y - bb.Min.Y) * s, 0),
                          new Point3d((ln.To.X - bb.Min.X) * s, (ln.To.Y - bb.Min.Y) * s, 0));
        if (t.Length <= tol) continue;
        lines.Add(t); kinds.Add(k);
      }
    res.Segments = lines.Count;
    res.Cuts = groups[KC].Count;

    // 4. planar arrangement: split at crossings and T-junctions (same as OrigamiSolver.cs Build steps 2-3)
    int n = lines.Count;
    List<double>[] cuts = new List<double>[n];
    BoundingBox[] boxes = new BoundingBox[n];
    for (int i = 0; i < n; i++)
    {
      cuts[i] = new List<double> { 0.0, 1.0 };
      boxes[i] = lines[i].BoundingBox; boxes[i].Inflate(tol);
    }
    for (int i = 0; i < n; i++)
      for (int j = i + 1; j < n; j++)
      {
        if (!Overlap(boxes[i], boxes[j])) continue;
        Line la = lines[i], lb = lines[j];
        double ta, tb;
        if (Rhino.Geometry.Intersect.Intersection.LineLine(la, lb, out ta, out tb, tol, true))
        { AddCut(cuts[i], ta, la.Length, tol); AddCut(cuts[j], tb, lb.Length, tol); }
        EndpointCuts(la, lb, cuts[i], tol);
        EndpointCuts(lb, la, cuts[j], tol);
      }
    List<Point3d> verts = new List<Point3d>();
    Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    Dictionary<long, int> edgeKind = new Dictionary<long, int>(new LongHash());
    for (int i = 0; i < n; i++)
    {
      List<double> ts = cuts[i]; ts.Sort();
      double len = lines[i].Length;
      for (int k = 0; k + 1 < ts.Count; k++)
      {
        if ((ts[k + 1] - ts[k]) * len <= tol) continue;
        int u = VertexId(lines[i].PointAt(ts[k]), verts, grid, tol);
        int w = VertexId(lines[i].PointAt(ts[k + 1]), verts, grid, tol);
        if (u == w) continue;
        long ek = EdgeKey(u, w);
        int old;
        if (!edgeKind.TryGetValue(ek, out old) || Priority(kinds[i]) > Priority(old)) edgeKind[ek] = kinds[i];
      }
    }
    int NV = verts.Count;
    res.Vertices = NV;
    double[] X = new double[NV], Y = new double[NV];
    for (int k = 0; k < NV; k++) { X[k] = verts[k].X; Y[k] = verts[k].Y; }
    List<HashSet<int>> nbr = new List<HashSet<int>>(NV);
    for (int k = 0; k < NV; k++) nbr.Add(new HashSet<int>());
    foreach (long ek in edgeKind.Keys) { int lo = (int)(ek >> 32), hi = (int)(ek & 0xffffffffL); nbr[lo].Add(hi); nbr[hi].Add(lo); }

    stage("arrangement");
    // 5. face walk (neighbours sorted CCW; next = clockwise neighbour of the way back). CCW loops = faces,
    //    CW loops = outer boundaries. Only the component holding the largest outer boundary is printed.
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
    List<int> outer = null; double outerArea = 0;
    for (int u = 0; u < NV; u++)
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
        if (!ok) continue;
        double a = SignedArea(loop, X, Y);
        if (a > tol * tol) faces.Add(loop);
        else if (a < outerArea) { outerArea = a; outer = loop; }
      }
    if (outer == null || faces.Count == 0) throw new Exception("no closed faces: check that the Border is closed and lines meet within tolerance");

    int[] comp = new int[NV];
    for (int k = 0; k < NV; k++) comp[k] = k;
    foreach (long ek in edgeKind.Keys) Union(comp, (int)(ek >> 32), (int)(ek & 0xffffffffL));
    int mainComp = Find(comp, outer[0]);
    int otherFaces = faces.RemoveAll(f => Find(comp, f[0]) != mainComp);
    int strayEdges = edgeKind.Keys.Count(ek => Find(comp, (int)(ek >> 32)) != mainComp);
    if (otherFaces + strayEdges > 0)
      res.Warnings.Add((otherFaces + strayEdges) + " face(s)/line(s) not connected to the sheet were ignored.");

    stage("faces");
    // 6. panels: inset every face by GROOVE/2 on grooved edges, GROOVE/2 + EPS on cut edges (the slot is
    //    GROOVE wide) and EPS on border edges. EPS keeps every panel strictly inside the hinge layer, so the
    //    groove floor is one planar face with the panels as inner loops (step 9) and no boolean solids are needed.
    List<List<Curve>> panelGroups = new List<List<Curve>>();
    foreach (List<int> f0 in faces)
    {
      List<int> f = new List<int>(f0);
      List<Line> dangling = RemoveSpikes(f, verts);
      List<Point3d> P = f.Select(k => verts[k]).ToList();
      int m = P.Count;
      double[] d = new double[m];
      for (int i = 0; i < m; i++)
      {
        int kind = edgeKind[EdgeKey(f[i], f[(i + 1) % m])];
        d[i] = kind == KB ? EPS : (kind == KC ? GROOVE / 2 + EPS : GROOVE / 2);
      }

      // convex face: the inset is exactly the intersection of the edges' inward half-planes
      if (dangling.Count == 0 && IsConvex(P))
      {
        List<Point3d> clipped = ClipHalfPlanes(P, d);
        if (clipped.Count >= 3 && Math.Abs(PolyArea(clipped)) >= MIN_AREA) panelGroups.Add(new List<Curve> { Closed(clipped) });
        else res.FacesDropped++;
        continue;
      }
      List<Point3d> Q = dangling.Count == 0 ? Inset(P, d) : null;
      if (Q != null && ValidInset(P, Q))
      {
        if (Math.Abs(PolyArea(Q)) >= MIN_AREA) panelGroups.Add(new List<Curve> { Closed(Q) });
        else res.FacesDropped++;
        continue;
      }
      // fallback: face minus strips along its edges (2·d wide) and its dangling lines
      res.InsetFallbacks++;
      List<Curve> strips = new List<Curve>();
      for (int i = 0; i < m; i++) strips.Add(Strip(P[i], P[(i + 1) % m], 2 * d[i]));
      foreach (Line ln in dangling) strips.Add(Strip(ln.From, ln.To, GROOVE + 2 * EPS));
      Curve[] pieces = Curve.CreateBooleanDifference(Closed(P), strips, tol);
      int kept = 0;
      if (pieces != null)
        foreach (Curve pc in pieces)
          if (pc != null && pc.IsClosed && CurveArea(pc) >= MIN_AREA) { panelGroups.Add(new List<Curve> { pc }); kept++; }
      if (kept == 0) res.FacesDropped++;
    }
    res.Faces = faces.Count;

    stage("insets");
    // 7. holes at interior vertices of degree >= holeDeg (grooved and cut edges counted).
    //    The hinge layer gets HOLE_D; panels are cut EPS wider so they stay off the hole wall.
    int[] deg = new int[NV];
    bool[] onBorder = new bool[NV];
    foreach (KeyValuePair<long, int> kv in edgeKind)
    {
      int lo = (int)(kv.Key >> 32), hi = (int)(kv.Key & 0xffffffffL);
      if (Find(comp, lo) != mainComp) continue;
      if (kv.Value == KB) { onBorder[lo] = true; onBorder[hi] = true; }
      else { deg[lo]++; deg[hi]++; }
    }
    List<Point3d> holePts = new List<Point3d>();
    if (holeDeg > 0)
      for (int k = 0; k < NV; k++)
        if (!onBorder[k] && deg[k] >= holeDeg && Find(comp, k) == mainComp) holePts.Add(verts[k]);
    res.Holes = holePts.Count;
    double rPanel = HOLE_D / 2 + EPS;
    if (holePts.Count > 0)
      for (int i = panelGroups.Count - 1; i >= 0; i--)
      {
        Curve panel = panelGroups[i][0];
        BoundingBox pb = panel.GetBoundingBox(false);
        pb.Inflate(rPanel);
        List<Point3d> near = holePts.Where(h => pb.Contains(h)).ToList();
        if (near.Count == 0) continue;
        // usual case: each hole clips one panel corner -> replace the corner with an arc (no boolean)
        Polyline pl;
        if (panel.TryGetPolyline(out pl))
        {
          List<Point3d> poly = pl.Take(pl.Count - 1).ToList();
          Orient(poly, true);
          List<Point3d> hard = new List<Point3d>();
          foreach (Point3d h in near)
          {
            double dmin = double.MaxValue;
            for (int k = 0; k < poly.Count; k++) dmin = Math.Min(dmin, SegDist(h, poly[k], poly[(k + 1) % poly.Count]));
            if (dmin >= rPanel && !InLoop(h, poly)) continue;   // hole misses this panel
            List<Point3d> cut = CutCorner(poly, h, rPanel);
            if (cut != null) poly = cut; else hard.Add(h);
          }
          if (hard.Count == 0)
          {
            if (Math.Abs(PolyArea(poly)) >= MIN_AREA) panelGroups[i] = new List<Curve> { Closed(poly) };
            else { panelGroups.RemoveAt(i); res.FacesDropped++; }
            continue;
          }
          panel = Closed(poly);
          near = hard;
        }
        List<Curve> hit = near.Select(h => Circle(h, rPanel)).ToList();
        Curve[] pieces = Curve.CreateBooleanDifference(panel, hit, tol);
        panelGroups.RemoveAt(i);
        if (pieces == null) { res.FacesDropped++; continue; }
        // a hole wholly inside a panel comes back as an extra (inner) loop: keep all loops of this panel together
        List<Curve> keep = pieces.Where(pc => pc != null && pc.IsClosed && CurveArea(pc) >= 1e-6).ToList();
        if (keep.Count > 0 && keep.Max(pc => CurveArea(pc)) >= MIN_AREA) panelGroups.Insert(i, keep);
        else res.FacesDropped++;
      }

    stage("holes");
    // 8. hinge layer outline: sheet outline minus holes and cut slots. Cutters that overlap are merged
    //    first; only those touching the outline need a boolean, the rest become inner loops.
    List<Point3d> outerPts = outer.Select(k => verts[k]).ToList();
    outerPts.Reverse();   // CW -> CCW
    Curve outline = Closed(outerPts);
    List<Curve> cutters = holePts.Select(h => Circle(h, HOLE_D / 2)).ToList();
    foreach (KeyValuePair<long, int> kv in edgeKind)
      if (kv.Value == KC && Find(comp, (int)(kv.Key >> 32)) == mainComp)
        cutters.Add(Strip(verts[(int)(kv.Key >> 32)], verts[(int)(kv.Key & 0xffffffffL)], GROOVE, GROOVE / 2 - EPS));
    cutters = MergeOverlapping(cutters, tol);
    List<Curve> crossing = new List<Curve>(), inside = new List<Curve>();
    foreach (Curve c in cutters)
    {
      if (Rhino.Geometry.Intersect.Intersection.CurveCurve(outline, c, tol, tol).Count > 0) crossing.Add(c);
      else if (outline.Contains(c.PointAtStart, Plane.WorldXY, tol) == PointContainment.Inside) inside.Add(c);
    }
    List<Curve> baseCurves = new List<Curve>();
    Curve[] outlines = crossing.Count > 0 ? Curve.CreateBooleanDifference(outline, crossing, tol) : new Curve[] { outline };
    if (outlines == null || outlines.Length == 0) throw new Exception("could not cut the holes/slots out of the sheet outline");
    baseCurves.AddRange(outlines);
    baseCurves.AddRange(inside);
    baseCurves = baseCurves.Select(Segmented).ToList();

    stage("base");
    // 9. mesh, built directly (no Breps): every loop is a closed polygon; the horizontal faces - bottom (z 0),
    //    groove floor (z hinge: hinge outline with the panels as holes) and panel tops (z height) - are
    //    triangulated with earcut, the walls are quads on the same loop points, so every edge is shared
    //    by exactly two faces and the mesh is closed by construction.
    List<List<Point3d>> baseLoops = new List<List<Point3d>>();
    foreach (Curve c in baseCurves) { List<Point3d> l = ToLoop(c, tol); if (l != null) baseLoops.Add(l); }
    if (baseLoops.Count == 0) throw new Exception("could not build the hinge layer outline");
    List<List<List<Point3d>>> panelSets = new List<List<List<Point3d>>>();
    foreach (List<Curve> grp in panelGroups)
    {
      List<List<Point3d>> ls = new List<List<Point3d>>();
      foreach (Curve c in grp) { List<Point3d> l = ToLoop(c, tol); if (l != null) ls.Add(l); }
      if (ls.Count > 0) panelSets.Add(ls); else res.FacesDropped++;
    }
    MeshBuilder mb = new MeshBuilder();
    int capFailures = 0;
    // hinge layer: bottom and walls 0..hinge
    stage("loops");
    int[] bDepth = Depths(baseLoops);
    for (int i = 0; i < baseLoops.Count; i++) Orient(baseLoops[i], bDepth[i] % 2 == 0);
    foreach (Region rg in Regions(baseLoops, bDepth)) { res.BaseArea += rg.Area; if (!mb.Cap(rg, 0, false)) capFailures++; }
    foreach (List<Point3d> l in baseLoops) mb.Walls(l, 0, hinge);
    // panels: walls hinge..height and tops
    List<List<Point3d>> allLoops = new List<List<Point3d>>(baseLoops);
    foreach (List<List<Point3d>> set in panelSets)
    {
      int[] pDepth = Depths(set);
      for (int i = 0; i < set.Count; i++) Orient(set[i], pDepth[i] % 2 == 0);
      foreach (Region rg in Regions(set, pDepth)) { res.PanelArea += rg.Area; res.Panels++; if (!mb.Cap(rg, height, true)) capFailures++; }
      foreach (List<Point3d> l in set) mb.Walls(l, hinge, height);
      allLoops.AddRange(set);
    }
    stage("panelCaps");
    // groove floor: nesting of all loops together decides what is floor
    int[] allDepth = Depths(allLoops);
    stage("floorNesting");
    foreach (Region rg in Regions(allLoops, allDepth)) if (!mb.Cap(rg, hinge, true)) capFailures++;
    stage("floorCap");
    if (capFailures > 0) res.Warnings.Add(capFailures + " face(s) could not be triangulated (" + string.Join("; ", mb.Failures.Take(3)) + ").");

    // 10. mesh checks
    mb.FixTJunctions();
    stage("tjunctions");
    mb.RemoveSlivers();
    stage("slivers");
    res.TJunctions = mb.TJunctionsFixed;
    res.Slivers = mb.SliversRemoved;
    Mesh mesh = mb.ToMesh();
    mesh.Faces.CullDegenerateFaces();
    mesh.Vertices.CullUnused();
    res.Closed = mesh.IsClosed;
    if (res.Closed && mesh.Volume() < 0) mesh.Flip(true, true, true);
    mesh.Normals.ComputeNormals();
    mesh.Compact();
    bool oriented, hasBoundary;
    res.Manifold = mesh.IsManifold(true, out oriented, out hasBoundary);
    res.Volume = res.Closed ? mesh.Volume() : 0;
    res.Shells = mesh.DisjointMeshCount;
    if (!res.Closed) res.Warnings.Add("The print mesh is not closed; check the pattern for tiny gaps or overlapping lines.");

    stage("mesh");
    // 11. place to the right of the pattern (mesh stays in mm)
    Vector3d move = new Vector3d(bb.Max.X + GAP_X * dx, bb.Min.Y, z0);
    mesh.Translate(move);
    foreach (Point3d p in holePts) res.HoleCentres.Add(p + move);
    res.Mesh = mesh;
    if (!string.IsNullOrEmpty(units) && units != "Millimeters")
      res.Warnings.Add("Document units are " + units + "; Print Mesh coordinates are in millimetres. Export the STL as millimetres.");
  }

  static string InfoText(Result r, double height, double hinge)
  {
    double panelH = height - hinge;
    double limit = 2 * Math.Atan(GROOVE / (2 * panelH)) * 180 / Math.PI;
    List<string> L = new List<string>
    {
      "scale=" + r.Scale.ToString("G6", IC) + " (pattern units -> mm)",
      "segments=" + r.Segments + " vertices=" + r.Vertices + " faces=" + r.Faces,
      "panels=" + r.Panels + " insetFallbacks=" + r.InsetFallbacks + " facesDropped=" + r.FacesDropped,
      "holes=" + r.Holes + " cuts=" + r.Cuts,
      "shells=" + r.Shells + " closed=" + r.Closed + " manifold=" + r.Manifold + " (repairs: " + r.TJunctions + " T-junctions, " + r.Slivers + " slivers)",
      "volume=" + r.Volume.ToString("F3", IC) + " mm3 (base " + r.BaseArea.ToString("F1", IC) + " mm2 x " + hinge.ToString("G4", IC) +
        " + panels " + r.PanelArea.ToString("F1", IC) + " mm2 x " + panelH.ToString("G4", IC) + ")",
      "foldLimitDeg=" + limit.ToString("F0", IC) + " (folds with the grooves inside close up past this; beyond it the hinge must stretch)",
      "ms=" + r.Ms.ToString("F0", IC) + " (" + r.Stages.Trim() + ")"
    };
    foreach (string e in r.Errors) L.Add("error: " + e);
    foreach (string w in r.Warnings) L.Add("warning: " + w);
    return string.Join("\n", L);
  }

  // ---------------------------------------------------------------------------------------------
  // Geometry helpers
  // ---------------------------------------------------------------------------------------------

  // Mitered inset of a CCW polygon: edge i (P[i] -> P[i+1]) moves d[i] to its left (inward).
  // Returns null when two consecutive edges are collinear with different offsets.
  static List<Point3d> Inset(List<Point3d> P, double[] d)
  {
    int n = P.Count;
    List<Point3d> Q = new List<Point3d>(n);
    for (int i = 0; i < n; i++)
    {
      int a = (i - 1 + n) % n;
      Vector3d ua = P[i] - P[a]; ua.Unitize();
      Vector3d ub = P[(i + 1) % n] - P[i]; ub.Unitize();
      Vector3d na = new Vector3d(-ua.Y, ua.X, 0), nb = new Vector3d(-ub.Y, ub.X, 0);
      Point3d A = P[a] + na * d[a], Bp = P[i] + nb * d[i];
      double det = ua.X * ub.Y - ua.Y * ub.X;
      if (Math.Abs(det) < 1e-9)
      {
        if (ua * ub > 0 && Math.Abs(d[a] - d[i]) < 1e-12) { Q.Add(P[i] + na * d[a]); continue; }
        return null;
      }
      Vector3d r = Bp - A;
      double t = (r.X * ub.Y - r.Y * ub.X) / det;   // A + t*ua = Bp + w*ub
      Q.Add(A + ua * t);
    }
    return Q;
  }

  static bool IsConvex(List<Point3d> P)   // CCW polygon, collinear corners allowed
  {
    for (int i = 0; i < P.Count; i++)
      if (Orient(P[i], P[(i + 1) % P.Count], P[(i + 2) % P.Count]) < -1e-12) return false;
    return true;
  }

  // Sutherland-Hodgman: keep the part of convex CCW polygon P on the inner side of every edge line moved
  // inward by d[i]
  static List<Point3d> ClipHalfPlanes(List<Point3d> P, double[] d)
  {
    List<Point3d> cur = new List<Point3d>(P);
    for (int i = 0; i < P.Count && cur.Count >= 3; i++)
    {
      Vector3d u = P[(i + 1) % P.Count] - P[i]; u.Unitize();
      Vector3d nIn = new Vector3d(-u.Y, u.X, 0);
      Point3d o = P[i] + nIn * d[i];
      Func<Point3d, double> side = q => (q - o) * nIn;
      List<Point3d> next = new List<Point3d>();
      for (int k = 0; k < cur.Count; k++)
      {
        Point3d a = cur[k], b = cur[(k + 1) % cur.Count];
        double sa = side(a), sb = side(b);
        if (sa >= 0) next.Add(a);
        if ((sa >= 0) != (sb >= 0)) next.Add(a + (b - a) * (sa / (sa - sb)));
      }
      cur = next;
    }
    // drop near-duplicate points left by clipping exactly through a vertex
    List<Point3d> outP = new List<Point3d>();
    foreach (Point3d q in cur) if (outP.Count == 0 || outP[outP.Count - 1].DistanceTo(q) > 1e-9) outP.Add(q);
    if (outP.Count > 1 && outP[0].DistanceTo(outP[outP.Count - 1]) <= 1e-9) outP.RemoveAt(outP.Count - 1);
    return outP;
  }

  // inset is usable if no edge flipped direction and the polygon does not cross itself
  static bool ValidInset(List<Point3d> P, List<Point3d> Q)
  {
    int n = P.Count;
    for (int i = 0; i < n; i++)
    {
      Vector3d u = P[(i + 1) % n] - P[i];
      Vector3d q = Q[(i + 1) % n] - Q[i];
      if (u * q <= 1e-12) return false;
    }
    for (int i = 0; i < n; i++)
      for (int j = i + 2; j < n; j++)
      {
        if (i == 0 && j == n - 1) continue;
        if (SegCross(Q[i], Q[(i + 1) % n], Q[j], Q[(j + 1) % n])) return false;
      }
    return PolyArea(Q) > 0;
  }

  static bool SegCross(Point3d a, Point3d b, Point3d c, Point3d d)
  {
    double d1 = Orient(c, d, a), d2 = Orient(c, d, b), d3 = Orient(a, b, c), d4 = Orient(a, b, d);
    return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0)) && d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0;
  }

  static double Orient(Point3d a, Point3d b, Point3d c) { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }

  static double PolyArea(List<Point3d> P)
  {
    double a = 0;
    for (int i = 0; i < P.Count; i++) { Point3d p = P[i], q = P[(i + 1) % P.Count]; a += p.X * q.Y - q.X * p.Y; }
    return 0.5 * a;
  }

  static double CurveArea(Curve c)
  {
    AreaMassProperties amp = AreaMassProperties.Compute(c);
    return amp != null ? amp.Area : 0;
  }

  // Dangling lines inside a face show up as spikes (… a, v, a …). Remove them; return them as lines.
  static List<Line> RemoveSpikes(List<int> f, List<Point3d> verts)
  {
    List<Line> dangling = new List<Line>();
    bool changed = true;
    while (changed && f.Count >= 3)
    {
      changed = false;
      for (int i = 0; i < f.Count; i++)
      {
        int a = f[(i - 1 + f.Count) % f.Count], v = f[i], b = f[(i + 1) % f.Count];
        if (a != b) continue;
        dangling.Add(new Line(verts[a], verts[v]));
        // drop v and the repeated a that follows it
        int j = (i + 1) % f.Count;
        if (j > i) { f.RemoveAt(j); f.RemoveAt(i); } else { f.RemoveAt(i); f.RemoveAt(j); }
        changed = true;
        break;
      }
    }
    return dangling;
  }

  static Curve Closed(List<Point3d> P)
  {
    PolyCurve pc = new PolyCurve();
    for (int i = 0; i < P.Count; i++) pc.Append(new LineCurve(P[i], P[(i + 1) % P.Count]));
    return pc;
  }

  // boolean results can come back as kinked polylines: split them into one segment per side
  static Curve Segmented(Curve c)
  {
    Curve[] segs = c.DuplicateSegments();
    if (segs == null || segs.Length < 2) return c;
    PolyCurve pc = new PolyCurve();
    foreach (Curve sg in segs) pc.Append(sg);
    return pc;
  }

  // rectangle w wide along a-b, extended ext past both ends (default w/2). Cut slots use GROOVE/2 - EPS so a
  // slot ending at a crease stops EPS short of the panel on the far side of that groove.
  static Curve Strip(Point3d a, Point3d b, double w, double ext = -1)
  {
    if (ext < 0) ext = w / 2;
    Vector3d u = b - a; u.Unitize();
    Vector3d nn = new Vector3d(-u.Y, u.X, 0) * (w / 2);
    Point3d a2 = a - u * ext, b2 = b + u * ext;
    return Closed(new List<Point3d> { a2 + nn, b2 + nn, b2 - nn, a2 - nn });
  }

  static double SegDist(Point3d p, Point3d a, Point3d b)
  {
    Vector3d ab = b - a;
    double t = ab.SquareLength > 0 ? Math.Max(0, Math.Min(1, ((p - a) * ab) / ab.SquareLength)) : 0;
    return p.DistanceTo(a + ab * t);
  }

  // Panel (CCW) minus a disk that contains exactly one of its corners and crosses only that corner's two
  // edges: the corner becomes an arc, walked clockwise so the panel stays on the left. Otherwise null.
  static List<Point3d> CutCorner(List<Point3d> P, Point3d c, double r)
  {
    int n = P.Count, q = -1;
    for (int i = 0; i < n; i++)
      if (P[i].DistanceTo(c) < r) { if (q >= 0) return null; q = i; }
    if (q < 0) return null;
    for (int j = 0; j < n; j++)
    {
      if (j == q || (j + 1) % n == q) continue;
      if (SegDist(c, P[j], P[(j + 1) % n]) <= r) return null;
    }
    Point3d A = P[(q - 1 + n) % n], Q = P[q], B = P[(q + 1) % n];
    Point3d e1 = CircleCross(A, Q, c, r), e2 = CircleCross(B, Q, c, r);
    double a1 = Math.Atan2(e1.Y - c.Y, e1.X - c.X), a2 = Math.Atan2(e2.Y - c.Y, e2.X - c.X);
    double sweep = a1 - a2;
    while (sweep <= 0) sweep += 2 * Math.PI;
    while (sweep > 2 * Math.PI) sweep -= 2 * Math.PI;
    int steps = Math.Max(1, (int)Math.Ceiling(sweep / (2 * Math.PI / HOLE_SEGS)));
    List<Point3d> outP = new List<Point3d>();
    for (int i = 0; i < n; i++)
    {
      if (i != q) { outP.Add(P[i]); continue; }
      outP.Add(e1);
      for (int k = 1; k < steps; k++)
      {
        double t = a1 - sweep * k / steps;
        outP.Add(new Point3d(c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t), 0));
      }
      outP.Add(e2);
    }
    return outP;
  }

  // point where segment out -> inside (out outside the circle, inside within it) crosses the circle
  static Point3d CircleCross(Point3d outside, Point3d inside, Point3d c, double r)
  {
    Vector3d d = inside - outside, f = outside - c;
    double a = d * d, b = 2 * (f * d), cc = f * f - r * r;
    double disc = Math.Max(0, b * b - 4 * a * cc);
    double t = (-b - Math.Sqrt(disc)) / (2 * a);
    return outside + d * Math.Max(0, Math.Min(1, t));
  }

  // hole polygon (HOLE_SEGS straight sides)
  static Curve Circle(Point3d c, double r)
  {
    List<Point3d> pts = new List<Point3d>();
    for (int i = 0; i < HOLE_SEGS; i++)
    {
      double t = 2 * Math.PI * i / HOLE_SEGS;
      pts.Add(new Point3d(c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t), 0));
    }
    return Closed(pts);
  }

  // cutters that overlap are merged into one outline each (union-find on curve intersections)
  static List<Curve> MergeOverlapping(List<Curve> cs, double tol)
  {
    int n = cs.Count;
    if (n < 2) return cs;
    int[] parent = new int[n];
    for (int i = 0; i < n; i++) parent[i] = i;
    BoundingBox[] bx = cs.Select(c => c.GetBoundingBox(false)).ToArray();
    for (int i = 0; i < n; i++)
      for (int j = i + 1; j < n; j++)
      {
        if (!Overlap2(bx[i], bx[j])) continue;
        if (Rhino.Geometry.Intersect.Intersection.CurveCurve(cs[i], cs[j], tol, tol).Count > 0
            || cs[i].Contains(cs[j].PointAtStart, Plane.WorldXY, tol) == PointContainment.Inside
            || cs[j].Contains(cs[i].PointAtStart, Plane.WorldXY, tol) == PointContainment.Inside)
          Union(parent, i, j);
      }
    List<Curve> merged = new List<Curve>();
    foreach (IGrouping<int, int> g in Enumerable.Range(0, n).GroupBy(i => Find(parent, i)))
    {
      List<Curve> grp = g.Select(i => cs[i]).ToList();
      if (grp.Count == 1) { merged.Add(grp[0]); continue; }
      Curve[] u = Curve.CreateBooleanUnion(grp, tol);
      if (u != null && u.Length > 0) merged.AddRange(u); else merged.AddRange(grp);
    }
    return merged;
  }

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

  // the solver's Miura 4x4 demo (OrigamiSolver.cs Demo(2, ...))
  static void Demo(List<Line> m, List<Line> v, List<Line> b)
  {
    int nx = 4, ny = 4;
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
  // Direct mesh construction
  // ---------------------------------------------------------------------------------------------

  // closed curve -> polygon points (no repeated end point, no duplicate or collinear points)
  static List<Point3d> ToLoop(Curve c, double tol)
  {
    List<Point3d> pts = new List<Point3d>();
    Polyline pl;
    if (c.TryGetPolyline(out pl)) pts.AddRange(pl);
    else
    {
      PolylineCurve pc = c.ToPolyline(0, 0, 0.1, 0, 0, tol, 0, 0, true);
      if (pc == null || !pc.TryGetPolyline(out pl)) return null;
      pts.AddRange(pl);
    }
    for (int i = 0; i < pts.Count; i++) pts[i] = new Point3d(pts[i].X, pts[i].Y, 0);
    bool changed = true;
    while (changed && pts.Count >= 3)
    {
      changed = false;
      for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
      {
        Point3d a = pts[(i - 1 + pts.Count) % pts.Count], p = pts[i], b = pts[(i + 1) % pts.Count];
        double cross = (p.X - a.X) * (b.Y - p.Y) - (p.Y - a.Y) * (b.X - p.X);
        if (p.DistanceTo(b) < 1e-9 || Math.Abs(cross) <= 1e-12 * (1 + a.DistanceTo(p) * p.DistanceTo(b)))
        { pts.RemoveAt(i); changed = true; i--; }
      }
    }
    return pts.Count >= 3 ? pts : null;
  }

  static double LoopArea(List<Point3d> l) { return PolyArea(l); }

  static void Orient(List<Point3d> l, bool ccw) { if ((LoopArea(l) > 0) != ccw) l.Reverse(); }

  static bool InLoop(Point3d p, List<Point3d> l)
  {
    bool inside = false;
    for (int i = 0, j = l.Count - 1; i < l.Count; j = i++)
      if (((l[i].Y > p.Y) != (l[j].Y > p.Y)) && (p.X < (l[j].X - l[i].X) * (p.Y - l[i].Y) / (l[j].Y - l[i].Y) + l[i].X))
        inside = !inside;
    return inside;
  }

  // nesting depth of each loop (how many other loops contain it); loops never cross each other
  static int[] Depths(List<List<Point3d>> loops)
  {
    int n = loops.Count;
    int[] depth = new int[n];
    BoundingBox[] bx = new BoundingBox[n];
    double[] ar = new double[n];
    for (int i = 0; i < n; i++) { bx[i] = new BoundingBox(loops[i]); ar[i] = Math.Abs(LoopArea(loops[i])); }
    for (int i = 0; i < n; i++)
      for (int j = 0; j < n; j++)
        if (i != j && ar[j] > ar[i] && Overlap2(bx[j], bx[i]) && bx[j].Contains(bx[i].Min) && bx[j].Contains(bx[i].Max) && InLoop(loops[i][0], loops[j]))
          depth[i]++;
    return depth;
  }

  class Region { public List<Point3d> Outer; public List<List<Point3d>> Holes = new List<List<Point3d>>(); public double Area; }

  // even-depth loops are outlines; each odd-depth loop is a hole in the smallest outline one level up
  static List<Region> Regions(List<List<Point3d>> loops, int[] depth)
  {
    int n = loops.Count;
    Dictionary<int, Region> byOuter = new Dictionary<int, Region>();
    for (int i = 0; i < n; i++)
      if (depth[i] % 2 == 0) byOuter[i] = new Region { Outer = loops[i], Area = Math.Abs(LoopArea(loops[i])) };
    for (int i = 0; i < n; i++)
    {
      if (depth[i] % 2 == 0) continue;
      int best = -1; double bestA = double.MaxValue;
      foreach (int j in byOuter.Keys)
      {
        if (depth[j] != depth[i] - 1) continue;
        double a = byOuter[j].Area;
        if (a < bestA && InLoop(loops[i][0], loops[j])) { best = j; bestA = a; }
      }
      if (best < 0) continue;
      byOuter[best].Holes.Add(loops[i]);
      byOuter[best].Area -= Math.Abs(LoopArea(loops[i]));
    }
    return byOuter.Values.ToList();
  }

  class MeshBuilder
  {
    readonly List<Point3d> P = new List<Point3d>();
    readonly List<int[]> F = new List<int[]>();
    readonly Dictionary<Tuple<long, long, long>, int> ids = new Dictionary<Tuple<long, long, long>, int>();
    public readonly List<string> Failures = new List<string>();
    public int TJunctionsFixed;

    int V(Point3d p, double z)
    {
      Tuple<long, long, long> k = Tuple.Create((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(z * 1e6));
      int id;
      if (!ids.TryGetValue(k, out id)) { id = P.Count; P.Add(new Point3d(p.X, p.Y, z)); ids[k] = id; }
      return id;
    }

    // vertical quads under a loop that has the solid on its left (outer loops CCW, holes CW): normals point out
    public void Walls(List<Point3d> l, double z0, double z1)
    {
      for (int i = 0; i < l.Count; i++)
      {
        Point3d a = l[i], b = l[(i + 1) % l.Count];
        F.Add(new int[] { V(a, z0), V(b, z0), V(b, z1), V(a, z1) });
      }
    }

    // horizontal face over a region (earcut); returns false if nothing could be triangulated
    public bool Cap(Region rg, double z, bool up)
    {
      List<Point3d> pts = new List<Point3d>(rg.Outer);
      List<int> holeStarts = new List<int>();
      foreach (List<Point3d> h in rg.Holes) { holeStarts.Add(pts.Count); pts.AddRange(h); }
      double[] X = pts.Select(p => p.X).ToArray(), Y = pts.Select(p => p.Y).ToArray();
      List<int> tri = Earcut.Triangulate(X, Y, holeStarts);
      for (int t = 0; t + 2 < tri.Count; t += 3)
      {
        int a = tri[t], b = tri[t + 1], c = tri[t + 2];
        double s = (X[b] - X[a]) * (Y[c] - Y[a]) - (Y[b] - Y[a]) * (X[c] - X[a]);
        if (s == 0) continue;
        if ((s > 0) != up) { int tmp = b; b = c; c = tmp; }
        F.Add(new int[] { V(pts[a], z), V(pts[b], z), V(pts[c], z) });
      }
      if (tri.Count == 0)
      {
        Failures.Add("z=" + z.ToString("G4", IC) + " outline " + rg.Outer.Count + " pts, " + rg.Holes.Count + " holes");
        return false;
      }
      return true;
    }

    // Earcut drops vertices that are exactly collinear with their neighbours (common: panel edges along a
    // straight border all lie on one line). The walls still use those vertices, so a triangle edge then
    // passes over them (a T-junction). Split such triangles into fans through the skipped vertices.
    public void FixTJunctions()
    {
      for (int pass = 0; pass < 4; pass++)
      {
        Dictionary<long, int> count = new Dictionary<long, int>(new LongHash());
        foreach (int[] f in F)
          for (int k = 0; k < f.Length; k++)
          {
            long e = EdgeKey(f[k], f[(k + 1) % f.Length]);
            int c; count.TryGetValue(e, out c); count[e] = c + 1;
          }
        HashSet<int> nakedVerts = new HashSet<int>();
        foreach (KeyValuePair<long, int> kv in count)
          if (kv.Value == 1) { nakedVerts.Add((int)(kv.Key >> 32)); nakedVerts.Add((int)(kv.Key & 0xffffffffL)); }
        if (nakedVerts.Count == 0) return;
        Dictionary<long, List<int>> byZ = new Dictionary<long, List<int>>();
        foreach (int v in nakedVerts)
        {
          long zk = (long)Math.Round(P[v].Z * 1e6);
          List<int> l; if (!byZ.TryGetValue(zk, out l)) { l = new List<int>(); byZ[zk] = l; }
          l.Add(v);
        }
        bool changed = false;
        for (int fi = 0; fi < F.Count; fi++)
        {
          int[] f = F[fi];
          if (f.Length != 3) continue;
          for (int k = 0; k < 3; k++)
          {
            int a = f[k], b = f[(k + 1) % 3], c = f[(k + 2) % 3];
            int ce;
            if (!count.TryGetValue(EdgeKey(a, b), out ce) || ce != 1) continue;
            List<int> cand;
            if (!byZ.TryGetValue((long)Math.Round(P[a].Z * 1e6), out cand)) continue;
            Vector3d ab = P[b] - P[a];
            double len2 = ab.SquareLength;
            List<KeyValuePair<double, int>> on = new List<KeyValuePair<double, int>>();
            foreach (int v in cand)
            {
              if (v == a || v == b) continue;
              Vector3d av = P[v] - P[a];
              double t = (av * ab) / len2;
              if (t <= 1e-9 || t >= 1 - 1e-9) continue;
              double cross = Math.Abs(av.X * ab.Y - av.Y * ab.X) / Math.Sqrt(len2);
              if (cross < 1e-7) on.Add(new KeyValuePair<double, int>(t, v));
            }
            if (on.Count == 0) continue;
            on.Sort((p, q) => p.Key.CompareTo(q.Key));
            List<int> chain = new List<int> { a };
            chain.AddRange(on.Select(x => x.Value));
            chain.Add(b);
            F[fi] = new int[] { chain[0], chain[1], c };
            for (int j = 1; j + 1 < chain.Count; j++) F.Add(new int[] { chain[j], chain[j + 1], c });
            TJunctionsFixed += on.Count;
            changed = true;
            break;
          }
        }
        if (!changed) return;
      }
    }

    // Zero-area triangles (one vertex on the opposite edge) can come out of earcut and the fans above.
    // Remove each by splitting the face across its long edge at that vertex; the mesh stays closed.
    public void RemoveSlivers()
    {
      Func<int[], bool> isSliver = f => f != null && f.Length == 3 && Vector3d.CrossProduct(P[f[1]] - P[f[0]], P[f[2]] - P[f[0]]).Length <= 2e-9;
      Queue<int> queue = new Queue<int>();
      for (int fi = 0; fi < F.Count; fi++) if (isSliver(F[fi])) queue.Enqueue(fi);
      if (queue.Count == 0) return;
      Dictionary<long, List<int>> faces = new Dictionary<long, List<int>>(new LongHash());
      Action<int, bool> index = (fi, add) =>
      {
        int[] f = F[fi];
        for (int k = 0; k < f.Length; k++)
        {
          long e = EdgeKey(f[k], f[(k + 1) % f.Length]);
          List<int> l;
          if (!faces.TryGetValue(e, out l)) { l = new List<int>(); faces[e] = l; }
          if (add) l.Add(fi); else l.Remove(fi);
        }
      };
      for (int fi = 0; fi < F.Count; fi++) index(fi, true);
      int guard = 0;
      while (queue.Count > 0 && guard++ < 100000)
      {
        int fi = queue.Dequeue();
        int[] f = F[fi];
        if (!isSliver(f)) continue;
        // long edge (a, b) and the vertex c lying on it
        int k = 0; double best = -1;
        for (int j = 0; j < 3; j++) { double d = P[f[j]].DistanceTo(P[f[(j + 1) % 3]]); if (d > best) { best = d; k = j; } }
        int a = f[k], b = f[(k + 1) % 3], c = f[(k + 2) % 3];
        List<int> nb;
        if (!faces.TryGetValue(EdgeKey(a, b), out nb) || nb.Count != 2) continue;
        int gi = nb[0] == fi ? nb[1] : nb[0];
        // insert c between b and a in the neighbour (it runs b -> a), then fan from c
        List<int> g = new List<int>(F[gi]);
        int ib = g.IndexOf(b);
        if (ib < 0 || g[(ib + 1) % g.Count] != a) continue;
        index(fi, false); index(gi, false);
        g.Insert(ib + 1, c);
        int ic = ib + 1;
        List<int> created = new List<int>();
        for (int j = 1; j + 1 < g.Count; j++)
        {
          int[] t = new int[] { g[ic], g[(ic + j) % g.Count], g[(ic + j + 1) % g.Count] };
          if (j == 1) { F[gi] = t; created.Add(gi); } else { F.Add(t); created.Add(F.Count - 1); }
        }
        F[fi] = new int[0];
        foreach (int ni in created) { index(ni, true); if (isSliver(F[ni])) queue.Enqueue(ni); }
        SliversRemoved++;
      }
      F.RemoveAll(x => x.Length == 0);
    }

    public int SliversRemoved;

    public Mesh ToMesh()
    {
      Mesh m = new Mesh();
      m.Vertices.UseDoublePrecisionVertices = true;
      foreach (Point3d p in P) m.Vertices.Add(p);
      foreach (int[] f in F)
        if (f.Length == 4) m.Faces.AddFace(f[0], f[1], f[2], f[3]); else m.Faces.AddFace(f[0], f[1], f[2]);
      return m;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Earcut: polygon-with-holes triangulation, a C# port of mapbox/earcut 2.2.4 (ISC licence,
  // https://github.com/mapbox/earcut). Indices refer to the X/Y arrays; holeStarts = first index of each hole.
  // ---------------------------------------------------------------------------------------------
  static class Earcut
  {
    class Node
    {
      public int i; public double x, y; public Node prev, next; public long z; public Node prevZ, nextZ; public bool steiner;
      public Node(int i, double x, double y) { this.i = i; this.x = x; this.y = y; }
    }

    public static List<int> Triangulate(double[] X, double[] Y, List<int> holeStarts)
    {
      List<int> tris = new List<int>();
      int n = X.Length;
      bool hasHoles = holeStarts != null && holeStarts.Count > 0;
      int outerLen = hasHoles ? holeStarts[0] : n;
      Node outer = LinkedList(X, Y, 0, outerLen, true);
      if (outer == null || outer.next == outer.prev) return tris;
      if (hasHoles) outer = EliminateHoles(X, Y, holeStarts, n, outer);
      double minX = 0, minY = 0, invSize = 0;
      if (n > 80)
      {
        double maxX, maxY;
        minX = maxX = X[0]; minY = maxY = Y[0];
        for (int k = 1; k < outerLen; k++)
        {
          if (X[k] < minX) minX = X[k]; if (Y[k] < minY) minY = Y[k];
          if (X[k] > maxX) maxX = X[k]; if (Y[k] > maxY) maxY = Y[k];
        }
        invSize = Math.Max(maxX - minX, maxY - minY);
        invSize = invSize != 0 ? 32767 / invSize : 0;
      }
      EarcutLinked(outer, tris, minX, minY, invSize, 0);
      return tris;
    }

    // Change from mapbox/earcut: every input vertex is marked steiner, so FilterPoints never drops it. The
    // walls of the print mesh use every loop vertex, and a dropped collinear vertex would leave a T-junction.
    static Node LinkedList(double[] X, double[] Y, int start, int end, bool clockwise)
    {
      Node last = null;
      if (clockwise == (SignedArea(X, Y, start, end) > 0))
        for (int k = start; k < end; k++) last = InsertNode(k, X[k], Y[k], last);
      else
        for (int k = end - 1; k >= start; k--) last = InsertNode(k, X[k], Y[k], last);
      if (last != null && Same(last, last.next)) { RemoveNode(last); last = last.next; }
      if (last != null) { Node p = last; do { p.steiner = true; p = p.next; } while (p != last); }
      return last;
    }

    static Node FilterPoints(Node start, Node end)
    {
      if (start == null) return start;
      if (end == null) end = start;
      Node p = start;
      bool again;
      do
      {
        again = false;
        if (!p.steiner && (Same(p, p.next) || Area(p.prev, p, p.next) == 0))
        {
          RemoveNode(p);
          p = end = p.prev;
          if (p == p.next) break;
          again = true;
        }
        else p = p.next;
      } while (again || p != end);
      return end;
    }

    static void EarcutLinked(Node ear, List<int> tris, double minX, double minY, double invSize, int pass)
    {
      if (ear == null) return;
      if (pass == 0 && invSize != 0) IndexCurve(ear, minX, minY, invSize);
      Node stop = ear;
      while (ear.prev != ear.next)
      {
        Node prev = ear.prev, next = ear.next;
        if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
        {
          tris.Add(prev.i); tris.Add(ear.i); tris.Add(next.i);
          RemoveNode(ear);
          ear = next.next;
          stop = next.next;
          continue;
        }
        ear = next;
        if (ear == stop)
        {
          if (pass == 0) EarcutLinked(FilterPoints(ear, null), tris, minX, minY, invSize, 1);
          else if (pass == 1)
          {
            ear = CureLocalIntersections(FilterPoints(ear, null), tris);
            EarcutLinked(ear, tris, minX, minY, invSize, 2);
          }
          else if (pass == 2) SplitEarcut(ear, tris, minX, minY, invSize);
          break;
        }
      }
    }

    static bool IsEar(Node ear)
    {
      Node a = ear.prev, b = ear, c = ear.next;
      if (Area(a, b, c) >= 0) return false;
      double x0 = Math.Min(a.x, Math.Min(b.x, c.x)), y0 = Math.Min(a.y, Math.Min(b.y, c.y));
      double x1 = Math.Max(a.x, Math.Max(b.x, c.x)), y1 = Math.Max(a.y, Math.Max(b.y, c.y));
      Node p = c.next;
      while (p != a)
      {
        if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 &&
            PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
        p = p.next;
      }
      return true;
    }

    static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
    {
      Node a = ear.prev, b = ear, c = ear.next;
      if (Area(a, b, c) >= 0) return false;
      double x0 = Math.Min(a.x, Math.Min(b.x, c.x)), y0 = Math.Min(a.y, Math.Min(b.y, c.y));
      double x1 = Math.Max(a.x, Math.Max(b.x, c.x)), y1 = Math.Max(a.y, Math.Max(b.y, c.y));
      long minZ = ZOrder(x0, y0, minX, minY, invSize), maxZ = ZOrder(x1, y1, minX, minY, invSize);
      Node p = ear.prevZ, n = ear.nextZ;
      while (p != null && p.z >= minZ && n != null && n.z <= maxZ)
      {
        if (Blocks(p, a, b, c, x0, y0, x1, y1)) return false;
        p = p.prevZ;
        if (Blocks(n, a, b, c, x0, y0, x1, y1)) return false;
        n = n.nextZ;
      }
      while (p != null && p.z >= minZ) { if (Blocks(p, a, b, c, x0, y0, x1, y1)) return false; p = p.prevZ; }
      while (n != null && n.z <= maxZ) { if (Blocks(n, a, b, c, x0, y0, x1, y1)) return false; n = n.nextZ; }
      return true;
    }

    static bool Blocks(Node p, Node a, Node b, Node c, double x0, double y0, double x1, double y1)
    {
      return p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != a && p != c &&
             PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) && Area(p.prev, p, p.next) >= 0;
    }

    static Node CureLocalIntersections(Node start, List<int> tris)
    {
      Node p = start;
      do
      {
        Node a = p.prev, b = p.next.next;
        if (!Same(a, b) && Intersects(a, p, p.next, b) && LocallyInside(a, b) && LocallyInside(b, a))
        {
          tris.Add(a.i); tris.Add(p.i); tris.Add(b.i);
          RemoveNode(p); RemoveNode(p.next);
          p = start = b;
        }
        p = p.next;
      } while (p != start);
      return FilterPoints(p, null);
    }

    static void SplitEarcut(Node start, List<int> tris, double minX, double minY, double invSize)
    {
      Node a = start;
      do
      {
        Node b = a.next.next;
        while (b != a.prev)
        {
          if (a.i != b.i && IsValidDiagonal(a, b))
          {
            Node c = SplitPolygon(a, b);
            a = FilterPoints(a, a.next);
            c = FilterPoints(c, c.next);
            EarcutLinked(a, tris, minX, minY, invSize, 0);
            EarcutLinked(c, tris, minX, minY, invSize, 0);
            return;
          }
          b = b.next;
        }
        a = a.next;
      } while (a != start);
    }

    static Node EliminateHoles(double[] X, double[] Y, List<int> holeStarts, int n, Node outer)
    {
      List<Node> queue = new List<Node>();
      for (int k = 0; k < holeStarts.Count; k++)
      {
        int start = holeStarts[k], end = k < holeStarts.Count - 1 ? holeStarts[k + 1] : n;
        Node list = LinkedList(X, Y, start, end, false);
        if (list == null) continue;
        if (list == list.next) list.steiner = true;
        queue.Add(GetLeftmost(list));
      }
      queue.Sort((p, q) => p.x.CompareTo(q.x));
      foreach (Node h in queue) outer = EliminateHole(h, outer);
      return outer;
    }

    static Node EliminateHole(Node hole, Node outer)
    {
      Node bridge = FindHoleBridge(hole, outer);
      if (bridge == null) return outer;
      Node bridgeReverse = SplitPolygon(bridge, hole);
      FilterPoints(bridgeReverse, bridgeReverse.next);
      return FilterPoints(bridge, bridge.next);
    }

    static Node FindHoleBridge(Node hole, Node outer)
    {
      Node p = outer, m = null;
      double hx = hole.x, hy = hole.y, qx = double.NegativeInfinity;
      do
      {
        if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
        {
          double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
          if (x <= hx && x > qx)
          {
            qx = x;
            m = p.x < p.next.x ? p : p.next;
            if (x == hx) return m;
          }
        }
        p = p.next;
      } while (p != outer);
      if (m == null) return null;
      Node stop = m;
      double mx = m.x, my = m.y, tanMin = double.PositiveInfinity;
      p = m;
      do
      {
        if (hx >= p.x && p.x >= mx && hx != p.x &&
            PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
        {
          double tan = Math.Abs(hy - p.y) / (hx - p.x);
          if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
          { m = p; tanMin = tan; }
        }
        p = p.next;
      } while (p != stop);
      return m;
    }

    static bool SectorContainsSector(Node m, Node p) { return Area(m.prev, m, p.prev) < 0 && Area(p.next, m, m.next) < 0; }

    static void IndexCurve(Node start, double minX, double minY, double invSize)
    {
      Node p = start;
      do
      {
        if (p.z == 0) p.z = ZOrder(p.x, p.y, minX, minY, invSize);
        p.prevZ = p.prev; p.nextZ = p.next; p = p.next;
      } while (p != start);
      p.prevZ.nextZ = null; p.prevZ = null;
      SortLinked(p);
    }

    static Node SortLinked(Node list)
    {
      int inSize = 1, numMerges;
      do
      {
        Node p = list, tail = null; list = null; numMerges = 0;
        while (p != null)
        {
          numMerges++;
          Node q = p; int pSize = 0;
          for (int k = 0; k < inSize; k++) { pSize++; q = q.nextZ; if (q == null) break; }
          int qSize = inSize;
          while (pSize > 0 || (qSize > 0 && q != null))
          {
            Node e;
            if (pSize != 0 && (qSize == 0 || q == null || p.z <= q.z)) { e = p; p = p.nextZ; pSize--; }
            else { e = q; q = q.nextZ; qSize--; }
            if (tail != null) tail.nextZ = e; else list = e;
            e.prevZ = tail; tail = e;
          }
          p = q;
        }
        tail.nextZ = null;
        inSize *= 2;
      } while (numMerges > 1);
      return list;
    }

    static long ZOrder(double x, double y, double minX, double minY, double invSize)
    {
      long xi = (long)((x - minX) * invSize), yi = (long)((y - minY) * invSize);
      xi = (xi | (xi << 8)) & 0x00FF00FF; xi = (xi | (xi << 4)) & 0x0F0F0F0F; xi = (xi | (xi << 2)) & 0x33333333; xi = (xi | (xi << 1)) & 0x55555555;
      yi = (yi | (yi << 8)) & 0x00FF00FF; yi = (yi | (yi << 4)) & 0x0F0F0F0F; yi = (yi | (yi << 2)) & 0x33333333; yi = (yi | (yi << 1)) & 0x55555555;
      return xi | (yi << 1);
    }

    static Node GetLeftmost(Node start)
    {
      Node p = start, left = start;
      do { if (p.x < left.x || (p.x == left.x && p.y < left.y)) left = p; p = p.next; } while (p != start);
      return left;
    }

    static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
    {
      return (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
             (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
             (bx - px) * (cy - py) >= (cx - px) * (by - py);
    }

    static bool IsValidDiagonal(Node a, Node b)
    {
      return a.next.i != b.i && a.prev.i != b.i && !IntersectsPolygon(a, b) &&
             (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
              (Area(a.prev, a, b.prev) != 0 || Area(a, b.prev, b) != 0) ||
              Same(a, b) && Area(a.prev, a, a.next) > 0 && Area(b.prev, b, b.next) > 0);
    }

    static double Area(Node p, Node q, Node r) { return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y); }

    static bool Same(Node a, Node b) { return a.x == b.x && a.y == b.y; }

    static bool Intersects(Node p1, Node q1, Node p2, Node q2)
    {
      int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
      if (o1 != o2 && o3 != o4) return true;
      if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
      if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
      if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
      if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
      return false;
    }

    static bool OnSegment(Node p, Node q, Node r)
    {
      return q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);
    }

    static int Sign(double v) { return v > 0 ? 1 : (v < 0 ? -1 : 0); }

    static bool IntersectsPolygon(Node a, Node b)
    {
      Node p = a;
      do
      {
        if (p.i != a.i && p.next.i != a.i && p.i != b.i && p.next.i != b.i && Intersects(p, p.next, a, b)) return true;
        p = p.next;
      } while (p != a);
      return false;
    }

    static bool LocallyInside(Node a, Node b)
    {
      return Area(a.prev, a, a.next) < 0
        ? Area(a, b, a.next) >= 0 && Area(a, a.prev, b) >= 0
        : Area(a, b, a.prev) < 0 || Area(a, a.next, b) < 0;
    }

    static bool MiddleInside(Node a, Node b)
    {
      Node p = a;
      bool inside = false;
      double px = (a.x + b.x) / 2, py = (a.y + b.y) / 2;
      do
      {
        if (((p.y > py) != (p.next.y > py)) && p.next.y != p.y && (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x))
          inside = !inside;
        p = p.next;
      } while (p != a);
      return inside;
    }

    static Node SplitPolygon(Node a, Node b)
    {
      Node a2 = new Node(a.i, a.x, a.y), b2 = new Node(b.i, b.x, b.y), an = a.next, bp = b.prev;
      a.next = b; b.prev = a;
      a2.next = an; an.prev = a2;
      b2.next = a2; a2.prev = b2;
      bp.next = b2; b2.prev = bp;
      return b2;
    }

    static Node InsertNode(int i, double x, double y, Node last)
    {
      Node p = new Node(i, x, y);
      if (last == null) { p.prev = p; p.next = p; }
      else { p.next = last.next; p.prev = last; last.next.prev = p; last.next = p; }
      return p;
    }

    static void RemoveNode(Node p)
    {
      p.next.prev = p.prev;
      p.prev.next = p.next;
      if (p.prevZ != null) p.prevZ.nextZ = p.nextZ;
      if (p.nextZ != null) p.nextZ.prevZ = p.prevZ;
    }

    static double SignedArea(double[] X, double[] Y, int start, int end)
    {
      double sum = 0;
      for (int k = start, j = end - 1; k < end; j = k++) sum += (X[j] - X[k]) * (Y[k] + Y[j]);
      return sum;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Arrangement helpers (copied from OrigamiSolver.cs)
  // ---------------------------------------------------------------------------------------------
  // Edge keys pack two vertex ids as (lo << 32) | hi. long.GetHashCode() is lo ^ hi, which collides for
  // nearby ids and made the edge dictionaries quadratic; this comparer mixes the bits first.
  class LongHash : IEqualityComparer<long>
  {
    public bool Equals(long a, long b) { return a == b; }
    public int GetHashCode(long x) { unchecked { ulong z = (ulong)x * 0x9E3779B97F4A7C15UL; z ^= z >> 31; return (int)(z ^ (z >> 32)); } }
  }

  static long EdgeKey(int p, int q) { int lo = Math.Min(p, q), hi = Math.Max(p, q); return ((long)lo << 32) | (uint)hi; }

  static long HalfKey(int from, int to) { return ((long)from << 32) | (uint)to; }

  static int Find(int[] parent, int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }

  static void Union(int[] parent, int a, int b) { a = Find(parent, a); b = Find(parent, b); if (a != b) parent[b] = a; }

  static bool Overlap(BoundingBox a, BoundingBox b)
  {
    return a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;
  }

  static bool Overlap2(BoundingBox a, BoundingBox b)
  {
    return a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y;
  }

  static void AddCut(List<double> cuts, double t, double len, double tol)
  {
    if (t * len > tol && (1.0 - t) * len > tol) cuts.Add(t);
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
}
