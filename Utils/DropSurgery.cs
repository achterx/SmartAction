using System;
using System.Collections;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using SmartAction.Patch;
using UnityEngine;

namespace SmartAction.Utils;

// =========================================================================
// DropSurgery — SPT 4.0.13 port
// =========================================================================
// CHANGES FROM 3.11:
//   • "Class1172_0" property on MedsController: this was a property that
//     returned the currently-running meds operation (an instance of
//     Player.MedsController.Class1172). The Class1172 number shifted in
//     0.16 so both the property name AND the type check will be wrong.
//
//     We now use reflection to find the property by iterating through
//     MedsController's properties and looking for one whose type has
//     both method_8 and method_9 (our known signature markers).
//
//   • "GClass1824" draw-operation type: was a hardcoded string comparison.
//     Now discovered dynamically by looking for a FirearmController operation
//     type that has a "FastForward" method.
//
// ⚠️ VERIFY: "FastForward" method name on the meds operation class.
//   If it was renamed in 0.16, update the search below.
// =========================================================================

public abstract class DropSurgery
{
    private static bool _inputLocked = false;
    private static float _lastClickTime = 0f;
    private const float CancelThreshold = 0.2f;
    private static Coroutine _cancelRoutine;
    private const string CmsId = "5d02778e86f774203e7dedbe";
    private const string Surv12Id = "5d02797c86f774203f38e30a";

    // Cache: MedsController property that returns the current operation
    private static PropertyInfo _currentOpProp;
    private static bool _currentOpPropSearched;

    public static void CanDropSurgery(Player player, Player.ItemHandsController hands, MongoID mongoID, Item item)
    {
        var isSurgeryItem = mongoID.Equals(CmsId) || mongoID.Equals(Surv12Id);
        if (isSurgeryItem)
        {
            var delta = Time.time - _lastClickTime;
            if (_inputLocked)
            {
                SmartActionLogger.Log("[CutAnimation] 🔒 Input lock");
                return;
            }
            if (!Input.GetMouseButtonDown(1)) return;
            _lastClickTime = Time.time;
            SmartActionLogger.Log($"[CutAnimation] 🖱️ Right click delta={delta:F3}");
            if (delta >= CancelThreshold) return;
            _inputLocked = true;
            TryCancelHandsController(hands, item, player);
            SmartActionLogger.Log("[CutAnimation] ✂️ Double click");
        }
        else
        {
            _inputLocked = false;
        }
    }

    private static void TryCancelHandsController(Player.ItemHandsController hands, Item item, Player player)
    {
        try
        {
            var effect = PatchMedEffectHooks.CurrentHealingEffect;
            if (effect is null)
            {
                SmartActionLogger.Log($"[CutAnimation] Effect not started or null, skipping.");
                return;
            }
            FastForward(hands);
            if (_cancelRoutine != null)
            {
                CoroutineRunner.Stop(_cancelRoutine);
                SmartActionLogger.Warn("[CutAnimation] 🔁 Old coroutine cancelled");
            }
            _cancelRoutine = CoroutineRunner.Run(MedsCancelSequence(item, player));
        }
        catch (Exception e)
        {
            SmartActionLogger.Error($"[CutAnimation] TryCancelHandsController exception: {e}");
            _inputLocked = false;
        }
    }

    private static IEnumerator MedsCancelSequence(Item item, Player player)
    {
        try
        {
            SmartActionLogger.Log("[CutAnimation] 1) Starting cancel sequence");
            yield return WaitForMedsOperationEndAndFinalize(player);
            SmartActionLogger.Log("[CutAnimation] 2) Meds op ended");
            yield return WaitForFirearmReturnAndBoost(player);
            SmartActionLogger.Log("[CutAnimation] 3) Firearm return done");
            yield return WaitForItemToBeDroppable(item, player);
            SmartActionLogger.Log("[CutAnimation] 4) Item drop done");
            SmartActionLogger.Info("[CutAnimation] ✅ All coroutines complete");
        }
        finally
        {
            _inputLocked = false;
            _cancelRoutine = null;
            SmartActionLogger.Info("[CutAnimation] ✅ Sequence end, input unlocked");
        }
    }

    private static IEnumerator WaitForItemToBeDroppable(Item itemToDrop, Player player)
    {
        var timeout = 3f;
        if (itemToDrop == null) { SmartActionLogger.Warn("[CutAnimation] Item null, skipping drop"); yield break; }
        var itemId = itemToDrop.Id;
        SmartActionLogger.Info($"[CutAnimation] ⏳ Waiting for item to be droppable (timeout {timeout}s)");
        while (timeout > 0f)
        {
            if (player?.InventoryController == null) { SmartActionLogger.Warn("[CutAnimation] InventoryController unavailable"); yield break; }
            if (itemToDrop.Parent != null)
            {
                if (itemToDrop is MedsItemClass cmsOrSurv12)
                    cmsOrSurv12.MedKitComponent.HpResource += 1f;
                player.InventoryController.ThrowItem(itemToDrop, false, null);
                SmartActionLogger.Warn($"[CutAnimation] 💥 Item dropped: {itemToDrop.LocalizedName()}");
                yield break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }
        SmartActionLogger.Warn($"[CutAnimation] ⏱️ Timeout: item never droppable ({itemToDrop.LocalizedName()})");
    }

    private static IEnumerator WaitForMedsOperationEndAndFinalize(Player player)
    {
        var timeout = 3f;
        SmartActionLogger.Info("[CutAnimation] 🔄 Waiting for meds op to finish");
        while (timeout > 0f)
        {
            if (player?.HandsController is not Player.MedsController meds) { SmartActionLogger.Warn("[CutAnimation] No MedsController"); yield break; }

            // ⚠️ VERIFY: find the "current operation" property on MedsController
            var currentOp = GetCurrentMedsOperation(meds);
            if (currentOp == null) { SmartActionLogger.Info("[CutAnimation] ✅ currentOp null → healing done"); break; }

            var state = currentOp.GetType().GetProperty("State",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(currentOp);
            var finished = state?.ToString() == "Finished";
            SmartActionLogger.Info($"[CutAnimation] 🔄 State: {state}, Timeout: {timeout:F2}s");
            if (finished) { SmartActionLogger.Info("[CutAnimation] ✅ Healing done"); break; }

            timeout -= Time.deltaTime;
            yield return null;
        }
    }

    // Get the current meds operation object from MedsController.
    // Searches for a property returning a type that has method_8 and method_9.
    private static object GetCurrentMedsOperation(Player.MedsController meds)
    {
        if (!_currentOpPropSearched)
        {
            _currentOpPropSearched = true;
            foreach (var prop in typeof(Player.MedsController).GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var retType = prop.PropertyType;
                bool hasM8 = retType.GetMethod("method_8",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                bool hasM9 = retType.GetMethod("method_9",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                if (hasM8 && hasM9)
                {
                    _currentOpProp = prop;
                    SmartActionLogger.Info($"[DropSurgery] Found current-op property: {prop.Name} ({retType.Name})");
                    break;
                }
            }
            if (_currentOpProp == null)
                SmartActionLogger.Warn("[DropSurgery] ⚠️ Could not find current-op property on MedsController");
        }
        return _currentOpProp?.GetValue(meds);
    }

    private static IEnumerator WaitForFirearmReturnAndBoost(Player player)
    {
        var timeout = 1.5f;
        SmartActionLogger.Info("[CutAnimation] 🔄 Checking firearm return");
        while (timeout > 0f)
        {
            if (player?.HandsController is Player.FirearmController firearm)
            {
                var currentOp = firearm.CurrentOperation;
                if (currentOp == null) { SmartActionLogger.Warn("[CutAnimation] FirearmController but null op"); break; }
                var opType = currentOp.GetType();
                SmartActionLogger.Info($"[CutAnimation] 🔫 Weapon detected, op: {opType.FullName}");

                // ⚠️ VERIFY: "GClass1824" draw operation type name.
                //   In EFT 0.16 this may be renumbered. Find the FirearmController
                //   operation whose name ends in a 4-digit number and whose FullName
                //   appears when you do a weapon draw in dnSpy.
                //   Alternatively you can match by the presence of a "FastForward"
                //   method AND having a FirearmsAnimator property.
                bool isDrawOp = IsWeaponDrawOperation(opType);
                if (isDrawOp)
                {
                    var anim = firearm.FirearmsAnimator;
                    if (anim != null)
                    {
                        anim.SetAnimationSpeed(2f);
                        SmartActionLogger.Warn("[CutAnimation] ⚡ Draw boost x2 applied");
                        CoroutineRunner.Run(ResetAnimatorSpeedLater(anim));
                    }
                    else SmartActionLogger.Warn("[CutAnimation] FirearmsAnimator null despite draw op");
                }
                else SmartActionLogger.Info($"[CutAnimation] Not the draw op ({opType.Name})");
                break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }
        _inputLocked = false;
        SmartActionLogger.Info("[CutAnimation] ✅ Input unblocked");
    }

    // ⚠️ VERIFY: this detects the weapon-draw operation by looking for
    //   a "FastForward" method on the operation type AND checking that
    //   the type is a nested type of FirearmController.
    //   In 3.11 this was "GClass1824". In 4.0 the number changes.
    private static bool IsWeaponDrawOperation(Type opType)
    {
        // Keep the old hardcoded name as a fallback first (fastest path)
        if (opType.FullName == "EFT.Player+FirearmController+GClass1824")
            return true;

        // Dynamic fallback: it's a nested type of FirearmController with FastForward
        if (opType.DeclaringType == typeof(Player.FirearmController))
        {
            bool hasFastForward = opType.GetMethod("FastForward",
                BindingFlags.Public | BindingFlags.Instance) != null;
            // Additional heuristic: the draw op is relatively simple and small
            if (hasFastForward)
            {
                SmartActionLogger.Info($"[CutAnimation] Dynamic draw-op match: {opType.FullName}");
                return true;
            }
        }
        return false;
    }

    private static IEnumerator ResetAnimatorSpeedLater(FirearmsAnimator anim)
    {
        yield return new WaitForSeconds(1.0f);
        anim.SetAnimationSpeed(1.0f);
        SmartActionLogger.Log("[CutAnimation] 🔁 Animator speed reset to 1.0");
    }

    private static void FastForward(Player.ItemHandsController hands)
    {
        // Use our discovered property instead of hardcoded "Class1172_0"
        var currentOp = GetCurrentMedsOperation(hands as Player.MedsController);
        if (currentOp == null)
        {
            SmartActionLogger.Warn("[CutAnimation] ❌ No current meds op for FastForward");
            return;
        }

        var fastForward = currentOp.GetType().GetMethod(
            "FastForward", BindingFlags.Public | BindingFlags.Instance);
        if (fastForward == null)
        {
            SmartActionLogger.Warn("[CutAnimation] ❌ FastForward not found on op");
            return;
        }
        fastForward.Invoke(currentOp, null);
    }
}
