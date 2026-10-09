using System;
using System.Collections.Generic;

using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Owns shared program handles and failed attempts keyed by domain, basename, and ABI.</summary>
internal sealed class ItemSlotIndicatorProgramCache(IItemSlotIndicatorResourceBackend backend) : IDisposable
{
    private readonly Dictionary<(string Domain, string Name, int Abi), (IShaderProgram? Program, string? Error)> entries = [];

    #region Public API
    /// <summary>Prepares a program once per attempt, sharing both success and failure between appearance variants.</summary>
    internal string? Prepare(ItemSlotIndicatorEffectDefinition definition)
    {
        var key = (definition.ShaderAssetDomain, definition.ShaderName, definition.AbiVersion);
        if (entries.TryGetValue(key, out var existing)) return existing.Error;
        IShaderProgram? program = null;
        try
        {
            // Keep the handle before stage configuration/registration so every partial creation can be released.
            program = backend.CreateProgram();
            backend.RegisterProgram(definition, program);
            if (!program.Compile()) throw new InvalidOperationException("Engine shader compilation failed.");
            backend.ValidateProgram(program);
            entries.Add(key, (program, null));
            return null;
        }
        catch (Exception exception)
        {
            if (program is not null && !program.Disposed) program.Dispose();
            entries.Add(key, (null, exception.Message));
            return exception.Message;
        }
    }

    /// <summary>Returns only an already prepared live program; lookup never retries compilation.</summary>
    internal IShaderProgram? Get(ItemSlotIndicatorEffectDefinition definition) =>
        entries.TryGetValue((definition.ShaderAssetDomain, definition.ShaderName, definition.AbiVersion), out var entry)
            && entry.Program is { Disposed: false } ? entry.Program : null;

    /// <summary>Invalidates every handle before replacement, tolerating disposal already performed by engine reload.</summary>
    public void Dispose()
    {
        var previous = new List<(IShaderProgram? Program, string? Error)>(entries.Values);
        entries.Clear();
        foreach (var entry in previous)
            if (entry.Program is { Disposed: false }) entry.Program.Dispose();
    }
    #endregion
}
