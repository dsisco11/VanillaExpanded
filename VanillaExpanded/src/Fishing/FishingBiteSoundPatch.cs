using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.GameContent;

namespace VanillaExpanded.Fishing;

[HarmonyPatch(typeof(EntityBobber), "playCatchEffects")]
internal static class FishingBiteSoundPatch
{
    private const string VanillaSoundPath = "sounds/environment/mediumsplash";
    private const string CorrectedSoundPath = "survival:sounds/environment/mediumsplash";
    private const float BiteSoundVolume = 2f;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> CorrectSplashSound(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> instructionList = instructions.ToList();
        int soundPathIndex = instructionList.FindIndex(instruction =>
            instruction.opcode == OpCodes.Ldstr && instruction.operand as string == VanillaSoundPath);

        if (soundPathIndex < 0)
        {
            return instructionList;
        }

        instructionList[soundPathIndex].operand = CorrectedSoundPath;

        int playSoundIndex = instructionList.FindIndex(soundPathIndex, instruction =>
            instruction.operand is MethodInfo method && method.Name == "PlaySoundAt");

        for (int index = playSoundIndex - 1; index > soundPathIndex; index--)
        {
            if (instructionList[index].opcode != OpCodes.Ldc_R4)
            {
                continue;
            }

            instructionList[index].operand = BiteSoundVolume;
            break;
        }

        return instructionList;
    }
}