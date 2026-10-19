using System;
using VanillaExpanded.EcosystemMap.Presentation;
using VanillaExpanded.EcosystemMap.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap;

/// <summary>Routes ecosystem data and native full-map callbacks to their feature-owned lifetimes.</summary>
public sealed class EcosystemMapLayer : MapLayer
{
    public override string Title => Lang.Get("maplayer-vanillaexpandedecosystem");
    public override string LayerGroupCode => "vanillaexpandedecosystem";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Server;
    public override bool RequireChunkLoaded => false;
    private EcosystemMapSystem? owner;
    private EcosystemMapClient? client;

    #region Public API
    #region Construction and lifecycle
    /// <summary>Creates an initially inactive layer; environmental capture does not depend on tab activation.</summary>
    public EcosystemMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink) { Active = false; }

    /// <summary>Starts acquisition after vanilla's purge command exists, then attaches client-only presentation ownership.</summary>
    public override void OnLoaded()
    {
        owner = api.ModLoader.GetModSystem<EcosystemMapSystem>();
        owner.LoadLayer(this);
        if (api is ICoreClientAPI capi && owner.Client is { } session)
        {
            client = new(capi, session, LayerGroupCode, EcosystemActiveRule.Select);
            owner.Stopping += StopPresentation;
        }
    }
    /// <summary>Releases disposable map work on close, retaining catalogue selection and persistent acquisition.</summary>
    public override void OnMapClosedClient() => client?.Close();
    /// <summary>Stops presentation before joining this feature's persistent writes.</summary>
    public override void OnShutDown() => owner?.StopWorld();
    /// <summary>Provides an idempotent map-manager disposal fallback.</summary>
    public override void Dispose() => owner?.StopWorld();
    #endregion

    #region Map composition, rendering and transport
    /// <summary>Composes the species panel using the native full-map extra-composer callback.</summary>
    public override void ComposeDialogExtras(GuiDialogWorldMap dialog, GuiComposer composer)
    {
        try { client?.Compose(dialog, composer); }
        catch (Exception error) { api.Logger.Error("Ecosystem map controls unavailable: {0}", error); }
    }
    /// <summary>Routes map drawing through exact full-map eligibility and explored-coverage checks.</summary>
    public override void Render(GuiElementMap mapElem, float dt) => client?.Render(mapElem, Active);
    /// <summary>Accepts only server-originated payloads on the existing registered layer route.</summary>
    public override void OnDataFromServer(byte[] data) => owner?.Client?.Receive(data);
    #endregion
    #endregion

    #region Private
    /// <summary>Detaches presentation before session workers/storage close, regardless of leave-world subscriber order.</summary>
    private void StopPresentation()
    {
        if (owner != null) owner.Stopping -= StopPresentation;
        try { client?.Dispose(); }
        finally { client = null; }
    }
    #endregion
}
