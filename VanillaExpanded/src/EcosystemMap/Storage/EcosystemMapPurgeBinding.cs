using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Coordinates ecosystem cache clearing with the existing map command without opening or patching vanilla database storage.</summary>
internal sealed class EcosystemMapPurgeBinding : IDisposable
{
    private static readonly FieldInfo Handler = FindHandler();
    private readonly IChatCommand command;
    private readonly OnCommandDelegate previous;
    private readonly OnCommandDelegate replacement;
    private bool disposed;

    #region Public API
    /// <summary>Attaches after vanilla's terrain layer creates the purge command; no layer registration or database access is performed.</summary>
    internal static EcosystemMapPurgeBinding Attach(ICoreClientAPI api, EnvironmentalStorage storage, Action clearDerived)
    {
        IChatCommand? map = api.ChatCommands.Get("map");
        if (map == null || !map.AllSubcommands.TryGetValue("purgedb", out var purge))
            throw new InvalidOperationException("Vanilla's map purge command is not initialized.");
        return new EcosystemMapPurgeBinding(purge, storage, clearDerived);
    }

    /// <summary>Wraps only the existing command, preserving arguments/result and invalidating derived state only after successful purge.</summary>
    internal EcosystemMapPurgeBinding(IChatCommand command, EnvironmentalStorage storage, Action clearDerived)
    {
        ArgumentNullException.ThrowIfNull(clearDerived);
        this.command = command;
        previous = Handler.GetValue(command) as OnCommandDelegate
            ?? throw new NotSupportedException("Vanilla's map purge handler is unavailable.");
        replacement = args =>
        {
            TextCommandResult result = previous(args);
            if (result.Status == EnumCommandStatus.Success)
            {
                // Disk work runs on the storage worker; explicit purge waits for the clear before it reports success.
                try { storage.PurgeAsync().GetAwaiter().GetResult(); }
                finally { clearDerived(); }
            }
            return result;
        };
        command.HandleWith(replacement);
    }

    /// <summary>Restores the original handler only while this binding still owns the command.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(Handler.GetValue(command), replacement)) command.HandleWith(previous);
    }
    #endregion

    #region Private
    /// <summary>Checks the narrow command boundary instead of reaching into ChunkMapLayer or MapDB.</summary>
    private static FieldInfo FindHandler()
    {
        var field = typeof(ChatCommandImpl).GetField("handler", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.FieldType != typeof(OnCommandDelegate))
            throw new NotSupportedException("The installed game's map command handler boundary is incompatible.");
        return field;
    }
    #endregion
}
