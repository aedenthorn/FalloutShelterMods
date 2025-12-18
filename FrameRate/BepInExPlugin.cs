using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace FrameRate
{
    [BepInPlugin("aedenthorn.FrameRate", "Frame Rate", "0.1.0")]
    public class BepInExPlugin: BaseUnityPlugin
    {
        public static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> vSync;
        public static ConfigEntry<int> targetFrameRate;
        public static ConfigEntry<string> qualityLevel;
        public static ConfigEntry<EGameQuality> gameQuality;
        public static ConfigEntry<EDeviceDefinitionType> deviceDefinitionType;

        public static void Dbgl(object obj, BepInEx.Logging.LogLevel level = BepInEx.Logging.LogLevel.Debug)
        {
            if (isDebug.Value)
                context.Logger.Log(level, obj);
        }
        public void Awake()
        {
            context = this;
            modEnabled = Config.Bind<bool>("General", "ModEnabled", true, "Enable mod");
			isDebug = Config.Bind<bool>("General", "IsDebug", true, "Enable debug");
			vSync = Config.Bind<bool>("Options", "vSync", true, "Enable vSync");
			targetFrameRate = Config.Bind<int>("Options", "TargetFrameRate", 60, "Set custom target framerate");
            qualityLevel = Config.Bind<string>("Options", "QualityLevel", "HighSteam", "Set quality level");
            gameQuality = Config.Bind<EGameQuality>("Options", "GameQuality", EGameQuality.eSuperHighQuality, "Set game quality");
            deviceDefinitionType = Config.Bind<EDeviceDefinitionType>("Options", "DeviceDefinitionType", EDeviceDefinitionType.HD, "Set device definition type");

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }


        [HarmonyPatch(typeof(GameQualityManager), nameof(GameQualityManager.SetGameQuality))]
        public static class GameQualityManager_SetGameQuality_Patch
        {
            public static bool Prefix(GameQualityManager __instance, ref EGameQuality ___m_eCurrentQuality, EDeviceDefinitionType ___m_eDeviceDefinitionType)
            {
                if (!modEnabled.Value)
                    return true;
                if(!string.IsNullOrEmpty(qualityLevel.Value))
                    __instance.SetQualityLevelByName(qualityLevel.Value);
                ___m_eCurrentQuality = gameQuality.Value;
                ___m_eDeviceDefinitionType = deviceDefinitionType.Value;
                return false;
            }
        }

        [HarmonyPatch(typeof(GameQualityManager), "SetTargetFrameRate")]
        public static class GameQualityManager_SetTargetFrameRate_Patch
        {
            public static bool Prefix()
            {
                if(!modEnabled.Value)
                    return true;
                QualitySettings.vSyncCount = vSync.Value ? 1 : 0;
                if(!vSync.Value)
                    Application.targetFrameRate = targetFrameRate.Value;
                return false;
            }
        }
    }
}
