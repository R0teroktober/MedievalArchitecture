using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    /// <summary>
    /// Finished archway. Building steps (ctrl + right click):
    /// boards (registry "woods") -> door lintel (block "blockCodeWithLintel"), glass pane or window -> window (block "blockCodeWithGlass").
    /// Archways left unfinished by older versions (originblock "none") can be finished with a fill block (registry "fills").
    /// </summary>
    public class BlockBehaviorArchway(Block block) : BlockBehavior(block), IInteractable
    {
        private const string StyleAttribute = "style";
        private const string GlassAttribute = "glass";

        /// <summary>Attributes that are kept when glass or planks are added. "originblock" only exists in archways of older versions.</summary>
        private static readonly string[] KeptAttributes =
        [
            StyleAttribute,
            ArchwayMaterialRegistry.RimAttribute,
            ArchwayMaterialRegistry.FillAttribute,
            ArchwayMaterialRegistry.LegacyFillAttribute
        ];

        private string? blockCodeWithGlass;
        private string? blockCodeWithLintel;
        /// <summary>Glass panes for a window. Also the number of fill blocks needed to finish the archway (same as "intMultiplicator" of the frame).</summary>
        private int glassSize;
        private int lintelSize;
        private Dictionary<string, string> glassCodeByType = new();

        private AssetLocation finishSound = new AssetLocation();
        private AssetLocation soundAddWood = new AssetLocation();
        private AssetLocation soundAddGlass = new AssetLocation();

        public override void Initialize(JsonObject properties)
        {
            glassCodeByType = new Dictionary<string, string>(MedievalArchitectureModSystem.Config.GlassCodeByType);
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
            return base.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handling = EnumHandling.PreventDefault;
                return true;
            }
            return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handled = EnumHandling.PreventDefault;
                if (byPlayer.CurrentBlockSelection == null) return false;
                if (world.Side == EnumAppSide.Client)
                {
                    (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.BlockInteract);
                }
                return true;
            }
            return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref handled);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            if (byPlayer == null || blockSel == null || !byPlayer.Entity.Controls.CtrlKey) return;
            ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack? heldStack = slot?.Itemstack;
            if (slot == null || heldStack == null) return;

            BlockPos controlPos = ResolveControlPos(world, blockSel);
            BlockEntity? be = world.BlockAccessor.GetBlockEntity(controlPos);
            BlockEntityBehaviorShapeTexturesFromAttributes? arl = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (be == null || arl?.Variants == null) return;

            if (!IsFinished(arl.Variants))
            {
                TryFinish(world, byPlayer, slot, heldStack, be, arl, controlPos);
                return;
            }

            string? orientation = world.BlockAccessor.GetBlock(controlPos).Variant?["side"];
            ArchwayMaterial? wood = GetLintelWood(world, heldStack);

            if (wood != null && blockCodeWithLintel != null)
            {
                if (heldStack.StackSize < lintelSize) return;
                TryAddPlanks(world, byPlayer, slot, wood, arl.Variants, controlPos, orientation, blockCodeWithLintel);
            }
            else if (IsGlass(heldStack) && blockCodeWithGlass != null)
            {
                if (heldStack.StackSize < glassSize) return;
                TryAddGlass(world, byPlayer, slot, heldStack, arl.Variants, controlPos, orientation, blockCodeWithGlass);
            }
        }

        private void TryAddPlanks(IWorldAccessor world, IPlayer player, ItemSlot slot, ArchwayMaterial wood, Variants oldVariants, BlockPos controlPos, string? orientation, string baseCode)
        {
            Block? newBlock = GetTargetBlock(world, baseCode, orientation, controlPos);
            if (newBlock == null) return;

            PlaySound(world, soundAddWood, controlPos, player);
            if (world.Side != EnumAppSide.Server) return;

            Dictionary<string, string> newVariants = CopyKeptVariants(oldVariants);
            newVariants[ArchwayMaterialRegistry.WoodAttribute] = wood.Id;
            ReplaceArchway(world, player, slot, controlPos, newBlock, newVariants, lintelSize);
        }

        private void TryAddGlass(IWorldAccessor world, IPlayer player, ItemSlot slot, ItemStack heldStack, Variants oldVariants, BlockPos controlPos, string? orientation, string baseCode)
        {
            Block? newBlock = GetTargetBlock(world, baseCode, orientation, controlPos);
            if (newBlock == null) return;

            PlaySound(world, soundAddGlass, controlPos, player);
            if (world.Side != EnumAppSide.Server) return;

            Variants glassVariants = Variants.FromStack(heldStack);
            if (!glassVariants.FindByVariant(glassCodeByType, out string glass) || string.IsNullOrEmpty(glass)) glass = "plain";

            Dictionary<string, string> newVariants = CopyKeptVariants(oldVariants);
            newVariants[GlassAttribute] = glass;
            ReplaceArchway(world, player, slot, controlPos, newBlock, newVariants, glassSize);
        }

        /// <summary>Finishes an archway that was left unfinished by an older version, with a fill block of the registry.</summary>
        private void TryFinish(IWorldAccessor world, IPlayer player, ItemSlot slot, ItemStack heldStack, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes arl, BlockPos controlPos)
        {
            ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(world.Api);
            ArchwayMaterial? fill = registry == null ? null : GetBuildableFill(registry, heldStack);
            int amount = glassSize;
            if (registry == null || fill == null || heldStack.StackSize < amount) return;

            // No fallback: an archway with unknown rim stones would show missing textures.
            string? rimId = arl.Variants.Get(ArchwayMaterialRegistry.RimAttribute);
            if (string.IsNullOrEmpty(rimId) || !registry.TryGetRim(rimId, out _))
            {
                if (world.Api is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "archway-unknownrim", Lang.Get("confession:archway-unknownrim"));
                }
                else
                {
                    world.Logger.Warning("[MedievalArchitecture] Archway at {0} cannot be finished: rim stones \"{1}\" are not in the archway material registry.", controlPos, rimId);
                }
                return;
            }

            PlaySound(world, finishSound, controlPos, player);
            if (world.Side != EnumAppSide.Server) return;

            TakeMaterial(player, slot, amount);

            // "fill" alone marks the archway as finished, the old "none" is removed.
            arl.Variants.RemoveKeys(ArchwayMaterialRegistry.LegacyFillAttribute);
            var values = new Dictionary<string, string> { [ArchwayMaterialRegistry.FillAttribute] = fill.Id };
            if (be is BlockEntityConstructable constructable)
            {
                constructable.SetVariants(values);
            }
            else
            {
                arl.Variants.Set(values);
                be.MarkDirty(true);
            }
        }

        /// <summary>
        /// Server: replaces the archway by the block with glass or planks. The material is taken right away from the slot
        /// that was used (B6: older versions took it 100 ms later from the slot that was active then).
        /// </summary>
        private static void ReplaceArchway(IWorldAccessor world, IPlayer player, ItemSlot slot, BlockPos pos, Block newBlock, Dictionary<string, string> variants, int amount)
        {
            TakeMaterial(player, slot, amount);

            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityConstructable newBe)
            {
                newBe.SetVariants(variants);
                return;
            }

            // Should not happen: the block entity is created by SetBlock. Try once more a moment later.
            world.Logger.Warning("[MedievalArchitecture] Archway at {0}: block entity not ready, setting its materials a moment later.", pos);
            world.RegisterCallback(_ => (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityConstructable)?.SetVariants(variants), 100);
        }

        /// <summary>B12: the archway block with glass or planks for this orientation. Null (and an error in the log) if it does not exist.</summary>
        private Block? GetTargetBlock(IWorldAccessor world, string baseCode, string? orientation, BlockPos pos)
        {
            Block? target = string.IsNullOrEmpty(orientation) ? null : world.GetBlock(new AssetLocation(baseCode + "-" + orientation));
            if (target == null && world.Side == EnumAppSide.Server)
            {
                world.Logger.Error("[MedievalArchitecture] Archway {0} at {1}: block \"{2}-{3}\" not found.", block.Code, pos, baseCode, orientation);
            }
            return target;
        }

        private static Dictionary<string, string> CopyKeptVariants(Variants variants)
        {
            var result = new Dictionary<string, string>();
            foreach (string key in KeptAttributes)
            {
                string? value = variants.Get(key);
                if (!string.IsNullOrEmpty(value)) result[key] = value;
            }
            return result;
        }

        private static bool IsFinished(Variants variants)
        {
            return ArchwayMaterialRegistry.IsFinishedArchway(
                variants.Get(ArchwayMaterialRegistry.FillAttribute),
                variants.Get(ArchwayMaterialRegistry.LegacyFillAttribute));
        }

        /// <summary>Boards of the registry ("woods") that can be used for the door lintel. Null for anything else.</summary>
        private static ArchwayMaterial? GetLintelWood(IWorldAccessor world, ItemStack stack)
        {
            ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(world.Api);
            if (registry == null) return null;
            return registry.TryGetWoodByCode(stack.Collectible.Code, out ArchwayMaterial? wood) && wood.Buildable ? wood : null;
        }

        private static bool IsGlass(ItemStack stack)
        {
            string code = stack.Collectible.Code.ToString();
            return code.StartsWith("confession:window-", StringComparison.Ordinal) || code.StartsWith("game:glasspane-", StringComparison.Ordinal);
        }

        private static ArchwayMaterial? GetBuildableFill(ArchwayMaterialRegistry registry, ItemStack stack)
        {
            if (stack.Block == null) return null;
            return registry.TryGetFillByCode(stack.Collectible.Code, out ArchwayMaterial? fill) && fill.Buildable ? fill : null;
        }

        private static void TakeMaterial(IPlayer player, ItemSlot slot, int amount)
        {
            if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            slot.TakeOut(amount);
            slot.MarkDirty();
        }

        private static void PlaySound(IWorldAccessor world, AssetLocation sound, BlockPos pos, IPlayer player)
        {
            if (world.Side == EnumAppSide.Client) world.PlaySoundAt(sound, pos, 0.5, player);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
        {
            ItemStack? heldStack = forPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            Variants? variants = world.BlockAccessor.GetBlockEntity(ResolveControlPos(world, selection))?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>()?.Variants;

            if (heldStack == null || variants == null)
            {
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);
            }

            if (!IsFinished(variants))
            {
                ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(world.Api);
                if (registry != null && GetBuildableFill(registry, heldStack) != null)
                {
                    return Help("confession:block-interaction-add-block", heldStack, glassSize);
                }
            }
            else if (blockCodeWithLintel != null && GetLintelWood(world, heldStack) != null)
            {
                // B10: planks need lintelSize, not glassSize
                return Help("confession:block-interaction-add-plank", heldStack, lintelSize);
            }
            else if (IsGlass(heldStack) && blockCodeWithGlass != null)
            {
                return Help("confession:block-interaction-add-glass", heldStack, glassSize);
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);
        }

        private static WorldInteraction[] Help(string langCode, ItemStack heldStack, int amount)
        {
            ItemStack displayStack = heldStack.Clone();
            displayStack.StackSize = amount;
            return
            [
                new WorldInteraction
                {
                    ActionLangCode = langCode,
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl",
                    Itemstacks = [displayStack]
                }
            ];
        }

        private static BlockPos ResolveControlPos(IWorldAccessor world, BlockSelection blockSel)
        {
            BlockPos pos = blockSel.Position.Copy();
            Block dummyBlock = world.BlockAccessor.GetBlock(pos);
            IMultiblockOffset? mbo = dummyBlock as IMultiblockOffset ?? dummyBlock.GetInterface<IMultiblockOffset>(world, pos);
            return mbo != null ? mbo.GetControlBlockPos(pos) : pos;
        }
    }
}
