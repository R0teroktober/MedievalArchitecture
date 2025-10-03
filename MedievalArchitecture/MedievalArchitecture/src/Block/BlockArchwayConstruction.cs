using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorConstructible : BlockBehavior
    {
        [DocumentAsJson("Required")]
        AssetLocation[] ConstructLevel1blockCodes;

        [DocumentAsJson("Required")]
        string actionlangcode;
        public BlockBehaviorConstructible(Block block) : base(block) { }

        public override void Initialize(JsonObject properties)
        {

            if (properties["ConstructLevel1Materials"].Exists)
            {

                string[] ConstructLevel1Materials = properties["ConstructLevel1Materials"].AsArray<string>();
                this.ConstructLevel1blockCodes = new AssetLocation[ConstructLevel1Materials.Length];
                for (int i = 0; i < ConstructLevel1Materials.Length; i++)
                {
                    this.ConstructLevel1blockCodes[i] = AssetLocation.Create(ConstructLevel1Materials[i], "game");

                }

            }
            //actionlangcode = properties["actionLangCode"].AsString();
            base.Initialize(properties);

        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return false;
            }


            if (!byPlayer.InventoryManager.ActiveHotbarSlot.Empty)
            {
                Block constructionBlock = blockSel.Block;
                AssetLocation activeBlockCode = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible?.Code;

                if (constructionBlock != null && activeBlockCode != null && ConstructLevel1blockCodes.Contains(activeBlockCode))
                {
                    handling = EnumHandling.PreventDefault;

                    if (constructionBlock.Attributes.KeyExists("rock"))
                    {
                        return true;

                    }
                    else
                    {
                        return false;
                    }

                }
                else {
                    handling = EnumHandling.PassThrough;
                    return true; }

            }
            else return true;

        }

    }
}
