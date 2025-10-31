using AttributeRenderingLibrary;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
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
    public class BlockBehaviorSmallArchwayConstruction(Block block) : BlockBehavior(block), IInteractable
    {
        public int rimStoneAmount;
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();
        string blockCodeBaseString;
        private bool constructionInProgress = false;
        AssetLocation finishSound = new AssetLocation();


        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            var config = MedievalArchitectureModSystem.Config;
            string blockCodeBaseString = properties["blockCodeBaseString"].AsString("arch_small_construction_");
            finishSound = AssetLocation.Create("sounds/effect/stonecrush");


            rimStoneAmount = config.RimStoneAmount;
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);
            styleCodeByType = new Dictionary<string, string>(config.StyleCodeByType);
            rockCodeByType = new Dictionary<string, string>(config.RockCodeByType);





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
            if (!beh.Variants.FindByVariant(stateCodeByType, out string state)) return;
            secondsUsed = 0; // Bauzeit zurücksetzten
            //if (world.Side == EnumAppSide.Server)
            //{

                switch (state)
                {
                    case "0":

                        TryAddRimStones(world, byPlayer, be, beh, heldItem, heldItemCode);
                        break;

                    case "1":
                        TryAddMortar(world, byPlayer, be, beh, heldItem, heldItemCode);
                        break;

                    case "2":
                        TryCompleteArch(world, byPlayer, beh, heldItem, heldItemCode);
                        break;

                }
            //}
           
        }
        private void TryAddRimStones(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {
            if (!heldItemCode.StartsWith("game:stone-") || heldItem.StackSize < rimStoneAmount) return;
            // Creative Mode Check
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) player.InventoryManager.ActiveHotbarSlot.TakeOut(rimStoneAmount);

            string rockType = heldItem.Collectible.Code.Path.Substring(6);
            var myBe = be as BlockEntitySmallArchwayConstruction;
            if (myBe != null)
            {
                myBe.SetVariants(new Dictionary<string, string> {
            { "state", "1" },
            { "rock", rockType }
        });
            }
            else
            {
                // Fallback: falls BE noch nicht gesetzt ist
                beh.Variants.Set("state", "1");
                beh.Variants.Set("rock", rockType);
                be.MarkDirty(true);
                world.BlockAccessor.MarkBlockDirty(be.Pos);
            }
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(new AssetLocation(finishSound), be.Pos.X, be.Pos.Y, be.Pos.Z, player);
                }
                catch
                {
                    //ignore errors)
                }
            }
            constructionInProgress = false;
        }


        private void TryAddMortar(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {
            if (heldItemCode != "game:mortar" || heldItem.StackSize < 1) return;

            // Creative Mode Check
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) player.InventoryManager.ActiveHotbarSlot.TakeOut(1);

            var myBe = be as BlockEntitySmallArchwayConstruction;
            if (myBe != null)
            {
                myBe.SetVariant("state", "2");
            }
            else
            {
                beh.Variants.Set("state", "2");
                be.MarkDirty(true);
                world.BlockAccessor.MarkBlockDirty(be.Pos);
            }
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(new AssetLocation(finishSound), be.Pos.X, be.Pos.Y, be.Pos.Z, player);
                }
                catch
                {
                    //ignore errors)
                }
            }
            constructionInProgress = false;
        }
        private void TryCompleteArch(IWorldAccessor world, IPlayer player, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {


            // Baumaterial
            var (originBlock, handMaterial, requiredAmount) = GetMaterialInfo(heldItemCode);
            if (heldItem.StackSize < requiredAmount) return;

            // Block
            if (originBlock == null) return;
            var block = player.CurrentBlockSelection.Block;
            if (block == null) return;
            string orientation = block.Variant.TryGetValue("side");

            // Varianten lesen
            var oldAttr = beh.Variants.Clone();
            oldAttr.FindByVariant(styleCodeByType, out string style);
            oldAttr.FindByVariant(rockCodeByType, out string rock);

            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation($"{blockCodeBaseString}-{orientation}"));
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

                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative) player.InventoryManager.ActiveHotbarSlot.TakeOut(requiredAmount);

                var newMyBe = newBe as BlockEntitySmallArchwayConstruction;
                
                if (newMyBe != null)
                {
                    newMyBe.SetVariants(new Dictionary<string, string> {
            { "style", style },
            { "rock", rock },
            { "originblock", originBlock }
        });
                }
                else
                {
                    newBeh.Variants.Set("style", style);
                    newBeh.Variants.Set("rock", rock);
                    newBeh.Variants.Set("originblock", originBlock);
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
                catch { 
                    //ignore errors)
                    }

            }
            constructionInProgress = false;
        }





        private (string originBlock, string handMaterial, int requiredAmount) GetMaterialInfo(string itemCode)
        {
            if (itemCode.StartsWith("game:rock-"))
                return ("rock", itemCode.Substring(10), 1);
            if (itemCode.StartsWith("game:cobblestone-"))
                return ("cobblestone", itemCode.Substring(17), 1);
            if (itemCode.StartsWith("game:stonebricks-"))
                return ("brick", itemCode.Substring(17), 1);
            if (itemCode.StartsWith("game:plaster-"))
                return ("plaster", "plaster", 1);
            if (itemCode.StartsWith("game:daubraw-"))
                return (itemCode.Substring(13), itemCode.Substring(13), 4);

            return (null, null, 0);
        }

       public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)

        {
            // 1) Auf den Block zugreifen
           var be = world.BlockAccessor?.GetBlockEntity(selection.Position);
           var beh = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();

            // 2) Attribut prüfen
            if (!beh.Variants.FindByVariant(stateCodeByType, out string state)) return null;
            switch (state)
            {
                case "0": 

                    break;
                case "1": 
                    
                    break;
                case "2": 
                    
                    break;

            }

            // 4) sonst keine Hilfe anzeigen
            return null;
        }



    }
}

