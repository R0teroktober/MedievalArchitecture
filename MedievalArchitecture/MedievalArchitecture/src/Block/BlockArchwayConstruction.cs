using AttributeRenderingLibrary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Datastructures;
using Vintagestory.ServerMods.NoObf;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorConstructionStateChanger : BlockBehavior, IInteractable
    {
        public BlockBehaviorConstructionStateChanger(Block block) : base(block) { }

        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> cobblestoneCodeByType = new();
        public Dictionary<string, string> brickCodeByType = new();

        public override void Initialize(JsonObject properties)

        {
            stateCodeByType = properties["state"].AsObject<Dictionary<string, string>>();
            rockCodeByType = properties["rock"].AsObject<Dictionary<string, string>>();
            cobblestoneCodeByType = properties["cobblestone"].AsObject<Dictionary<string, string>>();
            brickCodeByType = properties["brick"].AsObject<Dictionary<string, string>>();
            base.Initialize(properties);

        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            ItemStack heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (heldItem == null) return false;

   
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be == null) return false;

            // Hole das Behavior aus Attribute Rendering Library
            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh == null) return false;


            var attr = beh.Variants;

            // Aktuellen state herausfinden
            attr.FindByVariant<string>(stateCodeByType, out string state);
            {
                string heldItemCode = heldItem.Collectible.Code.ToString();
                if (state == "0" && heldItemCode.StartsWith("game:stone-"))
                {
                    string rockType = heldItem.Collectible.Code.Path;  // z. B. "rock-granite"
                    if (rockType.StartsWith("stone-")) rockType = rockType.Substring(6);

                    attr.Set("rock", rockType);
                    be.MarkDirty(true);
                    handling = EnumHandling.PreventDefault;
                    return true;
                }
                else if (state == "1" && heldItemCode == "game:mortar")
                {
                    attr.Set("state", "2");
                    be.MarkDirty(true);
                    handling = EnumHandling.PreventDefault;
                    return true;

                }
                else if (state == "2" && heldItemCode.StartsWith("game:cobblestone-*"))
                {
                    string materialType = heldItem.Collectible.Code.Path;
                    if (materialType.StartsWith("rock-"))
                    {
                        materialType = materialType.Substring(5);
                        attr.Set("rock", materialType);
                    }
                    else if (materialType.StartsWith("cobblestone-"))
                    {
                        materialType = materialType.Substring(12);
                        attr.Set("cobblestone", materialType);
                    }
                    else if (materialType.StartsWith("brick-"))
                    {
                        materialType = materialType.Substring(6);
                        attr.Set("brick", materialType);
                    }
                    be.MarkDirty(true);
                    return true;
                }

            }



            return false;
        }
  
    }
}

