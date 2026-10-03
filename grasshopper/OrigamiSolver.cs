// OrigamiSolver.cs
// Grasshopper (Rhino 8) legacy "C# Script" component source: a CPU port of OrigamiSimulator's
// dynamic solver (https://github.com/amandaghassaei/OrigamiSimulator, files cited inline).
//
// This file is NOT compiled on its own. The build script splits it on the "// ===== " markers and
// copies each region into the component's ScriptSource:
//   USING      -> UsingCode
//   SCRIPT     -> ScriptCode   (the body of RunScript)
//   ADDITIONAL -> AdditionalCode (members of the generated Script_Instance class)
//
// Component inputs : M (mountain), V (valley), B (border), F (facet), C (cut), H (hinge): List<Curve>
//                    (lines, polylines; curved segments are approximated), Fold (double), Reset (bool),
//                    Anchors (List<Point3d>)
// Line types follow the web app's SVG import (js/pattern.js:136-153, 497-531):
//   M/V crease to -/+180 deg; F flat crease (0 deg, facet stiffness); C cut: the sheet is split along it;
//   H hinge ("U", unassigned): edge with no angular spring; B border (a border line inside the sheet acts as a hinge).
// Component outputs: Mesh (plain folded mesh), Strain (per-node %, List<double>), Info (string)
// Strain colours are applied by the separate StrainColour.cs component.

// ===== USING =====
// Nothing extra: the legacy component already imports System, System.Collections.Generic, System.Linq,
// Rhino, Rhino.Geometry, Grasshopper and Grasshopper.Kernel (a repeat only produces warning CS0105).
// Everything else is fully qualified below.

// ===== SCRIPT (RunScript body) =====
double tol = (RhinoDocument != null) ? RhinoDocument.ModelAbsoluteTolerance : 0.001;
List<double> MA = OrigamiSim.TargetAngles(Component, "M", M, RhinoDocument);
List<double> VA = OrigamiSim.TargetAngles(Component, "V", V, RhinoDocument);
OrigamiSim.Result r = OrigamiSim.Run(Component.InstanceGuid, M, V, B, F, C, H, MA, VA, Fold, Reset, Anchors, tol);
if (r.Warning != null) Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Warning);
Mesh = r.OutMesh;
Strain = r.OutStrain;
Info = r.OutInfo;

// ===== ADDITIONAL =====
static class OrigamiSim
{
  // ---------------------------------------------------------------------------------------------
  // Constants: the web app's defaults
  // ---------------------------------------------------------------------------------------------
  const double AXIAL = 20.0;        // axialStiffness            js/globals.js:50
  const double CREASE = 0.7;        // creaseStiffness (M, V)    js/globals.js:51
  const double FACET = 0.7;         // panelStiffness (flat)     js/globals.js:52
  const double FACE = 0.2;          // faceStiffness             js/globals.js:53
  const double DAMP = 0.45;         // percentDamping (ratio)    js/globals.js:56
  const int STEPS = 100;            // solver steps per solve    js/globals.js:90
  const double TARGET_DEG = 180.0;  // full fold when a line has no TargetAngleDeg; M = -180, V = +180   js/pattern.js:75,85
  const string ANGLE_KEY = "TargetAngleDeg";   // per-line User Text = the web app's stroke opacity x 180
  const double MERGE_REL = 0.005;   // vertices closer than this x the pattern radius are merged (web app: vertTol = 3 px,
                                    // js/globals.js:63, about 0.5 % of a typical SVG sheet)
  const double MASS = 1.0;          // every node has mass 1     js/node.js:205
  const int DEMO_WHEN_EMPTY = 2;    // used only when M, V and B are all empty:
                                    // 0 none, 1 single valley (2 triangles), 2 Miura 4x4, 3 Miura 12x12
  // Animation is driven by the canvas Timer "Animate": every solve advances STEPS sub-steps.

  // line kinds = index into the groups array. Triangulation diagonals are not in the input and count as F.
  const int KIND_B = 0, KIND_M = 1, KIND_V = 2, KIND_F = 3, KIND_H = 4, KIND_C = 5, NKINDS = 6;
  static int Priority(int kind)   // which kind wins when two input lines overlap
  {
    switch (kind) { case KIND_C: return 5; case KIND_M: case KIND_V: return 4; case KIND_F: return 3; case KIND_H: return 2; default: return 1; }
  }

  public class Result
  {
    public Rhino.Geometry.Mesh OutMesh;
    public List<double> OutStrain;
    public string OutInfo;
    public string Warning;
  }

  class SimState
  {
    public ulong Key; public int Demo; public double Tol; public string Note;
    // the simulation runs in the web app's frame (js/model.js:340-357): model point = Center + sim point / Scale
    public Point3d Center; public double Scale;
    // nodes
    public int N; public Point3d[] Pos; public Vector3d[] Vel; public bool[] Fixed;
    public Vector3d[] Force; public double[] ErrSum; public int[] BeamCount;
    // beams (one per mesh edge)
    public int NB; public int[] BA, BB; public double[] BL0, BK, BD;
    // triangles
    public int NF; public int[] FA, FB, FC; public double[] NomA, NomB, NomC; public Vector3d[] Nrm;
    // creases (one per interior edge). C1/C2 = far vertex of face 1/2, C3/C4 = crease end points
    public int NC; public int[] C1, C2, C3, C4, CF1, CF2, CType;
    public double[] CK, CTarget, CTheta, CH1, CH2, CC1, CC2; public bool[] COn;
    // solver
    public double Dt, Fold; public int Frames; public long Steps;
  }

  static readonly Dictionary<Guid, SimState> States = new Dictionary<Guid, SimState>();

  // ---------------------------------------------------------------------------------------------
  // Entry point (one call per Grasshopper solve)
  // ---------------------------------------------------------------------------------------------
  // MA, VA: target angle in degrees per M / V curve (TargetAngles); null or missing entries -> TARGET_DEG
  public static Result Run(Guid id, List<Curve> M, List<Curve> V, List<Curve> B, List<Curve> F, List<Curve> C,
                           List<Curve> H, List<double> MA, List<double> VA, double fold, bool reset, List<Point3d> anchors, double tol)
  {
    if (!(tol > 0)) tol = 1e-6;
    int curved = 0;
    // polylines, rectangles and polycurves become line segments; groups[kind], angles[kind] = degrees per segment
    List<Line>[] groups = new List<Line>[NKINDS];
    List<double>[] angles = new List<double>[NKINDS];
    for (int g = 0; g < NKINDS; g++) angles[g] = new List<double>();
    groups[KIND_M] = ToLines(M, MA, tol, ref curved, angles[KIND_M]);
    groups[KIND_V] = ToLines(V, VA, tol, ref curved, angles[KIND_V]);
    groups[KIND_B] = ToLines(B, null, tol, ref curved, angles[KIND_B]);
    groups[KIND_F] = ToLines(F, null, tol, ref curved, angles[KIND_F]);
    groups[KIND_C] = ToLines(C, null, tol, ref curved, angles[KIND_C]);
    groups[KIND_H] = ToLines(H, null, tol, ref curved, angles[KIND_H]);
    int nLines = 0;
    foreach (List<Line> g in groups) nLines += g.Count;
    List<Point3d> an = anchors != null ? new List<Point3d>(anchors) : new List<Point3d>();

    int demo = 0;
    if (nLines == 0 && DEMO_WHEN_EMPTY > 0)
    {
      demo = DEMO_WHEN_EMPTY;
      Demo(demo, groups[KIND_M], groups[KIND_V], groups[KIND_B]);
      for (int g = 0; g < NKINDS; g++) while (angles[g].Count < groups[g].Count) angles[g].Add(TARGET_DEG);
    }

    ulong key = Hash(groups, angles, an, tol, demo);
    SimState st;
    States.TryGetValue(id, out st);
    if (st == null || st.Key != key || reset)
    {
      try { st = Build(groups, angles, an, tol); }
      catch (Exception ex)
      {
        States.Remove(id);
        Result bad = new Result();
        bad.OutStrain = new List<double>();
        bad.OutInfo = "build failed: " + ex.Message;
        bad.Warning = bad.OutInfo;
        return bad;
      }
      st.Key = key;
      st.Demo = demo;
      States[id] = st;
    }

    if (double.IsNaN(fold)) fold = 0;
    st.Fold = Math.Max(-1.0, Math.Min(1.0, fold));

    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
    if (!reset)  // while Reset is true the sheet is held flat (web app "Reset")
    {
      for (int s = 0; s < STEPS; s++) Step(st);
      st.Frames++;
    }
    sw.Stop();
    Result res = Output(st, sw.Elapsed.TotalMilliseconds);
    if (res.Warning == null && curved > 0)
      res.Warning = curved + " curved segment(s) were approximated with straight lines (curved creases are not simulated as curves).";
    return res;
  }

  // Per-curve target angle (degrees) from each referenced Rhino object's User Text TargetAngleDeg, read from
  // the input's volatile data (same order as the curve list). Unreferenced curves and missing, unparsable or
  // out-of-range (outside 0-180) values get TARGET_DEG. Returns null when the data does not line up with the list.
  public static List<double> TargetAngles(IGH_Component comp, string input, List<Curve> curves, RhinoDoc doc)
  {
    IGH_Param p = comp.Params.Input.Find(q => q.NickName == input);
    if (p == null || doc == null || curves == null) return null;
    List<double> res = new List<double>();
    foreach (Grasshopper.Kernel.Types.IGH_Goo g in p.VolatileData.AllData(false))
    {
      double a = TARGET_DEG;
      Grasshopper.Kernel.Types.IGH_GeometricGoo gg = g as Grasshopper.Kernel.Types.IGH_GeometricGoo;
      if (gg != null && gg.IsReferencedGeometry)
      {
        Rhino.DocObjects.RhinoObject o = doc.Objects.FindId(gg.ReferenceID);
        string s = o != null ? o.Attributes.GetUserString(ANGLE_KEY) : null;
        double v;
        if (s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)
            && v >= 0 && v <= 180) a = v;
      }
      res.Add(a);
    }
    return res.Count == curves.Count ? res : null;
  }

  // Curves from Rhino (lines, polylines, rectangles, polycurves) -> straight line segments.
  // Non-linear segments are approximated by a polyline and counted. Every segment gets its curve's angle
  // (ang[i], or TARGET_DEG) in outAng.
  static List<Line> ToLines(List<Curve> curves, List<double> ang, double tol, ref int approximated, List<double> outAng)
  {
    List<Line> lines = new List<Line>();
    if (curves == null) return lines;
    for (int i = 0; i < curves.Count; i++)
    {
      AddCurve(lines, curves[i], tol, ref approximated);
      double a = ang != null && i < ang.Count ? ang[i] : TARGET_DEG;
      while (outAng.Count < lines.Count) outAng.Add(a);
    }
    return lines;
  }

  static void AddCurve(List<Line> lines, Curve c, double tol, ref int approximated)
  {
    if (c == null || !c.IsValid) return;
    Polyline pl;
    if (c.TryGetPolyline(out pl)) { AddPolyline(lines, pl); return; }
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

  static void AddPolyline(List<Line> lines, Polyline pl)
  {
    for (int i = 0; i + 1 < pl.Count; i++) lines.Add(new Line(pl[i], pl[i + 1]));
  }

  // ---------------------------------------------------------------------------------------------
  // One solver step, same order as dynamicSolver.js:131 solveStep():
  // normalCalc -> thetaCalc -> updateCreaseGeo -> velocityCalc -> positionCalc (explicit Euler)
  // ---------------------------------------------------------------------------------------------
  static void Step(SimState s)
  {
    Point3d[] P = s.Pos; Vector3d[] Vl = s.Vel; Vector3d[] F = s.Force; double[] E = s.ErrSum;

    // a) face normals (normalCalc shader): n = normalize((b - a) x (c - a))
    for (int f = 0; f < s.NF; f++)
    {
      Vector3d nv = Vector3d.CrossProduct(P[s.FB[f]] - P[s.FA[f]], P[s.FC[f]] - P[s.FA[f]]);
      double len = nv.Length;
      s.Nrm[f] = len > 0 ? nv / len : Vector3d.Zero;
    }

    // b) fold angle (thetaCalc shader) and c) crease geometry (updateCreaseGeo shader)
    for (int c = 0; c < s.NC; c++)
    {
      Vector3d n1 = s.Nrm[s.CF1[c]], n2 = s.Nrm[s.CF2[c]];
      Vector3d cv = P[s.C4[c]] - P[s.C3[c]];
      double L = cv.Length;
      Vector3d cu = L > 0 ? cv / L : Vector3d.Zero;

      double x = Clamp(n1 * n2);
      double y = Vector3d.CrossProduct(n1, cu) * n2;
      double th = Math.Atan2(y, x);
      double d = th - s.CTheta[c];                       // unwrap across +-pi
      if (d < -5.0) d += 2.0 * Math.PI; else if (d > 5.0) d -= 2.0 * Math.PI;
      s.CTheta[c] += d;

      if (L < 1e-6) { s.COn[c] = false; continue; }      // crease collapsed: skip this step
      Vector3d v1 = P[s.C1[c]] - P[s.C3[c]];
      Vector3d v2 = P[s.C2[c]] - P[s.C3[c]];
      double p1 = cu * v1, p2 = cu * v2;
      double h1 = Math.Sqrt(Math.Abs(v1.SquareLength - p1 * p1));   // moment arms
      double h2 = Math.Sqrt(Math.Abs(v2.SquareLength - p2 * p2));
      if (h1 < 1e-6 || h2 < 1e-6) { s.COn[c] = false; continue; }
      s.COn[c] = true;
      s.CH1[c] = h1; s.CH2[c] = h2;
      s.CC1[c] = p1 / L; s.CC2[c] = p2 / L;
    }

    // d) forces (velocityCalc shader), accumulated per element instead of per node: same sums
    for (int i = 0; i < s.N; i++) { F[i] = Vector3d.Zero; E[i] = 0; }

    // beams: F = k (len - L0) along the beam + d (v_j - v_i)        js/beam.js:63-70
    for (int bi = 0; bi < s.NB; bi++)
    {
      int a = s.BA[bi], b = s.BB[bi];
      Vector3d dv = P[b] - P[a];
      double len = dv.Length;
      if (len <= 0) continue;
      Vector3d f = dv * (s.BK[bi] * (1.0 - s.BL0[bi] / len)) + (Vl[b] - Vl[a]) * s.BD[bi];
      F[a] += f; F[b] -= f;
      double e = Math.Abs(len / s.BL0[bi] - 1.0);
      E[a] += e; E[b] += e;
    }

    // creases: torque k (target * fold - theta) turned into node forces; no crease damping
    // (the shader's "+ creaseMeta[1]*thetas[1]" damping term is commented out)
    double fold = s.Fold;
    for (int c = 0; c < s.NC; c++)
    {
      if (!s.COn[c]) continue;
      double angF = s.CK[c] * (s.CTarget[c] * fold - s.CTheta[c]);
      Vector3d n1 = s.Nrm[s.CF1[c]], n2 = s.Nrm[s.CF2[c]];
      double h1 = s.CH1[c], h2 = s.CH2[c], c1 = s.CC1[c], c2 = s.CC2[c];
      F[s.C1[c]] += n1 * (angF / h1);                                              // node 1
      F[s.C2[c]] += n2 * (angF / h2);                                              // node 2
      F[s.C3[c]] -= n1 * (angF * (1.0 - c1) / h1) + n2 * (angF * (1.0 - c2) / h2); // node 3
      F[s.C4[c]] -= n1 * (angF * c1 / h1) + n2 * (angF * c2 / h2);                 // node 4
    }

    // faces: springs that keep each triangle's corner angles at their flat values
    for (int f = 0; f < s.NF; f++)
    {
      int ia = s.FA[f], ib = s.FB[f], ic = s.FC[f];
      Vector3d ab = P[ib] - P[ia], ac = P[ic] - P[ia], bc = P[ic] - P[ib];
      double lab = ab.Length, lac = ac.Length, lbc = bc.Length;
      if (lab < 1e-7 || lac < 1e-7 || lbc < 1e-7) continue;
      ab /= lab; ac /= lac; bc /= lbc;
      double dA = (s.NomA[f] - Math.Acos(Clamp(ab * ac))) * FACE;
      double dB = (s.NomB[f] - Math.Acos(Clamp(-(ab * bc)))) * FACE;
      double dC = (s.NomC[f] - Math.Acos(Clamp(ac * bc))) * FACE;
      Vector3d n = s.Nrm[f];
      Vector3d nAB = Vector3d.CrossProduct(n, ab) / lab;
      Vector3d nAC = Vector3d.CrossProduct(n, ac) / lac;
      Vector3d nBC = Vector3d.CrossProduct(n, bc) / lbc;
      F[ia] -= dA * (nAC - nAB); F[ia] -= dB * nAB;         F[ia] += dC * nAC;           // corner a
      F[ib] -= dA * nAB;         F[ib] += dB * (nAB + nBC); F[ib] -= dC * nBC;           // corner b
      F[ic] += dA * nAC;         F[ic] -= dB * nBC;         F[ic] += dC * (nBC - nAC);   // corner c
    }

    // e) integrate (velocityCalc + positionCalc shaders): v += F dt / m, x += v dt
    double dt = s.Dt;
    for (int i = 0; i < s.N; i++)
    {
      if (s.Fixed[i]) { Vl[i] = Vector3d.Zero; continue; }
      Vl[i] += F[i] * (dt / MASS);
      P[i] += Vl[i] * dt;
    }
    s.Steps++;
  }

  // ---------------------------------------------------------------------------------------------
  // Outputs: plain mesh, strain list, info string
  // ---------------------------------------------------------------------------------------------
  static Result Output(SimState s, double ms)
  {
    Result r = new Result();
    Rhino.Geometry.Mesh mesh = new Rhino.Geometry.Mesh();
    List<double> strain = new List<double>(s.N);
    bool finite = true;
    double sumStrain = 0, sumV = 0;
    for (int i = 0; i < s.N; i++)
    {
      Point3d p = s.Pos[i];
      if (!p.IsValid) finite = false;
      mesh.Vertices.Add(new Point3d(s.Center.X + p.X / s.Scale, s.Center.Y + p.Y / s.Scale, s.Center.Z + p.Z / s.Scale));
      // strain = mean |L/L0 - 1| over the node's beams, in %; fixed nodes report 0 (velocityCalc shader)
      double e = (s.Fixed[i] || s.BeamCount[i] == 0) ? 0 : s.ErrSum[i] / s.BeamCount[i] * 100.0;
      strain.Add(e);
      sumStrain += e;
      sumV += s.Vel[i].Length;
    }
    for (int f = 0; f < s.NF; f++) mesh.Faces.AddFace(s.FA[f], s.FB[f], s.FC[f]);
    mesh.Normals.ComputeNormals();

    double maxErr = 0, sumErr = 0; int mv = 0, mvOk = 0;
    SortedDictionary<long, int> hist = new SortedDictionary<long, int>();   // M/V creases per target in whole degrees (M < 0)
    for (int c = 0; c < s.NC; c++)
    {
      if (s.CType[c] != 1) continue;
      mv++;
      long deg = (long)Math.Round(s.CTarget[c] * 180.0 / Math.PI);
      int cnt;
      hist.TryGetValue(deg, out cnt);
      hist[deg] = cnt + 1;
      double target = s.CTarget[c] * s.Fold;
      maxErr = Math.Max(maxErr, Math.Abs(s.CTheta[c] - target));
      sumErr += Math.Abs(s.CTheta[c] - target);
      if (target == 0 || s.CTheta[c] * target > 0) mvOk++;
    }
    double meanStrain = s.N > 0 ? sumStrain / s.N : 0;
    if (double.IsNaN(meanStrain) || double.IsInfinity(meanStrain)) finite = false;

    System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
    r.OutInfo = string.Format(ci,
      "nodes={0} beams={1} creases={2} mvCreases={3} faces={4} dt={5:E3} frame={6} steps={7} msPerSolve={8:F2} " +
      "meanStrain%={9:F4} maxThetaErrDeg={10:F2} meanThetaErrDeg={18:F2} mvSenseOk={11}/{3} meanAbsV={12:E2} fold={13:F2} demo={14} finite={15} targets={17}{16}",
      s.N, s.NB, s.NC, mv, s.NF, s.Dt, s.Frames, s.Steps, ms,
      meanStrain, maxErr * 180.0 / Math.PI, mvOk, s.N > 0 ? sumV / s.N : 0, s.Fold, s.Demo,
      finite ? "true" : "false", string.IsNullOrEmpty(s.Note) ? "" : " note=" + s.Note,
      HistText(hist), mv > 0 ? sumErr / mv * 180.0 / Math.PI : 0);
    if (!finite) r.Warning = "Simulation diverged (non-finite positions). Toggle Reset; lower AXIAL or raise DAMP.";
    r.OutMesh = mesh;
    r.OutStrain = strain;
    return r;
  }

  // ---------------------------------------------------------------------------------------------
  // Build: lines -> planar graph -> faces -> triangles -> nodes, beams, creases
  // (replaces pattern.js cleanup + triangulation and model.js sync)
  // ---------------------------------------------------------------------------------------------
  static SimState Build(List<Line>[] groups, List<double>[] angles, List<Point3d> anchors, double tol)
  {
    List<string> notes = new List<string>();

    // 1. tagged input lines (kind + target angle in degrees)
    List<Line> lines = new List<Line>();
    List<int> kinds = new List<int>();
    List<double> angs = new List<double>();
    for (int g = 0; g < NKINDS; g++) AddLines(lines, kinds, angs, groups[g], angles[g], g, tol);
    int n = lines.Count;
    if (n == 0) throw new Exception("no input lines longer than the document tolerance");

    // 1b. web app frame (js/model.js:340-357): centre on the bounding-box centre and scale to bounding radius 1, so the
    //     stiffness balance does not depend on the drawing units. Vertices merge within MERGE_REL of that radius.
    BoundingBox all = BoundingBox.Empty;
    foreach (Line ln in lines) { all.Union(ln.From); all.Union(ln.To); }
    Point3d center = all.Center;
    double radius = 0;
    foreach (Line ln in lines) radius = Math.Max(radius, Math.Max(center.DistanceTo(ln.From), center.DistanceTo(ln.To)));
    double scale = 1.0 / radius;
    for (int i = 0; i < n; i++)
      lines[i] = new Line(ToSim(lines[i].From, center, scale), ToSim(lines[i].To, center, scale));
    List<Point3d> simAnchors = new List<Point3d>();
    foreach (Point3d a in anchors) simAnchors.Add(ToSim(a, center, scale));
    double modelTol = tol;
    tol = Math.Max(tol * scale, MERGE_REL);

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

    // 3. merge vertices within tolerance, collect unique edges (M/V wins over B on duplicates);
    //    every piece of a split line keeps the line's angle
    List<Point3d> verts = new List<Point3d>();
    Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
    Dictionary<long, int> edgeKind = new Dictionary<long, int>();
    Dictionary<long, double> edgeAng = new Dictionary<long, double>();
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
        if (!edgeKind.TryGetValue(ek, out old) || Priority(kinds[i]) > Priority(old)) { edgeKind[ek] = kinds[i]; edgeAng[ek] = angs[i]; }
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
    if (dev > 10 * tol) notes.Add("notPlanar:maxDev=" + (dev / scale).ToString("G3", System.Globalization.CultureInfo.InvariantCulture));
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
    HashSet<long> visited = new HashSet<long>();
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
    HashSet<long> cutKeys = new HashSet<long>();
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
    }
    NV = verts.Count;

    // 8. edges of the triangle mesh. face "left" of lo->hi = the triangle whose CCW order runs lo->hi
    Dictionary<long, int[]> einfo = new Dictionary<long, int[]>();   // [leftFace, rightFace]
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

    // 9. nodes = vertices used by triangles
    int[] map = new int[NV];
    for (int k = 0; k < NV; k++) map[k] = -1;
    List<int> nodeVerts = new List<int>();
    foreach (int[] tr in tris) foreach (int k in tr) if (map[k] < 0) { map[k] = nodeVerts.Count; nodeVerts.Add(k); }

    SimState st = new SimState();
    st.Tol = modelTol;
    st.Center = center; st.Scale = scale;
    int N = nodeVerts.Count;
    st.N = N;
    st.Pos = new Point3d[N]; st.Vel = new Vector3d[N]; st.Fixed = new bool[N];
    st.Force = new Vector3d[N]; st.ErrSum = new double[N]; st.BeamCount = new int[N];
    for (int i = 0; i < N; i++) st.Pos[i] = verts[nodeVerts[i]];
    double anchorTol = Math.Max(10 * modelTol * scale, 1e-9);
    int nFixed = 0;
    foreach (Point3d a in simAnchors)
      for (int i = 0; i < N; i++)
        if (!st.Fixed[i] && st.Pos[i].DistanceTo(a) <= anchorTol) { st.Fixed[i] = true; nFixed++; }
    if (anchors.Count > 0) notes.Add("fixedNodes:" + nFixed);

    // triangles + flat corner angles (dynamicSolver.js:548-566)
    int NF = tris.Count;
    st.NF = NF;
    st.FA = new int[NF]; st.FB = new int[NF]; st.FC = new int[NF];
    st.NomA = new double[NF]; st.NomB = new double[NF]; st.NomC = new double[NF]; st.Nrm = new Vector3d[NF];
    for (int f = 0; f < NF; f++)
    {
      int a = map[tris[f][0]], bb = map[tris[f][1]], c = map[tris[f][2]];
      st.FA[f] = a; st.FB[f] = bb; st.FC[f] = c;
      Vector3d ab = st.Pos[bb] - st.Pos[a], ac = st.Pos[c] - st.Pos[a], bc = st.Pos[c] - st.Pos[bb];
      ab.Unitize(); ac.Unitize(); bc.Unitize();
      st.NomA[f] = Math.Acos(Clamp(ab * ac));
      st.NomB[f] = Math.Acos(Clamp(-(ab * bc)));
      st.NomC[f] = Math.Acos(Clamp(ac * bc));
    }

    // beams on every edge: k = axial / L0, d = zeta 2 sqrt(k m)          js/beam.js:63-77
    int NB = einfo.Count;
    st.NB = NB;
    st.BA = new int[NB]; st.BB = new int[NB]; st.BL0 = new double[NB]; st.BK = new double[NB]; st.BD = new double[NB];
    // creases on interior edges
    List<long> creaseKeys = new List<long>();
    int bi = 0, nHinges = 0;
    double maxOmega = 0;
    foreach (KeyValuePair<long, int[]> kv in einfo)
    {
      int lo = (int)(kv.Key >> 32), hi = (int)(kv.Key & 0xffffffffL);
      int a = map[lo], bb = map[hi];
      double L0 = st.Pos[a].DistanceTo(st.Pos[bb]);
      st.BA[bi] = a; st.BB[bi] = bb; st.BL0[bi] = L0;
      st.BK[bi] = AXIAL / L0;
      st.BD[bi] = DAMP * 2.0 * Math.Sqrt(st.BK[bi] * MASS);
      maxOmega = Math.Max(maxOmega, Math.Sqrt(st.BK[bi] / MASS));
      st.BeamCount[a]++; st.BeamCount[bb]++;
      bi++;
      // creases only on interior M, V, F edges and triangulation diagonals (pattern.js:826)
      if (kv.Value[0] >= 0 && kv.Value[1] >= 0)
      {
        int k = KindOf(kv.Key, edgeKind, origOf);
        if (k == KIND_M || k == KIND_V || k == KIND_F) creaseKeys.Add(kv.Key);
        else nHinges++;
      }
    }
    if (nHinges > 0) notes.Add("freeHinges:" + nHinges);
    // dt = 0.9 / (2 pi omega_max)                                    dynamicSolver.js:244-250
    st.Dt = 0.9 / (2.0 * Math.PI * maxOmega);

    int NC = creaseKeys.Count;
    st.NC = NC;
    st.C1 = new int[NC]; st.C2 = new int[NC]; st.C3 = new int[NC]; st.C4 = new int[NC];
    st.CF1 = new int[NC]; st.CF2 = new int[NC]; st.CType = new int[NC];
    st.CK = new double[NC]; st.CTarget = new double[NC]; st.CTheta = new double[NC];
    st.CH1 = new double[NC]; st.CH2 = new double[NC]; st.CC1 = new double[NC]; st.CC2 = new double[NC];
    st.COn = new bool[NC];
    for (int c = 0; c < NC; c++)
    {
      long ek = creaseKeys[c];
      int lo = (int)(ek >> 32), hi = (int)(ek & 0xffffffffL);
      int[] ei = einfo[ek];
      int f1 = ei[0], f2 = ei[1];                  // face 1 is left of node3 -> node4 (pattern.js creaseParams order)
      st.C3[c] = map[lo]; st.C4[c] = map[hi];
      st.C1[c] = map[Third(tris[f1], lo, hi)];
      st.C2[c] = map[Third(tris[f2], lo, hi)];
      st.CF1[c] = f1; st.CF2[c] = f2;
      int kind = KindOf(ek, edgeKind, origOf);
      double L0 = st.Pos[st.C3[c]].DistanceTo(st.Pos[st.C4[c]]);
      if (kind == KIND_M || kind == KIND_V)
      {
        st.CType[c] = 1;
        double deg;
        if (!edgeAng.TryGetValue(EdgeKey(origOf[lo], origOf[hi]), out deg)) deg = TARGET_DEG;
        st.CTarget[c] = (kind == KIND_M ? -deg : deg) * Math.PI / 180.0;
        st.CK[c] = CREASE * L0;                    // js/crease.js:44-48
      }
      else
      {
        st.CType[c] = 0;
        st.CTarget[c] = 0;
        st.CK[c] = FACET * L0;
      }
      st.CTheta[c] = 0;
      st.COn[c] = true;
    }

    st.Note = notes.Count > 0 ? string.Join(",", notes.ToArray()) : null;
    return st;
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
  static string HistText(SortedDictionary<long, int> hist)   // "45x18,180x83"
  {
    List<string> parts = new List<string>();
    foreach (KeyValuePair<long, int> kv in hist) parts.Add(kv.Key + "x" + kv.Value);
    return string.Join(",", parts.ToArray());
  }

  static Point3d ToSim(Point3d p, Point3d center, double scale)
  {
    return new Point3d((p.X - center.X) * scale, (p.Y - center.Y) * scale, (p.Z - center.Z) * scale);
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

  static void AddLines(List<Line> lines, List<int> kinds, List<double> angs, List<Line> src, List<double> srcAng, int kind, double tol)
  {
    for (int i = 0; i < src.Count; i++)
    {
      Line ln = src[i];
      if (!ln.IsValid || ln.Length <= tol) continue;
      lines.Add(ln); kinds.Add(kind); angs.Add(i < srcAng.Count ? srcAng[i] : TARGET_DEG);
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

  static ulong Hash(List<Line>[] groups, List<double>[] angles, List<Point3d> an, double tol, int demo)
  {
    ulong h = 14695981039346656037UL;
    h = Mix(h, demo);
    h = Mix(h, BitConverter.DoubleToInt64Bits(tol));
    for (int g = 0; g < groups.Length; g++)
    {
      h = Mix(h, 1000 + g);
      h = Mix(h, groups[g].Count);
      foreach (Line ln in groups[g]) { h = MixPt(h, ln.From, tol); h = MixPt(h, ln.To, tol); }
      foreach (double a in angles[g]) h = Mix(h, BitConverter.DoubleToInt64Bits(a));
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
