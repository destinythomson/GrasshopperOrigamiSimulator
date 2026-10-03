// OrigamiKangarooReadout.cs
// Grasshopper (Rhino 8) legacy "C# Script" component source: the "Origami Readout" of the Kangaroo2 version
// (grasshopper/OrigamiSim_Kangaroo.gh). It takes the folded mesh out of the Kangaroo Solver's O output and
// measures it the way v1 does (OrigamiSolver.cs Output): per-vertex strain and the fold angle of every
// crease. It works only from geometry, so it checks the goals rather than trusting them. M/V fold angles are
// read in the crease goal's side window, with the side taken from the sign of the target.
// Split on the "// ===== " markers like OrigamiSolver.cs.
//
// Component inputs : O (tree, Kangaroo Solver output), Flat (Mesh, from Origami Goals), Quads (List<int>),
//                    Targets (List<double>), Types (List<int>), I (int, Solver iterations)
// Component outputs: Mesh (folded mesh, same vertex order as Flat), Strain (List<double>, % per vertex),
//                    Info (string)

// ===== USING =====
using System.Linq;

// ===== SCRIPT (RunScript body) =====
OrigamiReadout.Result res = OrigamiReadout.Run(O, Flat, Quads, Targets, Types, I);
if (res.Warning != null) Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, res.Warning);
Mesh = res.OutMesh;
Strain = res.OutStrain;
Info = res.OutInfo;

// ===== ADDITIONAL =====
static class OrigamiReadout
{
  const double SETTLED_DEG = 0.5;      // "settled" when every M/V crease is this close to its target...
  const double SETTLED_STRAIN = 0.5;   // ...and the mean strain (%) is below this
  const double SENSE_SLACK_DEG = 5.0;  // mvSenseOk allows this much overshoot past a full fold

  public class Result
  {
    public Rhino.Geometry.Mesh OutMesh;
    public List<double> OutStrain = new List<double>();
    public string OutInfo, Warning;
  }

  public static Result Run(DataTree<object> O, Rhino.Geometry.Mesh flat, List<int> quads, List<double> targets,
                           List<int> types, int iterations)
  {
    Result r = new Result();
    if (flat == null) { r.OutInfo = "no Flat mesh"; return r; }
    int n = flat.Vertices.Count;

    // the Show goal's output: the first mesh in O with the same vertex count as Flat
    Rhino.Geometry.Mesh folded = null;
    if (O != null)
      foreach (object item in O.AllData())
      {
        Rhino.Geometry.Mesh m = item as Rhino.Geometry.Mesh;
        if (m == null && item is Grasshopper.Kernel.Types.GH_Mesh) m = ((Grasshopper.Kernel.Types.GH_Mesh)item).Value;
        if (m != null && m.Vertices.Count == n) { folded = m; break; }
      }
    if (folded == null) { r.OutInfo = "no folded mesh in O (is Flat wired to Show, and Show to the Solver?)"; r.Warning = r.OutInfo; return r; }

    Point3d[] P = new Point3d[n], P0 = new Point3d[n];
    bool finite = true;
    for (int i = 0; i < n; i++)
    {
      P[i] = folded.Vertices[i]; P0[i] = flat.Vertices[i];
      if (!P[i].IsValid) finite = false;
    }

    // strain = mean |L/L0 - 1| over the vertex's edges, in % (v1 Output, velocityCalc shader nodeError)
    HashSet<long> seen = new HashSet<long>();
    double[] errSum = new double[n]; int[] cnt = new int[n];
    for (int f = 0; f < flat.Faces.Count; f++)
    {
      MeshFace fc = flat.Faces[f];
      int[] v = { fc.A, fc.B, fc.C };
      for (int e = 0; e < 3; e++)
      {
        int a = v[e], b = v[(e + 1) % 3];
        long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        if (!seen.Add(key)) continue;
        double L0 = P0[a].DistanceTo(P0[b]);
        if (L0 <= 0) continue;
        double err = Math.Abs(P[a].DistanceTo(P[b]) / L0 - 1.0);
        errSum[a] += err; errSum[b] += err; cnt[a]++; cnt[b]++;
      }
    }
    double sumStrain = 0, maxStrain = 0;
    for (int i = 0; i < n; i++)
    {
      double e = cnt[i] > 0 ? errSum[i] / cnt[i] * 100.0 : 0;
      r.OutStrain.Add(e);
      sumStrain += e; maxStrain = Math.Max(maxStrain, e);
    }
    double meanStrain = n > 0 ? sumStrain / n : 0;
    if (double.IsNaN(meanStrain) || double.IsInfinity(meanStrain)) finite = false;

    // fold angles: v1 thetaCalcShader on faces (lo, hi, w1) and (hi, lo, w2)
    int nc = (quads != null ? quads.Count : 0) / 4;
    double maxErr = 0, maxFacet = 0; int mv = 0, mvOk = 0;
    for (int c = 0; c < nc && targets != null && types != null && c < targets.Count && c < types.Count; c++)
    {
      Point3d x3 = P[quads[4 * c]], x4 = P[quads[4 * c + 1]], x1 = P[quads[4 * c + 2]], x2 = P[quads[4 * c + 3]];
      Vector3d cu = x4 - x3; cu.Unitize();
      Vector3d n1 = Vector3d.CrossProduct(x4 - x3, x1 - x3); n1.Unitize();
      Vector3d n2 = Vector3d.CrossProduct(x3 - x4, x2 - x4); n2.Unitize();
      double th = Math.Atan2(Vector3d.CrossProduct(n1, cu) * n2, n1 * n2);
      double target = targets[c];
      int side = Math.Sign(target);                 // = the goal's Side whenever target != 0 (OrigamiKangaroo.cs Run)
      if (types[c] == 1 && side != 0)
      {
        // read theta in the goal's side window (OrigamiCrease.Calculate): valley (-90, 270], mountain [-270, 90)
        // deg. A crease on the wrong side, or folded through itself, lands outside (0, 180 + 5] deg and fails.
        double thu = th;
        if (side > 0) { if (thu <= -0.5 * Math.PI) thu += 2.0 * Math.PI; }
        else { if (thu >= 0.5 * Math.PI) thu -= 2.0 * Math.PI; }
        mv++;
        maxErr = Math.Max(maxErr, Math.Abs(target - thu));
        double s = side * thu;
        if (s > 0 && s <= Math.PI + SENSE_SLACK_DEG * Math.PI / 180.0) mvOk++;
        continue;
      }
      double d = th - target;                       // flat target: wrapped error, -179.9 deg counts as 0.1 from +180
      while (d > Math.PI) d -= 2.0 * Math.PI;
      while (d < -Math.PI) d += 2.0 * Math.PI;
      if (types[c] == 1) { mv++; mvOk++; maxErr = Math.Max(maxErr, Math.Abs(d)); }   // Fold 0: no side to check
      else maxFacet = Math.Max(maxFacet, Math.Abs(d));
    }

    Rhino.Geometry.Mesh outMesh = flat.DuplicateMesh();
    for (int i = 0; i < n; i++) outMesh.Vertices.SetVertex(i, P[i]);
    outMesh.Normals.ComputeNormals();
    r.OutMesh = outMesh;

    double maxErrDeg = maxErr * 180.0 / Math.PI;
    bool settled = finite && maxErrDeg <= SETTLED_DEG && meanStrain < SETTLED_STRAIN;
    System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
    r.OutInfo = string.Format(ci,
      "iterations={0} meanStrain%={1:F4} maxStrain%={2:F4} maxThetaErrDeg={3:F2} maxFacetDeg={4:F2} mvSenseOk={5}/{6} finite={7} settled={8}",
      iterations, meanStrain, maxStrain, maxErrDeg, maxFacet * 180.0 / Math.PI, mvOk, mv,
      finite ? "true" : "false", settled ? "true" : "false");
    if (!finite) r.Warning = "Simulation diverged (non-finite positions). Toggle Reset; raise Edge Strength or lower Crease Strength.";
    return r;
  }
}
