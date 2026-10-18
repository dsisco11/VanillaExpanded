using System.Text;
using Moq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using VariantMap = Vintagestory.API.Datastructures.OrderedDictionary<string, string>;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Supplies client-shaped contexts and local origin assets without server catalogue messages.</summary>
internal static class CatalogueFixture
{
    #region Public API
    /// <summary>Creates a loaded game asset from test-owned text.</summary>
    internal static IAsset Asset(string path, string json, string domain = "game") =>
        new Asset(Encoding.UTF8.GetBytes(json), new AssetLocation(domain, path), null!);

    /// <summary>Creates the code and variant data normally received in entity identity packets, with no server properties.</summary>
    internal static EntityProperties Entity(string code, params (string Key, string Value)[] variants)
    {
        var entity = new EntityProperties { Code = new AssetLocation(code) };
        foreach (var (key, value) in variants) entity.Variant[key] = value;
        return entity;
    }

    /// <summary>Provides a local origin whose asset category access also represents archive-backed content.</summary>
    internal static IAssetOrigin Origin(IEnumerable<IAsset> assets, bool gameplay = true)
    {
        IAsset[] content = assets.ToArray();
        var origin = new Mock<IAssetOrigin>();
        origin.Setup(value => value.IsAllowedToAffectGameplay()).Returns(gameplay);
        origin.Setup(value => value.GetAssets(It.IsAny<AssetCategory>(), true))
            .Returns((AssetCategory category, bool _) => content.Where(asset => asset.Location.Category == category).ToList());
        return origin.Object;
    }

    /// <summary>Builds only the verified client context getters; all other API operations are strict mock failures.</summary>
    internal static ICoreAPI Api(IEnumerable<IAssetOrigin> origins, IEnumerable<EntityProperties> entities, TreeAttribute? config = null)
    {
        var assets = new Mock<IAssetManager>(MockBehavior.Strict);
        assets.SetupGet(value => value.Origins).Returns(origins.ToList());
        var world = new Mock<IWorldAccessor>(MockBehavior.Strict);
        world.SetupGet(value => value.Config).Returns(config ?? new TreeAttribute());
        world.SetupGet(value => value.EntityTypes).Returns(entities.ToList());
        var logger = new Mock<ILogger>();
        world.SetupGet(value => value.Logger).Returns(logger.Object);
        var loader = new Mock<IModLoader>();
        loader.SetupGet(value => value.Mods).Returns(Array.Empty<Mod>());
        var api = new Mock<ICoreAPI>(MockBehavior.Strict);
        api.SetupGet(value => value.Assets).Returns(assets.Object);
        api.SetupGet(value => value.World).Returns(world.Object);
        api.SetupGet(value => value.Logger).Returns(logger.Object);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        api.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        return api.Object;
    }

    /// <summary>Creates entity identity fixtures from installed simple state lists, solely to exercise the complete supported inventory.</summary>
    internal static IEnumerable<EntityProperties> InstalledIdentities(IAsset asset)
    {
        JObject definition = JObject.Parse(asset.ToText());
        var variants = new List<VariantMap> { new() };
        if (definition["variantgroups"] is JArray groups)
        {
            foreach (JObject group in groups)
            {
                string key = group.Value<string>("code")!;
                variants = variants.SelectMany(current => group["states"]!.Values<string>().Select(state =>
                {
                    var expanded = new VariantMap(current) { [key] = state! };
                    return expanded;
                })).ToList();
            }
        }
        foreach (var variant in variants)
        {
            string code = definition.Value<string>("code")! + (variant.Count > 0 ? "-" + string.Join("-", variant.Values) : "");
            yield return new EntityProperties { Code = new AssetLocation("game", code), Variant = variant };
        }
    }
    #endregion
}
