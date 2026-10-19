using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Detaches only an owned map panel from the inspected native composer collection.</summary>
internal static class MapComposerOwnership
{
    private static readonly FieldInfo entries = typeof(GuiDialog.DlgComposers)
        .GetField("dialogComposers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("Map composer ownership collection changed.");

    #region Public API
    /// <summary>Prevents the dialog from retaining/disposal-repeating a panel released by its feature owner.</summary>
    internal static void Detach(GuiDialog.DlgComposers composers, string key, GuiComposer owned)
    {
        if (entries.GetValue(composers) is not IDictionary<string, GuiComposer> values)
            throw new NotSupportedException("Map composer ownership layout changed.");
        // Pair removal leaves a replacement installed by another owner untouched.
        values.Remove(new KeyValuePair<string, GuiComposer>(key, owned));
    }
    #endregion
}
