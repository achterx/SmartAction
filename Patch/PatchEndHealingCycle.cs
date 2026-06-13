using System;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using SmartAction.Utils;

namespace SmartAction.Patch
{
    // =========================================================================
    // PatchEndHealingCycle — SPT 4.0.13 port
    // =========================================================================
    // CHANGE FROM 3.11:
    //   Formerly hard-patched [HarmonyPatch(typeof(Player.MedsController.Class1172))]
    //   with [HarmonyPatch("method_9")].
    //
    //   In EFT 0.16 the nested class "Class1172" is renumbered. We now discover
    //   it at runtime via FindMedsOperationClass() which looks for the nested
    //   class that has both "method_8" (taking an IEffect parameter) and
    //   "method_9" (no parameters). This is much more robust.
    //
    // ⚠️ VERIFY: If init fails, open Player.MedsController in dnSpy and find
    //   the nested class that:
    //     - has a field "medsController_0" of type MedsController
    //     - has method_8(IEffect) and method_9()
    //   Update the search predicate below if the signature changed.
    // =========================================================================

    public static class PatchEndOfCycle
    {
        private static Type _medsOperationClassType;

        static PatchEndOfCycle()
        {
            try
            {
                _medsOperationClassType = FindMedsOperationClass();
                if (_medsOperationClassType == null)
                    SmartActionLogger.Error("[PatchEndOfCycle] ❌ Could not find MedsOperation class (was Class1172)");
                else
                    SmartActionLogger.Info($"[PatchEndOfCycle] ✅ Found class: {_medsOperationClassType.FullName}");
            }
            catch (Exception ex)
            {
                SmartActionLogger.Error($"[PatchEndOfCycle] ❌ Static init error: {ex.Message}");
            }
        }

        // Find the MedsController nested class that was "Class1172".
        // It has a "medsController_0" field and methods named "method_8" and "method_9".
        private static Type FindMedsOperationClass()
        {
            var medsType = typeof(Player.MedsController);
            foreach (var nested in medsType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                bool hasField = nested.GetField("medsController_0",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                bool hasMethod8 = nested.GetMethod("method_8",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                bool hasMethod9 = nested.GetMethod("method_9",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;

                if (hasField && hasMethod8 && hasMethod9)
                    return nested;
            }
            return null;
        }

        // Manual Harmony patching since we can't use [HarmonyPatch] attribute
        // on a type we don't know at compile time.
        public static void Apply(Harmony harmony)
        {
            if (_medsOperationClassType == null)
            {
                SmartActionLogger.Error("[PatchEndOfCycle] Skipping patch — class not found.");
                return;
            }

            // ⚠️ VERIFY: "method_9" — this is the end-of-healing-cycle callback.
            //   In dnSpy, find the no-parameter method on the operation class that
            //   calls back into MedsController to signal cycle end.
            var method9 = _medsOperationClassType.GetMethod("method_9",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method9 == null)
            {
                SmartActionLogger.Error("[PatchEndOfCycle] ❌ method_9 not found on class.");
                return;
            }

            var postfix = new HarmonyMethod(typeof(PatchEndOfCycle).GetMethod(
                nameof(Postfix_EndOfCycle), BindingFlags.Static | BindingFlags.Public));
            harmony.Patch(method9, postfix: postfix);
            SmartActionLogger.Info("[PatchEndOfCycle] ✅ Patched method_9.");
        }

        public static void Postfix_EndOfCycle(object __instance)
        {
            try
            {
                // ⚠️ VERIFY: "medsController_0" field name — holds the MedsController ref
                var medsControllerField = __instance.GetType().GetField(
                    "medsController_0",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var medsController = medsControllerField?.GetValue(__instance);

                // ⚠️ VERIFY: "_player" field name on MedsController
                var playerField = medsController?.GetType().GetField(
                    "_player",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var player = playerField?.GetValue(medsController) as Player;

                if (!player) return;
                if (!player.IsYourPlayer && !player.ActiveHealthController?.IsAlive == true) return;
                if (((Player.AbstractHandsController)medsController).Item is not MedsItemClass medsItem) return;
                if (medsItem != PatchDoMedEffect.LastHealingItem) return;

                SetActiveParamInterceptor.BlockSetActiveParam = false;
                SmartActionLogger.Log("[method_9] 🚀 End healing cycle! 🔓 reset SetActiveParam method_8");
            }
            catch (Exception ex)
            {
                SmartActionLogger.Error($"[PatchEndOfCycle.Postfix] ❌ {ex.Message}");
            }
        }
    }
}
