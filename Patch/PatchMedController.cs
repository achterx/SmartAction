using System.Collections.Generic;
using System.Reflection;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SmartAction.Utils;

namespace SmartAction.Patch;

// =========================================================================
// PatchMedController — SPT 4.0.13 port
// =========================================================================
// This file is mostly unchanged from 3.11. The patches target the MedEffect
// nested type (by name, not GClass number) and its lifecycle methods
// (Added, Started, Residue) which are stable human-readable names.
//
// ⚠️ VERIFY these field/property names if patches stop working:
//   "float_12"          — work time field on MedEffect (obfuscated, MAY change)
//   "WorkStateTime"     — property on GClass2813/equivalent (may be renamed)
//   "activeHealthController_0" — field on MedEffect
//   "MedItem"           — property on MedEffect
//
// The GClass2813 reference in PatchMedEffectStarted is now resolved
// through the cached WorkStateTime property instead of the type name.
// =========================================================================

public static class PatchMedEffectHooks
{
    public static readonly Dictionary<(Item item, EEffectState state), float> OriginalFloat12 = new();
    public static readonly Dictionary<(Item item, EEffectState state), float> OriginalWorkTime = new();
    public static readonly Dictionary<
        IEffect,
        (EPlayerState movementState, EEffectState effectState)
    > EffectUpdateCache = new();

    public static IEffect CurrentHealingEffect;

    [HarmonyPatch]
    public class PatchMedEffectAdded
    {
        private static MethodBase TargetMethod()
        {
            var medEffectType = ReflectionUtils.GetOrCacheNestedType(typeof(ActiveHealthController), "MedEffect");
            var method = ReflectionUtils.GetOrCacheMethod(medEffectType, "Added");
            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            SmartActionLogger.Log("[MedEffect.Added] Postfix");

            var (player, medItem, isValid) = ReflectionUtils.GetMedEffectContext(__instance, "Added");
            if (!isValid) return;

            // ⚠️ VERIFY: "float_12" field name on MedEffect
            var float12Field = ReflectionUtils.GetOrCacheField(__instance.GetType(), "float_12");

            if (__instance is not IEffect effect)
            {
                SmartActionLogger.Log("[MedEffect.Added] Instance is not IEffect");
                return;
            }

            EffectUpdateCache.Remove(effect);
            EffectUpdateCache[effect] = (EPlayerState.None, EEffectState.None);
            CurrentHealingEffect = effect;
            LoopTime.RestoreOriginalLoopTime();

            var key = (medItem, effect.State);

            if (float12Field?.GetValue(__instance) is not (float workTime and > 0f and < 20f))
            {
                SmartActionLogger.Log("[MedEffect.Added] Invalid work time field");
            }
            else
            {
                OriginalFloat12[key] = workTime;
                SmartActionLogger.Log($"[MedEffect.Added] save 💾 WorkTime={workTime:F2} for {medItem.Template._name}/{effect.State}");
            }
        }

        [HarmonyPatch]
        public class PatchMedEffectStarted
        {
            private static MethodBase TargetMethod()
            {
                var medEffectType = ReflectionUtils.GetOrCacheNestedType(typeof(ActiveHealthController), "MedEffect");
                var method = ReflectionUtils.GetOrCacheMethod(medEffectType, "Started");
                return method;
            }

            [HarmonyPostfix]
            private static void Postfix(object __instance)
            {
                var (player, medItem, isValid) = ReflectionUtils.GetMedEffectContext(__instance, "Started");
                if (!isValid) return;

                if (__instance is not IEffect effect)
                {
                    SmartActionLogger.Log("[MedEffect.Started] Instance is not IEffect");
                    return;
                }

                SetActiveParamInterceptor.BlockSetActiveParam = false;
                SmartActionLogger.Log("[MedEffect.Started] 🔓 SetActiveParam method_8 free");

                EffectUpdateCache.Remove(effect);
                EffectUpdateCache[effect] = (EPlayerState.None, EEffectState.None);
                CurrentHealingEffect = effect;

                var key = (medItem, effect.State);

                // ⚠️ VERIFY: "float_12" and "WorkStateTime" names
                var float12Field = ReflectionUtils.GetOrCacheField(__instance.GetType(), "float_12");

                // WorkStateTime was on ActiveHealthController.GClass2813 in 3.11.
                // We now find that type at runtime via LoopTime's type-discovery,
                // but for a cached property lookup we use the instance type directly
                // since __instance IS a GClass2813 (or its 4.0 equivalent).
                var workStateTimeProperty = ReflectionUtils.GetOrCacheProperty(__instance.GetType(), "WorkStateTime");

                if (float12Field?.GetValue(__instance) is not (float float12 and > 0f and < 20f) ||
                    workStateTimeProperty?.GetValue(__instance) is not (float workStateTime and > 0f and < 20f))
                {
                    SmartActionLogger.Log("[MedEffect.Started] Invalid float12 or WorkStateTime");
                }
                else
                {
                    OriginalFloat12[key] = float12;
                    OriginalWorkTime[key] = workStateTime;
                    SmartActionLogger.Log(
                        $"[MedEffect.Started] save 💾 Float12={float12:F2}, WorkTime={workStateTime:F2} for {medItem.Template._name}/{effect.State}");
                }
            }

            [HarmonyPatch]
            public class PatchMedEffectResidue
            {
                private static MethodBase TargetMethod()
                {
                    var medEffectType =
                        ReflectionUtils.GetOrCacheNestedType(typeof(ActiveHealthController), "MedEffect");
                    var method = ReflectionUtils.GetOrCacheMethod(medEffectType, "Residue");
                    return method;
                }

                [HarmonyPostfix]
                private static void Postfix(object __instance)
                {
                    SmartActionLogger.Log("[MedEffect.Residue] Postfix");

                    var (player, medItem, isValid) = ReflectionUtils.GetMedEffectContext(__instance, "Residue");
                    if (!isValid) return;

                    if (medItem != PatchDoMedEffect.LastHealingItem) return;

                    SmartActionLogger.Log("[MedEffect.Residue] 🔏 stop SetActiveParam method_8");
                    SetActiveParamInterceptor.BlockSetActiveParam = true;

                    if (__instance is not IEffect effect)
                    {
                        SmartActionLogger.Log("[MedEffect.Residue] Instance is not IEffect");
                        return;
                    }

                    EffectUpdateCache.Remove(effect);
                    EffectUpdateCache[effect] = (EPlayerState.None, EEffectState.None);
                    CurrentHealingEffect = effect;
                }
            }
        }
    }
}
