using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static RecipeList;

namespace CustomOutfits
{
    [BepInPlugin("aedenthorn.CustomOutfits", "Custom Outfits", "0.1.0")]
    public class BepInExPlugin : BaseUnityPlugin
    {
        public static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> freeCraft;

        private static Dictionary<string, string> _pathLookups = new Dictionary<string, string>();
        private static Dictionary<string, Texture2D> _atlases = new Dictionary<string, Texture2D>();
        private static Dictionary<int, Vector2> _slots = new Dictionary<int, Vector2>();
        private static Dictionary<string, ESpecialStat> _statLookup = new Dictionary<string, ESpecialStat>();
        private static Dictionary<string, ushort> _itemAtlasLookup = new Dictionary<string, ushort>();
        private static Dictionary<string, string> localizationDict = new Dictionary<string, string>();
        private static List<UIAtlas> _itemAtlasList = new List<UIAtlas>();
        private static Dictionary<string, ushort> _customItemAtlases = new Dictionary<string, ushort>();
        private static List<string> _usedIDs = new List<string>();
        private static List<RecipeStore> _recipes = new List<RecipeStore>();
        private static bool _customRecipesInjected = false;
        private static bool _customRecipeLootInjected = false;
        private static bool _customContentInitialized = false;
        private static int _customContentInitCount = 0;

        private static readonly Vector3 CustomRecipeIconScale = new Vector3(0.75f, 0.75f, 1f);
        private static readonly Vector3 VanillaRecipeIconScale = Vector3.one;
        private static readonly FieldInfo RecipeEntryItemSpriteField = AccessTools.Field(typeof(RecipeEntry), "m_itemSprite");
        private static readonly FieldInfo DwellerOutfitSpriteField = AccessTools.Field(typeof(DwellerOutfitItem), "m_OutfitSprite");
        private static readonly FieldInfo DwellerBaseItemSortIndexField = AccessTools.Field(typeof(DwellerBaseItem), "m_sortIndex");
        private static readonly FieldInfo UISpriteSpriteField = AccessTools.Field(typeof(UISprite), "mSprite");
        private static readonly FieldInfo UISpriteSpriteSetField = AccessTools.Field(typeof(UISprite), "mSpriteSet");
        private static readonly FieldInfo UIBasicSpriteChangedField = AccessTools.Field(typeof(UIBasicSprite), "mChanged");

        private static bool verboseSpriteLogging = false;
        private static bool _isBuildingVanillaRecipeCache = false;


        private struct RecipeStore
        {
            public EItemRarity Rarity;
            public EItemType Item;
            public Recipe Recipe;
        }

        public class CustomSpriteSizeInfo : MonoBehaviour
        {
            public int OriginalWidth;
            public int OriginalHeight;
            public bool Initialized;
        }

        public class CustomAtlasInfo : MonoBehaviour
        {
            public UIAtlas Atlas;
            public string SpriteName;
            public UIAtlas OriginalAtlas;
        }

        public class CustomOutfitSet
        {
            public List<CustomTexture> textures = new List<CustomTexture>();
            public List<ItemTexture> itemTextures = new List<ItemTexture>();
            public List<CustomOutfit> outfits = new List<CustomOutfit>();
            public List<CustomOverwrite> overwrite = new List<CustomOverwrite>();
        }

        public class CustomTexture
        {
            public string id;
            public string filePath;
            public int width;
            public int height;
        }

        public class CustomOutfit
        {
            public string id;
            public string displayName;
            public string rarity;
            public CustomStat[] stats = new CustomStat[0];
            public string femaleAtlas;
            public int femaleAtlasSlot;
            public string maleAtlas;
            public int maleAtlasSlot;
            public string itemTexture;
            public string craftStat;
            public CustomIngredient[] craft = new CustomIngredient[0];
            public bool initiallyAvailable = true;
            public bool canBeFoundInWasteland;
            public bool canBeFoundOnRaiders;
            public bool canBeFoundInQuest;
            public int sellPrice;
            public bool isExclusive;
            public string color;
            public CustomPart femaleHelmet;
            public CustomPart femaleLargeHeadgear;
            public CustomPart maleHelmet;
            public CustomPart maleLargeHeadgear;
            public CustomPartOpenable femaleGloves;
            public CustomPartOpenable maleGloves;
            public bool hasSkirt;
            public Vector2 scale = new Vector2(0.5f, 0.25f);
        }


        //HELPERS

        private static void RegisterRecipeLoot(ItemParameters items, DwellerOutfitItem item)
        {
            if (items == null || item == null)
                return;

            if (!item.CanBeCrafted || !item.CanBeRecipe)
                return;

            if (!IsRecipeCurrentlyAvailable(item))
            {
                Dbgl("Recipe loot skipped for \"" + item.m_outfitId + "\" because recipe is not currently available.", LogLevel.Debug);
                return;
            }

            try
            {
                object lootManager = AccessTools.Field(typeof(ItemParameters), "m_recipeLootManager").GetValue(items);
                if (lootManager == null)
                {
                    Dbgl("m_recipeLootManager is null, cannot register recipe loot for \"" + item.m_outfitId + "\"", LogLevel.Warning);
                    return;
                }

                DwellerItem dwellerItem = item.GetAsDwellerItem(false);
                if (dwellerItem == null)
                {
                    Dbgl("Failed to create DwellerItem for recipe loot \"" + item.m_outfitId + "\"", LogLevel.Warning);
                    return;
                }

                IList[] allLoot = AccessTools.Field(lootManager.GetType(), "AllLoot").GetValue(lootManager) as IList[];
                if (allLoot == null)
                {
                    Dbgl("Recipe loot manager AllLoot is null for \"" + item.m_outfitId + "\"", LogLevel.Warning);
                    return;
                }

                int typeIndex = (int)EItemType.Outfit;
                if (typeIndex < 0 || typeIndex >= allLoot.Length || allLoot[typeIndex] == null)
                {
                    Dbgl("Recipe loot manager AllLoot bucket is invalid for \"" + item.m_outfitId + "\"", LogLevel.Warning);
                    return;
                }

                bool alreadyPresent = false;
                IList bucket = allLoot[typeIndex];
                for (int i = 0; i < bucket.Count; i++)
                {
                    DwellerItem existing = bucket[i] as DwellerItem;
                    if (existing != null && existing.Id == dwellerItem.Id)
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    bucket.Add(dwellerItem);
                    Dbgl("Registered recipe loot for \"" + item.m_outfitId + "\"", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Dbgl("Failed to register recipe loot for \"" + item.m_outfitId + "\": " + ex.Message, LogLevel.Warning);
            }
        }

        private static HashSet<string> _customOutfitIds = new HashSet<string>();
        private static void RegisterAllCustomRecipeLoot(ItemParameters items)
        {
            if (items == null)
                return;

            DwellerOutfitItem[] outfits = items.OutfitList;
            if (outfits == null)
                return;

            for (int i = 0; i < outfits.Length; i++)
            {
                DwellerOutfitItem item = outfits[i];
                if (item == null)
                    continue;

                if (!_customOutfitIds.Contains(item.m_outfitId))
                    continue;

                RegisterRecipeLoot(items, item);
            }
        }
        private static void ResetCustomRuntimeStateForFreshLoad()
        {
            _recipes.Clear();
            _customOutfitIds.Clear();
            _customRecipesInjected = false;
            _customRecipeLootInjected = false;
            _isBuildingVanillaRecipeCache = false;
        }

        private static bool HasRecipeForBuiltItem(string builtItemId)
        {
            if (string.IsNullOrEmpty(builtItemId))
                return false;

            for (int i = 0; i < _recipes.Count; i++)
            {
                if (_recipes[i].Recipe != null && _recipes[i].Recipe.BuiltItemId == builtItemId)
                    return true;
            }

            return false;
        }

        private sealed class JsonLoadResult
        {
            public CustomOutfitSet Set;
            public string RepairedText;
            public List<string> Repairs = new List<string>();
        }

        private static readonly JsonSerializerSettings OutfitJsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };



        private static void SortRecipeIngredients(RecipeList.Recipe recipe)
        {
            if (recipe == null || recipe.Ingredients == null || recipe.Ingredients.Length <= 1)
                return;

            try
            {
                MethodInfo method = AccessTools.Method(typeof(RecipeList), "SortRecipes");
                if (method != null)
                    method.Invoke(null, new object[] { recipe });
            }
            catch (Exception ex)
            {
                Dbgl("Failed to sort recipe ingredients: " + ex.Message, LogLevel.Debug);
            }
        }

        private static bool IsRecipeCurrentlyAvailable(DwellerOutfitItem item)
        {
            if (item == null)
                return false;

            try
            {
                ItemRecipeData recipeData =
                    AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ItemRecipeData>(item, "m_recipeData");

                if (recipeData == null || recipeData.RelevantData == null)
                    return false;

                return recipeData.RelevantData.isCurrentlyAvailable();
            }
            catch (Exception ex)
            {
                Dbgl("Failed to check recipe availability for \"" + item.m_outfitId + "\": " + ex.Message, LogLevel.Debug);
                return false;
            }
        }
        private static EItemRarity ParseRecipeRarity(string rarity)
        {
            if (string.IsNullOrEmpty(rarity))
                return EItemRarity.Normal;

            string value = rarity.Trim().ToLowerInvariant();

            switch (value)
            {
                case "normal":
                case "common":
                    return EItemRarity.Normal;

                case "rare":
                    return EItemRarity.Rare;

                case "legendary":
                    return EItemRarity.Legendary;

                default:
                    Dbgl("Unknown recipe rarity \"" + rarity + "\". Fallback to Normal.", LogLevel.Warning);
                    return EItemRarity.Normal;
            }
        }
        private static void InjectCustomRecipesIntoCache(ItemParameters items)
        {
            if (items == null || items.CraftParameters == null)
                return;

            for (int i = 0; i < _recipes.Count; i++)
            {
                RecipeStore recipeStore = _recipes[i];
                if (recipeStore.Recipe == null)
                    continue;

                try
                {
                    items.CraftParameters.AddToCachedRecipes(recipeStore.Rarity, recipeStore.Item, recipeStore.Recipe);
                    Dbgl("Injected custom cached recipe for \"" + recipeStore.Recipe.BuiltItemId + "\" (" + recipeStore.Rarity + ")", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Dbgl("Failed to inject cached recipe for \"" + recipeStore.Recipe.BuiltItemId + "\" with rarity \"" + recipeStore.Rarity + "\": " + ex.Message, LogLevel.Warning);
                }
            }
        }

        private static EItemRarity ParseIngredientRarity(string rarity)
        {
            return ParseRecipeRarity(rarity);
        }

        private static bool ValidateCraftDefinition(CustomOutfit outfitData)
        {
            if (outfitData == null)
                return false;

            if (outfitData.craft == null || outfitData.craft.Length == 0)
                return true;

            if (outfitData.craft.Length > 3)
            {
                Dbgl("Recipe for outfit \"" + outfitData.id + "\" has more than 3 ingredients. Fallout Shelter supports at most 3 component slots.", LogLevel.Warning);
                return false;
            }

            HashSet<EComponent> usedComponents = new HashSet<EComponent>();

            for (int i = 0; i < outfitData.craft.Length; i++)
            {
                CustomIngredient ingredient = outfitData.craft[i];
                if (ingredient == null)
                {
                    Dbgl("Recipe for outfit \"" + outfitData.id + "\" contains null ingredient at index " + i, LogLevel.Warning);
                    return false;
                }

                if (string.IsNullOrEmpty(ingredient.component))
                {
                    Dbgl("Recipe for outfit \"" + outfitData.id + "\" contains empty component at index " + i, LogLevel.Warning);
                    return false;
                }

                if (ingredient.count <= 0)
                {
                    Dbgl("Recipe for outfit \"" + outfitData.id + "\" contains invalid count for component \"" + ingredient.component + "\"", LogLevel.Warning);
                    return false;
                }

                EComponent component = SmartParseEnum<EComponent>(ingredient.component, EComponent.None);
                if (component == EComponent.None)
                {
                    Dbgl("Recipe for outfit \"" + outfitData.id + "\" contains unknown component \"" + ingredient.component + "\"", LogLevel.Warning);
                    return false;
                }

                if (usedComponents.Contains(component))
                {
                    Dbgl(
                        "Recipe for outfit \"" + outfitData.id + "\" has duplicate component \"" + ingredient.component +
                        "\". Allowed for direct JSON recipe, but may affect vanilla component-slot logic.",
                        LogLevel.Warning);
                }
                else
                {
                    usedComponents.Add(component);
                }

                ParseIngredientRarity(ingredient.rarity);
            }

            ParseRecipeRarity(outfitData.rarity);
            return true;
        }


        private static JsonLoadResult LoadOutfitSetFromFile(string filePath)
        {
            string originalText = File.ReadAllText(filePath);
            List<string> repairs;
            string repairedText = JsonRepairUtility.Repair(originalText, out repairs);

            ValidateJsonOrThrow(repairedText, filePath);

            CustomOutfitSet set = JsonConvert.DeserializeObject<CustomOutfitSet>(repairedText, OutfitJsonSettings);
            if (set == null)
                throw new Exception("JSON deserialized to null: \"" + Path.GetFileName(filePath) + "\"");

            NormalizeOutfitSet(set);

            JsonLoadResult result = new JsonLoadResult();
            result.Set = set;
            result.RepairedText = repairedText;
            result.Repairs = repairs;
            return result;
        }

        private static class JsonRepairUtility
        {
            public static string Repair(string input, out List<string> repairs)
            {
                repairs = new List<string>();

                if (string.IsNullOrEmpty(input))
                    return input ?? string.Empty;

                string text = input;

                text = RemoveBom(text, repairs);
                text = NormalizeQuotes(text, repairs);
                text = StripComments(text, repairs);
                text = RepairStructure(text, repairs);

                return text;
            }

            private static string RemoveBom(string text, List<string> repairs)
            {
                string trimmed = text.TrimStart('\uFEFF', '\u200B');
                if (trimmed != text)
                    repairs.Add("Removed BOM/zero-width chars at file start");
                return trimmed;
            }

            private static string NormalizeQuotes(string text, List<string> repairs)
            {
                string normalized = text
                    .Replace('“', '"')
                    .Replace('”', '"')
                    .Replace('„', '"')
                    .Replace('«', '"')
                    .Replace('»', '"')
                    .Replace('‘', '\'')
                    .Replace('’', '\'');

                if (normalized != text)
                    repairs.Add("Normalized smart quotes");

                return normalized;
            }

            private static string StripComments(string text, List<string> repairs)
            {
                StringBuilder sb = new StringBuilder(text.Length);
                bool inString = false;
                bool escape = false;
                bool changed = false;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];

                    if (inString)
                    {
                        sb.Append(c);

                        if (escape)
                        {
                            escape = false;
                        }
                        else if (c == '\\')
                        {
                            escape = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '/' && i + 1 < text.Length)
                    {
                        char next = text[i + 1];

                        if (next == '/')
                        {
                            changed = true;
                            i += 2;
                            while (i < text.Length && text[i] != '\n')
                                i++;
                            if (i < text.Length)
                                sb.Append(text[i]);
                            continue;
                        }

                        if (next == '*')
                        {
                            changed = true;
                            i += 2;
                            while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                                i++;
                            i++;
                            continue;
                        }
                    }

                    sb.Append(c);
                }

                if (changed)
                    repairs.Add("Removed comments");

                return sb.ToString();
            }

            private static string RepairStructure(string text, List<string> repairs)
            {
                StringBuilder output = new StringBuilder(text.Length + 64);
                bool inString = false;
                bool escape = false;

                char lastSignificant = '\0';
                int insertedCommas = 0;
                int removedTrailingCommas = 0;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];

                    if (inString)
                    {
                        output.Append(c);

                        if (escape)
                        {
                            escape = false;
                        }
                        else if (c == '\\')
                        {
                            escape = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                            lastSignificant = '"';
                        }

                        continue;
                    }

                    if (c == '"')
                    {
                        if (NeedsCommaBeforeStringProperty(text, i, lastSignificant))
                        {
                            output.Append(',');
                            insertedCommas++;
                            lastSignificant = ',';
                        }

                        inString = true;
                        output.Append(c);
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                    {
                        output.Append(c);
                        continue;
                    }

                    if ((c == '}' || c == ']') && EndsWithCommaIgnoringWhitespace(output))
                    {
                        RemoveTrailingCommaIgnoringWhitespace(output);
                        removedTrailingCommas++;
                    }

                    output.Append(c);

                    if (!char.IsWhiteSpace(c))
                        lastSignificant = c;
                }

                if (insertedCommas > 0)
                    repairs.Add("Inserted missing commas: " + insertedCommas);

                if (removedTrailingCommas > 0)
                    repairs.Add("Removed trailing commas: " + removedTrailingCommas);

                return output.ToString();
            }

            private static bool NeedsCommaBeforeStringProperty(string text, int quoteIndex, char lastSignificant)
            {
                if (!IsValueEnd(lastSignificant))
                    return false;

                int closing = FindStringEnd(text, quoteIndex);
                if (closing < 0)
                    return false;

                int afterClosing = SkipWhitespaceForward(text, closing + 1);
                if (afterClosing >= text.Length)
                    return false;

                return text[afterClosing] == ':';
            }

            private static bool IsValueEnd(char c)
            {
                if (c == '"')
                    return true;
                if (c == '}')
                    return true;
                if (c == ']')
                    return true;
                if (char.IsDigit(c))
                    return true;
                if (c == 'e' || c == 'E')
                    return true;
                if (c == 't') // true
                    return true;
                if (c == 'f') // false
                    return true;
                if (c == 'l') // null
                    return true;

                return false;
            }

            private static int SkipWhitespaceForward(string text, int index)
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
                return index;
            }

            private static int FindStringEnd(string text, int startQuoteIndex)
            {
                bool escape = false;

                for (int i = startQuoteIndex + 1; i < text.Length; i++)
                {
                    char c = text[i];

                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                        return i;
                }

                return -1;
            }


            private static bool EndsWithCommaIgnoringWhitespace(StringBuilder sb)
            {
                for (int i = sb.Length - 1; i >= 0; i--)
                {
                    if (char.IsWhiteSpace(sb[i]))
                        continue;

                    return sb[i] == ',';
                }

                return false;
            }

            private static void RemoveTrailingCommaIgnoringWhitespace(StringBuilder sb)
            {
                for (int i = sb.Length - 1; i >= 0; i--)
                {
                    if (char.IsWhiteSpace(sb[i]))
                        continue;

                    if (sb[i] == ',')
                        sb.Remove(i, 1);

                    return;
                }
            }
        }
        private static void NormalizeOutfitSet(CustomOutfitSet set)
        {
            if (set == null)
                return;

            if (set.textures == null)
                set.textures = new List<CustomTexture>();

            if (set.itemTextures == null)
                set.itemTextures = new List<ItemTexture>();

            if (set.outfits == null)
                set.outfits = new List<CustomOutfit>();

            if (set.overwrite == null)
                set.overwrite = new List<CustomOverwrite>();

            foreach (CustomOutfit outfit in set.outfits)
            {
                if (outfit == null)
                    continue;

                if (outfit.stats == null)
                    outfit.stats = new CustomStat[0];

                if (outfit.craft == null)
                    outfit.craft = new CustomIngredient[0];

                if (outfit.scale == default(Vector2))
                    outfit.scale = new Vector2(0.5f, 0.25f);

                NormalizePart(outfit.femaleHelmet);
                NormalizePart(outfit.femaleLargeHeadgear);
                NormalizePart(outfit.maleHelmet);
                NormalizePart(outfit.maleLargeHeadgear);

                NormalizeOpenablePart(outfit.femaleGloves);
                NormalizeOpenablePart(outfit.maleGloves);
            }

            foreach (CustomOverwrite overwrite in set.overwrite)
            {
                if (overwrite == null)
                    continue;

                if (overwrite.stats == null)
                    overwrite.stats = new CustomStat[0];
            }
        }

        private static void NormalizePart(CustomPart part)
        {
            if (part == null)
                return;

            if (part.atlas != null)
                part.atlas = part.atlas.Trim();
        }

        private static void NormalizeOpenablePart(CustomPartOpenable part)
        {
            if (part == null)
                return;

            NormalizePart(part.open);
            NormalizePart(part.close);
        }

        private static void ValidateJsonOrThrow(string jsonText, string filePath)
        {
            try
            {
                JToken.Parse(jsonText);
            }
            catch (JsonReaderException ex)
            {
                throw new Exception(
                    "Invalid JSON in \"" + Path.GetFileName(filePath) + "\" after auto-repair " +
                    "at line " + ex.LineNumber + ", position " + ex.LinePosition + ": " + ex.Message,
                    ex);
            }
        }

        public class CustomPart
        {
            public string atlas;
            public int x;
            public int y;
            public int grabX;
            public int grabY;
            public int width;
            public int height;
            public bool isExclusive;
            public bool isExcludingFaceMask;
            public bool isIncludedWithoutCostume;
        }

        public class CustomPartOpenable
        {
            public CustomPart open;
            public CustomPart close;
        }

        public class CustomOverwrite
        {
            public string id;
            public string rarity;
            public CustomStat[] stats = new CustomStat[0];
        }

        public class CustomIngredient
        {
            public string component;
            public int count;
            public string rarity;
        }

        public class CustomStat
        {
            public string id;
            public int value;
        }

        public class ItemTexture
        {
            public string id;
            public string atlas;
            public int x;
            public int y;
            public int width;
            public int height;
        }

        private static string OutfitsFolder
        {
            get
            {
                return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "CustomOutfits");
            }
        }

        public static void Dbgl(object obj, LogLevel level = LogLevel.Debug)
        {
            if (!isDebug.Value)
                return;
        }

        public void Awake()
        {
            context = this;

            modEnabled = Config.Bind("General", "ModEnabled", true, "Enable mod");
            isDebug = Config.Bind("General", "IsDebug", true, "Enable debug");
            freeCraft = Config.Bind("Options", "FreeCraft", false, "Enable free crafting");

            InitializeStaticData();
            EnsureOutfitsFolder();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
            Dbgl("Mod initialized", LogLevel.Debug);
        }

        private static void InitializeStaticData()
        {
            _slots = new Dictionary<int, Vector2>
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

            _statLookup = new Dictionary<string, ESpecialStat>
            {
                { "S", ESpecialStat.Strength },
                { "P", ESpecialStat.Perception },
                { "E", ESpecialStat.Endurance },
                { "C", ESpecialStat.Charisma },
                { "I", ESpecialStat.Intelligence },
                { "A", ESpecialStat.Agility },
                { "L", ESpecialStat.Luck }
            };
        }

        private static void EnsureOutfitsFolder()
        {
            if (!Directory.Exists(OutfitsFolder))
                Directory.CreateDirectory(OutfitsFolder);
        }

        public static Texture2D Load(string filePath, string idName, int width, int height)
        {
            if (_pathLookups.TryGetValue(filePath, out var cachedId))
            {
                if (_atlases.TryGetValue(cachedId, out var cachedTexture))
                {
                    if (idName != cachedId)
                        _atlases[idName] = cachedTexture;

                    return cachedTexture;
                }

                _pathLookups.Remove(filePath);
            }

            if (!File.Exists(filePath))
                return null;

            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);

            var loadImageMethod = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
            if (loadImageMethod == null)
            {
                Debug.LogError("Could not find LoadImage method!");
                return null;
            }

            bool loaded = (bool)loadImageMethod.Invoke(null, new object[] { texture, bytes });
            if (!loaded)
                return null;

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _atlases[idName] = texture;
            _pathLookups[filePath] = idName;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            return texture;
        }

        public static bool HasAtlas(string name)
        {
            return _atlases.ContainsKey(name);
        }

        public static Texture2D GetAtlas(string name)
        {
            if (_atlases.TryGetValue(name, out var atlas))
                return atlas;

            return Catalog.Instance.EmptyTexture;
        }

        public static void Unload(string id)
        {
            if (!_atlases.ContainsKey(id))
                return;

            List<string> linkedPaths = new List<string>();
            foreach (var kvp in _pathLookups)
            {
                if (kvp.Value == id)
                    linkedPaths.Add(kvp.Key);
            }

            foreach (string path in linkedPaths)
                _pathLookups.Remove(path);

            UnityEngine.Object.Destroy(_atlases[id]);
            _atlases.Remove(id);
        }

        public static void UnloadAll()
        {
            foreach (var kvp in _atlases)
                UnityEngine.Object.Destroy(kvp.Value);

            _atlases.Clear();
            _pathLookups.Clear();
        }

        private static void ApplyCustomIconScale(UISprite sprite, bool isCustom, float scale = 0.75f)
        {
            if (sprite == null)
                return;

            var sizeInfo = sprite.GetComponent<CustomSpriteSizeInfo>();
            if (sizeInfo == null)
                sizeInfo = sprite.gameObject.AddComponent<CustomSpriteSizeInfo>();

            if (!sizeInfo.Initialized)
            {
                sizeInfo.OriginalWidth = sprite.width;
                sizeInfo.OriginalHeight = sprite.height;
                sizeInfo.Initialized = true;
            }

            if (isCustom)
            {
                sprite.width = Mathf.RoundToInt(sizeInfo.OriginalWidth * scale);
                sprite.height = Mathf.RoundToInt(sizeInfo.OriginalHeight * scale);
            }
            else
            {
                sprite.width = sizeInfo.OriginalWidth;
                sprite.height = sizeInfo.OriginalHeight;
            }

            sprite.MarkAsChanged();
        }

        private static bool IsCustomAtlas(UIAtlas atlas)
        {
            if (atlas == null)
                return false;

            return _itemAtlasList.Contains(atlas);
        }

        private static bool IsRecipeEntryItemSprite(UISprite sprite)
        {
            if (sprite == null || RecipeEntryItemSpriteField == null)
                return false;

            var recipeEntry = sprite.GetComponentInParent<RecipeEntry>(true);
            if (recipeEntry == null)
                return false;

            var recipeItemSprite = RecipeEntryItemSpriteField.GetValue(recipeEntry) as UISprite;
            return ReferenceEquals(recipeItemSprite, sprite);
        }

        private static string MakeCustomSpriteKey(string rawId)
        {
            if (string.IsNullOrEmpty(rawId))
                return null;

            return "co_" + rawId;
        }

        private static bool TryGetCustomItemAtlas(string spriteName, out UIAtlas atlas, out ushort atlasIndex)
        {
            atlas = null;
            atlasIndex = 0;

            if (string.IsNullOrEmpty(spriteName) || _customItemAtlases == null)
                return false;

            if (!_customItemAtlases.TryGetValue(spriteName, out atlasIndex))
                return false;

            if (atlasIndex >= _itemAtlasList.Count)
                return false;

            atlas = _itemAtlasList[atlasIndex];
            return atlas != null;
        }

        private static void AssignSpriteAtlas(UISprite sprite, UIAtlas atlas)
        {
            if (sprite == null || atlas == null || sprite.atlas == atlas)
                return;

            sprite.atlas = atlas;
        }

        private static void ResetSpriteCache(UISprite sprite)
        {
            if (sprite == null)
                return;

            UISpriteSpriteField?.SetValue(sprite, null);
            UISpriteSpriteSetField?.SetValue(sprite, false);
            UIBasicSpriteChangedField?.SetValue(sprite, true);
        }

        private static void RefreshSprite(UISprite sprite)
        {
            if (sprite == null)
                return;

            ResetSpriteCache(sprite);
            sprite.MarkAsChanged();
            sprite.enabled = false;
            sprite.enabled = true;
        }

        private static void RegisterItemTexture(ItemTexture itemTexture)
        {
            string spriteName = MakeCustomSpriteKey(itemTexture.id);
            if (_customItemAtlases.ContainsKey(spriteName))
            {
                Dbgl($"\"{spriteName}\" item texture already loaded, skipping...", LogLevel.Debug);
                return;
            }

            if (!HasAtlas(itemTexture.atlas))
            {
                Dbgl($"\"{itemTexture.atlas}\" not found for item texture \"{spriteName}\"!", LogLevel.Warning);
                return;
            }

            ushort atlasIndex;
            UIAtlas atlas;

            if (_itemAtlasLookup.TryGetValue(itemTexture.atlas, out atlasIndex))
            {
                atlas = _itemAtlasList[atlasIndex];
            }
            else
            {
                atlas = CreateUIAtlas(GetAtlas(itemTexture.atlas), itemTexture.atlas);
                if (atlas == null)
                {
                    Dbgl($"Failed to create UIAtlas for atlas \"{itemTexture.atlas}\"", LogLevel.Error);
                    return;
                }

                _itemAtlasList.Add(atlas);
                atlasIndex = (ushort)(_itemAtlasList.Count - 1);
                _itemAtlasLookup[itemTexture.atlas] = atlasIndex;
            }

            if (atlas.spriteList == null)
                atlas.spriteList = new List<UISpriteData>();

            atlas.spriteList.Add(new UISpriteData
            {
                name = spriteName,
                x = itemTexture.x,
                y = itemTexture.y,
                width = itemTexture.width,
                height = itemTexture.height
            });

            atlas.MarkAsChanged();
            atlas.MarkSpriteListAsChanged();

            _customItemAtlases[spriteName] = atlasIndex;
            Dbgl($"Registered item texture \"{spriteName}\" in atlas \"{itemTexture.atlas}\"", LogLevel.Debug);
        }

        private static DwellerOutfitItem CreateOutfit(CustomOutfit outfitData)
        {
            DwellerOutfitItem item = new DwellerOutfitItem
            {
                CodeId = outfitData.id,
                m_outfitId = outfitData.id,
                m_category = EOutfitCategory.None
            };

            AccessTools.FieldRefAccess<DwellerBaseItem, ExportableMonthDayHour>(item, "m_startDate") = new ExportableMonthDayHour();
            AccessTools.FieldRefAccess<DwellerBaseItem, ExportableMonthDayHour>(item, "m_endDate") = new ExportableMonthDayHour();
            AccessTools.FieldRefAccess<DwellerOutfitItem, OutfitItemSpecialStats>(item, "m_specialStats") = default(OutfitItemSpecialStats);

            if (_usedIDs.Contains(item.CodeId))
                Dbgl($"WARNING: The ID \"{item.CodeId}\" is already used by another mod item!", LogLevel.Warning);

            AccessTools.FieldRefAccess<DwellerBaseItem, EItemRarity>(item, "m_itemRarity") =
    ParseRecipeRarity(outfitData.rarity);

            AccessTools.FieldRefAccess<DwellerBaseItem, int>(item, "m_sellPrice") = outfitData.sellPrice;

            SetupCraftComponents(item, outfitData);

            AccessTools.FieldRefAccess<DwellerOutfitItem, OutfitItemSpecialStats>(item, "m_specialStats") = GetModStats(item, outfitData.stats);

            AccessTools.FieldRefAccess<DwellerOutfitItem, SpecialStatsData[]>(item, "m_modificationStats") = item.ModificationStats;

            string localizationId = "CUSTOM_Outfit_Name_" + item.CodeId;
            item.m_outfitNameLocalizationId = localizationId;
            localizationDict[localizationId] = outfitData.displayName;

            Color[] colors = CreateOutfitColors(outfitData);
            AccessTools.FieldRefAccess<DwellerOutfitItem, DwellerOutfit>(item, "m_maleOutfit") = CreateOutfitVariant(outfitData, colors, true);
            AccessTools.FieldRefAccess<DwellerOutfitItem, DwellerOutfit>(item, "m_femaleOutfit") = CreateOutfitVariant(outfitData, colors, false);

            ItemRecipeData recipeData = new ItemRecipeData();
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipeData, "m_isInitiallyAvailable") = outfitData.initiallyAvailable;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipeData, "m_canBeFoundInQuest") = outfitData.canBeFoundInQuest;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipeData, "m_canBeFoundOnRaiders") = outfitData.canBeFoundOnRaiders;
            AccessTools.FieldRefAccess<ItemRecipeData, bool>(recipeData, "m_canBeFoundInWasteland") = outfitData.canBeFoundInWasteland;
            AccessTools.FieldRefAccess<ItemRecipeData, WeightAndDateData>(recipeData, "m_defaultData") = new WeightAndDateData();
            AccessTools.FieldRefAccess<ItemRecipeData, WeightAndDateData>(recipeData, "m_overrideData") = new WeightAndDateData();
            AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ItemRecipeData>(item, "m_recipeData") = recipeData;

            CreateRecipe(item, outfitData);

            if (!string.IsNullOrEmpty(outfitData.craftStat) && _statLookup.ContainsKey(outfitData.craftStat))
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, ESpecialStat>(item, "m_craftingAssociatedStat") = _statLookup[outfitData.craftStat];

            string rawSpriteName = string.IsNullOrEmpty(outfitData.itemTexture) ? outfitData.id : outfitData.itemTexture;
            string spriteName = MakeCustomSpriteKey(rawSpriteName);

            if (!_customItemAtlases.ContainsKey(spriteName))
                Dbgl($"No registered item texture for outfit \"{outfitData.id}\" (sprite: \"{spriteName}\", raw: \"{rawSpriteName}\")", LogLevel.Warning);
            else
                Dbgl($"Outfit \"{outfitData.id}\" uses sprite \"{spriteName}\"", LogLevel.Debug);

            DwellerOutfitSpriteField?.SetValue(item, spriteName);
            DwellerBaseItemSortIndexField?.SetValue(item, 9999);

            _usedIDs.Add(item.m_outfitId);
            _customOutfitIds.Add(item.m_outfitId);
            item.SetName($"{outfitData.id} ({item.ItemRarity})");

            var male = item.GetOutfitByGender(EGender.Male);
            var female = item.GetOutfitByGender(EGender.Female);
            if (male == null || female == null)
            {
                Dbgl($"Invalid outfit {item.m_outfitId}: male={male != null}, female={female != null}", LogLevel.Error);
                return null;
            }

            return item;
        }

        private static void SetupCraftComponents(DwellerOutfitItem item, CustomOutfit outfitData)
        {
            if (item == null || outfitData == null)
                return;

            if (outfitData.craft == null || outfitData.craft.Length == 0)
                return;

            if (!ValidateCraftDefinition(outfitData))
            {
                item.CanBeCrafted = false;
                item.CanBeRecipe = false;
                Dbgl("Craft disabled for outfit \"" + outfitData.id + "\" because recipe validation failed.", LogLevel.Warning);
                return;
            }

            item.CanBeCrafted = true;
            item.CanBeRecipe = true;

            AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(item, "m_primaryComponent") =
                SmartParseEnum<EComponent>(outfitData.craft[0].component, EComponent.None);

            AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(item, "m_secondaryComponent") = EComponent.None;
            AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(item, "m_tertiaryComponent") = EComponent.None;

            if (outfitData.craft.Length > 1)
            {
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(item, "m_secondaryComponent") =
                    SmartParseEnum<EComponent>(outfitData.craft[1].component, EComponent.None);
            }

            if (outfitData.craft.Length > 2)
            {
                AccessTools.FieldRefAccess<CraftableDwellerBaseItem, EComponent>(item, "m_tertiaryComponent") =
                    SmartParseEnum<EComponent>(outfitData.craft[2].component, EComponent.None);
            }
        }

        private static Color[] CreateOutfitColors(CustomOutfit outfitData)
        {
            if (!string.IsNullOrEmpty(outfitData.color))
                return new[] { ParseColorHexaString(outfitData.color) };

            return new[] { Color.white };
        }

        private static DwellerOutfit CreateOutfitVariant(CustomOutfit outfitData, Color[] colors, bool isMale)
        {
            DwellerOutfit outfit = ScriptableObject.CreateInstance<DwellerOutfit>();
            outfit.m_helmet = null;
            outfit.m_largeHeadgear = null;
            outfit.m_hasSkirt = outfitData.hasSkirt;
            outfit.m_glovePoses = new DwellerGlovePose[2];
            outfit.m_colors = colors;
            outfit.m_coloringMask = ScriptableObject.CreateInstance<DwellerOutfitColoringMask>();

            string atlasName = isMale ? outfitData.maleAtlas : outfitData.femaleAtlas;
            int atlasSlot = isMale ? outfitData.maleAtlasSlot : outfitData.femaleAtlasSlot;
            if (!string.IsNullOrEmpty(atlasName) && HasAtlas(atlasName))
            {
                Texture2D atlas = GetAtlas(atlasName);
                Vector2 offset = _slots[atlasSlot];
                Rect bounds = new Rect(
                    offset.x * atlas.width,
                    offset.y * atlas.height,
                    atlas.width * outfitData.scale.x,
                    atlas.height * outfitData.scale.y);

                outfit.SetAtlasRef(atlas, bounds, 0);
            }

            CustomPart helmet = isMale ? outfitData.maleHelmet : outfitData.femaleHelmet;
            if (helmet != null)
                outfit.m_helmet = CreateHelmet(helmet, outfitData.scale);

            CustomPart largeHeadgear = isMale ? outfitData.maleLargeHeadgear : outfitData.femaleLargeHeadgear;
            if (largeHeadgear != null)
                outfit.m_largeHeadgear = CreateLargeHeadgear(largeHeadgear);

            CustomPartOpenable gloves = isMale ? outfitData.maleGloves : outfitData.femaleGloves;
            if (gloves != null)
                CreateGlovePosesForOutfit(outfit, gloves);

            return outfit;
        }

        private static OutfitItemSpecialStats GetModStats(DwellerOutfitItem item, CustomStat[] stats)
        {
            OutfitItemSpecialStats itemSpecialStats = new OutfitItemSpecialStats();

            if (stats.Length > 4)
                Dbgl($"WARNING: Defining more than 4 SPECIAL attributes for one outfit might crash the game (Outfit \"{item.CodeId}\").", LogLevel.Warning);


            for (int i = 0; i < stats.Length; i++)
            {
                if (stats[i].value > 10)
                    Dbgl($"WARNING: Stat values greater than 10 might be broken (Outfit \"{item.CodeId}\").", LogLevel.Warning);
                switch (stats[i].id)
                {
                    case "S":
                        itemSpecialStats.Strength.Value = stats[i].value;
                        break;
                    case "P":
                        itemSpecialStats.Perception.Value = stats[i].value;
                        break;
                    case "E":
                        itemSpecialStats.Endurance.Value = stats[i].value;
                        break;
                    case "C":
                        itemSpecialStats.Charisma.Value = stats[i].value;
                        break;
                    case "I":
                        itemSpecialStats.Intelligence.Value = stats[i].value;
                        break;
                    case "A":
                        itemSpecialStats.Agility.Value = stats[i].value;
                        break;
                    case "L":
                        itemSpecialStats.Luck.Value = stats[i].value;
                        break;
                }
            }
            return itemSpecialStats;
        }

        private static DwellerHelmet CreateHelmet(CustomPart helmetData, Vector2 scale)
        {
            DwellerHelmet helmet = ScriptableObject.CreateInstance<DwellerHelmet>();
            Texture2D atlas = GetAtlas(helmetData.atlas);

            float y = atlas.height - (helmetData.y + helmetData.height);
            Vector2 offset = new Vector2(helmetData.x / (float)atlas.width, y / atlas.height);

            Rect bounds = new Rect(
                offset.x * atlas.width,
                offset.y * atlas.height,
                atlas.width * scale.x,
                atlas.height * scale.y);

            helmet.SetAtlasRef(atlas, bounds, 0);
            helmet.m_isExclusive = helmetData.isExclusive;
            helmet.m_isExcludingFaceMask = helmetData.isExcludingFaceMask;
            helmet.m_isIncludedWithoutCostume = helmetData.isIncludedWithoutCostume;
            return helmet;
        }

        private static void CreateGlovePosesForOutfit(DwellerOutfit outfit, CustomPartOpenable gloveData)
        {
            if (gloveData?.open == null || gloveData.close == null)
                return;

            outfit.m_glovePoses = new[]
            {
                CreateGlovePose(gloveData.open, EHandPoseType.Open),
                CreateGlovePose(gloveData.close, EHandPoseType.Fist)
            };
        }

        private static DwellerGlovePose CreateGlovePose(CustomPart data, EHandPoseType poseType)
        {
            DwellerGlovePose glovePose = ScriptableObject.CreateInstance<DwellerGlovePose>();
            Texture2D atlas = GetAtlas(data.atlas);

            Vector2 scale = new Vector2(data.width / (float)atlas.width, data.height / (float)atlas.height);
            float y = atlas.height - (data.y + data.height);
            Vector2 offset = new Vector2(data.x / (float)atlas.width, y / atlas.height);

            Rect bounds = new Rect(
                offset.x * atlas.width,
                offset.y * atlas.height,
                atlas.width * scale.x,
                atlas.height * scale.y);

            glovePose.m_pose = poseType;
            glovePose.SetAtlasRef(atlas, bounds, 0);
            return glovePose;
        }

        private static DwellerLargeHeadgear CreateLargeHeadgear(CustomPart data)
        {
            DwellerLargeHeadgear headgear = ScriptableObject.CreateInstance<DwellerLargeHeadgear>();
            Texture2D atlas = GetAtlas(data.atlas);

            Vector2 scale = new Vector2(data.width / (float)atlas.width, data.height / (float)atlas.height);
            float normalizedGrabY = 1f - ((data.grabY - data.y) / (float)data.height);
            Vector2 offset = new Vector2((data.grabX - data.x) / (float)atlas.width, normalizedGrabY);

            Rect bounds = new Rect(
                offset.x * atlas.width * scale.x,
                offset.y * atlas.height * scale.y,
                atlas.width * scale.x,
                atlas.height * scale.y);

            headgear.SetAtlasRef(atlas, bounds, 0);
            return headgear;
        }

        private static void CreateOverwrite(DwellerOutfitItem item, CustomOverwrite data)
        {
            if (!string.IsNullOrEmpty(data.rarity))
                AccessTools.FieldRefAccess<DwellerBaseItem, EItemRarity>(item, "m_itemRarity") = SmartParseEnum(data.rarity, EItemRarity.Normal);

            if (data.stats == null || !data.stats.Any())
                return;

            SpecialStatsData[] stats = new SpecialStatsData[data.stats.Length];

            OutfitItemSpecialStats itemSpecialStats = new OutfitItemSpecialStats();

            for (int i = 0; i < data.stats.Length; i++)
            {
                switch (data.stats[i].id)
                {
                    case "S":
                        itemSpecialStats.Strength.Value = data.stats[i].value;
                        break;
                    case "P":
                        itemSpecialStats.Perception.Value = data.stats[i].value;
                        break;
                    case "E":
                        itemSpecialStats.Endurance.Value = data.stats[i].value;
                        break;
                    case "C":
                        itemSpecialStats.Charisma.Value = data.stats[i].value;
                        break;
                    case "I":
                        itemSpecialStats.Intelligence.Value = data.stats[i].value;
                        break;
                    case "A":
                        itemSpecialStats.Agility.Value = data.stats[i].value;
                        break;
                    case "L":
                        itemSpecialStats.Luck.Value = data.stats[i].value;
                        break;
                }
            }
            AccessTools.FieldRefAccess<DwellerOutfitItem, OutfitItemSpecialStats>(item, "m_specialStats") = itemSpecialStats;

            AccessTools.FieldRefAccess<DwellerOutfitItem, SpecialStatsData[]>(item, "m_modificationStats") = item.ModificationStats;
        }

        private static void CreateRecipe(DwellerOutfitItem item, CustomOutfit outfitData)
        {
            if (item == null || outfitData == null)
                return;

            if (outfitData.craft == null || outfitData.craft.Length == 0)
                return;

            if (!ValidateCraftDefinition(outfitData))
                return;

            List<RecipeList.IngredientEntry> ingredients = new List<RecipeList.IngredientEntry>();

            for (int i = 0; i < outfitData.craft.Length; i++)
            {
                CustomIngredient ingredientData = outfitData.craft[i];
                if (ingredientData == null)
                    continue;

                EComponent component = SmartParseEnum<EComponent>(ingredientData.component, EComponent.None);
                if (component == EComponent.None)
                    continue;

                RecipeList.IngredientEntry entry = new RecipeList.IngredientEntry();
                entry.Component = component;
                entry.Count = ingredientData.count;
                entry.Rarity = ParseIngredientRarity(ingredientData.rarity);
                ingredients.Add(entry);
            }

            if (ingredients.Count == 0)
            {
                Dbgl("Recipe for outfit \"" + outfitData.id + "\" has no valid ingredients.", LogLevel.Warning);
                return;
            }

            RecipeList.Recipe recipe = new RecipeList.Recipe();
            recipe.BuiltItemId = item.m_outfitId;
            recipe.Ingredients = ingredients.ToArray();

            SortRecipeIngredients(recipe);

            RecipeStore store = new RecipeStore();
            store.Rarity = ParseRecipeRarity(outfitData.rarity);
            store.Item = EItemType.Outfit;
            store.Recipe = recipe;

            _recipes.Add(store);

            Dbgl(
                "Prepared direct JSON recipe for outfit \"" + outfitData.id + "\" with rarity \"" + store.Rarity +
                "\" and " + ingredients.Count + " ingredient(s).",
                LogLevel.Debug);
        }

        private static DwellerOutfitItem FindOutfit(DwellerOutfitItem[] items, string id)
        {
            return items.FirstOrDefault(item => item.m_outfitId == id);
        }

        private static T SmartParseEnum<T>(string value, T defaultValue) where T : struct, IConvertible
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            foreach (T enumValue in Enum.GetValues(typeof(T)))
            {
                if (string.Equals(enumValue.ToString(), value, StringComparison.InvariantCultureIgnoreCase))
                    return enumValue;
            }

            return defaultValue;
        }

        private static T SmartParseEnum<T>(string value) where T : struct, IConvertible
        {
            return SmartParseEnum(value, default(T));
        }

        private static Color ParseColorHexaString(string hexa)
        {
            if (string.IsNullOrEmpty(hexa) || hexa.Length != 6)
            {
                Dbgl($"The color is not in the correct format: \"{hexa}\"", LogLevel.Warning);
                return Color.white;
            }

            try
            {
                return new Color(
                    Convert.ToByte(hexa.Substring(0, 2), 16) / 255f,
                    Convert.ToByte(hexa.Substring(2, 2), 16) / 255f,
                    Convert.ToByte(hexa.Substring(4, 2), 16) / 255f);
            }
            catch
            {
                return Color.white;
            }
        }

        private static void TryLoadTexture(CustomTexture tex)
        {
            string filePath = tex.filePath;
            if (!File.Exists(filePath))
            {
                Dbgl($"\tFile not found: \"{filePath}\"", LogLevel.Debug);
                return;
            }

            if (HasAtlas(tex.id))
            {
                Dbgl($"Atlas \"{tex.id}\" already loaded.", LogLevel.Debug);
                return;
            }

            if (Load(filePath, tex.id, tex.width, tex.height) != null)
                Dbgl($"\t\"{tex.id}\" loaded.", LogLevel.Debug);
            else
                Dbgl($"\tFailed to load \"{tex.id}\"", LogLevel.Debug);
        }

        private static UIAtlas CreateUIAtlas(Texture2D texture, string name)
        {
            var sprite = UnityEngine.Object.FindObjectsByType<UISprite>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(s => s.atlas?.spriteMaterial != null);

            if (sprite?.atlas == null)
            {
                Dbgl("Couldn't find atlas or sprite material", LogLevel.Debug);
                return null;
            }

            UIAtlas newAtlas = UnityEngine.Object.Instantiate(sprite.atlas);
            newAtlas.name = name;
            newAtlas.correctMipmaps = false;
            newAtlas.holdResolution = false;
            newAtlas.padding = 2;
            newAtlas.pixelSize = 0.5f;
            newAtlas.replacement = null;

            Shader shader =
                Shader.Find("Unlit/Transparent Colored") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Sprites/Default");

            Material material = new Material(shader);
            material.mainTexture = texture;

            AccessTools.FieldRefAccess<UIAtlas, Material>(newAtlas, "material") = material;
            newAtlas.spriteMaterial = material;
            newAtlas.spriteList = new List<UISpriteData>();

            var spriteIndices = AccessTools.FieldRefAccess<UIAtlas, Dictionary<string, int>>(newAtlas, "mSpriteIndices");
            spriteIndices?.Clear();

            UnityEngine.Object.DontDestroyOnLoad(newAtlas);
            UnityEngine.Object.DontDestroyOnLoad(material);
            return newAtlas;
        }

        private static void RefreshAllCustomAtlases()
        {
            if (_itemAtlasList == null)
                return;

            foreach (var atlas in _itemAtlasList)
            {
                if (atlas == null)
                    continue;

                atlas.MarkSpriteListAsChanged();

                if (atlas.spriteMaterial == null)
                    atlas.spriteMaterial = new Material(Shader.Find("Unlit/Transparent"));
            }
        }

        private static void ForceRefreshUI()
        {
            try
            {
                Dbgl("Forcing atlas refresh...", LogLevel.Debug);
                RefreshAllCustomAtlases();
                Dbgl("Atlas refresh completed", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Dbgl($"Error in ForceRefreshUI: {ex.Message}", LogLevel.Debug);
            }
        }

        private static void LoadOutfitSet(string filePath, List<CustomOutfit> outfitsToAdd, List<CustomOverwrite> overwrites)
        {
            Dbgl("Loading \"" + Path.GetFileName(filePath) + "\"", LogLevel.Debug);

            try
            {
                JsonLoadResult loadResult = LoadOutfitSetFromFile(filePath);
                CustomOutfitSet set = loadResult.Set;

                if (loadResult.Repairs != null && loadResult.Repairs.Count > 0)
                {
                    Dbgl(
                        "Auto-repaired JSON \"" + Path.GetFileName(filePath) + "\": " +
                        string.Join("; ", loadResult.Repairs),
                        LogLevel.Warning);
                }

                outfitsToAdd.AddRange(set.outfits);
                overwrites.AddRange(set.overwrite);

                foreach (CustomTexture texture in set.textures)
                {
                    if (texture == null || string.IsNullOrEmpty(texture.filePath))
                        continue;

                    texture.filePath = Path.Combine(Path.GetDirectoryName(filePath), texture.filePath);
                    Dbgl("texture path: " + texture.filePath, LogLevel.Debug);
                    TryLoadTexture(texture);
                }

                foreach (ItemTexture itemTexture in set.itemTextures)
                {
                    if (itemTexture == null || string.IsNullOrEmpty(itemTexture.id))
                        continue;

                    RegisterItemTexture(itemTexture);
                    Dbgl("[CustomOutfits] Registered item texture: " + MakeCustomSpriteKey(itemTexture.id));
                }
            }
            catch (Exception ex)
            {
                Dbgl("Failed to load config \"" + Path.GetFileName(filePath) + "\"", LogLevel.Error);
                Dbgl("Details: " + ex, LogLevel.Error);
            }
        }

        private static void AddCustomOutfits(GameParameters game, List<CustomOutfit> outfitsToAdd)
        {
            List<DwellerOutfitItem> existingOutfits = new List<DwellerOutfitItem>(game.Items.OutfitList);
            _usedIDs = existingOutfits.Select(o => o.m_outfitId).ToList();

            Dbgl("Count before: " + game.Items.OutfitList.Length, LogLevel.Debug);

            foreach (var outfitData in outfitsToAdd)
            {
                if (_usedIDs.Contains(outfitData.id))
                    continue;

                var newOutfit = CreateOutfit(outfitData);
                if (newOutfit == null)
                    continue;

                existingOutfits.Add(newOutfit);
                Dbgl("[CustomOutfits] Created outfit: " + newOutfit.m_outfitId);
                Dbgl($"Item with ID \"{newOutfit.m_outfitId}\" added.", LogLevel.Debug);
            }

            AccessTools.FieldRefAccess<ItemParameters, DwellerOutfitItem[]>(game.Items, "m_outfitList") = existingOutfits.ToArray();
            Dbgl("Count after: " + existingOutfits.Count, LogLevel.Debug);

        }



        private static void ApplyOverwrites(GameParameters game, List<CustomOverwrite> overwrites)
        {
            DwellerOutfitItem[] outfits = game.Items.OutfitList;

            foreach (var overwrite in overwrites)
            {
                if (string.IsNullOrEmpty(overwrite.id))
                {
                    Dbgl("No ID found in overwrite entry!", LogLevel.Warning);
                    continue;
                }

                DwellerOutfitItem targetOutfit = FindOutfit(outfits, overwrite.id);
                if (targetOutfit == null)
                {
                    Dbgl($"No outfit found to overwrite with ID: \"{overwrite.id}\"", LogLevel.Warning);
                    continue;
                }

                Dbgl($"Applying overwrites for \"{overwrite.id}\"", LogLevel.Debug);
                CreateOverwrite(targetOutfit, overwrite);
            }
        }

        [HarmonyPatch(typeof(UISprite), nameof(UISprite.spriteName))]
        [HarmonyPatch(MethodType.Setter)]
        public static class UISprite_set_spriteName_Patch
        {
            private static bool _isRestoring;

            public static void Postfix(UISprite __instance, string value)
            {
                if (!modEnabled.Value || __instance == null || _isRestoring || string.IsNullOrEmpty(value))
                    return;

                if (IsRecipeEntryItemSprite(__instance))
                {
                    if (verboseSpriteLogging)
                        Dbgl($"[spriteName patch] skipped RecipeEntry item sprite '{value}' on '{__instance.name}'", LogLevel.Debug);

                    return;
                }

                var info = __instance.GetComponent<CustomAtlasInfo>();
                if (TryGetCustomItemAtlas(value, out var customAtlas, out _))
                {
                    if (info == null)
                        info = __instance.gameObject.AddComponent<CustomAtlasInfo>();

                    if (info.OriginalAtlas == null && __instance.atlas != null && !IsCustomAtlas(__instance.atlas))
                        info.OriginalAtlas = __instance.atlas;

                    if (__instance.atlas != customAtlas)
                    {
                        _isRestoring = true;
                        AssignSpriteAtlas(__instance, customAtlas);
                        _isRestoring = false;
                    }

                    ApplyCustomIconScale(__instance, true);
                    __instance.MarkAsChanged();

                    if (verboseSpriteLogging)
                        Dbgl($"[spriteName patch] applied custom atlas for sprite '{value}' on '{__instance.name}'", LogLevel.Debug);

                    return;
                }

                if (info != null && info.OriginalAtlas != null && __instance.atlas != info.OriginalAtlas)
                {
                    _isRestoring = true;
                    AssignSpriteAtlas(__instance, info.OriginalAtlas);
                    _isRestoring = false;
                }

                ApplyCustomIconScale(__instance, false);
                __instance.MarkAsChanged();

                if (verboseSpriteLogging)
                    Dbgl($"[spriteName patch] restored original atlas for vanilla sprite '{value}' on '{__instance.name}'", LogLevel.Debug);
            }
        }

        [HarmonyPatch(typeof(UISprite), "OnFill")]
        public static class UISprite_OnFill_DebugPatch
        {
            public static void Prefix(UISprite __instance)
            {
                if (!modEnabled.Value || string.IsNullOrEmpty(__instance.spriteName))
                    return;

                if (_customItemAtlases == null || !_customItemAtlases.ContainsKey(__instance.spriteName))
                    return;

                if (__instance.atlas == null)
                {
                    Dbgl($"[DEBUG] UISprite '{__instance.name}' custom sprite '{__instance.spriteName}' has NULL atlas", LogLevel.Warning);
                    return;
                }

                var spriteData = __instance.atlas.GetSprite(__instance.spriteName);
                if (spriteData == null)
                    Dbgl($"[DEBUG] UISprite '{__instance.name}' atlas '{__instance.atlas.name}' has no sprite '{__instance.spriteName}'", LogLevel.Warning);
            }
        }

        [HarmonyPatch(typeof(ResourceParticleMgr), "AddResourceParticle")]
        public static class ResourceParticleMgr_AddResourceParticle_Patch
        {
            public static void Prefix(string ___m_spriteName, ref UIAtlas ___m_outfitAtlas)
            {
                if (string.IsNullOrEmpty(___m_spriteName))
                    return;

                if (TryGetCustomItemAtlas(___m_spriteName, out var customAtlas, out _))
                    ___m_outfitAtlas = customAtlas;
            }
        }

        [HarmonyPatch(typeof(LocalizationManager), "GetTermTranslation")]
        public static class LocalizationManager_GetTermTranslation_Patch
        {
            public static bool Prefix(string Term, ref string __result)
            {
                if (string.IsNullOrEmpty(Term) || !modEnabled.Value)
                    return true;

                if (!localizationDict.TryGetValue(Term, out var translation))
                    return true;

                __result = translation;
                return false;
            }
        }


        [HarmonyPatch(typeof(GameParameters), "OnAwake")]
        public static class GameParameters_OnAwake_Patch
        {
            public static void Prefix()
            {
                if(freeCraft.Value)
                    MonoSingleton<VaultGUIManager>.Instance.m_recipeCraftingWindow.DebugFreeCrafting = true;
                _customContentInitCount++;
                Dbgl("[CustomOutfits] GameParameters.OnAwake prefix entered");
                if (!modEnabled.Value)
                    return;

                if (_customContentInitialized)
                {
                    Dbgl("Skipping duplicate custom content initialization pass #" + _customContentInitCount, LogLevel.Warning);
                    return;
                }

                ResetCustomRuntimeStateForFreshLoad();

                Dbgl("GameParameters.OnAwake prefix entered");
                Dbgl("Awake", LogLevel.Debug);

                EnsureOutfitsFolder();

                try
                {
                    Dbgl("CustomOutfits mod version: " + context.Info.Metadata.Version, LogLevel.Debug);

                    List<CustomOutfit> outfitsToAdd = new List<CustomOutfit>();
                    List<CustomOverwrite> overwrites = new List<CustomOverwrite>();

                    foreach (string filePath in Directory.GetFiles(OutfitsFolder, "*.json", SearchOption.AllDirectories))
                        LoadOutfitSet(filePath, outfitsToAdd, overwrites);

                    ForceRefreshUI();

                    GameParameters game = MonoSingleton<GameParameters>.Instance;
                    AddCustomOutfits(game, outfitsToAdd);
                    ApplyOverwrites(game, overwrites);
                    RefreshAllCustomAtlases();
                    _customContentInitialized = true;
                    Dbgl("Custom content initialization completed", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Dbgl("Failed to load custom outfits (general error): " + ex.Message, LogLevel.Error);
                    Dbgl("At: " + ex.StackTrace, LogLevel.Error);
                }
            }
        }

        [HarmonyPatch(typeof(SurvivalWindow), "Initialize")]
        public static class SurvivalWindow_Initialize_Patch
        {
            public static void Postfix(SurvivalWindow __instance)
            {
                if (!modEnabled.Value)
                    return;

                try
                {
                    var unlockedRecipesField = AccessTools.Field(typeof(SurvivalWindow), "m_unlockedRecipes");
                    if (!(unlockedRecipesField?.GetValue(__instance) is List<string> unlockedRecipes))
                        return;

                    int added = 0;
                    foreach (var recipeStore in _recipes)
                    {
                        if (recipeStore.Recipe == null || unlockedRecipes.Contains(recipeStore.Recipe.BuiltItemId))
                            continue;

                        unlockedRecipes.Add(recipeStore.Recipe.BuiltItemId);
                        added++;
                        Dbgl("Auto-unlocked recipe: " + recipeStore.Recipe.BuiltItemId, LogLevel.Debug);
                    }

                    if (added > 0)
                        Dbgl($"Auto-unlocked {added} custom recipes in SurvivalWindow", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Dbgl("Error auto-unlocking recipes: " + ex.Message, LogLevel.Error);
                }
            }
        }
        [HarmonyPatch(typeof(ItemParameters), "Initialize")]
        public static class ItemParameters_Initialize_Patch
        {
            public static void Postfix(ItemParameters __instance)
            {
                if (!modEnabled.Value || _customRecipeLootInjected)
                    return;

                try
                {
                    RegisterAllCustomRecipeLoot(__instance);
                    _customRecipeLootInjected = true;
                    Dbgl("Custom recipe loot injected after ItemParameters.Initialize", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Dbgl("Failed to inject custom recipe loot in ItemParameters postfix: " + ex.Message, LogLevel.Error);
                }
            }
        }

        [HarmonyPatch(typeof(CraftParameter), "InitializeCachedRecipesOnce")]
        public static class CraftParameter_InitializeCachedRecipesOnce_Patch
        {
            public static void Prefix()
            {
                _isBuildingVanillaRecipeCache = true;
            }

            public static void Postfix(CraftParameter __instance)
            {
                _isBuildingVanillaRecipeCache = false;

                if (!modEnabled.Value || _customRecipesInjected)
                    return;

                try
                {
                    GameParameters game = MonoSingleton<GameParameters>.Instance;
                    if (game == null || game.Items == null)
                        return;

                    InjectCustomRecipesIntoCache(game.Items);
                    _customRecipesInjected = true;
                    Dbgl("Custom recipes injected after vanilla InitializeCachedRecipesOnce", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Dbgl("Failed to inject custom recipes in CraftParameter postfix: " + ex.Message, LogLevel.Error);
                }
            }
        }

        [HarmonyPatch(typeof(RecipeList), "BuildRecipe")]
        public static class RecipeList_BuildRecipe_Patch
        {
            public static bool Prefix(EItemType eItemType, CraftableDwellerBaseItem item, ItemParameters itemParameter, ref RecipeList.Recipe __result, string itemId = null)
            {
                if (!modEnabled.Value)
                    return true;

                if (!_isBuildingVanillaRecipeCache)
                    return true;

                if (eItemType != EItemType.Outfit)
                    return true;

                string id = itemId;
                if (string.IsNullOrEmpty(id) && item is DwellerOutfitItem)
                    id = ((DwellerOutfitItem)item).m_outfitId;

                if (string.IsNullOrEmpty(id))
                    return true;

                if (_customOutfitIds.Contains(id))
                {
                    Dbgl("Skipping vanilla BuildRecipe for custom outfit \"" + id + "\"", LogLevel.Debug);
                    __result = null;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(RecipeEntry), "FillData")]
        public static class RecipeEntry_FillData_AtlasFixPatch
        {
            public static void Postfix(RecipeEntry __instance, RecipeEntry.RecipeData recipeData)
            {
                if (!modEnabled.Value || __instance == null || recipeData == null)
                    return;

                if (recipeData.ItemType != EItemType.Outfit)
                    return;

                try
                {
                    UISprite itemSprite = RecipeEntryItemSpriteField?.GetValue(__instance) as UISprite;
                    if (itemSprite == null || string.IsNullOrEmpty(itemSprite.spriteName))
                        return;

                    if (TryGetCustomItemAtlas(itemSprite.spriteName, out var customAtlas, out _))
                    {
                        AssignSpriteAtlas(itemSprite, customAtlas);
                        itemSprite.transform.localScale = CustomRecipeIconScale;
                    }
                    else
                    {
                        AssignSpriteAtlas(itemSprite, MonoSingleton<GameParameters>.Instance.Items.OutfitAtlas);
                        itemSprite.transform.localScale = VanillaRecipeIconScale;
                    }

                    RefreshSprite(itemSprite);
                }
                catch (Exception ex)
                {
                    Dbgl("[RecipeEntry_FillData_AtlasFixPatch] " + ex, LogLevel.Error);
                }
            }
        }
    }
}

