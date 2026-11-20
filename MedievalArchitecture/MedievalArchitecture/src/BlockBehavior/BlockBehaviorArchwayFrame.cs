﻿using AttributeRenderingLibrary;
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
    /// Construction frame of an archway. Building steps (ctrl + right click):
    /// state 0 + rim stones (registry "rims") -> state 1, state 1 + mortar -> state 2,
    /// state 2 + fill block (registry "fills") -> the frame is replaced by the finished archway.
    /// </summary>
    public class BlockBehaviorArchwayFrame(Block block) : BlockBehavior(block), IInteractable
    {
        private const string StateAttribute = "state";
        private const string StyleAttribute = "style";
        /// <summary>originblock value of archways whose fill material is stored in "fill".</summary>
        private const string RegistryFillOriginblock = "fill";

        private int rimStoneAmount;
        private int intMultiplicator;
        private string? blockCodeBaseString;

        private AssetLocation finishSound = new AssetLocation();
        private AssetLocation soundAddStone = new AssetLocation();
        private AssetLocation soundAddMortar = new AssetLocation();

        public override void Initialize(JsonObject properties)
        {
            soundAddStone = AssetLocation.Create("sounds/effect/stonecrush");
            soundAddMortar = AssetLocation.Create("sounds/block/sand");
            finishSound = AssetLocation.Create("sounds/block/rock-break-pickaxe");

            intMultiplicator = properties["intMultiplicator"].AsInt(1);
            blockCodeBaseString = properties["blockCodeBaseString"].AsString();
            rimStoneAmount = MedievalArchitectureModSystem.Config.RimStoneAmount;

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

            ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(world.Api);
            if (registry == null) return;

            switch (arl.Variants.Get(StateAttribute))
            {
                case "0":
                    {
                        int amount = rimStoneAmount * intMultiplicator;
                        ArchwayMaterial? rim = GetBuildableRim(registry, heldStack);
                        if (rim == null || heldStack.StackSize < amount) return;

                        PlaySound(world, soundAddStone, controlPos, byPlayer);
                        if (world.Side != EnumAppSide.Server) return;

                        TakeMaterial(byPlayer, slot, amount);
                        SetFrameVariants(be, arl, new Dictionary<string, string>
                        {
                            [StateAttribute] = "1",
                            [ArchwayMaterialRegistry.RimAttribute] = rim.Id
                        });
                        return;
                    }

                case "1":
                    {
                        int amount = intMultiplicator;
                        if (!IsMortar(heldStack) || heldStack.StackSize < amount) return;

                        PlaySound(world, soundAddMortar, controlPos, byPlayer);
                        if (world.Side != EnumAppSide.Server) return;

                        TakeMaterial(byPlayer, slot, amount);
                        SetFrameVariants(be, arl, new Dictionary<string, string> { [StateAttribute] = "2" });
                        return;
                    }

                case "2":
                    {
                        int amount = intMultiplicator;
                        ArchwayMaterial? fill = GetBuildableFill(registry, heldStack);
                        if (fill == null || heldStack.StackSize < amount) return;

                        // No fallback: an archway with unknown rim stones would show missing textures.
                        string? rimId = arl.Variants.Get(ArchwayMaterialRegistry.RimAttribute);
                        if (string.IsNullOrEmpty(rimId) || !registry.TryGetRim(rimId, out _))
                        {
                            if (world.Api is ICoreClientAPI capi)
                            {
                                capi.TriggerIngameError(this, "archwayframe-unknownrim", Lang.Get("confession:archwayframe-unknownrim"));
                            }
                            else
                            {
                                world.Logger.Warning("[MedievalArchitecture] Archway frame at {0} cannot be finished: rim stones \"{1}\" are not in the archway material registry.", controlPos, rimId);
                            }
                            return;
                        }

                        PlaySound(world, finishSound, controlPos, byPlayer);
                        if (world.Side != EnumAppSide.Server) return;

                        TryCompleteArch(world, byPlayer, slot, arl, controlPos, rimId, fill, amount);
                        return;
                    }
            }
        }

        /// <summary>Server: replaces the frame by the finished archway and drops an empty frame.</summary>
        private void TryCompleteArch(IWorldAccessor world, IPlayer player, ItemSlot slot, BlockEntityBehaviorShapeTexturesFromAttributes frameArl, BlockPos controlPos, string rimId, ArchwayMaterial fill, int amount)
        {
            Block frameBlock = world.BlockAccessor.GetBlock(controlPos);
            string? orientation = frameBlock.Variant?["side"];
            Block? archBlock = string.IsNullOrEmpty(blockCodeBaseString) || string.IsNullOrEmpty(orientation)
                ? null
                : world.GetBlock(new AssetLocation(blockCodeBaseString + "-" + orientation));
            if (archBlock == null)
            {
                world.Logger.Error("[MedievalArchitecture] Archway frame {0} at {1}: archway block \"{2}-{3}\" not found.", frameBlock.Code, controlPos, blockCodeBaseString, orientation);
                return;
            }

            string style = frameArl.Variants.Get(StyleAttribute) ?? "a";

            // Take the material right away from the slot that was used (fixes the delayed take-out of older versions).
            TakeMaterial(player, slot, amount);

            world.BlockAccessor.SetBlock(archBlock.BlockId, controlPos);
            world.BlockAccessor.MarkBlockDirty(controlPos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(controlPos);

            var archVariants = new Dictionary<string, string>
            {
                [StyleAttribute] = style,
                [ArchwayMaterialRegistry.RimAttribute] = rimId,
                [ArchwayMaterialRegistry.FillAttribute] = fill.Id,
                [ArchwayMaterialRegistry.LegacyFillAttribute] = RegistryFillOriginblock,
                ["glass"] = "none"
            };

            if (world.BlockAccessor.GetBlockEntity(controlPos) is BlockEntityConstructable archBe)
            {
                archBe.SetVariants(archVariants);
            }
            else
            {
                // Should not happen: the block entity is created by SetBlock. Try once more a moment later.
                world.Logger.Warning("[MedievalArchitecture] Archway at {0}: block entity not ready, setting its materials a moment later.", controlPos);
                world.RegisterCallback(_ => (world.BlockAccessor.GetBlockEntity(controlPos) as BlockEntityConstructable)?.SetVariants(archVariants), 100);
            }

            DropEmptyFrame(world, controlPos, style);
        }

        /// <summary>Drops this frame (north variant) without materials, so it can be used again.</summary>
        private void DropEmptyFrame(IWorldAccessor world, BlockPos pos, string style)
        {
            Block? frame = world.GetBlock(block.CodeWithVariant("side", "north"));
            if (frame == null) return;

            var stack = new ItemStack(frame);
            Variants variants = Variants.FromStack(stack);
            variants.Set(new Dictionary<string, string>
            {
                [StyleAttribute] = style,
                [ArchwayMaterialRegistry.RimAttribute] = "none",
                [StateAttribute] = "0"
            });
            variants.ToStack(stack);
            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        private static void SetFrameVariants(BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes arl, Dictionary<string, string> values)
        {
            if (be is BlockEntityConstructable constructable)
            {
                constructable.SetVariants(values);
                return;
            }
            // Frames placed before they had their own block entity class use a generic one, which does not redraw
            // itself on the client when its attributes change. Request a second redraw after the data has arrived.
            arl.Variants.Set(values);
            be.MarkDirty(true);
            BlockPos pos = be.Pos.Copy();
            IWorldAccessor world = be.Api.World;
            world.RegisterCallback(_ => world.BlockAccessor.MarkBlockDirty(pos), 250);
        }

        private static void TakeMaterial(IPlayer player, ItemSlot slot, int amount)
        {
            if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
            slot.TakeOut(amount);
            slot.MarkDirty();
        }

        private static void PlaySound(IWorldAccessor world, AssetLocation sound, BlockPos pos, IPlayer player)
        {
            if (world.Side == EnumAppSide.Client) world.PlaySoundAt(sound, pos, 1, player);
        }

        private static ArchwayMaterial? GetBuildableRim(ArchwayMaterialRegistry registry, ItemStack stack)
        {
            return registry.TryGetRimByCode(stack.Collectible.Code, out ArchwayMaterial? rim) && rim.Buildable ? rim : null;
        }

        private static ArchwayMaterial? GetBuildableFill(ArchwayMaterialRegistry registry, ItemStack stack)
        {
            if (stack.Block == null) return null;
            return registry.TryGetFillByCode(stack.Collectible.Code, out ArchwayMaterial? fill) && fill.Buildable ? fill : null;
        }

        private static bool IsMortar(ItemStack stack) => stack.Collectible.Code.Path == "mortar";

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
        {
            ItemStack? heldStack = forPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            BlockEntity? be = world.BlockAccessor.GetBlockEntity(ResolveControlPos(world, selection));
            Variants? variants = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>()?.Variants;
            ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(world.Api);

            if (heldStack == null || variants == null || registry == null)
            {
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer, ref handling);
            }

            switch (variants.Get(StateAttribute))
            {
                case "0":
                    if (GetBuildableRim(registry, heldStack) != null)
                    {
                        return Help("confession:block-interaction-add-rimStones", heldStack, rimStoneAmount * intMultiplicator);
                    }
                    break;

                case "1":
                    if (IsMortar(heldStack))
                    {
                        return Help("confession:block-interaction-add-mortar", heldStack, intMultiplicator);
                    }
                    break;

                case "2":
                    if (GetBuildableFill(registry, heldStack) != null)
                    {
                        return Help("confession:block-interaction-add-block", heldStack, intMultiplicator);
                    }
                    break;
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
