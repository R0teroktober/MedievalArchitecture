using AttributeRenderingLibrary;
using Microsoft.VisualBasic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorArchwayFrame(Block block) : BlockBehavior(block), IInteractable
    {
        private int rimStoneAmount;
        private int intMultiplicator;
        private string blockCodeBaseString;
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();
        AssetLocation finishSound= new AssetLocation();
        AssetLocation soundAddStone = new AssetLocation();
        AssetLocation soundAddMortar = new AssetLocation();
        WorldInteraction[] interactions;

        private static List<ItemStack> mortarItems = new List<ItemStack>();
        private static List<ItemStack> rimStoneItems = new List<ItemStack>();
        private static List<ItemStack> originBlocks = new List<ItemStack>();


        public override void Initialize(JsonObject properties)

        {
            
            var config = MedievalArchitectureModSystem.Config;
            soundAddStone = AssetLocation.Create("sounds/effect/stonecrush");
            soundAddMortar = AssetLocation.Create("sounds/block/sand");
            finishSound = AssetLocation.Create("sounds/block/rock-break-pickaxe");

            intMultiplicator = properties["intMultiplicator"].AsInt(1);
            blockCodeBaseString = properties["blockCodeBaseString"].AsString();

            rimStoneAmount = config.RimStoneAmount;
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);
            styleCodeByType = new Dictionary<string, string>(config.StyleCodeByType);
            rockCodeByType = new Dictionary<string, string>(config.RockCodeByType);

            base.Initialize(properties);





        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handling = EnumHandling.PreventDefault;
                return true;
            }
            else
                return base.OnBlockInteractStart(world, byPlayer, blockSel,ref handling);


        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handling = EnumHandling.PreventDefault;
                secondsUsed = 0;
                return true;
            }
            else
                return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
            
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handled = EnumHandling.PreventDefault;

                
                if (byPlayer.CurrentBlockSelection == null) return false;
                //  Nur auf dem Client Animation
                if (world.Side == EnumAppSide.Client)
                {
                    (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.BlockInteract);

                }
                return true;
            }
            else
                return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref handled);
            
           

        }
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            //if (secondsUsed < 0.5) base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, ref handled); // Bauzeit
            //secondsUsed = 0;
            
            handled = EnumHandling.PreventDefault;
            
            // Baumaterial
            if (byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack == null || blockSel == null) return;
            var heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
             string heldItemCode = heldItem.Collectible.Code.ToString();
            // Control position wenn dummy Block
            BlockPos controlPos = ResolveControlPos(world, blockSel);
            if (controlPos == null) return;

            // BlockEntity
            var be = world.BlockAccessor?.GetBlockEntity(controlPos);
            if (be == null) return;
            // Blockbehavior
            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh?.Variants == null) return;
            if (!beh.Variants.FindByVariant(stateCodeByType, out string state)) return;
            
            
            //{

                switch (state)
                {
                    case "0":
                    if (RequirementsMet(heldItem, 1, byPlayer)) {
                        if (world.Side == EnumAppSide.Client)
                        {
                            try
                            {
                                world.PlaySoundAt(soundAddStone, controlPos, 1, byPlayer);
                            }
                            catch { }
                        }

                        TryAddRimStones(world, byPlayer, be, beh, heldItem, heldItemCode);
                    }
                    break;

                    case "1":
                    if (RequirementsMet(heldItem, 2, byPlayer))
                    {
                        if (world.Side == EnumAppSide.Client)
                        {
                            try
                            {
                                world.PlaySoundAt(soundAddMortar, controlPos, 1, byPlayer);
                            }
                            catch { }
                        }
                        TryAddMortar(world, byPlayer, be, beh, heldItem, heldItemCode);
                    }
                    break;

                    case "2":
                    if (RequirementsMet(heldItem, 3, byPlayer))
                    {
                        if (world.Side == EnumAppSide.Client)
                        {
                            try
                            {
                                world.PlaySoundAt(finishSound, controlPos, 1, byPlayer);
                            }
                            catch { }
                        }
                        TryCompleteArch(world, byPlayer, beh, heldItem, heldItemCode, controlPos);
                    }
                    break;

                }
            
            // Bauzeit zurücksetzten
            //}

        }


        private bool RequirementsMet(ItemStack heldItem, int phase, IPlayer byPlayer)
        {
            if (!byPlayer.Entity.Controls.CtrlKey) return false;
            if (phase == 1)
            {
                if (!heldItem.Collectible.Code.Path.StartsWith("stone")) return false;
                if (heldItem.StackSize < (rimStoneAmount * intMultiplicator)) return false;
                return true;
            }
            else if (phase == 2)
            {
                if (heldItem.Collectible.Code.Path != "mortar") return false;
                if (heldItem.StackSize < intMultiplicator) return false;
                return true;
            }
             else if (phase == 3)
            {
                var (originBlock, handMaterial, requiredAmount) = GetMaterialInfo(heldItem.Collectible.Code.ToString());
                if (requiredAmount == 0) return false;
                if (heldItem.StackSize < requiredAmount) return false;
                return true;
            } else return false;
        }

        private void TryAddRimStones(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        { 
            
            string rockType = heldItem.Collectible.Code.Path.Substring(6);
            if (world.Side == EnumAppSide.Server)
            {
                 var myBe = be as BlockEntityConstructable;
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
                 }

                // Creative Mode Check
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    var slot = player.InventoryManager.ActiveHotbarSlot;
                    slot.TakeOut(rimStoneAmount * intMultiplicator);
                    slot.MarkDirty();
                }
            }
                    

                }


        private void TryAddMortar(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode)
        {

            if (world.Side == EnumAppSide.Server)
            {

                var myBe = be as BlockEntityConstructable;
                if (myBe != null)
                {
                    myBe.SetVariant("state", "2");
                }
                else
                {
                    beh.Variants.Set("state", "2");
                    be.MarkDirty(true);
                }
                // Creative Mode Check
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    var slot = player.InventoryManager.ActiveHotbarSlot;
                    slot.TakeOut(intMultiplicator);
                    slot.MarkDirty();
                }
            }
        }
        private void TryCompleteArch(IWorldAccessor world, IPlayer player, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string heldItemCode, BlockPos controlPos)
        {

            // Baumaterial
            var (originBlock, handMaterial, requiredAmount) = GetMaterialInfo(heldItemCode);
            // Block
            if (originBlock == null) return;

            var block = world.BlockAccessor.GetBlock(controlPos);
            if (block == null) return;
            string orientation = block.Variant.TryGetValue("side");

            // Varianten lesen
            var oldAttr = beh.Variants.Clone();
            oldAttr.FindByVariant(styleCodeByType, out string style);
            oldAttr.FindByVariant(rockCodeByType, out string rock);

            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation(blockCodeBaseString + "-" + orientation));
            if (newBlock == null) return;
            if (world.Side == EnumAppSide.Server)
            {
     
                // Setzen und Attribute übertragen
                BlockPos newBlockPos = controlPos.Copy();
                world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
                world.BlockAccessor.MarkBlockDirty(newBlockPos);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);

                // Itemstack drop thanks to Dana
                ItemStack stack = new ItemStack();
                switch (blockCodeBaseString)
                {
                    case "confession:arch_small":
                        stack = new ItemStack(world.GetBlock(new AssetLocation("confession:construction_small-north")));
                        break;
                    case "confession:arch2x2":
                        stack = new ItemStack(world.GetBlock(new AssetLocation("confession:construction_medium2x2-north")));
                        break;
                    case "confession:arch2x1":
                        stack = new ItemStack(world.GetBlock(new AssetLocation("confession:construction_medium2x1-north")));
                        break;
                }
                if (stack != null && world.Side == EnumAppSide.Server)
                {
                    Variants variants = Variants.FromStack(stack);
                    var newVariants = new Dictionary<string, string>()
                    {
                        ["style"] = style,
                        ["rock"] = "none",
                        ["state"] = "0"
                    };
                    variants.Set(newVariants);
                    variants.ToStack(stack);
                    world.SpawnItemEntity(stack, newBlockPos.ToVec3d().Add(0.5, 0.5, 0.5));
                }


                // Callback um auf Blockentity zu warten
                world.RegisterCallback(dt =>
                {
                    var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                    var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                    if (newBeh == null) return;

                    if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        var slot = player.InventoryManager.ActiveHotbarSlot;
                        slot.TakeOut(requiredAmount);
                        slot.MarkDirty();
                    }

                    var newMyBe = newBe as BlockEntityConstructable;

                    if (newMyBe != null)
                    {
                        newMyBe.SetVariants(new Dictionary<string, string> {
            { "style", style },
            { "rock", rock },
            { "originblock", originBlock },
            { "glass", "none" }
            });
                    }
                    else
                    {
                        newBeh.Variants.Set("style", style);
                        newBeh.Variants.Set("rock", rock);
                        newBeh.Variants.Set("originblock", originBlock);
                        newBeh.Variants.Set("glass", "none");
                        newBe.MarkDirty(true);


                    }


                }, 100);
                
            }
            

        }





        private (string originBlock, string handMaterial, int requiredAmount) GetMaterialInfo(string itemCode)
        {
            if (itemCode.StartsWith("game:rock-"))
                return ("rock", itemCode.Substring(10), intMultiplicator);
            if (itemCode.StartsWith("game:cobblestone-"))
                return ("cobblestone", itemCode.Substring(17), intMultiplicator);
            if (itemCode.StartsWith("game:stonebricks-"))
                return ("brick", itemCode.Substring(17), intMultiplicator);
            if (itemCode.StartsWith("game:plaster-"))
                return ("plaster", "plaster", intMultiplicator);
            if (itemCode.StartsWith("game:daub-"))
            {
                var daubString = itemCode.Substring(10);
                var i = daubString.Length;
                daubString = daubString.Remove((i-7), 7);
                return (daubString, daubString, intMultiplicator);
            }

                

            return (null, null, 0);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)

        {
            // 1) Auf den Block zugreifen
            var be = world.BlockAccessor?.GetBlockEntity(selection.Position);
            var beh = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();

            // 2) Itemstack
            ItemStack displayStack;
            int amount = 0;

            // 3) Attribut prüfen
            if (beh == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);


            if (beh.Variants.FindByVariant(stateCodeByType, out string state) && forPlayer.InventoryManager.ActiveHotbarSlot.Itemstack != null)
            {

                switch (state)
                {
                    case "0":
                        if (forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Item?.Code.Path.StartsWith("stone-") == true)
                        {
                            amount = rimStoneAmount * intMultiplicator;
                            displayStack = GetRequiredDisplayStack(forPlayer, amount);
                            return [new WorldInteraction(){ActionLangCode = "confession:block-interaction-add-rimStones",MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                        }
                        break;
                        
                    case "1":
                        if (forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack.Item?.Code.Path == "mortar")
                        {
                            amount = intMultiplicator;
                            displayStack = GetRequiredDisplayStack(forPlayer, amount);
                            return [new WorldInteraction() { ActionLangCode = "confession:block-interaction-add-mortar", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                        }
                        break;
                      
                    case "2":
                        if (
                        forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Code.Path.StartsWith("cobblestone")  == true ||
                        forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Code.Path.StartsWith("rock") == true ||
                        forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Code.Path.StartsWith("stonebricks") == true ||
                        forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Code.Path.StartsWith("plaster") == true ||
                        forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.Code.Path.StartsWith("daub") == true)
                        {
                            amount = intMultiplicator;
                            displayStack = GetRequiredDisplayStack(forPlayer, amount);
                            return [new WorldInteraction() { ActionLangCode = "confession:block-interaction-add-block", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                        }
                        break;
                }
                
                // 4) sonst keine Hilfe anzeigen
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);
            }
            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);

        }
        private BlockPos ResolveControlPos(IWorldAccessor world, BlockSelection blockSel)
        {
            if (blockSel == null) return null;

            BlockPos pos = blockSel.Position.Copy();
            Block dummyBlock = world.BlockAccessor.GetBlock(pos);

            IMultiblockOffset mbo =
                dummyBlock as IMultiblockOffset
                ?? dummyBlock?.GetInterface<IMultiblockOffset>(world, pos);

            if (mbo != null)
            {
                pos = mbo.GetControlBlockPos(pos);
            }

            return pos;
        }
        private ItemStack GetRequiredDisplayStack(IPlayer forPlayer, int amount)
        {
            var heldStack = forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (heldStack == null) return null;

            if (amount <= 0) return null;

            ItemStack displayStack = heldStack.Clone();
            displayStack.StackSize = amount;
            return displayStack;
        }



    }
}

