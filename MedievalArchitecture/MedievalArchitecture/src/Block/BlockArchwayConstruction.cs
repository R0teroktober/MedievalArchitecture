using AttributeRenderingLibrary;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Datastructures;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;
using System.Reflection;
using Vintagestory.API.Server;



namespace MedievalArchitecture
{
    public class BlockBehaviorConstructionStateChanger(Block block) : BlockBehavior(block), IInteractable
    {
        public int rimStoneAmount;
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();

        AssetLocation finishSound;
        public float lastUsedDuration;


        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            var config = MedievalArchitectureModSystem.Config;
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
            return secondsUsed < 1;
 
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            
            if (secondsUsed < 1) return;
            handled = EnumHandling.PreventDefault;
            

                // Bauzeit

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
                secondsUsed = 0;
            if (world.Side == EnumAppSide.Server)
            {


                // TreeAttribute lesen - nötig?
                //beh.Variants.ToTreeAttributes(tree);
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
            }
            RebuildArlMeshIfPossible(beh);

#if DEBUG
            world.Logger.Notification($"[ConstructionStateChanger] OnBlockInteractStop called on {world.Side} at {blockSel?.Position}");
#endif




        }
        private void UpdateVariants(IWorldAccessor world, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, BlockPos pos, IPlayer player)
        {
            if (world == null || be == null || beh == null || pos == null) return;
           // var tree = beh.Blockentity.FromTreeAttributes(I)
            //beh.Variants.ToTreeAttribute(be.Block.);
            world.Logger.Event($"[ConstructionStateChanger] {world.Side}: UpdateVariants called at {pos}");


            // --- SERVER SIDE ---
            if (world.Side == EnumAppSide.Server)
            {
 
                //beh.Variants.ToTreeAttribute(be.Api)
#if DEBUG
                world.Logger.Event($"[ConstructionStateChanger] SERVER: marking block & BE dirty at {pos}");
#endif
                world.BlockAccessor.MarkBlockDirty(pos);
                world.BlockAccessor.MarkBlockEntityDirty(pos);
                be.MarkDirty(true); // triggers BE sync to client

            }

            // --- CLIENT SIDE ---
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(new AssetLocation(finishSound), pos.X, pos.Y, pos.Z, player);
#if DEBUG
                    world.Logger.Notification($"[ConstructionStateChanger] CLIENT: playing sound at {pos}");
#endif
                }
                catch { /* ignore audio errors */ }

                // Defer one tick: wait until attribute sync from server has arrived
            }
        }


        private void TryAddRimStones(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {
            if (!heldItemCode.StartsWith("game:stone-") || heldItem.StackSize < rimStoneAmount) return;
            // Creative Check
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                player.InventoryManager.ActiveHotbarSlot.TakeOut(rimStoneAmount);

            string rockType = heldItem.Collectible.Code.Path.Substring(6);
            //if (world.Side == EnumAppSide.Server)
            //{
                beh.Variants.Set("state", "1");
                beh.Variants.Set("rock", rockType);
            //}
            UpdateVariants(world, be, beh, player.CurrentBlockSelection.Position, player);
        }

        private void TryAddMortar(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {
            if (heldItemCode != "game:mortar" || heldItem.StackSize < 1) return;

            // Creative Check
            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                player.InventoryManager.ActiveHotbarSlot.TakeOut(1);
            //if (world.Side == EnumAppSide.Server)
            //{
                beh.Variants.Set("state", "2");
            //}
            UpdateVariants(world, be, beh, player.CurrentBlockSelection.Position, player);

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
            var newBlock = world.GetBlock(new AssetLocation($"confession:arch_small-{orientation}"));
            if (newBlock == null) return;

            // Setzen und Attribute übertragen
            BlockPos newBlockPos = player.CurrentBlockSelection.Position.Copy();
            //if (world.Side == EnumAppSide.Server)
            //{
                
                world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);
            //world.BlockAccessor.MarkBlockDirty(newBlockPos);
            //}


            world.RegisterCallback(dt =>
            {
                var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                if (newBeh == null) return;

                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    player.InventoryManager.ActiveHotbarSlot.TakeOut(requiredAmount);
                //if (world.Side == EnumAppSide.Server)
                //{
                    newBeh.Variants.Set("style", style);
                    newBeh.Variants.Set("rock", rock);
                    newBeh.Variants.Set("originblock", originBlock);
                //}
                // Make sure we mark the new block's entity dirty on the server and rebuild on client (UpdateVariants handles side checks)
                UpdateVariants(world, newBe, newBeh, newBlockPos, player);
            }, 0);
            






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

#if DEBUG
            var worldField = beh.GetType().GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);
            var api = worldField?.GetValue(beh) as ICoreAPI;
            api?.Logger?.Notification("[ConstructionStateChanger] Attempting ARL mesh rebuild via reflection...");
#endif

            // 1) Try calling the non-public Init() which internally creates the mesh
            var initM = beh.GetType().GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);
            if (initM != null)
            {
                try
                {
                    initM.Invoke(beh, null);
#if DEBUG
                    api?.Logger?.Notification("[ConstructionStateChanger] ARL mesh rebuilt successfully via Init()");
#endif
                    return;
                }
                catch (Exception e)
                {
#if DEBUG
                    api?.Logger?.Warning($"[ConstructionStateChanger] Init() failed, falling back: {e.Message}");
#endif
                    // fall through to fallback
                }
            }

            // 2) Fallback: call OwnBehavior.GetOrCreateMesh(Variants) and set private "mesh" field
            var own = beh.OwnBehavior;
            if (own != null)
            {
                var getMeshM = own.GetType().GetMethod("GetOrCreateMesh",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (getMeshM != null)
                {
                    try
                    {
                        var newMesh = getMeshM.Invoke(own, new object[] { beh.Variants });
                        var meshField = beh.GetType().GetField("mesh", BindingFlags.Instance | BindingFlags.NonPublic);

                        if (meshField != null && newMesh != null)
                        {
                            meshField.SetValue(beh, newMesh);
#if DEBUG
                            api?.Logger?.Notification("[ConstructionStateChanger] ARL mesh rebuilt successfully via fallback method");
#endif
                        }
                    }
                    catch (Exception e)
                    {
#if DEBUG
                        api?.Logger?.Error($"[ConstructionStateChanger] Fallback mesh rebuild failed: {e.Message}");
#endif
                    }
                }
                else
                {
#if DEBUG
                    api?.Logger?.Warning("[ConstructionStateChanger] GetOrCreateMesh() not found on OwnBehavior.");
#endif
                }
            }
            else
            {
#if DEBUG
                api?.Logger?.Warning("[ConstructionStateChanger] OwnBehavior is null, cannot rebuild mesh.");
#endif
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

