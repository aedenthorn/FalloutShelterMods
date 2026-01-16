using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Inventory;
using static ParameterDataMgr;
using static RecipeList;

namespace CustomOutfits
{
    [BepInPlugin("aedenthorn.CustomOutfits", "Custom Outfits", "0.1.0")]
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

            if (!Directory.Exists(OutfitsFolder))
            {
                Directory.CreateDirectory(OutfitsFolder);
            }

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
            Dbgl("Mod initialized");
            //checker = context.gameObject.AddComponent<DebugChecker>();
        }

        public static Texture2D Load(string filePath, string idName, int width, int height)
        {
            if (_pathLookups.ContainsKey(filePath))
            {
                string text = _pathLookups[filePath];
                if (_atlases.ContainsKey(text))
                {
                    if (idName == text)
                    {
                        return _atlases[text];
                    }
                    _atlases.Add(idName, _atlases[text]);
                    return _atlases[idName];
                }
                else
                {
                    _pathLookups.Remove(filePath);
                }
            }
            byte[] array = File.ReadAllBytes(filePath);
            Texture2D texture2D = new Texture2D(width, height, TextureFormat.ARGB32, false);
            bool flag = texture2D.LoadImage(array);
            _atlases.Add(idName, texture2D);
            _pathLookups.Add(filePath, idName);
            DontDestroyOnLoad(texture2D);
            if (!flag)
            {
                return null;
            }
            return texture2D;
        }

        public static bool HasAtlas(string name)
        {
            return _atlases.ContainsKey(name);
        }

        public static Texture2D GetAtlas(string name)
        {
            if (_atlases.ContainsKey(name))
            {
                return _atlases[name];
            }
            return Catalog.Instance.EmptyTexture;
        }

        public static void Unload(string id)
        {
            if (_atlases.ContainsKey(id))
            {
                List<string> list = new List<string>();
                foreach (KeyValuePair<string, string> keyValuePair in _pathLookups)
                {
                    if (keyValuePair.Value == id)
                    {
                        list.Add(keyValuePair.Key);
                    }
                }
                foreach (string text in list)
                {
                    _pathLookups.Remove(text);
                }
                Destroy(_atlases[id]);
                _atlases.Remove(id);
            }
        }

        public static void UnloadAll()
        {
            foreach (KeyValuePair<string, Texture2D> keyValuePair in _atlases)
            {
                Destroy(keyValuePair.Value);
            }
            _atlases.Clear();
            _pathLookups.Clear();
        }

        private static Dictionary<string, string> _pathLookups = new Dictionary<string, string>();

        private static Dictionary<string, Texture2D> _atlases = new Dictionary<string, Texture2D>();

        private static string OutfitsFolder
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "CustomOutfits");
            }
        }

        private static void RegisterItemTexture(ItemTexture itemTexture)
        {
            string id = itemTexture.id;
            if (_customItemAtlases.ContainsKey(id))
            {
                Dbgl("\t\"" + id + "\" item texture already loaded, skipping...");
                return;
            }

            string atlas = itemTexture.atlas;
            if (!HasAtlas(atlas))
            {
                Dbgl("\t\"" + atlas + "\" not found!");
            }
            ushort num;
            UIAtlas uiatlas;
            if (_itemAtlasLookup.ContainsKey(atlas))
            {
                num = _itemAtlasLookup[atlas];
                uiatlas = _itemAtlasList[num];
                Dbgl("\tAtlas looked up!");
            }
            else
            {
                uiatlas = CreateUIAtlas(GetAtlas(atlas), atlas);
                _itemAtlasList.Add(uiatlas);
                num = (ushort)(_itemAtlasList.Count - 1);
                _itemAtlasLookup.Add(atlas, num);
                Dbgl("\tAtlas added x" + _itemAtlasList.Count.ToString());
            }
            uiatlas.spriteList.Add(new UISpriteData
            {
                name = id,
                x = itemTexture.x,
                y = itemTexture.y,
                width = itemTexture.width,
                height = itemTexture.height
            });
            uiatlas.MarkAsChanged();
            _customItemAtlases.Add(id, num);
        }

        private static DwellerOutfitItem CreateOutfit(CustomOutfit outfitData)
        {
            DwellerOutfitItem dwellerOutfitItem = new DwellerOutfitItem();
            dwellerOutfitItem.CodeId = outfitData.id;
            dwellerOutfitItem.m_outfitId = dwellerOutfitItem.CodeId;
            AccessTools.FieldRefAccess<DwellerBaseItem, ExportableMonthDayHour>(dwellerOutfitItem, "m_startDate") = new ExportableMonthDayHour();
            AccessTools.FieldRefAccess<DwellerBaseItem, ExportableMonthDayHour>(dwellerOutfitItem, "m_endDate") = new ExportableMonthDayHour();
            if (_usedIDs.Contains(dwellerOutfitItem.CodeId))
            {
                Dbgl("WARNING: The ID \"" + dwellerOutfitItem.CodeId + "\" is already used by another mod item!");
            }
            if (!string.IsNullOrEmpty(outfitData.rarity))
            {
                AccessTools.FieldRefAccess<DwellerBaseItem, EItemRarity>(dwellerOutfitItem, "m_itemRarity") = SmartParseEnum<EItemRarity>(outfitData.rarity, EItemRarity.Normal);
            }
            AccessTools.FieldRefAccess<DwellerBaseItem, int>(dwellerOutfitItem, "m_sellPrice") = outfitData.sellPrice;
            if (outfitData.craft.Any())
            {
                dwellerOutfitItem.CanBeCrafted = true;
                dwellerOutfitItem.CanBeRecipe = true;
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(dwellerOutfitItem, "m_primaryComponent") = SmartParseEnum<EComponent>(outfitData.craft[0].component);

                if (outfitData.craft.Length > 1)
                {
                    AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(dwellerOutfitItem, "m_secondaryComponent") = SmartParseEnum<EComponent>(outfitData.craft[1].component);
                }
                if (outfitData.craft.Length > 2)
                {
                    AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(dwellerOutfitItem, "m_tertiaryComponent") = SmartParseEnum<EComponent>(outfitData.craft[2].component);
                }

            }
            dwellerOutfitItem.m_category = EOutfitCategory.None;
            AccessTools.FieldRefAccess<DwellerOutfitItem, OutfitItemSpecialStats>(dwellerOutfitItem, "m_specialStats") = default(OutfitItemSpecialStats);
            if (outfitData.stats.Length > 4)
            {
                Dbgl("WARNING: Defining more than 4 SPECIAL attributes for one outfit might crash the game (Outfit \"" + dwellerOutfitItem.CodeId + "\").");
            }
            AccessTools.FieldRefAccess<DwellerOutfitItem, SpecialStatsData[]>(dwellerOutfitItem, "m_modificationStats") = GetModStats(dwellerOutfitItem, outfitData.stats);

            string text = "CUSTOM_Outfit_Name_" + dwellerOutfitItem.CodeId;
            dwellerOutfitItem.m_outfitNameLocalizationId = text;
            localizationDict[text] =  outfitData.displayName;

            List<Color> list2 = new List<Color>();
            if (!string.IsNullOrEmpty(outfitData.color))
            {
                list2.Add(ParseColorHexaString(outfitData.color));
            }
            else
            {
                list2.Add(new Color(1f, 1f, 1f));
            }
            Color[] array2 = list2.ToArray();
            CustomPart femaleHelmet = outfitData.femaleHelmet;
            CustomPart maleHelmet = outfitData.maleHelmet;
            dwellerOutfitItem.m_HasHelmet = maleHelmet != null || femaleHelmet != null;
            if ((maleHelmet != null && femaleHelmet == null) || (maleHelmet == null && femaleHelmet != null))
            {
                Dbgl("WARNING: Helmet data is missing for one gender. (Outfit: \"" + dwellerOutfitItem.m_outfitId + "\")");
            }
            CustomPart femaleLargeHeadgear = outfitData.femaleLargeHeadgear;
            CustomPart maleLargeHeadgear = outfitData.maleLargeHeadgear;
            if ((maleLargeHeadgear != null && femaleLargeHeadgear == null) || (maleLargeHeadgear == null && femaleLargeHeadgear != null))
            {
                Dbgl("WARNING: Large headgear data is missing for one gender. (Outfit: \"" + dwellerOutfitItem.m_outfitId + "\")");
            }
            CustomPartOpenable femaleGloves = outfitData.femaleGloves;
            CustomPartOpenable maleGloves = outfitData.maleGloves;
            if (maleGloves != null && (maleGloves.open is null || maleGloves.close == null))
            {
                Dbgl("WARNING: Missing state in the male glove data secion!");
            }
            if (femaleGloves != null && (femaleGloves.open is null || femaleGloves.close == null))
            {
                Dbgl("WARNING: Missing state in the male glove data secion!");
            }
            if ((maleGloves != null && femaleGloves == null) || (maleGloves == null && femaleGloves != null))
            {
                Dbgl("WARNING: Glove data is missing for one gender. (Outfit: \"" + dwellerOutfitItem.m_outfitId + "\")");
            }
            DwellerOutfit dwellerOutfit = ScriptableObject.CreateInstance<DwellerOutfit>();
            dwellerOutfit.m_helmet = null;
            dwellerOutfit.m_largeHeadgear = null;
            dwellerOutfit.m_hasSkirt = outfitData.hasSkirt;
            dwellerOutfit.m_glovePoses = new DwellerGlovePose[2];
            dwellerOutfit.m_colors = array2;
            dwellerOutfit.m_coloringMask = ScriptableObject.CreateInstance<DwellerOutfitColoringMask>();
            string maleAtlas = outfitData.maleAtlas;
            int maleAtlasSlot = outfitData.maleAtlasSlot;
            if (!HasAtlas(maleAtlas))
            {
                Dbgl("\"" + maleAtlas + "\" texture not found, using an empty texture instead.");
            }
            var atlas = GetAtlas(maleAtlas);
            var offset = _slots[maleAtlasSlot];
            Rect bounds = new Rect(offset.x * atlas.width * outfitData.scale.x, offset.y * atlas.height * outfitData.scale.y, atlas.width * outfitData.scale.x, atlas.height * outfitData.scale.y);
            dwellerOutfit.SetAtlasRef(atlas, bounds, 0);
            if (maleHelmet != null)
            {
                dwellerOutfit.m_helmet = CreateHelmet(maleHelmet, outfitData.scale);
            }
            if (maleLargeHeadgear != null)
            {
                dwellerOutfit.m_largeHeadgear = CreateLargeHeadgear(maleLargeHeadgear);
            }
            if (maleGloves != null)
            {
                CreateGlovePosesForOutfit(dwellerOutfit, maleGloves);
            }
            AccessTools.FieldRefAccess<DwellerOutfitItem, DwellerOutfit>(dwellerOutfitItem, "m_maleOutfit") =  dwellerOutfit;

            DwellerOutfit dwellerOutfit2 = ScriptableObject.CreateInstance<DwellerOutfit>(); 
            dwellerOutfit2.m_helmet = null;
            dwellerOutfit2.m_largeHeadgear = null;
            dwellerOutfit2.m_hasSkirt = outfitData.hasSkirt;
            dwellerOutfit2.m_glovePoses = new DwellerGlovePose[2];
            dwellerOutfit2.m_colors = array2;
            dwellerOutfit2.m_coloringMask = ScriptableObject.CreateInstance<DwellerOutfitColoringMask>();
            string femaleAtlas = outfitData.femaleAtlas;
            int femaleAtlasSlot = outfitData.femaleAtlasSlot;
            if (!HasAtlas(femaleAtlas))
            {
                Dbgl("\"" + femaleAtlas + "\" texture not found, using an empty texture instead.");
            }
            atlas = GetAtlas(femaleAtlas);
            offset = _slots[femaleAtlasSlot];
            bounds = new Rect(offset.x * atlas.width * outfitData.scale.x, offset.y * atlas.height * outfitData.scale.y, atlas.width * outfitData.scale.x, atlas.height * outfitData.scale.y);
            dwellerOutfit2.SetAtlasRef(atlas, bounds, 0);
            if (femaleHelmet != null)
            {
                dwellerOutfit2.m_helmet = CreateHelmet(femaleHelmet, outfitData.scale);
            }
            if (femaleLargeHeadgear != null)
            {
                dwellerOutfit2.m_largeHeadgear = CreateLargeHeadgear(femaleLargeHeadgear);
            }
            if (femaleGloves != null)
            {
                CreateGlovePosesForOutfit(dwellerOutfit, femaleGloves);
            }
            AccessTools.FieldRefAccess<DwellerOutfitItem, DwellerOutfit>(dwellerOutfitItem, "m_femaleOutfit") = dwellerOutfit;

            var recipe = new ItemRecipeData();
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipe, "m_isInitiallyAvailable") = outfitData.initiallyAvailable;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipe, "m_canBeFoundInQuest") = outfitData.canBeFoundInQuest;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipe, "m_canBeFoundOnRaiders") = outfitData.canBeFoundOnRaiders;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipe, "m_canBeFoundInWasteland") = outfitData.canBeFoundInWasteland;
            AccessTools.FieldRefAccess<ItemRecipeData, WeightAndDateData>(recipe, "m_defaultData") = new WeightAndDateData();
            AccessTools.FieldRefAccess<ItemRecipeData, WeightAndDateData>(recipe, "m_overrideData") = new WeightAndDateData();
            
            AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ItemRecipeData>(dwellerOutfitItem, "m_recipeData") = recipe;
            //CreateRecipe(dwellerOutfitItem, outfitData);
            if (!string.IsNullOrEmpty(outfitData.craftStat))
            {
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ESpecialStat>(dwellerOutfitItem, "m_craftingAssociatedStat") = _statLookup[outfitData.craftStat];
            }
            string itemTexture = outfitData.itemTexture;
            AccessTools.FieldRefAccess<DwellerOutfitItem, string>(dwellerOutfitItem, "m_OutfitSprite") = string.IsNullOrEmpty(itemTexture) ? "jumpsuit" : itemTexture;
            _usedIDs.Add(dwellerOutfitItem.m_outfitId);
            dwellerOutfitItem.SetName($"{outfitData.id} ({dwellerOutfitItem.ItemRarity})");
            return dwellerOutfitItem;
        }

        private static SpecialStatsData[] GetModStats(DwellerOutfitItem dwellerOutfitItem, CustomStat[] stats)
        {

            List<SpecialStatsData> array = new List<SpecialStatsData>();
            List<ESpecialStat> list = new List<ESpecialStat>();
            foreach (var stat in stats)
            {
                ESpecialStat especialStat = _statLookup[stat.id];
                int value = stat.value;
                array.Add(new SpecialStatsData(especialStat, value, 10));
                if (value > 10)
                {
                    Dbgl("WARNING: Stat values greater than 10 might be broken (Outfit \"" + dwellerOutfitItem.CodeId + "\").");
                }
                if (list.Contains(especialStat))
                {
                    Dbgl(string.Concat(new string[]
                    {
                        "WARNING: The stat \"",
                        especialStat.ToString(),
                        "\" is defined more than once for outfit \"",
                        dwellerOutfitItem.CodeId,
                        "\"."
                    }));
                }
                else
                {
                    list.Add(especialStat);
                }
            }
            return array.ToArray();
        }

        private static DwellerHelmet CreateHelmet(CustomPart helmetData, Vector2 scale)
        {
            DwellerHelmet dwellerHelmet = ScriptableObject.CreateInstance<DwellerHelmet>();
            var atlas = GetAtlas(helmetData.atlas);
            float num = helmetData.y;
            num += helmetData.height;
            num = atlas.height - num;
            var offset = new Vector2(helmetData.x / (float)atlas.width, num / atlas.height);
            Rect bounds = new Rect(offset.x * atlas.width * scale.x, offset.y * atlas.height * scale.y, atlas.width * scale.x, atlas.height * scale.y);
            dwellerHelmet.SetAtlasRef(atlas, bounds, 0);

            dwellerHelmet.m_isExclusive = helmetData.isExclusive;
            dwellerHelmet.m_isExcludingFaceMask = helmetData.isExcludingFaceMask;
            dwellerHelmet.m_isIncludedWithoutCostume = helmetData.isIncludedWithoutCostume;
            return dwellerHelmet;
        }

        private static void CreateGlovePosesForOutfit(DwellerOutfit outfit, CustomPartOpenable gloveData)
        {
            outfit.m_glovePoses = new DwellerGlovePose[]
            {
                CreateGlovePose(gloveData.open, EHandPoseType.Open),
                CreateGlovePose(gloveData.close, EHandPoseType.Fist)
            };
        }

        private static DwellerGlovePose CreateGlovePose(CustomPart data, EHandPoseType poseType)
        {
            DwellerGlovePose dwellerGlovePose = ScriptableObject.CreateInstance<DwellerGlovePose>();
            Texture2D atlas = GetAtlas(data.atlas);
            var scale = new Vector2(data.width / (float)atlas.width, data.height / (float)atlas.height);
            float num = data.y;
            num += data.height;
            num = atlas.height - num;
            var offset = new Vector2(data.x / (float)atlas.width, num / atlas.height);
            dwellerGlovePose.m_pose = poseType;
            Rect bounds = new Rect(offset.x * atlas.width * scale.x, offset.y * atlas.height * scale.y, atlas.width * scale.x, atlas.height * scale.y);
            dwellerGlovePose.SetAtlasRef(atlas, bounds, 0);
            return dwellerGlovePose;
        }

        private static DwellerLargeHeadgear CreateLargeHeadgear(CustomPart data)
        {
            DwellerLargeHeadgear dwellerLargeHeadgear = ScriptableObject.CreateInstance<DwellerLargeHeadgear>();
            Texture2D atlas = GetAtlas(data.atlas);
            var scale = new Vector2(data.width / (float)atlas.width, data.height / (float)atlas.height);
            float num = (data.grabY - data.y) / data.height;
            num = 1 - num;
            var offset = new Vector2((data.grabX - data.x) / (float)atlas.width, num);
            Rect bounds = new Rect(offset.x * atlas.width * scale.x, offset.y * atlas.height * scale.y, atlas.width * scale.x, atlas.height * scale.y);
            dwellerLargeHeadgear.SetAtlasRef(atlas, bounds, 0);
            return dwellerLargeHeadgear;
        }

        private static void CreateOverwrite(DwellerOutfitItem item, CustomOverwrite data)
        {
            if (!string.IsNullOrEmpty(data.rarity))
            {
                AccessTools.FieldRefAccess<DwellerBaseItem, EItemRarity>(item, "m_itemRarity") = SmartParseEnum<EItemRarity>(data.rarity, EItemRarity.Normal);
            }
            if (data.stats.Any())
            {
                AccessTools.FieldRefAccess<DwellerOutfitItem, OutfitItemSpecialStats>(item, "m_specialStats") = default(OutfitItemSpecialStats);
                SpecialStatsData[] array = new SpecialStatsData[data.stats.Length];
                for (int i = 0; i < data.stats.Length; i++)
                {
                    array[i] = new SpecialStatsData(_statLookup[data.stats[i].id], data.stats[i].value, 10);
                }
                AccessTools.FieldRefAccess<DwellerOutfitItem, SpecialStatsData[]>(item, "m_modificationStats") = array.ToArray();
            }
        }

        private static void CreateRecipe(DwellerOutfitItem item, CustomOutfit data)
        {
            if (data.craft.Any())
            {
                item.CanBeCrafted = true;
                List<RecipeList.IngredientEntry> list = new List<RecipeList.IngredientEntry>();
                for (int i = 0; i < data.craft.Length; i++)
                {
                    CustomIngredient ing = data.craft[i];
                    list.Add(new RecipeList.IngredientEntry
                    {
                        Count = ing.count,
                        Component = SmartParseEnum<EComponent>(ing.component),
                        Rarity = SmartParseEnum<EItemRarity>(ing.rarity)
                    });
                }
                if (list.Count == 0)
                {
                    Dbgl("WARNING: Recipe for item \"" + item.m_outfitId + "\" has 0 ingredients!");
                }
                _recipes.Add(new RecipeStore
                {
                    Rarity = item.ItemRarity,
                    Item = EItemType.Outfit,
                    Recipe = new RecipeList.Recipe
                    {
                        BuiltItemId = item.m_outfitId,
                        Ingredients = list.ToArray()
                    }
                });
            }
            if (!string.IsNullOrEmpty(data.craftStat))
            {
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ESpecialStat>(item, "m_craftingAssociatedStat") = _statLookup[data.craftStat];
            }
        }

        private static bool CanUnlockRecipes()
        {
            VaultGUIManager instance = MonoSingleton<VaultGUIManager>.Instance;
            return !(instance == null) && !(instance.m_survivalWindow == null);
        }

        private static DwellerOutfitItem FindOutfit(DwellerOutfitItem[] items, string id)
        {
            foreach (DwellerOutfitItem dwellerOutfitItem in items)
            {
                if (dwellerOutfitItem.m_outfitId == id)
                {
                    return dwellerOutfitItem;
                }
            }
            return null;
        }

        private static T SmartParseEnum<T>(string value, T defaultValue) where T : struct, IConvertible
        {
            string[] names = Enum.GetNames(typeof(T));
            Array values = Enum.GetValues(typeof(T));
            for (int i = 0; i < names.Length; i++)
            {
                string text = names[i];
                if (text.Equals(value, StringComparison.InvariantCultureIgnoreCase))
                {
                    return (T)values.GetValue(i);
                }
            }
            return defaultValue;
        }

        private static T SmartParseEnum<T>(string value) where T : struct, IConvertible
        {
            var e = SmartParseEnum<T>(value, default(T));
            Dbgl($"Created enum {e} from {value}");
            return e;
        }

        private static Color ParseColorHexaString(string hexa)
        {
            Color color = new Color(1f, 1f, 1f);
            if (hexa.Length != 6)
            {
                Dbgl("The color is not in the correct format: \"" + hexa + "\"");
                return color;
            }
            color.r = Convert.ToByte(hexa.Substring(0, 2), 16) / 255f;
            color.g = Convert.ToByte(hexa.Substring(2, 2), 16) / 255f;
            color.b = Convert.ToByte(hexa.Substring(4, 2), 16) / 255f;
            return color;
        }

        private static void TryLoadTexture(CustomTexture tex)
        {
            string text = tex.filePath;
            if (!File.Exists(text))
            {
                Dbgl("\tFile not found: \"" + text + "\"");
                return;
            }
            string id = tex.id;
            if (HasAtlas(id))
            {
                Dbgl("Atlas \"" + id + "\" already loaded.");
                return;
            }
            if (Load(text, id, tex.width, tex.height) != null)
            {
                Dbgl("\t\"" + id + "\" loaded.");
                return;
            }
            Dbgl("\tFailed to load \"" + id + "\"");
        }

        private static UIAtlas CreateUIAtlas(Texture2D texture, string name)
        {
            UIAtlas uiatlas = FindObjectsByType<UISprite>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(s => s.atlas?.spriteMaterial != null)?.First().atlas;
            if (uiatlas == null)
            {
                Dbgl("Couldn't find atlas or sprite material");

                return null;
            }
            UIAtlas uiatlas2 = Instantiate<UIAtlas>(uiatlas);
            uiatlas2.name = name;
            uiatlas2.correctMipmaps = false;
            uiatlas2.holdResolution = false;
            uiatlas2.padding = 2;
            uiatlas2.pixelSize = 0.5f;
            uiatlas2.replacement = null;
            var material = new Material(uiatlas.spriteMaterial);
            material.mainTexture = texture;
            AccessTools.FieldRefAccess<UIAtlas, Material>(uiatlas2, "material") = material;
            uiatlas2.spriteMaterial = material;
            uiatlas2.spriteList.Clear();
            AccessTools.FieldRefAccess<UIAtlas, Dictionary<string,int>>(uiatlas2, "mSpriteIndices").Clear();
            AccessTools.FieldRefAccess<UIAtlas, Dictionary<string,int>>(uiatlas2, "mSpriteIndices").Clear();
            uiatlas2.spriteList.Clear();
            DontDestroyOnLoad(uiatlas2);
            return uiatlas2;
        }

        private static Dictionary<int, Vector2> _slots = new Dictionary<int, Vector2>
            {
                { 0, new Vector2(0f, 0.75f) },
                { 1, new Vector2(0.5f, 0.75f) },
                { 2, new Vector2(0f, 0.5f) },
                { 3, new Vector2(0.5f, 0.5f) },
                { 4, new Vector2(0f, 0.25f) },
                { 5, new Vector2(0.5f, 0.25f) },
                { 6, new Vector2(0f, 0f) },
                { 7, new Vector2(0.5f, 0f) }
            };

        private static Dictionary<string, ESpecialStat> _statLookup = new Dictionary<string, ESpecialStat>
            {
                { "S", ESpecialStat.Strength },
                { "P", ESpecialStat.Perception },
                { "E", ESpecialStat.Endurance },
                { "C", ESpecialStat.Charisma },
                { "I", ESpecialStat.Intelligence },
                { "A", ESpecialStat.Agility },
                { "L", ESpecialStat.Luck }
            };

        private static Dictionary<string, ushort> _itemAtlasLookup;

        private static Dictionary<string, string> localizationDict = new Dictionary<string, string>();

        private static List<UIAtlas> _itemAtlasList;

        private static Dictionary<string, ushort> _customItemAtlases;

        private static List<string> _usedIDs;

        private static List<RecipeStore> _recipes;
        private static DebugChecker checker;

        private struct RecipeStore
        {
            public EItemRarity Rarity;

            public EItemType Item;

            public RecipeList.Recipe Recipe;
        }

        [HarmonyPatch(typeof(UISprite), nameof(UISprite.atlas))]
        [HarmonyPatch(MethodType.Setter)]
        public static class UISprite_atlas_Patch
        {
            public static bool Prefix(UISprite __instance, ref UIAtlas ___mAtlas)
            {
                if (!modEnabled.Value)
                    return true;

                bool flag = false;
                if (_customItemAtlases != null && _customItemAtlases.TryGetValue(__instance.spriteName, out var num))
                {
                    AccessTools.FieldRefAccess<UISprite, UISpriteData>(__instance, "mSprite") = null;
                    AccessTools.FieldRefAccess<UISprite, bool>(__instance, "mSpriteSet") = false;
                    if (num < 0 || num >= _itemAtlasList.Count)
                    {
                        flag = true;
                    }
                    else
                    {
                        Dbgl($"Switching atlas");
                        UIAtlas uiatlas = _itemAtlasList[num];
                        CustomAtlasInfo customAtlasInfo = __instance.gameObject.GetComponent<CustomAtlasInfo>();
                        if (customAtlasInfo == null)
                        {
                            customAtlasInfo = __instance.gameObject.AddComponent<CustomAtlasInfo>();
                            customAtlasInfo.OriginalAtlas = __instance.atlas;
                        }
                        ___mAtlas = uiatlas;
                        __instance.MarkAsChanged();
                        return false;
                    }
                }
                else
                {
                    flag = true;
                }
                if (flag)
                {
                    CustomAtlasInfo component = __instance.gameObject.GetComponent<CustomAtlasInfo>();
                    if (component != null)
                    {
                        Dbgl($"Switching atlas back");
                        ___mAtlas = component.OriginalAtlas;
                        __instance.MarkAsChanged();
                        return false;
                    }
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(ResourceParticleMgr), nameof(ResourceParticleMgr.AddResourceParticle))]
        public static class ResourceParticleMgr_AddResourceParticle_Patch
        {
            public static void Postfix(ResourceParticleMgr __instance, string ___m_spriteName, ref UIAtlas ___m_outfitAtlas, SpriteManager.SpriteManager ___m_outfitSpriteManager, ResourceParticle particle)
            {
                if (particle == null)
                {
                    return;
                }
                if (particle.m_type == EResourceParticleType.CraftedOutfit)
                {
                    string m_spriteName = ___m_spriteName;
                    if (m_spriteName == null)
                    {
                        return;
                    }
                    CustomAtlasInfo customAtlasInfo = __instance.gameObject.GetComponent<CustomAtlasInfo>();
                    if (customAtlasInfo == null)
                    {
                        customAtlasInfo = __instance.gameObject.AddComponent<CustomAtlasInfo>();
                        customAtlasInfo.OriginalAtlas = ___m_outfitAtlas;
                    }
                    UIAtlas uiatlas;
                    if (_customItemAtlases != null && _customItemAtlases.ContainsKey(m_spriteName))
                    {
                        uiatlas = _itemAtlasList[_customItemAtlases[m_spriteName]];
                    }
                    else
                    {
                        uiatlas = customAtlasInfo.OriginalAtlas;
                    }
                    ___m_outfitAtlas = uiatlas;
                    if (___m_outfitSpriteManager?.ActiveMaterial != null)
                    {
                        ___m_outfitSpriteManager.ActiveMaterial = uiatlas.spriteMaterial;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.GetTermTranslation))]
        public static class LocalizationManager_GetTermTranslation_Patch
        {
            public static bool Prefix(string Term, ref string __result)
            {
                if (!modEnabled.Value || !localizationDict.TryGetValue(Term, out var str))
                    return true;
                __result = str;
                return false;
            }
        }

        [HarmonyPatch(typeof(GameParameters), "OnAwake")]
        public static class GameParameters_OnAwake_Patch
        {
            public static void Prefix()
            {
                if (!modEnabled.Value)
                    return;
                Dbgl("Awake");

                if (_customItemAtlases == null)
                {
                    _customItemAtlases = new Dictionary<string, ushort>();
                }
                if (_itemAtlasList == null)
                {
                    _itemAtlasList = new List<UIAtlas>();
                }
                if (_itemAtlasLookup == null)
                {
                    _itemAtlasLookup = new Dictionary<string, ushort>();
                }
                if (!Directory.Exists(OutfitsFolder))
                {
                    Directory.CreateDirectory(OutfitsFolder);
                }
                try
                {
                    Dbgl("CustomOutfits mod version: " + context.Info.Metadata.Version);
                    List<CustomOutfit> list = new List<CustomOutfit>();
                    List<CustomOverwrite> list2 = new List<CustomOverwrite>();
                    foreach (string filePath in Directory.GetFiles(OutfitsFolder, "*.json", SearchOption.AllDirectories))
                    {
                        Dbgl("Loading \"" + Path.GetFileName(filePath) + "\"");
                        try
                        {
                            CustomOutfitSet set = JsonConvert.DeserializeObject<CustomOutfitSet>(File.ReadAllText(filePath));
                            list.AddRange(set.outfits);
                            list2.AddRange(set.overwrite);
                            foreach (var t in set.textures)
                            {
                                t.filePath = Path.Combine(Path.GetDirectoryName(filePath), t.filePath);
                                Dbgl($"texture path: {t.filePath}");
                                TryLoadTexture(t);
                            }
                            foreach (var t in set.itemTextures)
                            {
                                RegisterItemTexture(t);
                            }
                        }
                        catch (Exception ex)
                        {
                            Dbgl("Failed to load config \"" + Path.GetFileName(filePath) + "\", probably the json syntax is wrong.");
                            Dbgl("Details: " + ex.ToString());
                        }
                    }
                    _recipes = new List<RecipeStore>();
                    //MonoSingleton<GameParameters>.Instance.Items.CraftParameters.ReInitializeCachedRecipesOnce();
                    Dbgl("Loading items...");
                    List<DwellerOutfitItem> list3 = new List<DwellerOutfitItem>(MonoSingleton<GameParameters>.Instance.Items.OutfitList);
                    _usedIDs = new List<string>();
                    foreach (DwellerOutfitItem dwellerOutfitItem in list3)
                    {
                        _usedIDs.Add(dwellerOutfitItem.m_outfitId);
                    }
                    Dbgl("Count before: " + MonoSingleton<GameParameters>.Instance.Items.OutfitList.Length.ToString());
                    foreach (CustomOutfit outfit in list)
                    {
                        DwellerOutfitItem dwellerOutfitItem = CreateOutfit(outfit);
                        list3.Add(dwellerOutfitItem);
                        Dbgl("Item with ID \"" + dwellerOutfitItem.m_outfitId + "\" added.");
                    }
                    AccessTools.FieldRefAccess<ItemParameters, DwellerOutfitItem[]>(MonoSingleton<GameParameters>.Instance.Items, "m_outfitList") = list3.ToArray();
                    Dbgl("Count after: " + list3.Count.ToString());
                    //List<RecipeList.Recipe> list4 = new List<RecipeList.Recipe>();
                    //foreach (RecipeStore recipeStore in _recipes)
                    //{
                    //    MonoSingleton<GameParameters>.Instance.Items.CraftParameters.AddToCachedRecipes(recipeStore.Rarity, recipeStore.Item, recipeStore.Recipe);
                    //    Dbgl("Recipe added for \"" + recipeStore.Recipe.BuiltItemId + "\"");
                    //    list4.Add(recipeStore.Recipe);
                    //}
                    //if (CanUnlockRecipes())
                    //{
                    //    Dbgl($"Unlocking {list4.Count} recipes");
                    //    AccessTools.Method(typeof(ItemParameters), "UnlockRecipies").Invoke(MonoSingleton<GameParameters>.Instance.Items, new object[] { list4 });
                    //}
                    //else
                    //{
                    //    Dbgl("Cannot unlock recipes in questing mode.");
                    //}
                    foreach (CustomOverwrite overwrite in list2)
                    {

                        if (string.IsNullOrEmpty(overwrite.id))
                        {
                            Dbgl("No ID found in overwrite entry!");
                        }
                        else
                        {
                            string id = overwrite.id;
                            DwellerOutfitItem dwellerOutfitItem3 = FindOutfit(MonoSingleton<GameParameters>.Instance.Items.OutfitList, id);
                            if (dwellerOutfitItem3 == null)
                            {
                                Dbgl("No outfit found to overwrite with ID: \"" + id + "\"");
                            }
                            else
                            {
                                Dbgl("Applying overwrites for \"" + id + "\"");
                                CreateOverwrite(dwellerOutfitItem3, overwrite);
                            }
                        }
                    }
                    foreach (UIAtlas uiatlas in _itemAtlasList)
                    {
                        uiatlas.MarkSpriteListAsChanged();
                    }
                }
                catch (Exception ex2)
                {
                    Dbgl("Failed to load custom outfits (general error): " + ex2.Message);
                    Dbgl("At: " + ex2.StackTrace);
                }
            }
        }

        [HarmonyPatch(typeof(RecipeCraftingWindow), "SortRecipeList")]
        public static class RecipeCraftingWindow_SortRecipeList_Patch
        {
            public static void Postfix(List<RecipeEntry.RecipeData> ___m_recipeSortedList)
            {
                for(int i = 0; i <___m_recipeSortedList.Count; i++)
                {
                    if (___m_recipeSortedList[i].BaseItem.CodeId == "Classic_t51b")
                    {
                        Dbgl($"Classic_t51b at {i}");
                        break;
                    }
                }
            }
        }
    }
}
