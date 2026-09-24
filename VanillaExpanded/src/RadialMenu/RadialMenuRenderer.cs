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
    private const float HoverBumpScale = 1.15f;
    private readonly ICoreClientAPI capi;
    private readonly Matrixf matrix = new();
    private readonly Dictionary<(string Id, bool Description), LoadedTexture> labels = new();
    private readonly Dictionary<(string Id, bool Description), string> renderedLabels = new();
    private readonly CairoFont labelFont = CairoFont.WhiteSmallText().WithStroke([0, 0, 0, 0.65], 1.5);
    private readonly LoadedTexture dimTexture;
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
        dimTexture = new LoadedTexture(capi) { Width = 1, Height = 1 };
        capi.Render.LoadOrUpdateTextureFromRgba([unchecked((int)0xffffffff)], false, 0, ref dimTexture);
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
        foreach (var id in new List<(string Id, bool Description)>(renderedLabels.Keys))
        {
            if (retainedIds.Contains(id.Id) && (!id.Description || id.Id == layout.CenterId)) continue;
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
            var guiShader = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
            guiShader.Use();
            capi.Render.Render2DTexture(dimTexture.TextureId, 0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight,
                35, new Vec4f(0.025f, 0.02f, 0.015f, 0.66f));
            guiShader.Stop();
            shader.Use();
            try
            {
                shader.Uniform("maskIndex", -1);
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
                shader.Uniform("separatorFraction", layout.WedgeIds.Count == 0 ? 0 : (float)(layout.SeparatorDegrees / layout.StepDegrees));
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
        dimTexture.Dispose();
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
                float scale = interaction.HoveredId == entry.Id ? HoverBumpScale : 1f;
                (double x, double y) = layout.GetWedgeCenter(i, centerX, centerY, radiusPixels, midRadius * scale);
                DrawClippedEntry(entry, i, x, y, radiusPixels * 0.12f * scale, guiShader);
            }
            RadialMenuEntry center = interaction.GetEntry(layout.CenterId);
            DrawEntry(center, centerX, centerY, radiusPixels * 0.2f);
            double footerY = Math.Min(centerY + radiusPixels + 12, capi.Render.FrameHeight - labelFont.GetFontExtents().Height - 8);
            DrawLabel((center.Id, true), center.Description ?? string.Empty, centerX, footerY);
        }
        finally
        {
            guiShader.Stop();
        }
    }

    /// <summary>Uses one stencil bit to confine a game-rendered icon to its curved wedge.</summary>
    private void DrawClippedEntry(RadialMenuEntry entry, int index, double x, double y, float iconSize, IShaderProgram guiShader)
    {
        if (entry.Icon is null) { DrawLabel((entry.Id, false), entry.Label, x, y + iconSize / 2f); return; }
        bool hadStencil = GL.IsEnabled(EnableCap.StencilTest);
        int oldWriteMask = GL.GetInteger(GetPName.StencilWritemask);
        int oldFunction = GL.GetInteger(GetPName.StencilFunc);
        int oldReference = GL.GetInteger(GetPName.StencilRef);
        int oldValueMask = GL.GetInteger(GetPName.StencilValueMask);
        int oldFail = GL.GetInteger(GetPName.StencilFail);
        int oldDepthFail = GL.GetInteger(GetPName.StencilPassDepthFail);
        int oldDepthPass = GL.GetInteger(GetPName.StencilPassDepthPass);
        int oldClearValue = GL.GetInteger(GetPName.StencilClearValue);
        try
        {
            // Reserve the high stencil bit and preserve any lower bits owned by the game UI.
            GL.Enable(EnableCap.StencilTest);
            GL.StencilMask(0x80);
            GL.ClearStencil(0);
            GL.Clear(ClearBufferMask.StencilBufferBit);
            GL.StencilFunc(StencilFunction.Always, 0x80, 0x80);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            GL.ColorMask(false, false, false, false);
            guiShader.Stop();
            shader!.Use();
            shader.Uniform("maskIndex", index);
            capi.Render.RenderMesh(mesh!);
            shader.Stop();
            GL.ColorMask(true, true, true, true);
            GL.StencilMask(0);
            GL.StencilFunc(StencilFunction.Equal, 0x80, 0x80);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            guiShader.Use();
            entry.Icon.Render(capi, x, y, iconSize, entry.Enabled);
        }
        finally
        {
            GL.ColorMask(true, true, true, true);
            GL.StencilMask(oldWriteMask);
            GL.StencilFunc((StencilFunction)oldFunction, oldReference, oldValueMask);
            GL.StencilOp((StencilOp)oldFail, (StencilOp)oldDepthFail, (StencilOp)oldDepthPass);
            GL.ClearStencil(oldClearValue);
            if (!hadStencil) GL.Disable(EnableCap.StencilTest);
            guiShader.Use();
        }
        DrawLabel((entry.Id, false), entry.Label, x, y + iconSize / 2f);
    }

    /// <summary>Refreshes changed content without touching the geometry cache.</summary>
    private void DrawEntry(RadialMenuEntry entry, double x, double y, float iconSize)
    {
        entry.Icon?.Render(capi, x, y, iconSize, entry.Enabled);
        DrawLabel((entry.Id, false), entry.Label, x, y + iconSize / 2f);
    }

    /// <summary>Caches changed text independently of icons and menu geometry.</summary>
    private void DrawLabel((string Id, bool Description) id, string text, double x, double y)
    {
        if (!renderedLabels.TryGetValue(id, out string? previous) || previous != text)
        {
            if (labels.Remove(id, out LoadedTexture? old)) old.Dispose();
            renderedLabels[id] = text;
            if (!string.IsNullOrEmpty(text))
            {
                var extents = labelFont.GetTextExtents(text);
                labels[id] = capi.Gui.TextTexture.GenTextTexture(text, labelFont, (int)Math.Ceiling(extents.Width) + 4, (int)Math.Ceiling(labelFont.GetFontExtents().Height) + 4, null, EnumTextOrientation.Center);
            }
        }

        if (labels.TryGetValue(id, out LoadedTexture? label))
        {
            capi.Render.GetEngineShader(EnumShaderProgram.Gui).Use();
            capi.Render.Render2DLoadedTexture(label, (float)x - label.Width / 2f, (float)y, 60);
        }
    }
    #endregion
}












