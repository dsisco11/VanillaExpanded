using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemRendering;

/// <summary>Prepares and submits configured radial items directly, leaving ordinary engine draws untouched.</summary>
internal sealed class ToolHeadPresentationRenderer
{
    private readonly ICoreClientAPI api;
    private readonly ToolHeadPresentationResolver resolver;
    private readonly ToolHeadRendererEligibility eligibility;
    private readonly System.Func<IShaderProgram, IDisposable> captureState;
    private readonly ConditionalWeakTable<CollectibleObject, object> reported = new();

    #region Public API
    /// <summary>Creates the client owner, with an optional graphics-boundary seam for headless verification.</summary>
    internal ToolHeadPresentationRenderer(ICoreClientAPI api, System.Func<IShaderProgram, IDisposable>? captureState = null)
    {
        this.api = api;
        this.captureState = captureState ?? (shader => new ToolHeadRenderState(api.Render, shader));
        eligibility = new ToolHeadRendererEligibility(api);
        resolver = new ToolHeadPresentationResolver((collectible, reason) =>
            api.Logger.Warning("[VanillaExpanded] Invalid radial presentation for {0}: {1}", collectible.Code, reason));
    }

    /// <summary>Returns false only before preparation so the caller can safely use ordinary engine fallback.</summary>
    internal bool TryRender(ICoreClientAPI caller, ItemSlot slot, double x, double y, float size, double wedgeDegrees)
    {
        if (!ReferenceEquals(api, caller) || slot.Itemstack is not { } stack) return false;
        var collectible = stack.Collectible;
        var result = resolver.Resolve(collectible);
        if (result.Properties is not { } properties || !eligibility.Supports(collectible)
            || !ToolHeadPresentationMatrix.TryCreate(properties, x, y, size, wedgeDegrees, out float[] model)) return false;
        IShaderProgram shader = api.Render.GetEngineShader(EnumShaderProgram.Gui);
        if (!IsReady(shader)) return false;
        // Capture before callbacks so nested draws and animation updates restore their caller.
        using var state = captureState(shader);
        shader.Uniform("applyAnimation", 0);
        ItemRenderInfo info = api.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Gui, 0);
        if (info?.ModelRef is null) return true;
        collectible.InGuiIdle(api.World, stack);
        if (!StillSupported(slot, stack, collectible, shader))
        {
            ReportInvalidation(collectible);
            return true;
        }
        if (!ToolHeadGuiShaderSettings.Apply(api, shader, stack, info, model))
        {
            ReportInvalidation(collectible);
            return true;
        }
        // Temperature/atlas access can be overridden too; do not submit after losing the supported boundary.
        if (!StillSupported(slot, stack, collectible, shader))
        {
            ReportInvalidation(collectible);
            return true;
        }
        api.Render.RenderMultiTextureMesh(info.ModelRef, "tex2d", 0);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Rejects callback changes that invalidate the selected prepared item or GUI draw path.</summary>
    private bool StillSupported(ItemSlot slot, ItemStack stack, CollectibleObject collectible, IShaderProgram shader)
        => ReferenceEquals(slot.Itemstack, stack) && ReferenceEquals(stack.Collectible, collectible)
            && eligibility.Supports(collectible) && IsReady(shader);

    /// <summary>Requires the menu's active GUI shader and the uniforms needed by ordinary mesh effects.</summary>
    private bool IsReady(IShaderProgram? shader)
    {
        if (shader is null || shader.Disposed || shader.LoadError || shader.ClampTexturesToEdge
            || !ReferenceEquals(api.Render.CurrentActiveShader, shader)) return false;
        // Matrix getters expose engine scratch arrays; inspect immediately and never retain them.
        float[] view = api.Render.CurrentModelviewMatrix;
        if (view is null || view.Length != 16) return false;
        foreach (float component in view)
            if (!float.IsFinite(component)) return false;
        foreach (string name in new[] { "modelMatrix", "modelViewMatrix", "projectionMatrix", "applyModelMat", "applyAnimation",
            "normalShaded", "lightPosition", "rgbaIn", "applyColor", "alphaTest", "extraGlow", "tempGlowMode", "rgbaGlowIn",
            "damageEffect", "overlayOpacity", "tex2d", "tex2dOverlay", "baseUvOrigin", "baseTextureSize",
            "overlayTextureSize", "noTexture", "darkEdges", "transparentCenter", "sepiaLevel" })
            if (!shader.HasUniform(name)) return false;
        return true;
    }

    /// <summary>Bounds diagnostics for draws cancelled after preparation, without retrying callbacks.</summary>
    private void ReportInvalidation(CollectibleObject collectible)
    {
        if (reported.TryGetValue(collectible, out _)) return;
        reported.Add(collectible, new object());
        api.Logger.Warning("[VanillaExpanded] Radial draw for {0} cancelled after item preparation changed rendering support.", collectible.Code);
    }
    #endregion
}
