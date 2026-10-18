using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Anchoring;

namespace VanillaExpanded.HudOverlays.Persistence;

/// <summary>One authoritative, versioned client layout including origin checkpoints and retired mirror keys.</summary>
internal sealed record HudLayoutSnapshot
{
    [JsonRequired]
    public int Version { get; init; } = 2;
    public long Revision { get; init; }
    [JsonRequired]
    public bool Initialized { get; init; } = true;
    [JsonRequired]
    public HudLayoutContainer[] Containers { get; init; } = Array.Empty<HudLayoutContainer>();
    public string[] RetiredKeys { get; init; } = Array.Empty<string>();

    #region Public API
    /// <summary>Rejects inconsistent ownership and bounded tree shapes before any state is exposed or written.</summary>
    public void Validate()
    {
        if (Version is not (1 or 2) || !Initialized || Revision < 0 || Containers == null || RetiredKeys == null || Containers.Length > 4096)
            throw new InvalidOperationException("Unsupported or invalid HUD layout snapshot.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in Containers)
        {
            if (container == null || string.IsNullOrWhiteSpace(container.Id) || !ids.Add(container.Id)
                || !OwnedKey(container.Key) || !keys.Add(container.Key) || !double.IsFinite(container.Padding) || container.Padding < 0
                || container.Tree == null || (container.X.HasValue != container.Y.HasValue)
                || container.TemplateId != null && string.IsNullOrWhiteSpace(container.TemplateId)
                || (container.TemplateId == null && !container.X.HasValue) || !MatchesIdentity(container))
                throw new InvalidOperationException("Invalid HUD container.");
            if (container.Attachment is { } attachment && (Version < 2 || !container.X.HasValue
                || !HudContainerAttachment.IsSupported(attachment)))
                throw new InvalidOperationException("Invalid HUD attachment.");
            ValidateTree(container.Tree, leaves, new HashSet<HudLayoutNode>(ReferenceEqualityComparer.Instance), 0);
        }
        if (RetiredKeys.Any(key => !OwnedKey(key))) throw new InvalidOperationException("Invalid HUD mirror ownership.");
    }

    /// <summary>Recognizes only supported VE mirror namespaces, never arbitrary native dialog settings.</summary>
    public static bool OwnedKey(string key) => key != null && new[] { "group:", "custom:", "indicator:", "default-remainder:" }
        .Any(prefix => key.StartsWith("vanillaexpanded:hud:" + prefix, StringComparison.Ordinal)
            && key.Length > "vanillaexpanded:hud:".Length + prefix.Length);
    #endregion

    #region Private
    /// <summary>Checks that a validated record can only repair the native mirror derived from its stable identity.</summary>
    private static bool MatchesIdentity(HudLayoutContainer container)
    {
        const string prefix = "vanillaexpanded:hud:";
        if (container.Key == prefix + "group:" + container.Id || container.Key == prefix + "custom:" + container.Id) return true;
        if (container.Id.StartsWith("default-remainder:", StringComparison.Ordinal))
            return container.Key == prefix + container.Id;
        return container.Tree.Indicator is string indicator && container.Id == "independent:" + indicator
            && container.Key == prefix + "indicator:" + indicator;
    }

    /// <summary>Validates structural leaves independent of current integrations or visibility.</summary>
    private static void ValidateTree(HudLayoutNode node, HashSet<string> leaves, HashSet<HudLayoutNode> visited, int depth)
    {
        if (depth > 128 || !visited.Add(node) || !Enum.IsDefined(node.Direction) || !Enum.IsDefined(node.Alignment)
            || !double.IsFinite(node.Gap) || node.Gap < 0) throw new InvalidOperationException("Invalid HUD tree.");
        if (node.Indicator != null)
        {
            if (string.IsNullOrWhiteSpace(node.Indicator) || node.First != null || node.Second != null || !leaves.Add(node.Indicator))
                throw new InvalidOperationException("Duplicate or invalid HUD member.");
        }
        else
        {
            if (node.First == null || node.Second == null) throw new InvalidOperationException("Incomplete HUD pair.");
            ValidateTree(node.First, leaves, visited, depth + 1);
            ValidateTree(node.Second, leaves, visited, depth + 1);
        }
    }
    #endregion
}
