using System;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ItemRendering;

/// <summary>Resolves dedicated collectible attributes on the client thread and bounds invalid-asset diagnostics.</summary>
internal sealed class ToolHeadPresentationResolver
{
    private const string AttributeName = "ve-radial-menu-properties";
    private readonly Action<CollectibleObject, string> diagnostic;
    private readonly ConditionalWeakTable<CollectibleObject, object> reported = new();

    #region Public API
    /// <summary>Creates a resolver whose caller supplies the client diagnostic sink and lifetime.</summary>
    public ToolHeadPresentationResolver(Action<CollectibleObject, string> diagnostic)
    {
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    /// <summary>Resolves current asset settings, selecting fallback for absent or invalid configuration.</summary>
    public ToolHeadPresentationResolution Resolve(CollectibleObject collectible)
    {
        ArgumentNullException.ThrowIfNull(collectible);
        JToken? token = collectible.Attributes?[AttributeName].Token;
        if (token is null) return new(ToolHeadPresentationStatus.Missing, null);

        // Read only the dedicated attribute; neither GUI defaults nor stack state participate.
        if (TryParse(token, out ToolHeadPresentationProperties? properties, out string reason))
            return new(ToolHeadPresentationStatus.Valid, properties);

        // Weak identity ownership bounds warnings to one per collectible during this resolver's lifetime.
        // Mark before invoking the sink so reentrant resolution cannot repeatedly emit the warning.
        if (!reported.TryGetValue(collectible, out _))
        {
            reported.Add(collectible, new object());
            diagnostic(collectible, reason);
        }
        return new(ToolHeadPresentationStatus.Invalid, null);
    }
    #endregion

    #region Private
    #region Configuration parsing
    /// <summary>Deserializes engine settings and validates that their transform is safe to render.</summary>
    private static bool TryParse(JToken token, out ToolHeadPresentationProperties? properties, out string reason)
    {
        properties = null;
        // Seed engine defaults and let typed deserialization determine accepted JSON input.
        var modelTransform = ModelTransform.ItemDefaultGui();
        // ModelTransform's deserialization callback treats an authored sentinel vector as missing.
        // Its input type has the same typed fields without that callback; seed engine defaults first.
        var authored = new ModelTransformNoDefaults
        {
            Rotation = modelTransform.Rotation,
            Translation = modelTransform.Translation,
            Origin = modelTransform.Origin,
            ScaleXYZ = modelTransform.ScaleXYZ,
            Rotate = modelTransform.Rotate
        };
        var settings = new ToolHeadPresentationInput { Transform = authored };
        try
        {
            JsonUtil.PopulateObject(settings, token, JsonSerializer.CreateDefault());
        }
        catch (Exception error) when (error is JsonException or OverflowException or InvalidCastException or FormatException)
        {
            reason = "Could not deserialize the radial presentation settings.";
            return false;
        }

        reason = "Expected a non-null presentation transform.";
        if (settings.Transform is null) return false;
        authored = settings.Transform;
        float? wedgeRotation = settings.WedgeRotationDegrees;
        modelTransform.Origin = authored.Origin;
        modelTransform.Rotate = authored.Rotate;
        modelTransform.Rotation = authored.Rotation;
        modelTransform.Translation = authored.Translation;
        modelTransform.ScaleXYZ = authored.ScaleXYZ;

        reason = "Expected finite presentation values and positive scale components.";
        FastVec3f rotation = modelTransform.Rotation, translation = modelTransform.Translation;
        FastVec3f scale = modelTransform.ScaleXYZ;
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z)
            || !float.IsFinite(translation.X) || !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z)
            || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0
            || (wedgeRotation.HasValue && !float.IsFinite(wedgeRotation.Value))) return false;
        // Finite inputs can still overflow when pivot, rotation, and scale are composed.
        // Reject the resulting engine matrix without imposing an arbitrary artwork tuning limit.
        reason = "Presentation values overflow the engine model transform.";
        foreach (float component in modelTransform.AsMatrix)
            if (!float.IsFinite(component)) return false;

        properties = new ToolHeadPresentationProperties(modelTransform, wedgeRotation);
        reason = string.Empty;
        return true;
    }

    #endregion
    #endregion
}
