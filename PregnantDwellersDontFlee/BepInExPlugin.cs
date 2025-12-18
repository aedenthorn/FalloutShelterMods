using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace PregnantDwellersDontFlee
{
    [BepInPlugin("aedenthorn.PregnantDwellersDontFlee", "Pregnant Dwellers Dont Flee", "0.1.0")]
    public class BepInExPlugin: BaseUnityPlugin
    {
        public static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;

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

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }




        [HarmonyPatch(typeof(Dweller.DwellerMoving), "UpdateNodeChange")]
        public static class Dweller_DwellerMoving_UpdateNodeChange_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller_DwellerMoving_UpdateNodeChange");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo info && info == AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.Pregnant)))
                    {
                        Dbgl("overriding pregnant check");
                        codes[i].operand = AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.BabyReady));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(Dweller), nameof(Dweller.OnEmergencyLoadOnVault))]
        public static class Dweller_OnEmergencyLoadOnVault_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller.OnEmergencyLoadOnVault");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo info && info == AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.Pregnant)))
                    {
                        Dbgl("overriding pregnant check");
                        codes[i].operand = AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.BabyReady));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(Dweller), nameof(Dweller.OnEmergencyStartsOnRoom))]
        public static class Dweller_OnEmergencyStartsOnRoom_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller.OnEmergencyStartsOnRoom");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo info && info == AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.Pregnant)))
                    {
                        Dbgl("overriding pregnant check");
                        codes[i].operand = AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.BabyReady));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(Dweller), nameof(Dweller.ActivateEmergencyMode))]
        public static class Dweller_ActivateEmergencyMode_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller.ActivateEmergencyMode");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo info && info == AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.Pregnant)))
                    {
                        Dbgl("overriding pregnant check");
                        codes[i].operand = AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.BabyReady));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(Dweller), nameof(Dweller.OnEmergencyOnVault))]
        public static class Dweller_OnEmergencyOnVault_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller.OnEmergencyOnVault");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo info && info == AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.Pregnant)))
                    {
                        Dbgl("overriding pregnant check");
                        codes[i].operand = AccessTools.PropertyGetter(typeof(Dweller), nameof(Dweller.BabyReady));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

    }
}
