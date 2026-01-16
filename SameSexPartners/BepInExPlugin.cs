using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace SameSexPartners
{
    [BepInPlugin("aedenthorn.SameSexPartners", "Same Sex Partners", "0.2.1")]
    public class BepInExPlugin: BaseUnityPlugin
    {
        public static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> allowFemale;
        public static ConfigEntry<bool> allowFemaleFemalePregnancies;
        public static ConfigEntry<bool> allowMaleMalePregnancies;
        public static ConfigEntry<bool> allowFemaleMalePregnancies;
        public static ConfigEntry<bool> allowMale;
        public static ConfigEntry<bool> requireFemale;
        public static ConfigEntry<bool> requireMale;
        public static ConfigEntry<EGender> overrideSex;
        public static ConfigEntry<bool> overrideSpecialDwellers;
        public static ConfigEntry<int> maxDwellers;

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

			allowFemale = Config.Bind<bool>("Relationships", "AllowFemale", true, "Allow female same-sex partners");
			allowMale = Config.Bind<bool>("Relationships", "AllowMale", true, "Allow male same-sex partners");
			requireFemale = Config.Bind<bool>("Relationships", "RequireFemale", false, "Require female same-sex partners");
            requireMale = Config.Bind<bool>("Relationships", "RequireMale", false, "Require male same-sex partners");

            allowFemaleFemalePregnancies = Config.Bind<bool>("Pregnancy", "AllowFemaleFemalePregnancies", true, "Allow female same-sex partners to get pregnant");
            allowMaleMalePregnancies = Config.Bind<bool>("Pregnancy", "AllowMaleMalePregnancies", true, "Allow male same-sex partners to get pregnant");
            allowFemaleMalePregnancies = Config.Bind<bool>("Pregnancy", "AllowFemaleMalePregnancies", true, "Allow heterosexual female partners to get pregnant");

            overrideSex = Config.Bind<EGender>("Override", "OverrideSex", EGender.Any, "Force new dwellers to this value");
            overrideSpecialDwellers = Config.Bind<bool>("Override", "OverrideSpecialDwellers", true, "Override spawning of special dwellers with one according to OverrideSex");
            maxDwellers = Config.Bind<int>("Override", "MaxDwellers", 200, "Set max dwellers in vault");

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }


        [HarmonyPatch(typeof(DwellerManager), "Start")]
        public static class DwellerManager_Start_Patch
        {
            public static void Prefix(ref int ___m_maximumDwellerCount)
            {
                if (modEnabled.Value)
                    ___m_maximumDwellerCount = maxDwellers.Value;
            }
        }


        [HarmonyPatch(typeof(GeneralQuestParameters), nameof(GeneralQuestParameters.CalculateQAL))]
        public static class GeneralQuestParameters_CalculateQAL_Patch
        {

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling GeneralQuestParameters.CalculateQAL");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Call && codes[i].operand is MethodInfo info && info.Name == "GetQALParameter")
                    {
                        Dbgl("adding dweller limit");
                        codes.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BepInExPlugin), nameof(BepInExPlugin.LimitDwellers))));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        private static int LimitDwellers(int value)
        {
            return Math.Min(value, 200);
        }

        [HarmonyPatch(typeof(DwellerSpawner), nameof(DwellerSpawner.CreateWaitingDweller))]
        public static class DwellerSpawner_CreateWaitingDweller_Patch
        {
            public static void Prefix(ref EGender gender)
            {
                if(modEnabled.Value && overrideSex.Value != EGender.Any)
                    gender = overrideSex.Value;
            }
        }

        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.FindLegendaryDwellerData))]
        public static class DwellerManager_FindLegendaryDwellerData_Patch
        {
            public static void Postfix(DwellerManager __instance, ref string assetName, ref UniqueDwellerData __result)
            {
                if (__result == null)
                {
                    __result = __instance.GetUniqueDwellerData(EDwellerRarity.Legendary, false);
                }
            }
        }

        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.FindRareDwellerData))]
        public static class DwellerPool_FindRareDwellerData_Patch
        {
            public static void Postfix(DwellerManager __instance, string assetName, UniqueDwellerData[] ___m_rareDwellers, ref UniqueDwellerData __result)
            {
                if (__result == null)
                {
                    if (__result == null)
                    {
                        __result = __instance.GetUniqueDwellerData(EDwellerRarity.Rare, false);
                    }
                }
            }
        }
        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.FindSpecialDwellerData))]
        public static class DwellerPool_FindSpecialDwellerData_Patch
        {
            public static void Postfix(DwellerManager __instance, string assetName, UniqueDwellerData[] ___m_customDwellers, ref UniqueDwellerData __result)
            {
                if (__result == null)
                {
                    __result = __instance.GetUniqueDwellerData(EDwellerRarity.Rare, false);
                }
            }
        }


        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.CreateSpecialDweller))]
        public static class DwellerPool_CreateSpecialDweller_Patch
        {
            public static bool Prefix(DwellerManager __instance, UniqueDwellerData data, Vector3 position, Quaternion rotation, ref Dweller __result)
            {
                if (modEnabled.Value && overrideSex.Value != EGender.Any && overrideSpecialDwellers.Value && data.Gender != overrideSex.Value)
                {
                    __result = __instance.CreateRandomSpecialDweller(EDwellerRarity.Legendary, position, rotation);
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.GetUniqueDwellerData), new Type[] { typeof(EDwellerRarity), typeof(bool) })]
        public static class DwellerManager_GetUniqueDwellerData_Patch
        {
            public static bool Prefix(DwellerManager __instance, ShuffleBag<UniqueDwellerData> ___m_rareDwellerShuffle, ShuffleBag<UniqueDwellerData> ___m_legendaryDwellersShuffle, EDwellerRarity rarity, bool includeAll, ref UniqueDwellerData __result)
            {
                if (!modEnabled.Value || !overrideSpecialDwellers.Value || overrideSex.Value == EGender.Any)
                    return true;
                if (rarity == EDwellerRarity.Rare)
                {
                    __result = ___m_rareDwellerShuffle.Next((UniqueDwellerData dweller) => dweller.Gender == overrideSex.Value);
                }
                __result = ___m_legendaryDwellersShuffle.Next((UniqueDwellerData dweller) => dweller.Gender == overrideSex.Value);
                return false;
            }
        }


        [HarmonyPatch(typeof(DwellerManager), nameof(DwellerManager.CreateDweller))]
        public static class DwellerManager_CreateDweller_Patch
        {
            public static void Prefix(ref EGender gender)
            {
                if (modEnabled.Value && overrideSex.Value != EGender.Any)
                    gender = overrideSex.Value;
            }
        }



        [HarmonyPatch(typeof(DwellerPool), nameof(DwellerPool.GetInstance), new Type[] { typeof(EGender), typeof(Vector3), typeof(Quaternion), typeof(EDwellerRarity) })]
        public static class DwellerPool_GetInstance_Patch
        {
            public static void Prefix(ref EGender gender, ref EDwellerRarity rarity)
            {
                if (modEnabled.Value && overrideSex.Value != EGender.Any && (overrideSpecialDwellers.Value || !Environment.StackTrace.Contains("CreateSpecialDweller")))
                    gender = overrideSex.Value;
            }
        }
        [HarmonyPatch(typeof(DwellerPool), nameof(DwellerPool.GetInstance), new Type[] { typeof(EGender) })]
        public static class DwellerPool_GetInstance_Patch2
        {
            public static void Prefix(ref EGender gender)
            {
                if (modEnabled.Value && overrideSex.Value != EGender.Any)
                    gender = overrideSex.Value;
            }
        }

        [HarmonyPatch(typeof(DwellerPool), nameof(DwellerPool.GetRandomGender))]
        public static class DwellerPool_GetRandomGender_Patch
        {
            public static bool Prefix(ref EGender __result)
            {
                if (modEnabled.Value && overrideSex.Value != EGender.Any)
                {
                    __result = overrideSex.Value;
                    return false;
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(DwellerRelations), nameof(DwellerRelations.CheckCoupleCompatibility))]
        public static class DwellerRelations_CheckCoupleCompatibility_Patch
        {
            public static bool Prefix(Dweller dweller1, Dweller dweller2, ref bool __result)
            {
                if (!modEnabled.Value)
                    return true;
                if (dweller1.m_gender == dweller2.m_gender)
                {
                    if ((dweller1.m_gender == EGender.Female && allowFemale.Value) || (dweller1.m_gender == EGender.Male && allowMale.Value))
                    {
                        if (dweller1.IsChild || dweller2.IsChild)
                        {
                            __result = false;
                        }
                        else
                        {
                            __result = true;
                        }
                        return false;
                    }
                }
                else if((dweller1.m_gender == EGender.Female || dweller2.m_gender == EGender.Female) && requireFemale.Value)
                {
                    return false;
                }
                else if((dweller1.m_gender == EGender.Male || dweller2.m_gender == EGender.Male) && requireMale.Value)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(LivingQuartersRoom.LivingQuarterBreeding), nameof(LivingQuartersRoom.LivingQuarterBreeding.BreedingCycleCompleted))]
        public static class LivingQuartersRoom_LivingQuarterBreeding_BreedingCycleCompleted_Patch
        {
            public static void Prefix(LivingQuartersRoom.LivingQuarterBreeding __instance, LivingQuartersRoom ___m_room)
            {
                if (!modEnabled.Value)
                    return;

                if (__instance.m_maleDwellers.Any() != __instance.m_femaleDwellers.Any() && ___m_room.Dwellers.Count > 1)
                {
                    AccessTools.Method(typeof(LivingQuartersRoom.LivingQuarterBreeding), "UpdateGenderLists").Invoke(__instance, null);
                }
            }
        }

        [HarmonyPatch(typeof(LivingQuartersRoom.LivingQuarterBreeding), nameof(LivingQuartersRoom.LivingQuarterBreeding.GoToPotencialPartner))]
        public static class LivingQuartersRoom_LivingQuarterBreeding_GoToPotencialPartner_Patch
        {
            public static bool Prefix(LivingQuartersRoom.LivingQuarterBreeding __instance, LivingQuartersRoom ___m_room, Dweller dweller, DwellerRelation bestRelation, EMovementMode mode, EMovementIntent intent, ref bool ___m_needTestRelations)
            {
                if (!modEnabled.Value)
                    return true;

                if (__instance.m_maleDwellers.Contains(dweller))
                {
                    RoomDwellerPosition dwellerLocation = dweller.m_dwellerLocation;
                    RoomDwellerPosition partnerPathNone = bestRelation.m_targetDweller.m_dwellerLocation.m_partnerPathNone;
                    if (partnerPathNone != null && partnerPathNone != dweller.m_dwellerLocation)
                    {
                        Dweller dweller2 = partnerPathNone.m_dweller;
                        dweller.SetLocation(partnerPathNone);
                        if (dweller2 != null)
                        {
                            dweller2.SetLocation(dwellerLocation);
                            dweller2.MovingState.SetTarget(dwellerLocation.GetDwellerPathNode(), mode, EMovementIntent.GoingToAssignedRoom);
                            dweller2.ChangeState(dweller2.MovingState);
                        }
                        dweller.MovingState.SetTarget(partnerPathNone.GetDwellerPathNode(), mode, intent);
                        dweller.ChangeState(dweller.MovingState);
                        ___m_needTestRelations = true;
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(LivingQuartersRoom.LivingQuarterBreeding), "UpdateGenderLists")]
        public static class LivingQuartersRoom_LivingQuarterBreeding_UpdateGenderLists_Patch
        {
            public static bool Prefix(LivingQuartersRoom.LivingQuarterBreeding __instance, LivingQuartersRoom ___m_room)
            {
                if (!modEnabled.Value || (!allowFemale.Value && !allowMale.Value))
                    return true;
                __instance.m_maleDwellers.Clear();
                __instance.m_femaleDwellers.Clear();
                __instance.m_partnerDweller.Clear();
                __instance.m_searchingDweller.Clear();
                List<Dweller> dwellers = new List<Dweller>();
                dwellers.AddRange(___m_room.Dwellers);
                dwellers.Sort((Dweller a, Dweller b) => UnityEngine.Random.value < 0.5f ? -1 : 1);
                bool odd = true;
                if (allowFemale.Value || requireFemale.Value)
                {
                    foreach (Dweller dweller in dwellers)
                    {
                        if (dweller.Pregnant || dweller.m_gender == EGender.Male)
                            continue;
                        if (odd)
                        {
                            if (!dweller.Relations.HasPartner())
                            {
                                __instance.m_maleDwellers.Add(dweller);
                                __instance.m_searchingDweller.Add(dweller);
                            }
                            else
                            {
                                __instance.m_partnerDweller.Add(dweller);
                            }
                        }
                        else
                        {
                            if (!dweller.Relations.HasPartner())
                            {
                                __instance.m_femaleDwellers.Add(dweller);
                                __instance.m_searchingDweller.Add(dweller);
                            }
                            else
                            {
                                __instance.m_partnerDweller.Add(dweller);
                            }
                        }
                        odd = !odd;
                    }
                }
                if (allowMale.Value || requireMale.Value)
                {
                    odd = true;
                    foreach (Dweller dweller in dwellers)
                    {
                        if (dweller.Pregnant || dweller.m_gender == EGender.Female)
                            continue;
                        if (odd)
                        {
                            if (!dweller.Relations.HasPartner())
                            {
                                __instance.m_maleDwellers.Add(dweller);
                                __instance.m_searchingDweller.Add(dweller);
                            }
                            else
                            {
                                __instance.m_partnerDweller.Add(dweller);
                            }
                        }
                        else
                        {
                            if (!dweller.Relations.HasPartner())
                            {
                                __instance.m_femaleDwellers.Add(dweller);
                                __instance.m_searchingDweller.Add(dweller);
                            }
                            else
                            {
                                __instance.m_partnerDweller.Add(dweller);
                            }
                        }
                        odd = !odd;
                    }
                }
                
                return false;
            }
        }

        [HarmonyPatch(typeof(DwellerRelations.Ascendants), nameof(DwellerRelations.Ascendants.GetParent))]
        public static class DwellerRelations_Ascendants_GetParent_Patch
        {
            public static bool Prefix(DwellerRelations.Ascendants __instance, EGender gender, short[] ___m_parents, ref Dweller __result)
            {
                if (!modEnabled.Value)
                    return true;

                for (int i = 0; i < ___m_parents.Length; i++)
                {
                    Dweller dwellerById = MonoSingleton<DwellerManager>.Instance.GetDwellerById(___m_parents[i]);
                    if (dwellerById != null && i == (int)gender - 1)
                    {
                        __result = dwellerById;
                        return false;
                    }
                }
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(DwellerPartnership), nameof(DwellerPartnership.MakeBabyFinish))]
        public static class DwellerPartnership_MakeBabyFinish_Patch
        {
            public static bool Prefix(DwellerPartnership __instance, ref Dweller ___m_female, ref Dweller ___m_male)
            {
                if (!modEnabled.Value)
                    return true;
                bool disallow = false;
                if(___m_male.m_gender == EGender.Female)
                {
                    if (___m_female.m_gender == EGender.Female && !allowFemaleFemalePregnancies.Value)
                        disallow = true;
                    else if (___m_female.m_gender == EGender.Male && !allowFemaleMalePregnancies.Value)
                        disallow = true;
                }
                else if(___m_male.m_gender == EGender.Male)
                {
                    if (___m_female.m_gender == EGender.Male && !allowMaleMalePregnancies.Value)
                        disallow = true;
                    else if (___m_female.m_gender == EGender.Female && !allowFemaleMalePregnancies.Value)
                        disallow = true;
                }
                if (disallow)
                {
                    __instance.EndRelationship();
                }
                return !disallow;
            }
        }

        [HarmonyPatch(typeof(Dweller), nameof(Dweller.SetPregnant))]
        public static class Dweller_SetPregnant_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl($"Transpiling Dweller.SetPregnant");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo info && info == AccessTools.Field(typeof(Dweller), nameof(Dweller.m_gender)))
                    {
                        Dbgl("overriding gender check");
                        codes[i + 1].opcode = OpCodes.Call;
                        codes[i + 1].operand = AccessTools.Method(typeof(BepInExPlugin), nameof(BepInExPlugin.SetPregnantGenderCheck));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                        break;
                    }
                }


                return codes.AsEnumerable();
            }
        }

        private static int SetPregnantGenderCheck(Dweller __instance)
        {
            return (!modEnabled.Value || __instance.m_gender != EGender.Male || !allowMaleMalePregnancies.Value || __instance.Pregnant) ? (int)EGender.Female : (int)EGender.Male;
        }
    }
}
