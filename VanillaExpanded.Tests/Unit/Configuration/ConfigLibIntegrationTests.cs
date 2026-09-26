using Moq;

using System.Reflection;

using VanillaExpanded.ModSystems;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class ConfigLibIntegrationTests
{
    [Fact]
    public void BuildArgs_MinimalSignature_MapsRequiredArguments()
    {
        MethodInfo method = GetRegistrationMethod(nameof(RegistrationSignatures.Minimal));

        object?[] result = ConfigLibIntegrationModSystem.BuildArgs(method, () => { }, () => { });

        Assert.Equal(2, result.Length);
        Assert.Equal(Constants.ModId, result[0]);
        Assert.Same(VanillaExpandedModSystem.Config, result[1]);
    }

    [Fact]
    public void BuildArgs_CurrentSignature_MapsCallbacksToExpectedSlots()
    {
        MethodInfo method = GetRegistrationMethod(nameof(RegistrationSignatures.Current));
        Action onSyncedFromServer = () => { };
        Action onConfigSaved = () => { };

        object?[] result = ConfigLibIntegrationModSystem.BuildArgs(
            method,
            onSyncedFromServer,
            onConfigSaved);

        Assert.Equal(6, result.Length);
        Assert.Equal(Constants.ModId, result[0]);
        Assert.Same(VanillaExpandedModSystem.Config, result[1]);
        Assert.Equal(Constants.ConfigFileName, result[2]);
        Assert.Same(onSyncedFromServer, result[3]);
        Assert.Null(result[4]);
        Assert.Same(onConfigSaved, result[5]);
    }

    [Fact]
    public void HandleConfigUpdated_SynchronizesAndNotifiesWithoutPersisting()
    {
        var operations = new List<string>();
        var liveSystem = new RecordingLiveConfigurable(operations);
        var modLoader = new Mock<IModLoader>();
        modLoader.Setup(loader => loader.Systems).Returns(new ModSystem[] { liveSystem });

        var api = new Mock<ICoreAPI>();
        api.Setup(coreApi => coreApi.ModLoader).Returns(modLoader.Object);

        ConfigLibIntegrationModSystem.HandleConfigUpdated(api.Object, () => operations.Add("sync"));

        Assert.Equal(["sync", "reload"], operations);
        Assert.Equal(1, liveSystem.ReloadCount);
        Assert.Same(api.Object, liveSystem.LastApi);
        api.Verify(
            coreApi => coreApi.StoreModConfig(It.IsAny<VanillaExpandedConfig>(), It.IsAny<string>()),
            Times.Never);
    }

    private sealed class RecordingLiveConfigurable : ModSystem, ILiveConfigurable
    {
        private readonly List<string> operations;

        public RecordingLiveConfigurable(List<string> operations)
        {
            this.operations = operations;
        }

        public int ReloadCount { get; private set; }

        public ICoreAPI? LastApi { get; private set; }

        public void OnConfigReloaded(ICoreAPI api)
        {
            operations.Add("reload");
            ReloadCount++;
            LastApi = api;
        }
    }

    private static MethodInfo GetRegistrationMethod(string name)
    {
        return typeof(RegistrationSignatures).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public)!;
    }

    private static class RegistrationSignatures
    {
        public static void Minimal(string modId, object config)
        {
        }

        public static void Current(
            string modId,
            object config,
            string path,
            Action onSyncedFromServer,
            Action? onSettingChanged,
            Action onConfigSaved)
        {
        }
    }
}
