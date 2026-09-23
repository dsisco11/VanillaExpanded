using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.RadialMenu;

/// <summary>Owns the combined menu mesh, shader, and separate game-rendered entry content.</summary>
internal sealed class RadialMenuRenderer : IDisposable
{
    #region Resources
    private const string ShaderName = "radial_menu";
    private readonly ICoreClientAPI capi;
    private readonly Matrixf matrix = new();
    private readonly Dictionary<string, LoadedTexture> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> renderedLabels = new(StringComparer.Ordinal);
    private readonly CairoFont labelFont = CairoFont.WhiteSmallText().WithStroke([0, 0, 0, 0.65], 1.5);
    private RadialMenuLayout? meshLayout;
    private MeshRef? mesh;
    private ShaderProgram? shader;
    private double supportedRadiusPixels;
    private float animationTime;
    private bool disposed;
    #endregion

    #region Public API
    /// <summary>Creates menu-owned graphics resources for the client.</summary>
    public RadialMenuRenderer(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        ReloadShader();
    }

    /// <summary>Gets the number of mesh uploads, for geometry-lifetime verification.</summary>
    public int MeshUploadCount { get; private set; }

    /// <summary>Gets whether the background shader compiled successfully.</summary>
    public bool IsReady => shader is not null && !disposed;

    /// <summary>Disposes cached labels that cannot be used by the next fixed layout.</summary>
    public void PrepareLayout(RadialMenuLayout layout)
    {
        var retainedIds = new HashSet<string>(layout.WedgeIds, StringComparer.Ordinal) { layout.CenterId };
        foreach (string id in new List<string>(renderedLabels.Keys))
        {
            if (retainedIds.Contains(id)) continue;
            if (labels.Remove(id, out LoadedTexture? texture)) texture.Dispose();
            renderedLabels.Remove(id);
        }
    }

    /// <summary>Recompiles the menu shader after an engine shader reload.</summary>
    public bool ReloadShader()
    {
        if (disposed) return false;
        shader?.Dispose();
        shader = null;
        if (mesh is not null)
        {
            capi.Render.DeleteMesh(mesh);
            mesh = null;
            meshLayout = null;
        }
        ShaderProgram? program = null;
        try
        {
            // Register and compile against the current graphics context before exposing the menu.
            program = new ShaderProgram
            {
                VertexShader = (Shader)capi.Shader.NewShader(EnumShaderType.VertexShader),
                FragmentShader = (Shader)capi.Shader.NewShader(EnumShaderType.FragmentShader),
                AssetDomain = Constants.ModId
            };
            capi.Shader.RegisterFileShaderProgram(ShaderName, program);
            if (!program.Compile())
            {
                capi.Logger.Error("[VanillaExpanded] Failed to compile radial menu shader.");
                program.Dispose();
                return false;
            }
            shader = program;
            return true;
        }
        catch (Exception error)
        {
            program?.Dispose();
            capi.Logger.Error("[VanillaExpanded] Failed to reload radial menu shader: {0}", error);
            return false;
        }
    }

    /// <summary>Draws the cached background once, followed by game-rendered icons and cached text.</summary>
    public void Render(RadialMenuLayout layout, RadialMenuInteraction interaction, float deltaTime, float centerX, float centerY, float radiusPixels)
    {
        if (disposed || shader is null || radiusPixels <= 0) return;
        EnsureMesh(layout, radiusPixels);
        if (mesh is null) return;
        animationTime += deltaTime;

        var previousShader = capi.Render.CurrentActiveShader;
        bool hadDepthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool hadBlend = GL.IsEnabled(EnableCap.Blend);
        previousShader?.Stop();
        capi.Render.GLDisableDepthTest();
        capi.Render.GlToggleBlend(true);
        try
        {
            shader.Use();
            try
            {
                var states = new float[(layout.WedgeIds.Count + 1) * 4];
                for (int i = 0; i < layout.WedgeIds.Count; i++)
                {
                    states[i * 4] = interaction.GetEntry(layout.WedgeIds[i]).Enabled ? 1 : 0;
                    states[i * 4 + 1] = interaction.SelectedId == layout.WedgeIds[i] ? 1 : 0;
                }
                states[layout.WedgeIds.Count * 4] = interaction.GetEntry(layout.CenterId).Enabled ? 1 : 0;
                states[layout.WedgeIds.Count * 4 + 1] = interaction.SelectedId == layout.CenterId ? 1 : 0;
                int hovered = interaction.HoveredId == layout.CenterId ? layout.WedgeIds.Count : -1;
                if (hovered < 0 && interaction.HoveredId is not null)
                {
                    for (int i = 0; i < layout.WedgeIds.Count; i++) if (layout.WedgeIds[i] == interaction.HoveredId) { hovered = i; break; }
                }

                shader.Uniforms4("entryStates", layout.WedgeIds.Count + 1, states);
                shader.Uniform("entryCount", layout.WedgeIds.Count + 1);
                shader.Uniform("hoveredIndex", hovered);
                shader.Uniform("centerRadius", (float)layout.CenterRadius);
                shader.Uniform("innerRadius", (float)layout.InnerRadius);
                shader.Uniform("outerRadius", (float)layout.OuterRadius);
                shader.Uniform("separatorFraction", (float)(layout.SeparatorDegrees / layout.StepDegrees));
                shader.Uniform("animationTime", animationTime);
                matrix.Set(capi.Render.CurrentModelviewMatrix).Translate(centerX, centerY, 50).Scale(radiusPixels, radiusPixels, 1);
                ((IShaderProgram)shader).UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                ((IShaderProgram)shader).UniformMatrix("modelViewMatrix", matrix.Values);
                capi.Render.RenderMesh(mesh);
            }
            finally
            {
                shader.Stop();
            }

            DrawContent(layout, interaction, centerX, centerY, radiusPixels);
        }
        finally
        {
            if (hadDepthTest) capi.Render.GLEnableDepthTest();
            else capi.Render.GLDisableDepthTest();
            capi.Render.GlToggleBlend(hadBlend);
            previousShader?.Use();
        }
    }

    /// <summary>Disposes menu-owned GPU resources and cached content textures.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shader?.Dispose();
        if (mesh is not null) capi.Render.DeleteMesh(mesh);
        foreach (LoadedTexture texture in labels.Values) texture.Dispose();
        labelFont.Dispose();
        labels.Clear();
    }
    #endregion

    #region Geometry and content
    /// <summary>Uploads geometry only when layout or supported screen-space tolerance changes.</summary>
    private void EnsureMesh(RadialMenuLayout layout, double radiusPixels)
    {
        if (mesh is not null && meshLayout?.HasSameGeometry(layout) == true && radiusPixels <= supportedRadiusPixels) return;
        if (mesh is not null) capi.Render.DeleteMesh(mesh);
        supportedRadiusPixels = radiusPixels;
        mesh = capi.Render.UploadMesh(RadialMenuMesh.Build(layout, supportedRadiusPixels));
        meshLayout = layout;
        MeshUploadCount++;
    }

    /// <summary>Uses normal game icon and GUI text paths after the background shader has stopped.</summary>
    private void DrawContent(RadialMenuLayout layout, RadialMenuInteraction interaction, float centerX, float centerY, float radiusPixels)
    {
        var guiShader = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
        guiShader.Use();
        try
        {
            double midRadius = (layout.InnerRadius + layout.OuterRadius) / 2d;
            for (int i = 0; i < layout.WedgeIds.Count; i++)
            {
                RadialMenuEntry entry = interaction.GetEntry(layout.WedgeIds[i]);
                (double x, double y) = layout.GetWedgeCenter(i, centerX, centerY, radiusPixels, midRadius);
                DrawEntry(entry, x, y, radiusPixels * 0.12f);
            }
            DrawEntry(interaction.GetEntry(layout.CenterId), centerX, centerY, radiusPixels * 0.2f);
        }
        finally
        {
            guiShader.Stop();
        }
    }

    /// <summary>Refreshes changed content without touching the geometry cache.</summary>
    private void DrawEntry(RadialMenuEntry entry, double x, double y, float iconSize)
    {
        entry.Icon?.Render(capi, x, y, iconSize, entry.Enabled);

        if (!renderedLabels.TryGetValue(entry.Id, out string? previous) || previous != entry.Label)
        {
            if (labels.Remove(entry.Id, out LoadedTexture? old)) old.Dispose();
            renderedLabels[entry.Id] = entry.Label;
            if (!string.IsNullOrEmpty(entry.Label))
            {
                var extents = labelFont.GetTextExtents(entry.Label);
                labels[entry.Id] = capi.Gui.TextTexture.GenTextTexture(entry.Label, labelFont, (int)Math.Ceiling(extents.Width) + 4, (int)Math.Ceiling(labelFont.GetFontExtents().Height) + 4, null, EnumTextOrientation.Center);
            }
        }

        if (labels.TryGetValue(entry.Id, out LoadedTexture? label))
        {
            capi.Render.GetEngineShader(EnumShaderProgram.Gui).Use();
            capi.Render.Render2DLoadedTexture(label, (float)x - label.Width / 2f, (float)y + iconSize / 2f, 60);
        }
    }
    #endregion
}












