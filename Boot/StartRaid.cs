using EFT;
using HarmonyLib;
using SmartAction.Utils;

namespace SmartAction.Boot;

public abstract class StartRaid
{
    [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
    public static class PatchGameWorldOnGameStarted
    {
        private static void Postfix()
        {
            SmartActionLogger.Info("[OnGameStarted] Raid started!");
            LoopTime.Initialize();

            // Apply the manual patches that depend on runtime-discovered types
            var harmony = new HarmonyLib.Harmony("com.spt.SmartAction.manual");
            var medsOpType = FindMedsOperationClass();
            SmartAction.Patch.PatchEndOfCycle.Apply(harmony);
            SmartAction.Patch.PatchMethod8.Apply(harmony, medsOpType);
        }

        private static System.Type FindMedsOperationClass()
        {
            foreach (var nested in typeof(EFT.Player.MedsController).GetNestedTypes(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                bool hasField = nested.GetField("medsController_0",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null;
                bool hasMethod8 = nested.GetMethod("method_8",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null;
                bool hasMethod9 = nested.GetMethod("method_9",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null;
                if (hasField && hasMethod8 && hasMethod9)
                    return nested;
            }
            SmartActionLogger.Error("[StartRaid] ❌ MedsOperation class not found!");
            return null;
        }
    }
}
