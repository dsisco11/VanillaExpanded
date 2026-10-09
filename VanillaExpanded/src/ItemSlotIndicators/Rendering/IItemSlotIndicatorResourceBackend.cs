using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Separates engine graphics operations from resource ownership for headless lifecycle verification.</summary>
internal interface IItemSlotIndicatorResourceBackend
{
    #region Public API
    /// <summary>Creates an unregistered engine-owned program whose lifetime transfers to the caller.</summary>
    IShaderProgram CreateProgram();
    /// <summary>Configures and registers file stages under the definition's stable engine name.</summary>
    void RegisterProgram(ItemSlotIndicatorEffectDefinition definition, IShaderProgram program);
    /// <summary>Rejects a linked program that does not implement the required mesh and transform inputs.</summary>
    void ValidateProgram(IShaderProgram program);
    /// <summary>Uploads static geometry and transfers the returned mesh lifetime to the caller.</summary>
    MeshRef UploadMesh(MeshData mesh);
    /// <summary>Reports one unavailable effect or rectangle resource per explicit load attempt.</summary>
    void ReportFailure(string identity, string reason);
    #endregion
}
