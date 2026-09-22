using System;
using System.Collections.Generic;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VanillaExpanded.ModSystems;

internal sealed class AlloyDepositSystem : ModSystem
{
    private const int MaxCachedResponses = 128;

    private readonly Dictionary<string, Packet_AlloyDepositResult> responseCache = [];
    private readonly Queue<string> responseOrder = [];
    private ICoreClientAPI? clientApi;
    private ICoreServerAPI? serverApi;
    private IClientNetworkChannel? clientChannel;
    private IServerNetworkChannel? serverChannel;

    internal event Action<Packet_AlloyDepositResult>? DepositCompleted;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return VanillaExpandedModSystem.Config.EnableAlloyCalculator;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        clientChannel = api.Network.GetChannel(Constants.ModId);
        clientChannel.SetMessageHandler<Packet_AlloyDepositResult>(OnDepositResult);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        serverChannel = api.Network.GetChannel(Constants.ModId);
        serverChannel.SetMessageHandler<Packet_RequestAlloyDeposit>(OnDepositRequest);
    }

    public override void Dispose()
    {
        DepositCompleted = null;
        responseCache.Clear();
        responseOrder.Clear();
        clientApi = null;
        serverApi = null;
        clientChannel = null;
        serverChannel = null;
        base.Dispose();
    }

    internal bool RequestDeposit(Packet_RequestAlloyDeposit request)
    {
        if (clientChannel is null)
        {
            clientApi?.Logger.Error("Cannot send alloy deposit request: Network channel is null.");
            return false;
        }

        clientChannel.SendPacket(request);
        return true;
    }

    private void OnDepositResult(Packet_AlloyDepositResult result)
    {
        DepositCompleted?.Invoke(result);
    }

    private void OnDepositRequest(IServerPlayer player, Packet_RequestAlloyDeposit request)
    {
        if (serverApi is null || serverChannel is null) return;

        string cacheKey = $"{player.PlayerUID}:{request.RequestId}";
        if (responseCache.TryGetValue(cacheKey, out Packet_AlloyDepositResult? cachedResponse))
        {
            serverChannel.SendPacket(cachedResponse, player);
            return;
        }

        AlloyDepositResultCode resultCode = ValidateAndExecute(player, request);
        var response = new Packet_AlloyDepositResult
        {
            RequestId = request.RequestId,
            ResultCode = resultCode
        };

        CacheResponse(cacheKey, response);
        serverChannel.SendPacket(response, player);
    }

    private AlloyDepositResultCode ValidateAndExecute(IServerPlayer player, Packet_RequestAlloyDeposit request)
    {
        if (serverApi is null
            || request.Position is null
            || string.IsNullOrWhiteSpace(request.RequestId)
            || string.IsNullOrWhiteSpace(request.AlloyCode))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

#pragma warning disable CS0618 // Vintage Story marks this for a planned 1.23 signature change.
        var permissions = new BlockEntity.CachedAccessPerms(serverApi.World, request.Position, player);
#pragma warning restore CS0618
        if (!permissions.IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, true, "alloy calculator"))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        BlockEntityFirepit? firepit = serverApi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(request.Position);
        if (firepit is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        return AlloyDepositService.Execute(serverApi.World, player.InventoryManager, firepit, request);
    }

    private void CacheResponse(string cacheKey, Packet_AlloyDepositResult response)
    {
        responseCache[cacheKey] = response;
        responseOrder.Enqueue(cacheKey);

        while (responseOrder.Count > MaxCachedResponses)
        {
            responseCache.Remove(responseOrder.Dequeue());
        }
    }
}
