using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Bridges the engine's missing pre-link callback only while compiling a liquid feedback program.</summary>
[HarmonyPatch]
internal static class LiquidSloshTransformFeedbackLink
{
    private const string PatchId = Constants.ModId + ".liquid-feedback-link";
    private static readonly object compileLock = new();

    #region Public API
    /// <summary>Temporarily patches the active platform's linker while leaving compilation and diagnostics to the engine.</summary>
    internal static bool Compile(LiquidSloshSimulationShaderProgram program, Func<bool> engineCompile)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(engineCompile);
        // Compile is graphics-thread work. Serialize our patch lifetime and remove only this patch owner,
        // preserving unrelated mods' patches and avoiding a permanent global linker modification.
        lock (compileLock)
        {
            var harmony = new Harmony(PatchId);
            try
            {
                new PatchClassProcessor(harmony, typeof(LiquidSloshTransformFeedbackLink)).Patch();
                return engineCompile();
            }
            finally { harmony.UnpatchAll(PatchId); }
        }
    }

    /// <summary>Inserts a gated callback with the program ID already on the stack before exactly one engine link call.</summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> InsertConfiguration(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        int[] links = body.Select((instruction, index) => (instruction, index))
            .Where(entry => IsLinkCall(entry.instruction)).Select(entry => entry.index).ToArray();
        if (links.Length != 1)
            throw new InvalidOperationException("The engine shader linker no longer contains exactly one supported LinkProgram call.");
        int index = links[0];
        var duplicate = new CodeInstruction(OpCodes.Dup);
        // Branches and exception boundaries targeting the original link must also run configuration.
        duplicate.labels.AddRange(body[index].labels);
        duplicate.blocks.AddRange(body[index].blocks);
        body[index].labels.Clear();
        body[index].blocks.Clear();
        body.InsertRange(index,
        [
            duplicate,
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LiquidSloshTransformFeedbackLink), nameof(Configure)))
        ]);
        return body;
    }
    #endregion

    #region Private
    /// <summary>Selects the current client platform's concrete shader linker when the scoped patch is applied.</summary>
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var platform = ScreenManager.Platform ?? throw new InvalidOperationException("The client graphics platform is unavailable.");
        return AccessTools.Method(platform.GetType(), "CreateShaderProgram", [typeof(ShaderProgram)])
            ?? throw new MissingMethodException(platform.GetType().FullName, "CreateShaderProgram");
    }

    /// <summary>Recognizes the installed engine's OpenTK int program linker without relying on one GL namespace variant.</summary>
    private static bool IsLinkCall(CodeInstruction instruction) =>
        instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
        && method.Name == "LinkProgram" && method.DeclaringType?.FullName is { } type
        && type.StartsWith("OpenTK.Graphics.OpenGL", StringComparison.Ordinal)
        && method.ReturnType == typeof(void)
        && method.GetParameters() is [{ ParameterType: var argument }] && argument == typeof(int);

    /// <summary>Applies feedback declarations exclusively to the typed liquid solver, leaving ordinary shaders untouched.</summary>
    private static void Configure(int programId, ShaderProgram program)
    {
        if (program is LiquidSloshSimulationShaderProgram liquid) liquid.ConfigureFeedback(programId);
    }
    #endregion
}
