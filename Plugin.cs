using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SmartAction.Utils;
using UnityEngine;

namespace SmartAction
{
    // =========================================================================
    // SmartAction — ported to SPT 4.0.13 / EFT 0.16.x
    // =========================================================================
    // PORTING NOTES:
    //   GClass numbers shift every EFT patch. All GClass/Class references below
    //   use reflection-based discovery (AccessTools / GetNestedTypes) instead of
    //   direct type references so the mod survives minor GClass renumbering.
    //   Items that still reference hardcoded strings are marked with:
    //     // ⚠️ VERIFY: <what to check in dnSpy/ILSpy against your 4.0.x assembly>
    // =========================================================================

    [BepInPlugin("com.spt.SmartAction", "SmartAction", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource LOGSource;
        public static ConfigEntry<int> IdleSpeed;
        public static ConfigEntry<int> WalkSpeed;
        public static ConfigEntry<int> SprintSpeed;

        private static Harmony HarmonyInstance { get; set; }

        private void Awake()
        {
            IdleSpeed = Config.Bind(
                "Speed",
                "No Move",
                15,
                new ConfigDescription(
                    "When Player no move. Value 10 = x1 speed  /  20 = x2 speed",
                    new AcceptableValueRange<int>(10, 20)));
            WalkSpeed = Config.Bind(
                "Speed",
                "Walk",
                10,
                new ConfigDescription(
                    "When Player Walk. Value 9 = x0.9 speed / Value 10 = x1 speed",
                    new AcceptableValueRange<int>(9, 13)));
            SprintSpeed = Config.Bind(
                "Speed",
                "Sprint",
                9,
                new ConfigDescription(
                    "When Player Sprint. Value 9 = x0.9 speed  /  10 = x1 speed",
                    new AcceptableValueRange<int>(9, 13)));
            InitializeFiles();
            InitializeLogger();
            InitializeActionLogger();
            SetupHarmonyPatches();
        }

        private void InitializeFiles()
        {
            if (!File.Exists(PathsFile.DebugPath))
                File.WriteAllText(PathsFile.DebugPath, "false");

            if (!File.Exists(PathsFile.LogFilePath))
                File.WriteAllText(PathsFile.LogFilePath, "");

            Logger.LogInfo("Log file: " + PathsFile.LogFilePath);
        }

        private void InitializeLogger()
        {
            LOGSource = Logger;
        }

        private static void InitializeActionLogger()
        {
            SmartActionLogger.Init(EnumLoggerMode.DirectWrite);
            Application.quitting += SmartActionLogger.OnApplicationQuit;
        }

        private static void SetupHarmonyPatches()
        {
            HarmonyInstance = new Harmony("com.spt.SmartAction");
            HarmonyInstance.PatchAll();
        }
    }
}
