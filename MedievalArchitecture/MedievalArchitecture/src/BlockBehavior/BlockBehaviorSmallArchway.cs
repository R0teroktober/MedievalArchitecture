using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorSmallArchway(Block block) : BlockBehavior(block), IInteractable
    {
        private bool constructionInProgress = false;
        AssetLocation finishSound = new AssetLocation();
        AssetLocation finishGlassSound = new AssetLocation();
        public Dictionary<string, string> glassCodeByType = new();
        public Dictionary<string, string> woodCodeByType = new();
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();
        public Dictionary<string, string> originblockCodeByType = new();

        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            var config = MedievalArchitectureModSystem.Config;
            woodCodeByType = new Dictionary<string, string>(config.WoodCodeByType);
            glassCodeByType = new Dictionary<string, string>(config.GlassCodeByType);
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);
            styleCodeByType = new Dictionary<string, string>(config.StyleCodeByType);
            rockCodeByType = new Dictionary<string, string>(config.RockCodeByType);
            originblockCodeByType = new Dictionary<string, string>(config.OriginblockCodeByType);
            finishSound = AssetLocation.Create("sounds/block/planks");


        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PreventDefault;
            return true;
        }
        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PreventDefault;
            secondsUsed = 0;
            constructionInProgress = false;
            return true;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;


            if (byPlayer.CurrentBlockSelection == null) return false;
            //  Nur auf dem Client Animation
            if (world.Side == EnumAppSide.Client)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.BlockInteract);

            }
            return secondsUsed < 0.5;

        }
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;


            if (secondsUsed < 0.5) return; // Bauzeit
            if (constructionInProgress) return;
            constructionInProgress = true;
            // Baumaterial
            if (byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack == null || blockSel == null) return;
            var heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            string heldItemCode = heldItem.Collectible.Code.ToString();

            // Block
            var be = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
            if (be == null) return;
            // Blockbehavior
            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh?.Variants == null) return;
            var oldVariants = beh.Variants.Clone();
            oldVariants.FindByVariant(styleCodeByType, out string style);
            oldVariants.FindByVariant(rockCodeByType, out string rock);
            oldVariants.FindByVariant(originblockCodeByType, out string originblock);
            var block = byPlayer.CurrentBlockSelection.Block;
            if (block == null) return;
            string orientation = block.Variant.TryGetValue("side");
            secondsUsed = 0; // Bauzeit zurücksetzten



            if (heldItemCode.StartsWith("game:plank-"))
            {
                TryAddPlanks(world, byPlayer, be, beh, heldItem, orientation, style, rock, originblock);
            } else if (heldItemCode.StartsWith("confession:window-"))
            { 
                TryAddGlass(world, byPlayer, be, beh, heldItem,orientation, style, rock, originblock);
            }

        }


        private void TryAddPlanks(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem,string orientation, string style, string rock, string originblock)
        {
            // Item
            string heldItemCode = heldItem.Collectible.Code.ToString();
            string wood = heldItemCode.Substring(11);

            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation($"confession:arch_small_wood-{orientation}"));
            if (newBlock == null) return;

            // Setzen und Attribute übertragen
            BlockPos newBlockPos = player.CurrentBlockSelection.Position.Copy();
            world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);

            // Callback um auf Blockentity zu warten
            world.RegisterCallback(dt =>
            {
                var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                if (newBeh == null) return;
                var newMyBe = newBe as BlockEntitySmallArchwayConstruction;
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) player.InventoryManager.ActiveHotbarSlot.TakeOut(1);
                if (newMyBe != null)
                {
                    newMyBe.SetVariants(new Dictionary<string, string> {
                        { "style", style },
                        { "rock", rock },
                        { "originblock", originblock },
                        { "wood", wood }
                    });
                }
                else
                {
                    newBeh.Variants.Set("style", style);
                    newBeh.Variants.Set("rock", rock);
                    newBeh.Variants.Set("originblock", originblock);
                    newBeh.Variants.Set("wood", wood);
                    newBe.MarkDirty(true);
                    world.BlockAccessor.MarkBlockDirty(newBe.Pos);
                }


            }, 0);
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(new AssetLocation(finishSound), newBlockPos.X, newBlockPos.Y, newBlockPos.Z, player);
                }
                catch
                {
                    //ignore errors)
                }

            }

            constructionInProgress = false;

        }
        private void TryAddGlass(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem,string orientation, string style, string rock, string originblock)
        {
            // Item
            var glassVariants = Variants.FromStack(heldItem);
            glassVariants.FindByVariant(glassCodeByType, out string glass);

            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation($"confession:arch_small_window-{orientation}"));
            if (newBlock == null) return;

            // Setzen und Attribute übertragen
            BlockPos newBlockPos = player.CurrentBlockSelection.Position.Copy();
            world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);

            // Callback um auf Blockentity zu warten
            world.RegisterCallback(dt =>
            {
                var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                if (newBeh == null) return;
                var newMyBe = newBe as BlockEntitySmallArchwayConstruction;
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) player.InventoryManager.ActiveHotbarSlot.TakeOut(1);
                if (newMyBe != null)
                {
                    newMyBe.SetVariants(new Dictionary<string, string> {
                        { "style", style },
                        { "rock", rock },
                        { "originblock", originblock },
                        {"glass", glass }
                    });
                }
                else
                {
                    newBeh.Variants.Set("style", style);
                    newBeh.Variants.Set("rock", rock);
                    newBeh.Variants.Set("originblock", originblock);
                    newBeh.Variants.Set("glass", glass);
                    newBe.MarkDirty(true);
                    world.BlockAccessor.MarkBlockDirty(newBe.Pos);
                }


            }, 0);
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(new AssetLocation(finishSound), newBlockPos.X, newBlockPos.Y, newBlockPos.Z, player);
                }
                catch
                {
                    //ignore errors)
                }

            }
            constructionInProgress = false;

            

        }

    }
    
}
