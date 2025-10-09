using AttributeRenderingLibrary;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using Vintagestory.API.Client;
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


        public int rimStoneAmount;
        public Dictionary<string, string> stateCodeByType = new();
        public Dictionary<string, string> rockCodeByType = new();
        public Dictionary<string, string> styleCodeByType = new();

        public override void Initialize(JsonObject properties)

        {
            base.Initialize(properties);
            rimStoneAmount = block.Attributes["rimStoneAmount"].AsInt(6);
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
        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            return true;

        }
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            // Wenn Client, erlauben wir die Interaktion visuell (Rückgabe true), aber wir machen Logik nur auf Server
            if (world.Side == EnumAppSide.Client)
            {
                handling = EnumHandling.PreventDefault;
                return true;
            }

            // Server-Logik: prüfen, ob Interagieren erlaubt ist
            handling = EnumHandling.PreventDefault;

            if (byPlayer == null || byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack == null) return false;
            if (blockSel == null) return false;
            if (world.BlockAccessor?.GetBlockEntity(blockSel.Position) == null) return false;

            // Weitere Bedingungen hier (z. B. Shift gedrückt, passender Block, etc.)
            // Wenn alle Bedingungen erfüllt:
            return true;
        }
        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            if (blockSel == null) return false;

            // 🎬 Nur auf dem Client Animation & Sound
            if (world.Side == EnumAppSide.Client)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.BlockInteract);

                if (world.Rand.NextDouble() < 0.05)
                {
                    var sound = AssetLocation.Create("sounds/effect/stonecrush");
                    if (sound != null) world.PlaySoundAt(sound, blockSel.Position, 0, byPlayer);
                }
            }

            return secondsUsed < 2;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            // Nur auf dem Server ausführen – verhindert doppelte Aufrufe & Client-NREs
            if (world.Side != EnumAppSide.Server) return;

            // Sicherheitsprüfungen
            if (byPlayer == null || byPlayer.InventoryManager == null || blockSel == null) return;
            if (byPlayer.InventoryManager.ActiveHotbarSlot == null) return;

            ItemStack heldItem = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (heldItem == null || heldItem.Collectible == null) return;

            // BlockEntity + Behavior holen
            var be = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
            if (be == null) return;

            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh == null || beh.Variants == null) return;

            var attr = beh.Variants;
            if (!attr.FindByVariant(stateCodeByType, out string state)) return;

            string heldItemCode = heldItem.Collectible.Code.ToString();
            var block = blockSel.Block;

            // Status 0 = Steine hinzufügen
            if (state == "0" && heldItemCode.StartsWith("game:stone-"))
            {
                string rockType = heldItem.Collectible.Code.Path.Substring(6);

                if (heldItem.StackSize >= rimStoneAmount)
                {
                    if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(rimStoneAmount);
                    }

                    attr.Set("state", "1");
                    attr.Set("rock", rockType);

                    UpdateVariants(world, be, beh);
                }
            }

            // Status 1 = Mörtel hinzufügen
            else if (state == "1" && heldItemCode == "game:mortar")
            {
                if (heldItem.StackSize >= 1)
                {
                    if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(1);
                    }

                    attr.Set("state", "2");
                    UpdateVariants(world, be, beh);
                }
            }

            // Status 2 = Block hinzufügen und zu fertigem Torbogen transformieren
            else if (state == "2")
            {
                string originblockCode = "none";
                string handMaterial = "none";

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

                if (originblockCode != "none" && handMaterial != "none")
                {
                    var oldattr = attr.Clone();
                    var orientation = block.Variant.TryGetValue("side");
                    oldattr.FindByVariant(styleCodeByType, out string oldstyle);
                    oldattr.FindByVariant(rockCodeByType, out string oldrock);

                    string newStyle = oldstyle;
                    string newRock = oldrock;
                    int takeStackSize = 1;

                    bool hasEnough =
                        (newRock == handMaterial && heldItem.StackSize >= 1)
                        || (heldItemCode.StartsWith("game:daubraw-") && heldItem.StackSize >= 4)
                        || (heldItemCode.StartsWith("game:plaster-") && heldItem.StackSize >= 1);

                    if (hasEnough)
                    {
                        var newBlock = world.GetBlock(new AssetLocation("confession:arch_small-" + orientation));
                        if (newBlock == null) return;

                        world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);

                        var newBe = world.BlockAccessor.GetBlockEntity(blockSel.Position);
                        if (newBe == null) return;

                        var newBeh = newBe.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                        if (newBeh == null) return;

                        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                        {
                            if (heldItemCode.StartsWith("game:daubraw-"))
                            {
                                takeStackSize = 4;
                            }
                            byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(takeStackSize);
                        }

                        newBeh.Variants.Set("style", newStyle);
                        newBeh.Variants.Set("rock", newRock);
                        newBeh.Variants.Set("originblock", originblockCode);

                        UpdateVariants(world, newBe, newBeh);
                    }
                }
            }
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

