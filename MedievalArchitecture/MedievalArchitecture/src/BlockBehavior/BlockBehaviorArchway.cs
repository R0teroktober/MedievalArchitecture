using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorArchway(Block block) : BlockBehavior(block), IInteractable
    {
        private string blockCodeWithGlass;
        private string blockCodeWithLintel;
        private int glassSize;
        private int lintelSize;
        public Dictionary<string, string> glassCodeByType = new();
        public Dictionary<string, string> woodCodeByType = new();
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();
        public Dictionary<string, string> originblockCodeByType = new();
        AssetLocation finishSound = new AssetLocation();
        AssetLocation soundAddWood = new AssetLocation();
        AssetLocation soundAddGlass = new AssetLocation();

        public override void Initialize(JsonObject properties)

        {
            
            var config = MedievalArchitectureModSystem.Config;
            woodCodeByType = new Dictionary<string, string>(config.WoodCodeByType);
            glassCodeByType = new Dictionary<string, string>(config.GlassCodeByType);
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);
            styleCodeByType = new Dictionary<string, string>(config.StyleCodeByType);
            rockCodeByType = new Dictionary<string, string>(config.RockCodeByType);
            originblockCodeByType = new Dictionary<string, string>(config.OriginblockCodeByType);
            glassSize = properties["glassSize"].AsInt(1);
            lintelSize = properties["lintelSize"].AsInt(1);
            blockCodeWithGlass = properties["blockCodeWithGlass"].AsString();
            blockCodeWithLintel = properties["blockCodeWithLintel"].AsString();

            soundAddWood = AssetLocation.Create("sounds/block/planks");
            soundAddGlass = AssetLocation.Create("sounds/block/ingot");
            finishSound = AssetLocation.Create("sounds/block/rock-break-pickaxe");
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

                return base.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);


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
                secondsUsed = 0; // Bauzeit zurücksetzten
                handled = EnumHandling.PreventDefault;
                    
                    // Baumaterial
                    if (byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack == null || blockSel == null) return;
                    var heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
                    string heldItemCode = heldItem.Collectible.Code.ToString();

            // Control position wenn dummy Block
            BlockPos controlPos = ResolveControlPos(world, blockSel);
            if (controlPos == null) return;

            // Block
            var be = world.BlockAccessor?.GetBlockEntity(controlPos);
                    if (be == null) return;
                    // Blockbehavior
                    var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                    if (beh.Variants is null) return;
                    var oldVariants = beh.Variants.Clone();
                    oldVariants.FindByVariant(styleCodeByType, out string style);
                    oldVariants.FindByVariant(rockCodeByType, out string rock);
                    oldVariants.FindByVariant(originblockCodeByType, out string originblock);
                    var block = world.BlockAccessor.GetBlock(controlPos);
                    if (block == null) return;
           
                    string orientation = block.Variant.TryGetValue("side");

                    if (originblock != "none")
                    {


                        if (heldItemCode.StartsWith("game:plank-") && blockCodeWithLintel != null)
                        {
                            if (heldItem.StackSize < lintelSize) return;
                            TryAddPlanks(world, byPlayer, be, beh, heldItem, orientation, style, rock, originblock, controlPos, lintelSize);
                        }
                        else if (heldItemCode.StartsWith("confession:window-") && blockCodeWithGlass != null || heldItemCode.StartsWith("game:glasspane-") && blockCodeWithGlass != null)
                        {
                            if (heldItem.StackSize < glassSize) return;
                            TryAddGlass(world, byPlayer, be, beh, heldItem, orientation, style, rock, originblock, controlPos, glassSize);
                        }
                    }
                    else
                    {
                        if (heldItemCode.StartsWith("game:rock-"))
                        {
          
                            if (heldItem.StackSize <  glassSize) return;
                            TryComplete(world, byPlayer, blockSel, be, beh, heldItem, orientation, style, rock, originblock, controlPos, glassSize);
                        }

                        if (heldItemCode.StartsWith("game:cobblestone-"))
                        {
                            originblock = "cobblestone";
                            if (heldItem.StackSize < glassSize) return;
                            TryComplete(world, byPlayer, blockSel, be, beh, heldItem, orientation, style, rock, originblock, controlPos, glassSize);
                        }

                        if (heldItemCode.StartsWith("game:stonebricks-"))
                        {
                            originblock = "brick";
                            if (heldItem.StackSize < glassSize) return;
                            TryComplete(world, byPlayer, blockSel, be, beh, heldItem, orientation, style, rock, originblock, controlPos, glassSize);
                        }
                        if (heldItemCode.StartsWith("game:plaster-"))
                        {
                            originblock = "plaster";
                            if (heldItem.StackSize < glassSize) return;
                            TryComplete(world, byPlayer, blockSel, be, beh, heldItem, orientation, style, rock, originblock, controlPos, glassSize);
                        }
                        if (heldItemCode.StartsWith("game:daubraw-"))
                        {
                            originblock = heldItemCode.Substring(13);
                            var newGlassSize = 4 * glassSize;
                            if (heldItem.StackSize < newGlassSize) return;
                            TryComplete(world, byPlayer, blockSel, be, beh, heldItem, orientation, style, rock, originblock, controlPos, newGlassSize);
                        }
                    
            }
         


        }


        private void TryAddPlanks(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string orientation, string style, string rock, string originblock, BlockPos controlPos, int lintelSize)
        {
            // Item
            string heldItemCode = heldItem.Collectible.Code.ToString();
            string wood = heldItemCode.Substring(11);
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(soundAddWood, controlPos, 0.5, player);
                }
                catch { }
            }
            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation(blockCodeWithLintel + "-" + orientation));
            if (world.Side == EnumAppSide.Server)
            {

                // Setzen und Attribute übertragen
                BlockPos newBlockPos = controlPos.Copy();

                world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
                world.BlockAccessor.MarkBlockDirty(newBlockPos);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);

                // Callback um auf Blockentity zu warten
                world.RegisterCallback(dt =>
                {
                    var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                    var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                    if (newBeh == null) return;
                    var newMyBe = newBe as BlockEntityConstructable;
                    if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        var slot = player.InventoryManager.ActiveHotbarSlot;
                        slot.TakeOut(lintelSize);
                        slot.MarkDirty();
                    }
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

                    }


                }, 100);
            }
            

        }
        private void TryAddGlass(IWorldAccessor world, IPlayer player, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string orientation, string style, string rock, string originblock,BlockPos controlPos, int glassSize)
        {
            // Item
            var glassVariants = Variants.FromStack(heldItem);
            glassVariants.FindByVariant(glassCodeByType, out string glass);
            if (glass == null) glass = "plain";

            
            
            if (world.Side == EnumAppSide.Client)
            {

                world.PlaySoundAt(soundAddGlass, controlPos, 0.5, player);

            }
            // Neuer Block
            var newBlock = world.GetBlock(new AssetLocation(blockCodeWithGlass + "-" + orientation));
            if (newBlock == null) return;

            if (world.Side == EnumAppSide.Server)
            {


                // Setzen und Attribute übertragen
                BlockPos newBlockPos = controlPos.Copy();
                world.BlockAccessor.SetBlock(newBlock.BlockId, newBlockPos);
                world.BlockAccessor.MarkBlockDirty(newBlockPos);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(newBlockPos);

                // Callback um auf Blockentity zu warten
                world.RegisterCallback(dt =>
                {
                    var newBe = world.BlockAccessor?.GetBlockEntity(newBlockPos);
                    var MBBlock = world.BlockAccessor.GetBlock(newBlockPos, 1);
                    var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                    if (newBeh == null) return;
                    var newMyBe = newBe as BlockEntityConstructable;
                    if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        var slot = player.InventoryManager.ActiveHotbarSlot;
                        slot.TakeOut(glassSize);
                        slot.MarkDirty();
                    }
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

                    }


                }, 100);
            }
            



        }
        private void TryComplete(IWorldAccessor world, IPlayer player, BlockSelection blockSel, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, ItemStack heldItem, string orientation, string style, string rock, string originblock, BlockPos controlPos, int requiredAmount)
        {
            if (world.Side == EnumAppSide.Client)
            {
                try
                {
                    world.PlaySoundAt(finishSound, controlPos, 0.5, player);
                }
                catch { }
            }
            // Item
            string heldItemCode = heldItem.Collectible.Code.ToString();
            string originblockType = heldItemCode.Substring(11);
            if (world.Side == EnumAppSide.Server) {
                var myBe = be as BlockEntityConstructable;
                if (myBe != null)
                {
                    myBe.SetVariant("originblock", originblock);
                }
                else
                {
                    beh.Variants.Set("originblock", originblock);
                    be.MarkDirty(true);
                }
                // Creative Mode Check
                if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                var slot = player.InventoryManager.ActiveHotbarSlot;
                slot.TakeOut(requiredAmount);
                slot.MarkDirty();
                }
            }

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
            if (beh is null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling); // 4) sonst keine Hilfe anzeigen

                beh.Variants.FindByVariant(originblockCodeByType, out string originblock);
                beh.Variants.FindByVariant(rockCodeByType, out string rock);


                if (forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.PathStartsWith("window-") == true && originblock != "none" && blockCodeWithGlass != null)
                {
                    amount = glassSize;
                    displayStack = GetRequiredDisplayStack(forPlayer, amount);
                    return [new WorldInteraction() { ActionLangCode = "confession:block-interaction-add-glass", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                }
                else if (forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.PathStartsWith("glasspane-") == true && originblock != "none" && blockCodeWithGlass != null)
                {
                    amount = glassSize; ;
                    displayStack = GetRequiredDisplayStack(forPlayer, amount);
                    return [new WorldInteraction() { ActionLangCode = "confession:block-interaction-add-glass", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                }
                else if (forPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.PathStartsWith("plank-") == true && originblock != "none" && blockCodeWithLintel != null)
                {
                    amount = glassSize;
                    displayStack = GetRequiredDisplayStack(forPlayer, amount);
                    return [new WorldInteraction() { ActionLangCode = "confession:block-interaction-add-plank", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl", Itemstacks = displayStack == null ? null : new[] { displayStack } }];
                }
                else { return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling); } 

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
