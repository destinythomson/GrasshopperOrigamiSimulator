// StrainColour.cs
// Grasshopper (Rhino 8) legacy "C# Script" component source: colours the solver's folded mesh by
// axial strain, the same way the web app does (blue = 0 %, red >= STRAIN_CLIP).
// Split on the "// ===== " markers like OrigamiSolver.cs.
//
// Component inputs : Mesh (Mesh, item), Strain (List<double>, one value per mesh vertex, in %)
// Component outputs: StrainMesh (a coloured copy of Mesh)

// ===== USING =====
// Nothing extra: the legacy component already imports System, System.Collections.Generic, Rhino.Geometry
// and Grasshopper.Kernel.

// ===== SCRIPT (RunScript body) =====
if (Mesh == null) return;
if (Strain == null || Strain.Count != Mesh.Vertices.Count)
{
  Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
    "Strain has " + (Strain == null ? 0 : Strain.Count) + " values but the mesh has " + Mesh.Vertices.Count + " vertices.");
  return;
}
StrainMesh = StrainColours.Apply(Mesh, Strain);

// ===== ADDITIONAL =====
static class StrainColours
{
  const double STRAIN_CLIP = 5.0;   // strain % drawn red   js/globals.js:60

  public static Rhino.Geometry.Mesh Apply(Rhino.Geometry.Mesh mesh, List<double> strain)
  {
    Rhino.Geometry.Mesh m = mesh.DuplicateMesh();
    System.Drawing.Color[] colors = new System.Drawing.Color[m.Vertices.Count];
    for (int i = 0; i < colors.Length; i++)
    {
      // HSL hue (1 - e/clip) * 0.7, full saturation, half lightness   dynamicSolver.js:205-215
      double e = Math.Min(Math.Max(strain[i], 0), STRAIN_CLIP);
      colors[i] = Hsl((1.0 - e / STRAIN_CLIP) * 0.7, 1.0, 0.5);
    }
    m.VertexColors.SetColors(colors);
    return m;
  }

  static System.Drawing.Color Hsl(double h, double s, double l)   // three.js Color.setHSL
  {
    double q = l <= 0.5 ? l * (1 + s) : l + s - l * s;
    double p = 2 * l - q;
    double r = Hue(p, q, h + 1.0 / 3.0), g = Hue(p, q, h), b = Hue(p, q, h - 1.0 / 3.0);
    return System.Drawing.Color.FromArgb(ToByte(r), ToByte(g), ToByte(b));
  }

  static double Hue(double p, double q, double t)
  {
    if (t < 0) t += 1; if (t > 1) t -= 1;
    if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
    if (t < 0.5) return q;
    if (t < 2.0 / 3.0) return p + (q - p) * 6 * (2.0 / 3.0 - t);
    return p;
  }

  static int ToByte(double x) { return (int)Math.Round(Math.Max(0, Math.Min(1, x)) * 255); }
}
