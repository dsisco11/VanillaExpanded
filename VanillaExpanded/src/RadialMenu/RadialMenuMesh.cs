using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.RadialMenu;

/// <summary>Builds one combined center and wedge mesh with stable per-triangle entry identifiers.</summary>
internal static class RadialMenuMesh
{
    #region Geometry
    /// <summary>Builds tessellated geometry whose outer arc deviates by at most half a screen pixel at the supported radius.</summary>
    public static MeshData Build(RadialMenuLayout layout, double supportedRadiusPixels)
    {
        if (supportedRadiusPixels <= 0) throw new ArgumentOutOfRangeException(nameof(supportedRadiusPixels));
        double wedgeRadians = layout.WedgeIds.Count == 0 ? 0 : layout.StepDegrees * Math.PI / 180d;
        double tolerance = Math.Min(0.5d / (supportedRadiusPixels * layout.OuterRadius), 0.25d);
        double segmentAngle = 2d * Math.Acos(1d - tolerance);
        int segments = Math.Max(1, (int)Math.Ceiling(wedgeRadians / segmentAngle));
        int centerSegments = Math.Max(24, segments * layout.WedgeIds.Count);
        int vertices = layout.WedgeIds.Count * segments * 4 + centerSegments * 3;
        var mesh = new MeshData(vertices, layout.WedgeIds.Count * segments * 6 + centerSegments * 3, withRgba: false, withFlags: false);
        mesh.mode = EnumDrawMode.Triangles;

        // Each wedge quad owns its vertices so the entry ID is identical at all triangle corners.
        for (int wedge = 0; wedge < layout.WedgeIds.Count; wedge++)
        {
            double center = layout.StartAngleDegrees + (layout.Clockwise ? 1d : -1d) * wedge * layout.StepDegrees;
            double first = center - (layout.Clockwise ? 1d : -1d) * layout.StepDegrees / 2d;
            double direction = layout.Clockwise ? 1d : -1d;
            for (int segment = 0; segment < segments; segment++)
            {
                double fraction0 = (double)segment / segments;
                double fraction1 = (double)(segment + 1) / segments;
                double a0 = (first + direction * fraction0 * layout.StepDegrees) * Math.PI / 180d;
                double a1 = (first + direction * fraction1 * layout.StepDegrees) * Math.PI / 180d;
                int offset = mesh.VerticesCount;
                AddPolar(mesh, a0, layout.InnerRadius, wedge, fraction0);
                AddPolar(mesh, a0, layout.OuterRadius, wedge, fraction0);
                AddPolar(mesh, a1, layout.InnerRadius, wedge, fraction1);
                AddPolar(mesh, a1, layout.OuterRadius, wedge, fraction1);
                if (layout.Clockwise)
                {
                    mesh.AddIndex(offset);
                    mesh.AddIndex(offset + 1);
                    mesh.AddIndex(offset + 2);
                    mesh.AddIndex(offset + 1);
                    mesh.AddIndex(offset + 3);
                    mesh.AddIndex(offset + 2);
                }
                else
                {
                    // Reverse winding so counterclockwise layouts render with the same face culling.
                    mesh.AddIndex(offset);
                    mesh.AddIndex(offset + 2);
                    mesh.AddIndex(offset + 1);
                    mesh.AddIndex(offset + 1);
                    mesh.AddIndex(offset + 2);
                    mesh.AddIndex(offset + 3);
                }
            }
        }

        // A separate disc leaves the intentional nonselectable radial gap outside the center.
        for (int segment = 0; segment < centerSegments; segment++)
        {
            double a0 = 2d * Math.PI * segment / centerSegments;
            double a1 = 2d * Math.PI * (segment + 1) / centerSegments;
            int offset = mesh.VerticesCount;
            mesh.AddVertex(0, 0, 0, layout.WedgeIds.Count, 0.5f);
            AddPolar(mesh, a0, layout.CenterRadius, layout.WedgeIds.Count, 0);
            AddPolar(mesh, a1, layout.CenterRadius, layout.WedgeIds.Count, 1);
            mesh.AddIndex(offset);
            mesh.AddIndex(offset + 1);
            mesh.AddIndex(offset + 2);
        }

        return mesh;
    }

    /// <summary>Adds a screen-oriented polar vertex whose UV carries its stable entry index and angular edge position.</summary>
    private static void AddPolar(MeshData mesh, double angle, double radius, int entryIndex, double wedgeFraction)
    {
        mesh.AddVertex((float)(Math.Sin(angle) * radius), (float)(-Math.Cos(angle) * radius), 0, entryIndex, (float)wedgeFraction);
    }
    #endregion
}



