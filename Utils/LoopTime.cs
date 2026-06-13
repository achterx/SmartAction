using System;
using System.Linq;
using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;

namespace SmartAction.Utils
{
    // =========================================================================
    // LoopTime — SPT 4.0.13 port
    // =========================================================================
    // CHANGES FROM 3.11:
    //   • GClass2813 was renumbered in EFT 0.16.x. We now discover it at
    //     runtime by scanning ActiveHealthController's nested types for one
    //     that has a "WorkStateTime" property and a "LoopTime" field.
    //   • GClass2823_0 property was similarly renumbered; we find it by
    //     scanning the discovered GClass for a static property that returns
    //     another type containing a "MedEffect" field.
    // =========================================================================

    public abstract class LoopTime
    {
        public static float OriginalLoopTime { get; private set; }
        private static FieldInfo _loopTimeField;
        private static bool _isInitialized = false;
        private static object _medEffectInstance;

        // ⚠️ VERIFY: If init still fails, open ActiveHealthController in dnSpy
        //   and find the nested GClass that has both "WorkStateTime" (property)
        //   and "LoopTime" (field). Update _workStateTimeTypeName if needed.

        public static void Initialize()
        {
            if (_isInitialized)
            {
                SmartActionLogger.Info("[LoopTime] Already initialized.");
                return;
            }

            SmartActionLogger.Info("[LoopTime] Initializing...");

            try
            {
                // Step 1: Find the GClass that was formerly GClass2813.
                // It's a nested type of ActiveHealthController that has
                // a static property returning another nested type (the "factory"
                // singleton, formerly GClass2823) which itself has a MedEffect field.
                Type gclass2813 = FindMedEffectFactoryHostType();
                if (gclass2813 == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Could not find GClass2813 equivalent.");
                    return;
                }

                // Step 2: Find the static property returning the singleton instance
                // (was GClass2823_0).
                PropertyInfo singletonProp = FindSingletonProperty(gclass2813);
                if (singletonProp == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Could not find GClass2823_0 equivalent property.");
                    return;
                }

                object singletonInstance = singletonProp.GetValue(null);
                if (singletonInstance == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Singleton instance is null (raid not started yet?).");
                    return;
                }

                // Step 3: Get the MedEffect field on the singleton.
                FieldInfo medEffectField = AccessTools.Field(singletonInstance.GetType(), "MedEffect");
                if (medEffectField == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Could not find MedEffect field on singleton.");
                    return;
                }

                _medEffectInstance = medEffectField.GetValue(singletonInstance);
                if (_medEffectInstance == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] MedEffect instance is null.");
                    return;
                }

                // Step 4: Get the LoopTime field on MedEffect.
                _loopTimeField = _medEffectInstance.GetType()
                    .GetField("LoopTime",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_loopTimeField == null)
                    _loopTimeField = AccessTools.Field(_medEffectInstance.GetType(), "LoopTime");

                if (_loopTimeField == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Could not find LoopTime field.");
                    return;
                }

                OriginalLoopTime = (float)_loopTimeField.GetValue(_medEffectInstance);
                _isInitialized = true;
                SmartActionLogger.Info($"[LoopTime] ✅ Original LoopTime: {OriginalLoopTime:F2}");
            }
            catch (Exception ex)
            {
                SmartActionLogger.Error($"❌ [LoopTime] Init error: {ex.Message}");
            }
        }

        // Find the type that was formerly ActiveHealthController.GClass2813
        // by looking for a nested type with a "WorkStateTime" property.
        private static Type FindMedEffectFactoryHostType()
        {
            foreach (var nested in typeof(ActiveHealthController).GetNestedTypes(
                         BindingFlags.Public | BindingFlags.NonPublic))
            {
                // GClass2813 has a "WorkStateTime" property
                if (nested.GetProperty("WorkStateTime",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                {
                    SmartActionLogger.Info($"[LoopTime] Found GClass2813 equivalent: {nested.FullName}");
                    return nested;
                }
            }
            return null;
        }

        // Find the static property on gclass2813 whose value has a "MedEffect" field.
        private static PropertyInfo FindSingletonProperty(Type gclass2813)
        {
            foreach (var prop in gclass2813.GetProperties(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var val = prop.GetValue(null);
                    if (val == null) continue;
                    if (val.GetType().GetField("MedEffect",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                    {
                        SmartActionLogger.Info($"[LoopTime] Found singleton prop: {prop.Name}");
                        return prop;
                    }
                }
                catch { /* skip */ }
            }
            return null;
        }

        public static void SetLoopTime(float newValue)
        {
            try
            {
                if (!_isInitialized)
                {
                    SmartActionLogger.Warn("[LoopTime] Not initialized.");
                    return;
                }
                if (_loopTimeField == null || _medEffectInstance == null)
                {
                    SmartActionLogger.Error("❌ [LoopTime] Field or instance is null.");
                    return;
                }

                _loopTimeField.SetValue(_medEffectInstance, newValue);
                var afterSet = (float)_loopTimeField.GetValue(_medEffectInstance);
                if (Math.Abs(afterSet - newValue) < 0.01f)
                    SmartActionLogger.Log($"[LoopTime] ✅ Set to {afterSet:F2}");
                else
                    SmartActionLogger.Error($"[LoopTime] ❌ Set failed: got {afterSet:F2}, expected {newValue:F2}");
            }
            catch (Exception ex)
            {
                SmartActionLogger.Error($"[LoopTime] ⚠️ SetLoopTime error: {ex.Message}");
                _isInitialized = false;
                try
                {
                    Initialize();
                    if (_isInitialized && _loopTimeField != null)
                    {
                        _loopTimeField.SetValue(_medEffectInstance, newValue);
                        SmartActionLogger.Log("[LoopTime] ✅ Set after reinit.");
                    }
                }
                catch (Exception rex)
                {
                    SmartActionLogger.Error($"[LoopTime] ⚠️ Reinit failed: {rex.Message}");
                }
            }
        }

        public static void RestoreOriginalLoopTime()
        {
            try
            {
                SetLoopTime(OriginalLoopTime);
                SmartActionLogger.Info("[LoopTime] 🔄 Restored original LoopTime.");
            }
            catch (Exception ex)
            {
                SmartActionLogger.Error($"[LoopTime] ⚠️ Restore error: {ex.Message}");
            }
        }
    }
}
