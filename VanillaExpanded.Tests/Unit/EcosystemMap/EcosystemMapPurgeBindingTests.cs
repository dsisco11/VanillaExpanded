using System.Reflection;
using Moq;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks the narrow vanilla command adapter without opening or patching the game's database implementation.</summary>
public sealed class EcosystemMapPurgeBindingTests
{
    #region Public API
    /// <summary>Forwards exact arguments/results, clears only on success and restores only an owned handler.</summary>
    [Fact]
    public async Task SuccessfulPurgeCoordinatesOwnRecordsAndDerivedState()
    {
        using var fixture=new StorageFixture();
        await using var store=fixture.Open();
        var key=new EcosystemRegionKey(3,4);
        await store.WriteRegionAsync(new(key,Climate:StorageFixture.Map()),store.CurrentEpoch);
        var commands=new ChatCommandApi(Mock.Of<ICoreAPI>());
        var command=commands.Create("purgedb");
        var arguments=new TextCommandCallingArgs();
        TextCommandCallingArgs? forwarded=null;
        var result=TextCommandResult.Success("vanilla result");
        OnCommandDelegate original=args=>{forwarded=args;return result;};
        command.HandleWith(original);
        int cleared=0;
        using(var binding=new EcosystemMapPurgeBinding(command,store,()=>cleared++))
        {
            Assert.Same(result,GetHandler(command)(arguments));
            Assert.Same(arguments,forwarded);
            Assert.Equal(1,cleared);
            Assert.Null((await store.ReadRegionAsync(key)).Value);
        }
        Assert.Same(original,GetHandler(command));
        using(var binding=new EcosystemMapPurgeBinding(command,store,()=>cleared++))
        {
            OnCommandDelegate other=_=>result;
            command.HandleWith(other);
            binding.Dispose();
            Assert.Same(other,GetHandler(command));
        }
    }

    /// <summary>A failed vanilla purge leaves ecosystem data and derived state intact.</summary>
    [Fact]
    public async Task FailedVanillaPurgeDoesNotClearEcosystemRecords()
    {
        using var fixture=new StorageFixture();
        await using var store=fixture.Open();
        var key=new EcosystemRegionKey(3,4);
        await store.WriteRegionAsync(new(key,Climate:StorageFixture.Map()),store.CurrentEpoch);
        var command=new ChatCommandApi(Mock.Of<ICoreAPI>()).Create("purgedb");
        var failure=TextCommandResult.Error("failed");
        command.HandleWith(_=>failure);
        int cleared=0;
        using var binding=new EcosystemMapPurgeBinding(command,store,()=>cleared++);
        Assert.Same(failure,GetHandler(command)(new TextCommandCallingArgs()));
        Assert.Equal(0,cleared);
        Assert.NotNull((await store.ReadRegionAsync(key)).Value);
    }
    #endregion

    #region Private
    /// <summary>Inspects the same command-only field used by the production adapter; test execution bypasses unrelated permission parsing.</summary>
    private static OnCommandDelegate GetHandler(IChatCommand command) =>
        (OnCommandDelegate)typeof(ChatCommandImpl).GetField("handler",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(command)!;
    #endregion
}
