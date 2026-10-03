using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Reads the authoritative engine camera with a cached accessor for its nonpublic mode field.</summary>
internal sealed class ItemSlotIndicatorCameraSource(ICoreClientAPI api) : IItemSlotIndicatorCameraSource
{
    private static readonly AccessTools.FieldRef<Camera, EnumCameraMode> cameraMode =
        AccessTools.FieldRefAccess<Camera, EnumCameraMode>("CameraMode");

    #region Public API
    /// <summary>Copies the view basis and identities once per interested GUI frame, without using player yaw as a substitute.</summary>
    public ItemSlotIndicatorCameraSample? Capture()
    {
        var world = api.World;
        if (world is not ClientMain client || world.Player is not { } player || client.MainCamera is not { } camera)
            return null;
        double[] matrix = api.Render.CameraMatrixOrigin;
        if (matrix is null) return null;
        return new(world, player, camera, cameraMode(camera), ItemSlotIndicatorCameraBasis.FromViewMatrix(matrix));
    }
    #endregion
}
