using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SmartAction.Patch;

namespace SmartAction.Utils;

public abstract class ReflectionUtils
{
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo> CachedFields = new();
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo> CachedProperties = new();
    private static readonly ConcurrentDictionary<(Type, string), MethodInfo> CachedMethods = new();
    private static readonly ConcurrentDictionary<(Type ParentType, string Name), Type> CachedNestedTypes = new();

    // =========================================================================
    // SPT 4.0.13 PORT NOTES:
    //   GetMedEffectContext still looks for nested type "MedEffect" by name —
    //   this name is stable (it's a human-readable name BSG kept). If it breaks,
    //   open ActiveHealthController in dnSpy and find the nested type that has
    //   an "Added()", "Started()", "Residue()" lifecycle and a "MedItem" property.
    //
    //   Field names like "float_12", "activeHealthController_0", "Player"
    //   are obfuscated and MAY shift each patch. They are marked ⚠️ below.
    //   If GetMedEffectContext returns isValid=false, check these names first.
    // =========================================================================

    public static FieldInfo GetOrCacheField(Type type, string fieldName)
    {
        return CachedFields.GetOrAdd((type, fieldName), key =>
        {
            var (targetType, name) = key;
            var fieldInfo =
                targetType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fieldInfo == null)
                SmartActionLogger.Warn($"[Reflection] Field '{name}' not found in {targetType.FullName}");
            return fieldInfo;
        });
    }

    public static PropertyInfo GetOrCacheProperty(Type type, string propertyName)
    {
        return CachedProperties.GetOrAdd((type, propertyName), key =>
        {
            var (targetType, name) = key;
            var propertyInfo =
                targetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (propertyInfo == null)
                SmartActionLogger.Warn($"[Reflection] Property '{name}' not found in {targetType.FullName}");
            return propertyInfo;
        });
    }

    public static MethodInfo GetOrCacheMethod(Type type, string methodName)
    {
        return CachedMethods.GetOrAdd((type, methodName), key =>
        {
            var (targetType, name) = key;
            var methodInfo =
                targetType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (methodInfo == null)
                SmartActionLogger.Warn($"[Reflection] Method '{name}' not found in {targetType.FullName}");
            return methodInfo;
        });
    }

    public static Type GetOrCacheNestedType(Type parentType, string nestedTypeName)
    {
        return CachedNestedTypes.GetOrAdd((parentType, nestedTypeName), key =>
        {
            var (type, name) = key;
            var nestedType = AccessTools.Inner(type, name);
            if (nestedType == null)
                SmartActionLogger.Warn($"[Reflection] Nested type '{name}' not found in {type.FullName}");
            return nestedType;
        });
    }

    // -------------------------------------------------------------------------
    // GetMedEffectContext
    //
    // ⚠️ VERIFY these field/property names in dnSpy against your 4.0.x Assembly-CSharp:
    //   "MedItem"                  — property on MedEffect nested type
    //   "activeHealthController_0" — field on MedEffect holding the AHC reference
    //   "Player"                   — field on ActiveHealthController holding Player
    //
    // If any of these are renamed, update the strings here (and clear the caches
    // by restarting the game — they are populated lazily on first use).
    // -------------------------------------------------------------------------
    public static (Player player, Item medItem, bool isValid) GetMedEffectContext(object instance, string hookName)
    {
        var medEffectType = GetOrCacheNestedType(typeof(ActiveHealthController), "MedEffect");
        if (instance.GetType() != medEffectType)
        {
            SmartActionLogger.Log($"[MedEffect.{hookName}] ⚠️ Not MedEffect type (got {instance.GetType().Name})");
            return (null, null, false);
        }

        // ⚠️ VERIFY: "MedItem" property name on MedEffect
        var medItemProperty = GetOrCacheProperty(medEffectType, "MedItem");
        if (medItemProperty?.GetValue(instance) is not Item medItem)
        {
            SmartActionLogger.Log($"[MedEffect.{hookName}] ⚠️ No MedItem found");
            return (null, null, false);
        }

        if (PatchDoMedEffect.LastHealingItem != medItem)
        {
            SmartActionLogger.Log($"[MedEffect.{hookName}] ⚠️ Not the last healing item");
            return (null, medItem, false);
        }

        // ⚠️ VERIFY: "activeHealthController_0" field name on MedEffect
        var healthControllerField = GetOrCacheField(medEffectType, "activeHealthController_0");
        if (healthControllerField?.GetValue(instance) is not ActiveHealthController healthController)
        {
            SmartActionLogger.Log($"[MedEffect.{hookName}] ⚠️ Unable to get ActiveHealthController");
            return (null, medItem, false);
        }

        // ⚠️ VERIFY: "Player" field name on ActiveHealthController
        var playerField = GetOrCacheField(typeof(ActiveHealthController), "Player");
        if (playerField?.GetValue(healthController) is not Player player)
        {
            SmartActionLogger.Log($"[MedEffect.{hookName}] ⚠️ Unable to get Player");
            return (null, medItem, false);
        }

        if (!player.IsYourPlayer || !healthController.IsAlive)
            return (player, medItem, false);

        return (player, medItem, true);
    }

    public static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            var field = GetOrCacheField(type, name);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }

    public static PropertyInfo FindProperty(Type type, string name)
    {
        while (type != null)
        {
            var property = GetOrCacheProperty(type, name);
            if (property != null) return property;
            type = type.BaseType;
        }
        return null;
    }
}
