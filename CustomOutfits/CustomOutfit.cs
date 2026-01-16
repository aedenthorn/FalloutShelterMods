using System.Collections.Generic;
using UnityEngine;

namespace CustomOutfits
{
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
}