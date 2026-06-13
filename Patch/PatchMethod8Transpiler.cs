using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using SmartAction.Utils;

namespace SmartAction.Patch
{
    // =========================================================================
    // PatchMethod8Transpiler — SPT 4.0.13 port
    // =========================================================================
    // CHANGE FROM 3.11:
    //   Same as PatchEndHealingCycle — we can't use [HarmonyPatch] attribute
    //   directly on Player.MedsController.Class1172 because Class1172 is
    //   renumbered in 0.16. We patch method_8 manually via Apply().
    //
    //   The transpiler logic itself is unchanged — it finds the SetActiveParam
    //   callsite and redirects it through SetActiveParamInterceptor.
    //
    // ⚠️ VERIFY: "medsController_0" field and "firearmsAnimator_0" field
    //   names in the IL pattern match. If the transpiler reports 0 matches,
    //   open the class in dnSpy and check the IL of method_8 to find
    //   which field names are used in the guard block before SetActiveParam.
    // =========================================================================

    public static class PatchMethod8
    {
        public static void Apply(Harmony harmony, Type medsOperationClassType)
        {
            if (medsOperationClassType == null)
            {
                SmartActionLogger.Error("[PatchMethod8] Skipping — class not found.");
                return;
            }

            // ⚠️ VERIFY: "method_8" name
            var method8 = medsOperationClassType.GetMethod("method_8",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method8 == null)
            {
                SmartActionLogger.Error("[PatchMethod8] ❌ method_8 not found.");
                return;
            }

            var transpiler = new HarmonyMethod(typeof(PatchMethod8).GetMethod(
                nameof(Transpiler), BindingFlags.Static | BindingFlags.Public));
            harmony.Patch(method8, transpiler: transpiler);
            SmartActionLogger.Info("[PatchMethod8] ✅ Patched method_8 transpiler.");
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = instructions.ToList();
            var interceptorMethod = AccessTools.Method(typeof(SetActiveParamInterceptor), "SetBlockState");
            int patchCount = 0;

            for (var i = 0; i < code.Count - 4; i++)
            {
                // Pattern: ldarg.0 / ldfld medsController_0 / ldfld firearmsAnimator_0 / brfalse
                // ⚠️ VERIFY: field names "medsController_0" and "firearmsAnimator_0"
                if (code[i].opcode != OpCodes.Ldarg_0 ||
                    code[i + 1].opcode != OpCodes.Ldfld || code[i + 1].operand is not FieldInfo field1 ||
                    field1.Name != "medsController_0" ||
                    code[i + 2].opcode != OpCodes.Ldfld || code[i + 2].operand is not FieldInfo field2 ||
                    field2.Name != "firearmsAnimator_0" ||
                    code[i + 3].opcode != OpCodes.Brfalse)
                    continue;

                for (var j = i + 4; j < code.Count; j++)
                {
                    if ((code[j].opcode != OpCodes.Callvirt && code[j].opcode != OpCodes.Call) ||
                        code[j].operand is not MethodInfo methodInfo ||
                        methodInfo.Name != "SetActiveParam" ||
                        methodInfo.DeclaringType != typeof(ObjectInHandsAnimator))
                        continue;

                    SmartActionLogger.Log($"[PatchMethod8] ✅ Found SetActiveParam at IL index {j}");
                    code[j].operand = interceptorMethod;
                    patchCount++;
                    break;
                }
            }

            if (patchCount == 0)
                SmartActionLogger.Warn("[PatchMethod8] ⚠️ SetActiveParam callsite NOT found in method_8 IL — verify field names");
            else
                SmartActionLogger.Log($"[PatchMethod8] Patched {patchCount} callsite(s).");

            return code.AsEnumerable();
        }
    }

    public static class SetActiveParamInterceptor
    {
        public static bool BlockSetActiveParam = false;

        public static void SetBlockState(ObjectInHandsAnimator animator, bool active, bool resetLeftHand)
        {
            SmartActionLogger.Log($"[Interceptor] SetActiveParam(active={active}, resetLeftHand={resetLeftHand})");
            if (BlockSetActiveParam)
            {
                SmartActionLogger.Log("[Interceptor] 🚫 Blocked SetActiveParam.");
                return;
            }
            animator.SetActiveParam(active, resetLeftHand);
        }
    }
}
