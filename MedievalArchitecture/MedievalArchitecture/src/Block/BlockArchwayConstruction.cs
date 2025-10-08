using AttributeRenderingLibrary;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
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
        public Dictionary<string, string> originblockCodeByType = new();
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
            originblockCodeByType.Add("originblock-rock", "rock");
            originblockCodeByType.Add("originblock-cobblestone", "cobblestone");
            originblockCodeByType.Add("originblock-brick", "brick");
            originblockCodeByType.Add("originblock-plaster", "plaster");
            originblockCodeByType.Add("originblock-ash", "ash");
            originblockCodeByType.Add("originblock-blue", "blue");
            originblockCodeByType.Add("originblock-brown", "brown");
            originblockCodeByType.Add("originblock-browngolden", "browngolden");
            originblockCodeByType.Add("originblock-brownlight", "brownlight");
            originblockCodeByType.Add("originblock-brownweathered", "brownweathered");
            originblockCodeByType.Add("originblock-green", "green");
            originblockCodeByType.Add("originblock-orange", "orange");
            originblockCodeByType.Add("originblock-pink", "pink");
            originblockCodeByType.Add("originblock-tan", "tan");
            originblockCodeByType.Add("originblock-yellow", "yellow");





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
            attr.FindByVariant(stateCodeByType, out string state );
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
                else if (state == "2" && heldItemCode.StartsWith("game:rock-")|| state == "2" && heldItemCode.StartsWith("game:cobblestone-")|| state == "2" && heldItemCode.StartsWith("game:stonebricks-")|| state == "2" && heldItemCode.StartsWith("game:plaster-")|| state == "2" && heldItemCode.StartsWith("game:daub-"))
                {
                    string materialType = heldItem.Collectible.Code.Path;
                    var Oldattr =attr.Clone();
                    var orientation = blockSel.Block.Variant["side"];
                    var newBlock = world.GetBlock(new AssetLocation("confession:arch_small-" + orientation));
                    world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);
                    var newBe = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                    var newBeh = newBe?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                    if (newBeh != null)
                    {
                        Oldattr.FindByVariant(styleCodeByType, out string oldstyle);
                        {
                            newBeh.Variants.Set("style", oldstyle);
                        }
                        Oldattr.FindByVariant(rockCodeByType, out string oldrock);
                        {
                            newBeh.Variants.Set("rock", oldrock);
                            if (materialType.StartsWith("rock-") && materialType.Substring(5) == oldrock)
                            {
                                newBeh.Variants.Set("originblock", "rock");
                            }
                            else if (materialType.StartsWith("plaster-"))
                            {
                                newBeh.Variants.Set("originblock", "plaster");
                            }
                            else if (materialType.StartsWith("stonebricks-") && materialType.Substring(12) == oldrock)
                            {
                                newBeh.Variants.Set("originblock", "brick");
                            }
                            else if (materialType.StartsWith("cobblestone-") && materialType.Substring(12) == oldrock)
                            {
                                newBeh.Variants.Set("originblock", "cobblestone");
                            }
                            else if (materialType.StartsWith("daub-"))
                            {
                                var daubVariant = materialType.Substring(5);
                                newBeh.Variants.Set("originblock", daubVariant);
                            }
                            else return false;


                        }

                        UpdateVariants(world, newBe, newBeh);
                    }

              
                    return true;
                }

            }



            return false;
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

