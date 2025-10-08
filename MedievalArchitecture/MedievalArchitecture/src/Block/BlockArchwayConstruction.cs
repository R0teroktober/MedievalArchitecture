using AttributeRenderingLibrary;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
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



        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();

        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            stateCodeByType.Add("state-0", "0");
            stateCodeByType.Add("state-1", "1");
            stateCodeByType.Add("state-2", "2");
            styleCodeByType.Add("style-a", "a");
            styleCodeByType.Add("style-b", "b");
            styleCodeByType.Add("style-c", "c");
            styleCodeByType.Add("style-d", "d");
            rockCodeByType.Add("rock-andesite", "andesite");
            rockCodeByType.Add("rock-basalt", "basalt");
            rockCodeByType.Add("rock-bauxite", "bauxite");
            rockCodeByType.Add("rock-chalk", "chalk");
            rockCodeByType.Add("rock-chert", "chert");
            rockCodeByType.Add("rock-claystone", "claystone");
            rockCodeByType.Add("rock-conglomerate", "conglomerate");
            rockCodeByType.Add("rock-granite", "granite");
            rockCodeByType.Add("rock-greenmarble", "greenmarble");
            rockCodeByType.Add("rock-halite", "halite");
            rockCodeByType.Add("rock-kimberlite", "kimberlite");
            rockCodeByType.Add("rock-limestone", "limestone");
            rockCodeByType.Add("rock-meteorite-iron", "meteorite-iron");
            rockCodeByType.Add("rock-obsidian", "obsidian");
            rockCodeByType.Add("rock-peridotite", "peridotite");
            rockCodeByType.Add("rock-phyllite", "phyllite");
            rockCodeByType.Add("rock-remarble", "remarble");
            rockCodeByType.Add("rock-sandstone", "sandstone");
            rockCodeByType.Add("rock-scoria", "scoria");
            rockCodeByType.Add("rock-shale", "shale");
            rockCodeByType.Add("rock-slate", "slate");
            rockCodeByType.Add("rock-suevite", "suevite");
            rockCodeByType.Add("rock-tuff", "tuff");
            rockCodeByType.Add("rock-whitemarble", "whitemarble");






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
            attr.FindByVariant(stateCodeByType, out string state);
            {
                string heldItemCode = heldItem.Collectible.Code.ToString();
                if (state == "0" && heldItemCode.StartsWith("game:stone-"))
                {
                    string rockType = heldItem.Collectible.Code.Path;  // z. B. "rock-granite"
                    if (rockType.StartsWith("stone-")) rockType = rockType.Substring(6);

                    attr.Set("state", "1");
                    attr.Set("rock", rockType);

                    UpdateVariants(world, be, beh);
                    handling = EnumHandling.PreventDefault;
                    return true;
                }
                else if (state == "1" && heldItemCode == "game:mortar")
                {
                    attr.Set("state", "2");
                    UpdateVariants(world, be, beh);
                    handling = EnumHandling.PreventDefault;
                    return true;

                }
                else if (state == "2")
                {
                    var originblockCode = "none";
                    var handMaterial = "none";
                    if (heldItemCode.StartsWith("game:rock-"))
                    {
                        originblockCode = "rock";
                        handMaterial = heldItemCode.Substring(10);

                    }
                    else if (heldItemCode.StartsWith("game:cobblestone-"))
                    {
                        originblockCode = "cobblestone";
                        handMaterial = heldItemCode.Substring(17);
                    }
                    else if (heldItemCode.StartsWith("game:stonebricks-"))
                    {
                        originblockCode = "brick";
                        handMaterial = heldItemCode.Substring(17);
                    }
                    else if (heldItemCode.StartsWith("game:plaster-"))
                    {
                        originblockCode = "plaster";
                        handMaterial = originblockCode;
                    }
                    else if (heldItemCode.StartsWith("game:daubraw-"))
                    {
                        originblockCode = heldItemCode.Substring(13);
                        handMaterial = originblockCode;

                    }
                    else return false;
                    if (originblockCode != "none" && handMaterial != "none")
                    {
                        var oldattr = attr.Clone();
                        var orientation = blockSel.Block.Variant["side"];
                        oldattr.FindByVariant(styleCodeByType, out string oldstyle);
                        oldattr.FindByVariant(rockCodeByType, out string oldrock);
                        var newStyle = oldstyle;
                        var newRock = oldrock;
                        if (newRock == handMaterial || heldItemCode.StartsWith("game:daubraw-") || heldItemCode.StartsWith("game:plaster-"))
                        {
                            var newBlock = world.GetBlock(new AssetLocation("confession:arch_small-" + orientation));
                            world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);
                            var newBe = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                            var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                            if (newBeh != null && newBe != null)
                            {
                                newBeh.Variants.Set("style", newStyle);
                                newBeh.Variants.Set("rock", newRock);
                                newBeh.Variants.Set("originblock", originblockCode);
                                UpdateVariants(world, newBe, newBeh);

                            }

                        }
                    }
                    else return false;
                }
                else return false;

            }
            return true;
        }


        private void UpdateVariants(IWorldAccessor world, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh)
        {
            

            be.MarkDirty(true);
            if (world.Side == EnumAppSide.Client)
            {
                RebuildArlMeshIfPossible(beh);
            }
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

    }
}

