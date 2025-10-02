using System.Collections.Generic;
using System.Linq;
using System.Text;
using AttributeRenderingLibrary;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Datastructures;


public class BlockBehaviorConstructionStateChanger : BlockBehavior
{
    public BlockBehaviorConstructionStateChanger(Block block) : base(block) { }


    public override bool OnBlockInteractStart(BlockPos pos, IPlayer byPlayer, BlockSelection blockSel, ItemSlot itemslot, ref EnumHandHandling handling)
    {
        ItemStack heldItem = itemslot?.Itemstack;
        if (heldItem == null) return false;

        var world = byPlayer.Entity.World;
        var be = world.BlockAccessor.GetBlockEntity(pos);
        if (be == null) return false;

        // Hole das Behavior aus Attribute Rendering Library
        var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
        if (beh == null) return false;


        var attr = beh.Variants;

        // Aktuellen state herausfinden
        int state = attr.FindByVariant("state-0", );
        string heldItemCode = heldItem.Collectible.Code.ToString();

        // === Schritt 1: State 0 -> 1 mit rock-* ===
        if (state == 0 && heldItemCode.StartsWith("game:rock-"))
        {
            string rockType = heldItem.Collectible.Code.Path;  // z. B. "rock-granite"
            if (rockType.StartsWith("rock-")) rockType = rockType.Substring(5);

            attr.SetInt("state", 1);
            attr.SetString("rock", rockType);

            be.MarkDirty(true);
            handling = EnumHandHandling.PreventDefault;
            return true;
        }

        // === Schritt 2: State 1 -> 2 mit Mörtel ===
        if (state == 1 && heldItemCode == "game:mortar")
        {
            attr.SetInt("state", 2);

            be.MarkDirty(true);
            handling = EnumHandHandling.PreventDefault;
            return true;
        }

        // === Schritt 3: Fertigstellung mit stone-* Block ===
        if (state == 2 && heldItemCode.StartsWith("game:stone-"))
        {
            Block newBlock = world.BlockAccessor.GetBlock(new AssetLocation("confession:arch_small"));
            if (newBlock != null)
            {
                world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
                world.BlockAccessor.RemoveBlockEntity(pos);
            }

            handling = EnumHandHandling.PreventDefault;
            return true;
        }

        return false;
    }
    public override bool OnBlockInteractStep(float secondsUsed, BlockPos pos, IPlayer byPlayer, BlockSelection blockSel, ItemSlot itemslot, ref EnumHandHandling handling)
    { return true; }
}

