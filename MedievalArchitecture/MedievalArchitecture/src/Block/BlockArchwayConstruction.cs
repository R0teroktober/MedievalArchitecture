using AttributeRenderingLibrary;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Datastructures;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;



namespace MedievalArchitecture
{
    public class BlockBehaviorConstructionStateChanger : BlockBehavior, IInteractable
    {
        public BlockBehaviorConstructionStateChanger(Block block) : base(block) { }


        public int rimStoneAmount;
        public Dictionary<string, string> stateCodeByType;
        public Dictionary<string, string> rockCodeByType;
        public Dictionary<string, string> styleCodeByType;
        AssetLocation sound;
        ITreeAttribute tree;

        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            var config = MedievalArchitectureModSystem.Config;
            sound = AssetLocation.Create("sounds/effect/stonecrush");

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
            return true;
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;
            if (blockSel == null) return false;
            //  Nur auf dem Client Animation
            if (world.Side == EnumAppSide.Client)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.BlockInteract);

            }
     

            return secondsUsed < 1;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
  

            handled = EnumHandling.PreventDefault;
            
            if (secondsUsed < 0.9) return;
            // Sicherheitsprüfungen (Guard Clauses)
            if (byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack == null || blockSel == null) return;
            
            var heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            var be = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
            if (be == null) return;

            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh?.Variants == null) return;

            if (!beh.Variants.FindByVariant(stateCodeByType, out string state)) return;
            string heldItemCode = heldItem.Collectible.Code.ToString();
            be.ToTreeAttributes(tree);
            switch (state)
            {
                case "0":
                    
                    TryAddRimStones(world, byPlayer, be, beh, heldItem, heldItemCode, blockSel, tree );
                    break;

                case "1":
                    TryAddMortar(world, byPlayer,be , beh, heldItem, heldItemCode, blockSel, tree);
                    break;

                case "2":
                    TryCompleteArch(world, byPlayer, blockSel, beh, heldItem, heldItemCode, tree);
                    break;
            }
            
        }
        private void UpdateVariants(IWorldAccessor world, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, BlockSelection blockSel, ITreeAttribute tree)
        {
            if (world.Side == EnumAppSide.Client) be.MarkDirty(true);
            world.BlockAccessor.MarkBlockDirty(blockSel.Position);
            Variants.FromTreeAttribute(tree);
            if (world.Side == EnumAppSide.Client) beh.OwnBehavior.GetOrCreateMesh(beh.Variants);

        }

        private void TryAddRimStones(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode, BlockSelection blockSel, ITreeAttribute tree)
        {
            if (!heldItemCode.StartsWith("game:stone-")) return;
            if (heldItem.StackSize < rimStoneAmount) return;

            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                player.InventoryManager.ActiveHotbarSlot.TakeOut(rimStoneAmount);

            string rockType = heldItem.Collectible.Code.Path.Substring(6);

            beh.Variants.Set("state", "1");
            beh.Variants.Set("rock", rockType);
            beh.Variants.ToTreeAttribute(tree);
            UpdateVariants(world, be, beh, blockSel, tree);
        }

        private void TryAddMortar(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode, BlockSelection blockSel, ITreeAttribute tree)
        {
            if (heldItemCode != "game:mortar" || heldItem.StackSize < 1) return;

            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                player.InventoryManager.ActiveHotbarSlot.TakeOut(1);

            beh.Variants.Set("state", "2");
            beh.ToTreeAttributes(tree);
            UpdateVariants(world, be, beh, blockSel, tree);


        }
        private void TryCompleteArch(IWorldAccessor world, IPlayer player, BlockSelection blockSel, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode, ITreeAttribute tree)
        {
            
                var (originBlock, handMaterial, requiredAmount) = GetMaterialInfo(heldItemCode);
                if (originBlock == null) return;
                if (heldItem.StackSize < requiredAmount) return;

                var block = blockSel.Block;
                //block.CodeWithVariant("state", "1");
                if (block == null) return;

                string orientation = block.Variant.TryGetValue("side");

                var oldAttr = beh.Variants.Clone();
                oldAttr.FindByVariant(styleCodeByType, out string style);
                oldAttr.FindByVariant(rockCodeByType, out string rock);

                var newBlock = world.GetBlock(new AssetLocation($"confession:arch_small-{orientation}"));
                if (newBlock == null) return;

                // Setzen und Attribute übertragen
                world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(blockSel.Position);
                var newBe = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
                var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                if (newBeh == null) return;

                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    player.InventoryManager.ActiveHotbarSlot.TakeOut(requiredAmount);

                newBeh.Variants.Set("style", style);
                newBeh.Variants.Set("rock", rock);
                newBeh.Variants.Set("originblock", originBlock);

                beh.ToTreeAttributes(tree);
            UpdateVariants(world, newBe, newBeh, blockSel, tree  );


            //beh.OwnBehavior.GetOrCreateMesh(beh.Variants);

            // Soundeffekt (nur Server)



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



        private void RebuildArlMeshIfPossible(BlockEntityBehaviorShapeTexturesFromAttributes beh)
        {
            if (beh == null) return;

            // 1) Versuch: private Init() aufrufen (Init ruft intern GetOrCreateMesh)
            var initM = beh.GetType().GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);
            if (initM != null)
            {
                try { initM.Invoke(beh, null); return; }
                catch { /* swallow - fallback versucht werden soll */ }
            }

            // 2) Fallback: OwnBehavior.GetOrCreateMesh(Variants) und protected field "mesh" setzen
            var own = beh.OwnBehavior;
            if (own != null)
            {
                var getMeshM = own.GetType().GetMethod("GetOrCreateMesh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getMeshM != null)
                {
                    try
                    {
                        var newMesh = getMeshM.Invoke(own, new object[] { beh.Variants });
                        var meshField = beh.GetType().GetField("mesh", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (meshField != null && newMesh != null)
                        {
                            meshField.SetValue(beh, newMesh);
                        }
                    }
                    catch { /* ignore failure */ }
                }
            }
        }
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handled)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "confession:blockhelp-archway-add-stones",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction()
                {
                    ActionLangCode = "confession:blockhelp-archway-add-mortar",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction()
                {
                    ActionLangCode = "confession:blockhelp-archway-add-block",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

    }
}

