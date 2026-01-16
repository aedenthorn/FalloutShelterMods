using System.Collections.Generic;

namespace CustomOutfits
{
    public class CustomOutfitSet
    {
        public List<CustomTexture> textures = new List<CustomTexture>();
        public List<ItemTexture> itemTextures = new List<ItemTexture>();
        public List<CustomOutfit> outfits = new List<CustomOutfit>();
        public List<CustomOverwrite> overwrite = new List<CustomOverwrite>();
    }
}