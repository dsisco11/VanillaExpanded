using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.RadialMenu;

/// <summary>Builds one ring mesh with stable per-triangle entry identifiers.</summary>
internal static class RadialMenuMesh
{
    #region Geometry
    /// <summary>Builds tessellated ring geometry whose outer arc deviates by at most half a screen pixel.</summary>
    public static MeshData Build(RadialMenuLayout layout, double supportedRadiusPixels, int entryOffset = 0)
    {
        if (supportedRadiusPixels <= 0) throw new ArgumentOutOfRangeException(nameof(supportedRadiusPixels));
        double wedgeRadians = layout.StepDegrees * Math.PI / 180d;
        double tolerance = Math.Min(0.5d / (supportedRadiusPixels * layout.OuterRadius), 0.25d);
        double segmentAngle = 2d * Math.Acos(1d - tolerance);
        int segments = Math.Max(1, (int)Math.Ceiling(wedgeRadians / segmentAngle));
        int vertices = layout.IsSingleOption ? segments * 3 : layout.EntryIds.Count * segments * 4;
        int indices = layout.IsSingleOption ? segments * 3 : layout.EntryIds.Count * segments * 6;
        var mesh = new MeshData(vertices, indices, withRgba: false, withFlags: false);
        mesh.mode = EnumDrawMode.Triangles;

        if (layout.IsSingleOption)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                double a0 = 2d * Math.PI * segment / segments;
                double a1 = 2d * Math.PI * (segment + 1) / segments;
                int offset = mesh.VerticesCount;
                mesh.AddVertex(0, 0, 0, entryOffset, 0.5f);
                AddPolar(mesh, a0, layout.OuterRadius, entryOffset, 0);
                AddPolar(mesh, a1, layout.OuterRadius, entryOffset, 1);
                mesh.AddIndex(offset);
                mesh.AddIndex(offset + 1);
                mesh.AddIndex(offset + 2);
            }
            return mesh;
        }

        // Each wedge quad owns its vertices so the entry ID is identical at all triangle corners.
        for (int wedge = 0; wedge < layout.EntryIds.Count; wedge++)
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
                AddPolar(mesh, a0, layout.InnerRadius, entryOffset + wedge, fraction0);
                AddPolar(mesh, a0, layout.OuterRadius, entryOffset + wedge, fraction0);
                AddPolar(mesh, a1, layout.InnerRadius, entryOffset + wedge, fraction1);
                AddPolar(mesh, a1, layout.OuterRadius, entryOffset + wedge, fraction1);
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

        return mesh;
    }

    /// <summary>Adds a screen-oriented polar vertex whose UV carries its stable entry index and angular edge position.</summary>
    private static void AddPolar(MeshData mesh, double angle, double radius, int entryIndex, double wedgeFraction)
    {
        mesh.AddVertex((float)(Math.Sin(angle) * radius), (float)(-Math.Cos(angle) * radius), 0, entryIndex, (float)wedgeFraction);
    }
    #endregion
}



