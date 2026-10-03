using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.RadialMenu;

/// <summary>Owns nested ring meshes, their shader, and separate game-rendered entry content.</summary>
internal sealed class RadialMenuRenderer : IDisposable
{
    #region Resources
    private const string ShaderName = "radial_menu";
    private const float CenterLabelInsetPixels = 8f;
    private const int CenterLabelTexturePaddingPixels = 2;
    private readonly ICoreClientAPI capi;
    private readonly RadialMenuHoverAnimation hoverAnimation = new();
    private readonly Matrixf matrix = new();
    private readonly Dictionary<string, LoadedTexture> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> renderedLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> labelScales = new(StringComparer.Ordinal);
    private CairoFont? labelFont;
    private readonly LoadedTexture dimTexture;
    private readonly RadialMenuIconHalo iconHalo;
    private RadialMenuLayout? meshLayout;
    private readonly List<MeshRef> meshes = [];
    private ShaderProgram? shader;
    private double supportedRadiusPixels;
    private bool disposed;
    #endregion

    #region Public API
    /// <summary>Creates menu-owned graphics resources for the client.</summary>
    public RadialMenuRenderer(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        iconHalo = new RadialMenuIconHalo(capi);
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
        hoverAnimation.Retain(layout);
        var retainedIds = new HashSet<string>(layout.AllEntryIds, StringComparer.Ordinal);
        foreach (string id in new List<string>(renderedLabels.Keys))
        {
            if (retainedIds.Contains(id)) continue;
            if (labels.Remove(id, out LoadedTexture? texture)) texture.Dispose();
            renderedLabels.Remove(id);
            labelScales.Remove(id);
        }
    }

    /// <summary>Starts hover animation anew for one menu opening.</summary>
    public void ResetInteraction() => hoverAnimation.Reset();

    /// <summary>Recompiles the menu shader after an engine shader reload.</summary>
    public bool ReloadShader()
    {
        if (disposed) return false;
        shader?.Dispose();
        shader = null;
        DeleteMeshes();
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
            if (!iconHalo.ReloadShader()) { program.Dispose(); return false; }
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
        EnsureMeshes(layout, radiusPixels);
        if (meshes.Count == 0) return;
        hoverAnimation.Advance(layout, interaction.HoveredId, deltaTime);

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
                35, new Vec4f(0.025f, 0.02f, 0.015f, RadialMenuWedgeStyle.BackdropOpacity));
            guiShader.Stop();
            shader.Use();
            try
            {
                string[] entryIds = [.. layout.AllEntryIds];
                var states = new float[entryIds.Length * 4];
                int hovered = -1;
                for (int i = 0; i < entryIds.Length; i++)
                {
                    string id = entryIds[i];
                    RadialMenuEntry entry = interaction.GetEntry(id);
                    states[i * 4] = entry.Enabled ? 1 : 0;
                    states[i * 4 + 1] = interaction.SelectedId == id ? 1 : 0;
                    states[i * 4 + 2] = hoverAnimation.VisualProgress(id);
                    if (interaction.HoveredId == id) hovered = i;
                }

                shader.Uniforms4("entryStates", entryIds.Length, states);
                shader.Uniform("entryCount", entryIds.Length);
                shader.Uniform("hoveredIndex", hovered);
                shader.Uniform("radiusPixels", radiusPixels);
                shader.Uniform("cornerRadiusPixels", RadialMenuWedgeStyle.CornerRadiusPixels);
                shader.Uniform("borderWidthPixels", RadialMenuWedgeStyle.BorderWidthPixels);
                shader.Uniform("hoverScale", RadialMenuWedgeStyle.HoverScale);
                shader.Uniform("enabledOpacity", RadialMenuWedgeStyle.EnabledOpacity);
                shader.Uniform("disabledOpacity", RadialMenuWedgeStyle.DisabledOpacity);
                shader.Uniform("grainStrength", RadialMenuWedgeStyle.DefaultGrainStrength);
                shader.Uniform("disabledFill", RadialMenuWedgeStyle.DisabledFill);
                shader.Uniform("enabledFill", RadialMenuWedgeStyle.EnabledFill);
                shader.Uniform("hoverFill", RadialMenuWedgeStyle.HoverFill);
                shader.Uniform("selectedFill", RadialMenuWedgeStyle.SelectedFill);
                shader.Uniform("borderColor", RadialMenuWedgeStyle.Border);
                shader.Uniform("hoverBorderColor", RadialMenuWedgeStyle.HoverBorder);
                matrix.Set(capi.Render.CurrentModelviewMatrix).Translate(centerX, centerY, 50).Scale(radiusPixels, radiusPixels, 1);
                ((IShaderProgram)shader).UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                ((IShaderProgram)shader).UniformMatrix("modelViewMatrix", matrix.Values);
                int offset = 0;
                int meshIndex = 0;
                for (RadialMenuLayout? ring = layout; ring is not null; ring = ring.InnerMenu)
                {
                    ConfigureRingShader(ring, offset, maskIndex: -1);
                    capi.Render.RenderMesh(meshes[meshIndex++]);
                    offset += ring.EntryIds.Count;
                }
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
        DeleteMeshes();
        foreach (LoadedTexture texture in labels.Values) texture.Dispose();
        dimTexture.Dispose();
        iconHalo.Dispose();
        labelFont?.Dispose();
        labelFont = null;
        labels.Clear();
        labelScales.Clear();
    }
    #endregion

    #region Geometry and content
    /// <summary>Uploads geometry only when layout or supported screen-space tolerance changes.</summary>
    private void EnsureMeshes(RadialMenuLayout layout, double radiusPixels)
    {
        double requiredRadiusPixels = radiusPixels * RadialMenuWedgeStyle.HoverScale;
        if (meshes.Count > 0 && meshLayout?.HasSameGeometry(layout) == true && requiredRadiusPixels <= supportedRadiusPixels) return;
        DeleteMeshes();
        supportedRadiusPixels = requiredRadiusPixels;
        int offset = 0;
        for (RadialMenuLayout? ring = layout; ring is not null; ring = ring.InnerMenu)
        {
            meshes.Add(capi.Render.UploadMesh(RadialMenuMesh.Build(ring, supportedRadiusPixels, offset)));
            offset += ring.EntryIds.Count;
        }
        meshLayout = layout;
        MeshUploadCount++;
    }

    /// <summary>Deletes every ring mesh as one cached layout generation.</summary>
    private void DeleteMeshes()
    {
        foreach (MeshRef ringMesh in meshes) capi.Render.DeleteMesh(ringMesh);
        meshes.Clear();
        meshLayout = null;
    }

    /// <summary>Supplies geometry parameters for one ring while global entry states remain bound.</summary>
    private void ConfigureRingShader(RadialMenuLayout ring, int entryOffset, int maskIndex)
    {
        shader!.Uniform("maskIndex", maskIndex);
        shader.Uniform("ringEntryOffset", entryOffset);
        shader.Uniform("ringEntryCount", ring.EntryIds.Count);
        shader.Uniform("ringMode", ring.RenderAsCenter ? 1 : 0);
        shader.Uniform("innerRadius", (float)ring.InnerRadius);
        shader.Uniform("outerRadius", (float)ring.OuterRadius);
        shader.Uniform("separatorFraction", ring.RenderAsCenter ? 0 : (float)(ring.SeparatorDegrees / ring.StepDegrees));
        shader.Uniform("startAngleRadians", (float)(ring.StartAngleDegrees * Math.PI / 180d));
        shader.Uniform("clockwiseSign", ring.Clockwise ? 1f : -1f);
    }

    /// <summary>Uses normal game icon and GUI text paths after the background shader has stopped.</summary>
    private void DrawContent(RadialMenuLayout layout, RadialMenuInteraction interaction, float centerX, float centerY, float radiusPixels)
    {
        var guiShader = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
        guiShader.Use();
        try
        {
            int entryOffset = 0;
            int meshIndex = 0;
            for (RadialMenuLayout? ring = layout; ring is not null; ring = ring.InnerMenu)
            {
                if (ring.IsSingleOption)
                {
                    RadialMenuEntry single = interaction.GetEntry(ring.EntryIds[0]);
                    string? hoveredId = interaction.HoveredId;
                    bool showingHoveredLabel = hoveredId is not null && hoveredId != single.Id;
                    RadialMenuEntry label = showingHoveredLabel ? interaction.GetEntry(hoveredId!) : single;
                    single.Icon?.Render(capi, centerX, centerY, radiusPixels * (float)ring.OuterRadius, single.Enabled);
                    DrawCenterLabel(single.Id, label.Label, ring, centerX, centerY, radiusPixels);
                }
                else
                {
                    double midRadius = (ring.InnerRadius + ring.OuterRadius) / 2d;
                    float iconSize = ring.GetIconSizePixels(radiusPixels, RadialMenuWedgeStyle.IconInsetPixels);
                    for (int i = 0; i < ring.EntryIds.Count; i++)
                    {
                        RadialMenuEntry entry = interaction.GetEntry(ring.EntryIds[i]);
                        float scale = 1f + (RadialMenuWedgeStyle.HoverScale - 1f) * hoverAnimation.VisualProgress(entry.Id);
                        (double x, double y) = ring.GetWedgeCenter(i, centerX, centerY, radiusPixels, midRadius * scale);
                        DrawClippedEntry(entry, entryOffset + i, ring, meshes[meshIndex], entryOffset, x, y,
                            iconSize * scale, guiShader);
                    }
                }
                entryOffset += ring.EntryIds.Count;
                meshIndex++;
            }
        }
        finally
        {
            guiShader.Stop();
        }
    }

    /// <summary>Wraps and scales center text to fit the circular button without splitting words.</summary>
    private void DrawCenterLabel(string id, string text, RadialMenuLayout layout,
        float centerX, float centerY, float radiusPixels)
    {
        int usableDiameter = (int)Math.Max(1d,
            radiusPixels * layout.OuterRadius * 2d - CenterLabelInsetPixels * 2d);
        DrawLabel(id, text, centerX, centerY, usableDiameter);
    }

    /// <summary>Clips a depth-correct icon capture and its pixel-distance halo to the wedge stencil.</summary>
    private void DrawClippedEntry(RadialMenuEntry entry, int index, RadialMenuLayout ring, MeshRef ringMesh,
        int entryOffset, double x, double y, float iconSize, IShaderProgram guiShader)
    {
        if (entry.Icon is null)
        {
            DrawLabel(entry.Id, entry.Label, x, y);
            return;
        }
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
            iconHalo.Capture(entry.Icon, x, y, iconSize, entry.Enabled);
            // Reserve only the wedge bit; the unscaled icon mask lives in its own texture.
            GL.Enable(EnableCap.StencilTest);
            GL.StencilMask(0x80);
            GL.ClearStencil(0);
            GL.Clear(ClearBufferMask.StencilBufferBit);
            GL.StencilFunc(StencilFunction.Always, 0x80, 0x80);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            GL.ColorMask(false, false, false, false);
            guiShader.Stop();
            shader!.Use();
            ConfigureRingShader(ring, entryOffset, index);
            capi.Render.RenderMesh(ringMesh);
            shader.Stop();
            GL.ColorMask(true, true, true, true);
            GL.StencilMask(0);
            GL.StencilFunc(StencilFunction.Equal, 0x80, 0x80);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            iconHalo.Render();
            guiShader.Use();
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
    }

    /// <summary>Caches a text-only entry label and centers its texture at the entry position.</summary>
    private void DrawLabel(string id, string text, double x, double y, int circleDiameter = 0)
    {
        string cacheKey = text + '\0' + circleDiameter;
        if (!renderedLabels.TryGetValue(id, out string? previous) || previous != cacheKey)
        {
            if (labels.Remove(id, out LoadedTexture? old)) old.Dispose();
            renderedLabels[id] = cacheKey;
            if (!string.IsNullOrEmpty(text))
            {
                labelFont ??= CairoFont.WhiteSmallText().WithStroke([0, 0, 0, 0.65], 1.5);
                if (circleDiameter > 0)
                {
                    double lineHeight = labelFont.GetFontExtents().Height;
                    string wrapped = RadialMenuLabelLayout.FitToCircle(text, circleDiameter, lineHeight,
                        value => labelFont.GetTextExtents(value).Width, CenterLabelTexturePaddingPixels);
                    string[] lines = wrapped.Split('\n');
                    labelScales[id] = (float)RadialMenuLabelLayout.GetScaleForCircle(lines, circleDiameter,
                        lineHeight, value => labelFont.GetTextExtents(value).Width, CenterLabelTexturePaddingPixels);
                    double widestLine = 1d;
                    foreach (string line in lines)
                        widestLine = Math.Max(widestLine, labelFont.GetTextExtents(line).Width);
                    int width = (int)Math.Ceiling(widestLine) + CenterLabelTexturePaddingPixels * 2;
                    int height = (int)Math.Ceiling(lineHeight * lines.Length)
                        + CenterLabelTexturePaddingPixels * 2;
                    labels[id] = capi.Gui.TextTexture.GenTextTexture(wrapped, labelFont, width, height,
                        null, EnumTextOrientation.Center);
                }
                else
                {
                    labelScales.Remove(id);
                    labels[id] = capi.Gui.TextTexture.GenTextTexture(text, labelFont);
                }
            }
        }

        if (labels.TryGetValue(id, out LoadedTexture? label))
        {
            capi.Render.GetEngineShader(EnumShaderProgram.Gui).Use();
            float scale = circleDiameter > 0 && labelScales.TryGetValue(id, out float fittedScale)
                ? fittedScale
                : 1f;
            float width = label.Width * scale;
            float height = label.Height * scale;
            if (scale < 1f)
                capi.Render.Render2DTexture(label.TextureId, (float)x - width / 2f, (float)y - height / 2f,
                    width, height, 60);
            else
                capi.Render.Render2DLoadedTexture(label, (float)x - label.Width / 2f, (float)y - label.Height / 2f, 60);
        }
    }
    #endregion
}











