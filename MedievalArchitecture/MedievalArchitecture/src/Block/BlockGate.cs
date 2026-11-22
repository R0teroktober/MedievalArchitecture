using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public enum HorizontalRotation
    {
        North,
        East,
        South,
        West
    }

    public class BlockGateBase : BlockGeneric, IMultiBlockInteract, IMultiBlockColSelBoxes, IMultiBlockBlockBreaking, IMultiBlockBlockProperties
    {
        private int width = 1;
        private int height = 1;
        private int length = 1;

        // "airtight": the gate seals rooms (heat retention). "liquidBarrier": the closed gate holds back water (default: airtight).
        private bool airtight = true;
        private bool liquidBarrier = true;

        // Runtime lookup tables for the active block.
        // Key format: "x/y/z" in north-based local coordinates.
        private readonly Dictionary<string, Cuboidf[]> closedBoxesByOffset = new();
        private readonly Dictionary<string, Cuboidf[]> openBoxesByOffset = new();

        // Dummy block used for all parts of newly placed gates (see BlockGatePart)
        private static readonly AssetLocation GatePartCode = new AssetLocation("confession", "gatepart");
        private Block? gatePartBlock;


        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Read the north-based multiblock dimensions from block attributes.
            width = Attributes?["width"].AsInt(1) ?? 1;
            height = Attributes?["height"].AsInt(1) ?? 1;
            length = Attributes?["length"].AsInt(1) ?? 1;
            airtight = Attributes?["airtight"].AsBool(true) ?? true;
            liquidBarrier = Attributes?["liquidBarrier"].AsBool(airtight) ?? airtight;

            // While sneaking, ask the gate before the held item, so plumb and square / padlock can be redirected to the root
            PlacedPriorityInteract = true;

            gatePartBlock = api.World.GetBlock(GatePartCode);
            if (gatePartBlock == null)
            {
                api.Logger.Error("[MedievalArchitecture] Block {0} not found, {1} falls back to vanilla dummy blocks.", GatePartCode, Code);
            }

            LoadBoxSets();
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            // Rotation must be determined before placement checks.
            HorizontalRotation rotation = DetermineRotation(byPlayer, blockSel);

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            // Root position: all vanilla rules (replaceable, entities, land claims, block behaviors)
            if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                return false;
            }

            HorizontalRotation rotation = DetermineRotation(byPlayer, blockSel);
            BlockPos rootPos = blockSel.Position;

            // Every position of the gate is checked BEFORE anything is placed, so there is never a half gate.
            // The root is checked again here, because the vanilla entity check above does not know the rotation yet.
            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = 0; dz < length; dz++)
                    {
                        Vec3i off = GetPlacementOffset(dx, dy, dz, rotation);
                        BlockPos partPos = rootPos.AddCopy(off.X, off.Y, off.Z);

                        if (!CanPlacePartAt(world, byPlayer, partPos, new Vec3i(dx, dy, dz), rotation, ref failureCode))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Same checks as vanilla Block.CanPlaceBlock, for one position of the gate.
        /// </summary>
        private bool CanPlacePartAt(IWorldAccessor world, IPlayer byPlayer, BlockPos partPos, Vec3i northLocalOffset, HorizontalRotation rotation, ref string failureCode)
        {
            // Inside the world (e.g. a tall gate must not reach above the build height)
            if (!world.BlockAccessor.IsValidPos(partPos))
            {
                (world.Api as ICoreClientAPI)?.TriggerIngameError(this, "outsideworld", Lang.Get("confession:placefailure-gate-outsideworld"));
                failureCode = "__ignore__";
                return false;
            }

            // Free space (air or replaceable blocks like grass)
            if (!world.BlockAccessor.GetBlock(partPos).IsReplacableBy(this))
            {
                failureCode = "notreplaceable";
                return false;
            }

            // Land claims: a gate can reach into a neighbouring claim
            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, partPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                byPlayer.InventoryManager.ActiveHotbarSlot?.MarkDirty();
                failureCode = "claimed";
                return false;
            }

            // Players or animals inside the closed gate at this position
            Cuboidf[] boxes = GetClosedBoxesForPlacement(northLocalOffset, rotation);
            if (boxes.Length > 0 && world.GetIntersectingEntities(partPos, boxes, e => e.IsInteractable).Length != 0)
            {
                failureCode = "entityintersecting";
                return false;
            }

            return true;
        }

        private Cuboidf[] GetClosedBoxesForPlacement(Vec3i northLocalOffset, HorizontalRotation rotation)
        {
            string key = OffsetKey(northLocalOffset.X, northLocalOffset.Y, northLocalOffset.Z);
            if (!closedBoxesByOffset.TryGetValue(key, out Cuboidf[]? boxes)) return Array.Empty<Cuboidf>();
            return RotateBoxes(boxes, rotation);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            HorizontalRotation rotation = DetermineRotation(byPlayer, blockSel);
            BlockPos rootPos = blockSel.Position;

            // Place the controller block first.
            bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
            if (!placed)
            {
                return false;
            }

            // Place all dummy blocks after the controller.
            IBulkBlockAccessor bulkAccessor = world.GetBlockAccessorBulkUpdate(world.Side == EnumAppSide.Server, false);
            var placedParts = new List<(BlockPos pos, Vec3i offsetToRoot)>();

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = 0; dz < length; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            continue;
                        }

                        Vec3i off = GetPlacementOffset(dx, dy, dz, rotation);
                        BlockPos dummyPos = rootPos.AddCopy(off.X, off.Y, off.Z);

                        // New gates always use gatepart. Vanilla dummies only as emergency fallback if gatepart is missing.
                        Block? dummyBlock = gatePartBlock ?? GetDummyBlockForOffset(world, off);
                        if (dummyBlock == null)
                        {
                            world.Logger.Warning(
                                "MedievalArchitecture: Missing dummy block for offset {0}/{1}/{2} while placing {3} at {4}",
                                off.X, off.Y, off.Z, Code, rootPos
                            );
                            continue;
                        }

                        bulkAccessor.SetBlock(dummyBlock.Id, dummyPos);

                        if (dummyBlock == gatePartBlock)
                        {
                            placedParts.Add((dummyPos, new Vec3i(-off.X, -off.Y, -off.Z)));
                        }
                    }
                }
            }

            bulkAccessor.Commit();

            // The block entities of the parts only exist after Commit, so the offset can only be written now.
            foreach (var (partPos, offsetToRoot) in placedParts)
            {
                BEGatePart? partBe = world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos);
                if (partBe == null)
                {
                    world.BlockAccessor.SpawnBlockEntity("GatePart", partPos);
                    partBe = world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos);
                }

                if (partBe != null)
                {
                    partBe.SetOffsetToRoot(offsetToRoot);
                }
                else
                {
                    world.Logger.Warning("MedievalArchitecture: Could not create block entity for gate part at {0} (gate at {1})", partPos, rootPos);
                }
            }

            // Write the chosen rotation into the existing block entity behavior.
            BlockEntity be = world.BlockAccessor.GetBlockEntity(rootPos);
            if (be != null)
            {
                BEBehaviorGate gateState = be.GetBehavior<BEBehaviorGate>();
                if (gateState != null)
                {
                    gateState.SetRotation(rotation);

                    // New gate: all parts are gateparts already, no conversion needed
                    if (gatePartBlock != null)
                    {
                        gateState.SetPartsConverted();
                    }

                    be.MarkDirty(true);
                }
            }

            return true;
        }

        private HorizontalRotation DetermineRotation(IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockFacing[] faces = SuggestedHVOrientation(byPlayer, blockSel);
            BlockFacing facing = faces != null && faces.Length > 0 ? faces[0] : BlockFacing.NORTH;

            if (facing == BlockFacing.EAST) return HorizontalRotation.East;
            if (facing == BlockFacing.SOUTH) return HorizontalRotation.South;
            if (facing == BlockFacing.WEST) return HorizontalRotation.West;

            return HorizontalRotation.North;
        }
        private HorizontalRotation GetStoredRotation(IWorldAccessor world, BlockPos controllerPos)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(controllerPos);
            if (be == null) return HorizontalRotation.North;

            BEBehaviorGate gateBehavior = be.GetBehavior<BEBehaviorGate>();
            if (gateBehavior == null) return HorizontalRotation.North;

            return gateBehavior.Rotation;
        }

        private Vec3i GetPlacementOffset(int dx, int dy, int dz, HorizontalRotation rotation)
        {
            // Controller = south-western-most block in the default north-oriented layout.
            // Expansion directions in north layout:
            // - eastward  => X+
            // - northward => Z-
            switch (rotation)
            {
                case HorizontalRotation.East:
                    return new Vec3i(dz, dy, dx);

                case HorizontalRotation.South:
                    return new Vec3i(-dx, dy, dz);

                case HorizontalRotation.West:
                    return new Vec3i(-dz, dy, -dx);

                default: // North
                    return new Vec3i(dx, dy, -dz);
            }
        }

        private Block GetDummyBlockForOffset(IWorldAccessor world, Vec3i offset)
        {
            string codePath =
                "multiblock-monolithic" +
                OffsetToVariant(offset.X) +
                OffsetToVariant(offset.Y) +
                OffsetToVariant(offset.Z);

            return world.GetBlock(new AssetLocation("game", codePath));
        }

        private string OffsetToVariant(int value)
        {
            if (value == 0) return "-0";
            if (value < 0) return "-n" + (-value);
            return "-p" + value;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Old gate: convert first, so all parts behave the same from now on
            TryConvertLegacyParts(world, pos);

            // Rotation vorher sichern, weil die BE nach erfolgreichem Break evtl. weg ist
            HorizontalRotation rotation = GetStoredRotation(world, pos);

            // Erst normal den Controller brechen lassen.
            // Reinforcement darf hier den Break verhindern.
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

            // Wenn der Controller noch da ist, wurde der Break verhindert.
            Block remainingBlock = world.BlockAccessor.GetBlock(pos);
            if (remainingBlock != null && remainingBlock.Id == Id)
            {
                return;
            }

            RemoveDummyBlocks(world, pos, rotation);
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, string ignitedByPlayerUid)
        {
            // Explosions write into world.BulkBlockAccessor and only take effect on Commit.
            // One explosion can hit several parts of the gate; all of them forward to here before the commit,
            // while world.BlockAccessor still reports the root as present.
            // Dupe protection: if this explosion already removed the root, do nothing (no second drop).
            IBulkBlockAccessor explosionAccessor = world.BulkBlockAccessor;
            if (IsStagedForRemoval(explosionAccessor, pos))
            {
                return;
            }

            // Rotation before the root might be removed
            HorizontalRotation rotation = GetStoredRotation(world, pos);

            // Vanilla handling: reinforcement loses strength and prevents the removal, otherwise the root is removed and may drop
            base.OnBlockExploded(world, pos, explosionCenter, blastType, ignitedByPlayerUid);

            // Vanilla does not call OnBlockBroken for explosions, so the parts must be removed here
            if (IsStagedForRemoval(explosionAccessor, pos))
            {
                RemoveDummyBlocks(world, pos, rotation, explosionAccessor);
            }
        }

        #region Conversion of old gates (vanilla dummies -> gatepart)

        /// <summary>
        /// Converts the vanilla dummy blocks (game:multiblock-monolithic-*) of a gate placed before the gatepart system
        /// into gateparts, and deletes reinforcement that was (without effect) placed on a dummy.
        /// Server only. Runs once per gate; if not all chunks of the gate are loaded, nothing happens and it is retried later.
        /// Only dummies whose own offset provably points to this root are replaced (searched in a small area around the root).
        /// </summary>
        public void TryConvertLegacyParts(IWorldAccessor world, BlockPos rootPos)
        {
            if (world.Side != EnumAppSide.Server || gatePartBlock == null) return;
            if (world.BlockAccessor.GetBlock(rootPos) != this) return;

            BlockEntity? rootBe = world.BlockAccessor.GetBlockEntity(rootPos);
            BEBehaviorGate? gateBehavior = rootBe?.GetBehavior<BEBehaviorGate>();
            if (rootBe == null || gateBehavior == null || gateBehavior.PartsConverted) return;

            var legacyParts = new List<(BlockPos pos, Vec3i offsetToRoot)>();
            int gatepartsFound = 0;

            // Search a small area around the root instead of only the positions the current code would use:
            // older mod versions may have arranged the dummies differently. What makes a dummy "ours" is not its
            // position, but that its own offset provably points to this root.
            int horizontalRange = Math.Max(width, length) - 1;
            int checkedPositions = 0;

            for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = -horizontalRange; dz <= horizontalRange; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;

                        BlockPos partPos = rootPos.AddCopy(dx, dy, dz);

                        // "Not loaded" does not mean "not there": try again later, do not mark as converted
                        if (world.BlockAccessor.GetChunkAtBlockPos(partPos) == null)
                        {
                            world.Logger.Debug("[MedievalArchitecture] Gate at {0}: conversion postponed, not all chunks loaded", rootPos);
                            return;
                        }

                        checkedPositions++;
                        Block partBlock = world.BlockAccessor.GetBlock(partPos);

                        if (partBlock is BlockMultiblock dummy
                            && dummy.OffsetInv != null
                            && partPos.AddCopy(dummy.OffsetInv).Equals(rootPos))
                        {
                            legacyParts.Add((partPos, new Vec3i(dummy.OffsetInv.X, dummy.OffsetInv.Y, dummy.OffsetInv.Z)));
                        }
                        else if (partBlock is BlockGatePart
                            && rootPos.Equals(world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos)?.RootPos))
                        {
                            gatepartsFound++;
                        }
                    }
                }
            }

            if (legacyParts.Count == 0)
            {
                if (gatepartsFound > 0)
                {
                    // Already made of gateparts (e.g. placed with a development build before the marker existed)
                    gateBehavior.SetPartsConverted();
                    rootBe.MarkDirty();
                    world.Logger.Debug("[MedievalArchitecture] Gate at {0}: already consists of {1} gateparts, marked as converted", rootPos, gatepartsFound);
                }
                else
                {
                    // Not marked as converted, so a later attempt (or a fixed mod version) can still convert it
                    world.Logger.Debug("[MedievalArchitecture] Gate at {0}: no vanilla dummy parts found ({1} positions checked), not converted", rootPos, checkedPositions);
                }
                return;
            }

            // Reinforcement is stored per position, independent of the block, so it must be cleaned up explicitly
            string reinforcementResult = RemoveLegacyDummyReinforcement(world, legacyParts);

            foreach (var (partPos, offsetToRoot) in legacyParts)
            {
                world.BlockAccessor.SetBlock(gatePartBlock.Id, partPos);

                BEGatePart? partBe = world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos);
                if (partBe == null)
                {
                    world.BlockAccessor.SpawnBlockEntity("GatePart", partPos);
                    partBe = world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos);
                }

                if (partBe != null)
                {
                    partBe.SetOffsetToRoot(offsetToRoot);
                }
                else
                {
                    world.Logger.Warning("[MedievalArchitecture] Could not create block entity for converted gate part at {0} (gate at {1})", partPos, rootPos);
                }
            }

            gateBehavior.SetPartsConverted();
            rootBe.MarkDirty();

            world.Logger.Debug("[MedievalArchitecture] Gate at {0}: {1} dummies converted{2}", rootPos, legacyParts.Count, reinforcementResult);
        }

        /// <summary>
        /// Deletes reinforcement that was placed on a vanilla dummy part of an old gate.
        /// It never had an effect and was never shown to the player (only the root counts), so it is not transferred.
        /// The root's own reinforcement is not touched. Returns a short text for the log.
        /// </summary>
        private string RemoveLegacyDummyReinforcement(IWorldAccessor world, List<(BlockPos pos, Vec3i offsetToRoot)> legacyParts)
        {
            ModSystemBlockReinforcement? bre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (bre == null) return "";

            int removed = 0;
            foreach (var (partPos, _) in legacyParts)
            {
                if (bre.GetReinforcment(partPos) != null)
                {
                    bre.ClearReinforcement(partPos);
                    removed++;
                }
            }

            return removed > 0 ? ", removed ineffective reinforcement from " + removed + " dummies" : "";
        }

        #endregion

        private static bool IsStagedForRemoval(IBulkBlockAccessor accessor, BlockPos pos)
        {
            return accessor.StagedBlocks.ContainsKey(pos) && accessor.GetStagedBlockId(pos) == 0;
        }

        #region BlockInteract

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return MBOnBlockInteractStart(world, byPlayer, blockSel, new Vec3i(0, 0, 0));
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return MBGetPlacedBlockInteractionHelp(world, selection, forPlayer, new Vec3i(0, 0, 0));
        }

        /// <summary>
        /// Sneak + right click with plumb and square or padlock: handled by the gate (see MBOnBlockInteractStart).
        /// </summary>
        private static bool IsReinforceOrLockInteraction(IPlayer byPlayer)
        {
            if (byPlayer?.Entity?.Controls.ShiftKey != true) return false;

            CollectibleObject? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;
            return held is ItemPlumbAndSquare || held is ItemPadlock;
        }

        /// <summary>
        /// Runs the original vanilla item interaction (plumb and square / padlock), but on the root position.
        /// "Redirect instead of rebuild": claims, resources, group mode, strength, messages, sounds and costs
        /// stay exactly vanilla. Reinforcement and lock therefore always end up on the root block.
        /// </summary>
        private static void RedirectToolInteractionToRoot(IPlayer byPlayer, BlockSelection blockSel, BlockPos controllerPos)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            CollectibleObject held = slot.Itemstack.Collectible;

            BlockSelection rootSel = blockSel.Clone();
            rootSel.Position = controllerPos.Copy();

            EnumHandHandling handling = EnumHandHandling.NotHandled;
            held.OnHeldInteractStart(slot, byPlayer.Entity, rootSel, null, true, ref handling);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return MBOnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, new Vec3i(0, 0, 0));
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            MBOnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, new Vec3i(0, 0, 0));
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            return MBOnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, cancelReason, new Vec3i(0, 0, 0));
        }


        

        public bool MBDoParticalSelection(IWorldAccessor world, BlockPos pos, Vec3i offset)
        {
            // not used
            return false;
        }

        public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
        {

            var controlerPos = blockSel.Position.AddCopy(offset);

            // Old gate: convert on the first interaction at the latest (the player stands right in front of it, all chunks are loaded)
            TryConvertLegacyParts(world, controlerPos);

            // PlacedPriorityInteract = true: while sneaking, the gate is asked BEFORE the held item.
            if (byPlayer?.Entity?.Controls.ShiftKey == true)
            {
                if (IsReinforceOrLockInteraction(byPlayer))
                {
                    RedirectToolInteractionToRoot(byPlayer, blockSel, controlerPos);
                    // Handled: the item must not act a second time on this part, and the gate must not toggle (see Stop)
                    return true;
                }

                // Any other held item (e.g. a block to place): let the item act first, like vanilla
                if (byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack != null)
                {
                    return false;
                }
            }

            ModSystemBlockReinforcement bre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (bre.IsLockedForInteract(controlerPos, byPlayer))
            {
                if (world.Side == EnumAppSide.Client)
                {
                    (world.Api as ICoreClientAPI).TriggerIngameError(this, "locked", Lang.Get("ingameerror-locked"));
                }
                return false;
            } else

            return true;
        }

        public bool MBOnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
        {
            return true;
        }

        public void MBOnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Vec3i offset)
        {
            // Reinforcing / locking must not open or close the gate
            if (IsReinforceOrLockInteraction(byPlayer)) return;

            BlockPos controllerPos = blockSel.Position.AddCopy(offset);
            BlockEntity be = world.BlockAccessor.GetBlockEntity(controllerPos);
            if (be == null) return;
            BEBehaviorGate gateBehavior = be.GetBehavior<BEBehaviorGate>();
            if (gateBehavior == null) return;

            if (world.Side != EnumAppSide.Server)
            {
                return;
            }
            gateBehavior.Toggle();

            // Like vanilla doors: neighbouring water (and other blocks) re-check the gate in its new state
            TriggerNeighbourUpdates(world, controllerPos);
        }

        public bool MBOnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason, Vec3i offset)
        {

            return true;
        }

        public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset)
        {
            return new ItemStack(this);
        }

        public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer, Vec3i offset)
        {
            WorldInteraction[] baseHelp = base.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer) ?? Array.Empty<WorldInteraction>();

            if (forPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemPlumbAndSquare)
            {
                return baseHelp;
            }

            var help = new List<WorldInteraction>(baseHelp)
            {
                // Vanilla's held help says "right click"; for gates sneaking is needed (like vanilla doors)
                new WorldInteraction()
                {
                    ActionLangCode = "confession:blockhelp-gate-reinforce",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                }
            };

            bool isRoot = offset.X == 0 && offset.Y == 0 && offset.Z == 0;
            if (!isRoot)
            {
                // Removing reinforcement is handled by the item only (left click), it cannot be redirected without Harmony
                help.Add(new WorldInteraction()
                {
                    ActionLangCode = "confession:blockhelp-gate-removereinforcement-mainblock",
                    MouseButton = EnumMouseButton.Left
                });
            }

            return help.ToArray();
        }

        public BlockSounds MBGetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack stack, Vec3i offset)
        {
            return Sounds;
        }



        #endregion

        #region Rooms and liquids

        // Vanilla asks every block position for its heat retention (rooms, e.g. cellars) and for its liquid barrier (water).
        // Gate parts forward these questions to the root (IMultiBlockBlockProperties, also used by vanilla dummies).
        // A side of a gate position counts as closed if one collision box of the current state covers it completely.

        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
        {
            return GetGateRetention(api.World.BlockAccessor, pos, facing, type, new Vec3i(0, 0, 0));
        }

        public override float GetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos)
        {
            return GetGateLiquidBarrier(api.World.BlockAccessor, pos, face, new Vec3i(0, 0, 0));
        }

        public int MBGetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, Vec3i offsetInv)
        {
            return GetGateRetention(api.World.BlockAccessor, pos, facing, type, offsetInv);
        }

        public float MBGetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, Vec3i offsetInv)
        {
            return GetGateLiquidBarrier(api.World.BlockAccessor, pos, face, offsetInv);
        }

        // The remaining members of IMultiBlockBlockProperties keep the behavior old gates with vanilla dummies had before
        // (the vanilla dummy asked the root block directly with its own position).
        public bool MBCanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea, Vec3i offsetInv)
        {
            return CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
        }

        public bool MBSideIsSolid(BlockPos pos, int faceIndex, Vec3i offsetInv)
        {
            return SideIsSolid(pos, faceIndex);
        }

        public bool MBSideIsSolid(IBlockAccessor blockAccessor, BlockPos pos, int faceIndex, Vec3i offsetInv)
        {
            return SideIsSolid(blockAccessor, pos, faceIndex);
        }

        public JsonObject MBGetAttributes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetAttributes(blockAccessor, pos);
        }

        /// <summary>Same rules as vanilla doors and trapdoors (BlockBehaviorDoor/BlockBehaviorTrapDoor.GetRetention).</summary>
        private int GetGateRetention(IBlockAccessor blockAccessor, BlockPos pos, BlockFacing facing, EnumRetentionType type, Vec3i offset)
        {
            Cuboidf[] boxes = GetBoxesForState(blockAccessor, pos, offset, null);
            bool closed = IsSideCovered(boxes, facing);

            if (type == EnumRetentionType.Sound) return closed ? 3 : 0;
            if (!airtight) return 0;

            // Positions without any part of the gate in both states are air
            if (!HasBoxesInAnyState(blockAccessor, pos, offset)) return 0;

            int insulation = GetInsulation(blockAccessor, pos.AddCopy(offset));
            if (api.World.Config.GetBool("openDoorsNotSolid", false)) return closed ? insulation : 0;

            // Like vanilla doors: an airtight door is a wall for rooms, also when it is open
            return closed || IsSideCovered(boxes, facing.Opposite) ? insulation : 3;
        }

        private float GetGateLiquidBarrier(IBlockAccessor blockAccessor, BlockPos pos, BlockFacing face, Vec3i offset)
        {
            if (!liquidBarrier) return 0f;
            return IsSideCovered(GetBoxesForState(blockAccessor, pos, offset, null), face) ? 1f : 0f;
        }

        private int GetInsulation(IBlockAccessor blockAccessor, BlockPos rootPos)
        {
            EnumBlockMaterial material = GetBlockMaterial(blockAccessor, rootPos);
            if (material == EnumBlockMaterial.Ore || material == EnumBlockMaterial.Stone || material == EnumBlockMaterial.Soil || material == EnumBlockMaterial.Ceramic)
            {
                return -1;
            }
            return 1;
        }

        private bool HasBoxesInAnyState(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return GetBoxesForState(blockAccessor, pos, offset, false).Length > 0 || GetBoxesForState(blockAccessor, pos, offset, true).Length > 0;
        }

        /// <summary>True if one of the boxes covers the given side of the block completely (it does not have to touch it).</summary>
        private static bool IsSideCovered(Cuboidf[] boxes, BlockFacing side)
        {
            const float epsilon = 0.01f;

            foreach (Cuboidf box in boxes)
            {
                if (box == null) continue;

                bool fullX = Math.Min(box.X1, box.X2) <= epsilon && Math.Max(box.X1, box.X2) >= 1 - epsilon;
                bool fullY = Math.Min(box.Y1, box.Y2) <= epsilon && Math.Max(box.Y1, box.Y2) >= 1 - epsilon;
                bool fullZ = Math.Min(box.Z1, box.Z2) <= epsilon && Math.Max(box.Z1, box.Z2) >= 1 - epsilon;

                bool covered = side.Axis switch
                {
                    EnumAxis.X => fullY && fullZ,
                    EnumAxis.Y => fullX && fullZ,
                    _ => fullX && fullY
                };
                if (covered) return true;
            }

            return false;
        }

        #endregion

        #region BlockColSelBoxes

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // Controller queries do not go through IMultiBlockColSelBoxes.
            // Use the same runtime resolver with zero offset.
            return GetBoxesForCurrentState(blockAccessor, pos, new Vec3i(0, 0, 0));
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // Controller queries do not go through IMultiBlockColSelBoxes.
            // Use the same runtime resolver with zero offset.
            return GetBoxesForCurrentState(blockAccessor, pos, new Vec3i(0, 0, 0));
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return GetBoxesForCurrentState(blockAccessor, pos, offset);
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return GetBoxesForCurrentState(blockAccessor, pos, offset);
        }

        #endregion

        private Cuboidf[] GetBoxesForCurrentState(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return GetBoxesForState(blockAccessor, pos, offset, null);
        }

        /// <summary>Boxes of one gate position. toggledOverride: null = current state, true = open, false = closed.</summary>
        private Cuboidf[] GetBoxesForState(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset, bool? toggledOverride)
        {
            BlockPos controllerPos = pos.AddCopy(offset);
            BlockEntity be = blockAccessor.GetBlockEntity(controllerPos);

            HorizontalRotation rotation = HorizontalRotation.North;
            bool toggled = false;

            if (be != null)
            {
                BEBehaviorGate gateBehavior = be.GetBehavior<BEBehaviorGate>();
                if (gateBehavior != null)
                {
                    rotation = gateBehavior.Rotation;
                    toggled = gateBehavior.Toggled;
                }
            }

            if (toggledOverride.HasValue) toggled = toggledOverride.Value;

            // Convert the current world-space offset back into north-based local coordinates.
            Vec3i localOffset = WorldOffsetToNorthLocal(offset, rotation);

            Dictionary<string, Cuboidf[]> activeSet = toggled ? openBoxesByOffset : closedBoxesByOffset;
            string key = OffsetKey(localOffset.X, localOffset.Y, localOffset.Z);

            // Missing entries inside the declared volume fall back to empty boxes.
            if (!activeSet.TryGetValue(key, out Cuboidf[] boxes))
            {
                return Array.Empty<Cuboidf>();
            }

            // Rotate the north-based boxes into the actual placed rotation.
            return RotateBoxes(boxes, rotation);
        }

        private void LoadBoxSets()
        {
            closedBoxesByOffset.Clear();
            openBoxesByOffset.Clear();

            LoadBoxList("boxesClosed", closedBoxesByOffset);
            LoadBoxList("boxesOpen", openBoxesByOffset);
        }

        private void LoadBoxList(string attributeName, Dictionary<string, Cuboidf[]> target)
        {
            JsonObject[] entries = Attributes?[attributeName].AsArray();
            if (entries == null) return;

            foreach (JsonObject entry in entries)
            {
                int[] offset = entry["offset"].AsArray<int>();
                if (offset == null || offset.Length != 3) continue;

                Cuboidf[] boxes = ParseBoxes(entry["boxes"]);
                string key = OffsetKey(offset[0], offset[1], offset[2]);
                target[key] = boxes;
            }
        }

        private Cuboidf[] ParseBoxes(JsonObject boxesObject)
        {
            if (boxesObject == null || !boxesObject.Exists) return Array.Empty<Cuboidf>();

            JsonObject[] rawBoxes = boxesObject.AsArray();
            if (rawBoxes == null || rawBoxes.Length == 0) return Array.Empty<Cuboidf>();

            Cuboidf[] result = new Cuboidf[rawBoxes.Length];

            for (int i = 0; i < rawBoxes.Length; i++)
            {
                float[] values = rawBoxes[i].AsArray<float>();
                if (values == null || values.Length != 6) continue;

                result[i] = new Cuboidf(values[0], values[1], values[2], values[3], values[4], values[5]);
            }

            return Array.FindAll(result, box => box != null);
        }

        private Vec3i WorldOffsetToNorthLocal(Vec3i offsetToController, HorizontalRotation rotation)
        {
            // offsetToController points from the current dummy block to the controller.
            // Convert it to controller -> current part first.
            int wx = -offsetToController.X;
            int wy = -offsetToController.Y;
            int wz = -offsetToController.Z;

            // Undo the placement rotation so lookup stays north-based.
            switch (rotation)
            {
                case HorizontalRotation.East:
                    return new Vec3i(wz, wy, wx);

                case HorizontalRotation.South:
                    return new Vec3i(-wx, wy, wz);

                case HorizontalRotation.West:
                    return new Vec3i(-wz, wy, -wx);

                default: // North
                    return new Vec3i(wx, wy, -wz);
            }
        }

        private Cuboidf[] RotateBoxes(Cuboidf[] boxes, HorizontalRotation rotation)
        {
            if (boxes == null || boxes.Length == 0) return Array.Empty<Cuboidf>();
            if (rotation == HorizontalRotation.North) return boxes;


            // don't get confused! North is default orientiation in my shapefiles
            float degY = rotation switch
            {
                HorizontalRotation.East => 270f,
                HorizontalRotation.South => 180f,
                HorizontalRotation.West => 90f,
                _ => 0f
            };

            Cuboidf[] rotated = new Cuboidf[boxes.Length];

            for (int i = 0; i < boxes.Length; i++)
            {
                rotated[i] = boxes[i].RotatedCopy(0, degY, 0, new Vec3d(0.5, 0.5, 0.5));
            }

            return rotated;
        }

        private string OffsetKey(int x, int y, int z)
        {
            return x + "/" + y + "/" + z;
        }

        public void MBOnBlockBroken(IWorldAccessor world, BlockPos pos, Vec3i offset, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            BlockPos controllerPos = pos.AddCopy(offset);
            Block controllerBlock = world.BlockAccessor.GetBlock(controllerPos);

            // Old gate: convert first (see OnBlockBroken)
            TryConvertLegacyParts(world, controllerPos);

            if (controllerBlock == null || controllerBlock.Id == 0)
            {
                return;
            }

            controllerBlock.OnBlockBroken(world, controllerPos, byPlayer, dropQuantityMultiplier);
        }

        /// <summary>
        /// Removes all dummy parts (vanilla dummies and gateparts) of the gate at controllerPos.
        /// A part belongs to this gate if its own offset points to this root (not because of its position),
        /// so a neighbouring gate is never touched and parts of older mod versions are found as well.
        /// If an accessor is given (explosion), the removal is staged there and committed by the caller.
        /// </summary>
        private void RemoveDummyBlocks(IWorldAccessor world, BlockPos controllerPos, HorizontalRotation rotation, IBulkBlockAccessor? stagingAccessor = null)
        {
            IBulkBlockAccessor bulkAccessor = stagingAccessor ?? world.GetBlockAccessorBulkUpdate(world.Side == EnumAppSide.Server, false);

            int horizontalRange = Math.Max(width, length) - 1;

            for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = -horizontalRange; dz <= horizontalRange; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;

                        BlockPos partPos = controllerPos.AddCopy(dx, dy, dz);
                        if (IsOwnPart(world.BlockAccessor, partPos, controllerPos))
                        {
                            bulkAccessor.SetBlock(0, partPos);
                        }
                    }
                }
            }

            if (stagingAccessor == null)
            {
                bulkAccessor.Commit();
            }
        }

        /// <summary>
        /// Server: triggers a neighbour block update around the root and all parts of the gate at controllerPos.
        /// </summary>
        private void TriggerNeighbourUpdates(IWorldAccessor world, BlockPos controllerPos)
        {
            if (world.Side != EnumAppSide.Server) return;

            IBlockAccessor blockAccessor = world.BlockAccessor;
            blockAccessor.TriggerNeighbourBlockUpdate(controllerPos);

            int horizontalRange = Math.Max(width, length) - 1;

            for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = -horizontalRange; dz <= horizontalRange; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;

                        BlockPos partPos = controllerPos.AddCopy(dx, dy, dz);
                        if (IsOwnPart(blockAccessor, partPos, controllerPos))
                        {
                            blockAccessor.TriggerNeighbourBlockUpdate(partPos);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True if the block at partPos is a dummy part (vanilla dummy or gatepart) whose offset points to rootPos.
        /// </summary>
        private static bool IsOwnPart(IBlockAccessor blockAccessor, BlockPos partPos, BlockPos rootPos)
        {
            Block partBlock = blockAccessor.GetBlock(partPos);

            if (partBlock is BlockMultiblock dummy)
            {
                return dummy.OffsetInv != null && partPos.AddCopy(dummy.OffsetInv).Equals(rootPos);
            }

            if (partBlock is BlockGatePart)
            {
                return rootPos.Equals(blockAccessor.GetBlockEntity<BEGatePart>(partPos)?.RootPos);
            }

            return false;
        }

        public int MBGetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex, Vec3i offsetInv)
        {
            return GetRandomColor(capi, pos.AddCopy(offsetInv), facing, rndIndex);
        }

        public int MBGetColorWithoutTint(ICoreClientAPI capi, BlockPos pos, Vec3i offsetInv)
        {
            return GetColorWithoutTint(capi, pos.AddCopy(offsetInv));
        }

        public float MBOnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter, Vec3i offsetInv)
        {
            BlockSelection shiftedSelection = blockSel.Clone();
            shiftedSelection.Position.Add(offsetInv);

            return OnGettingBroken(player, shiftedSelection, itemslot, remainingResistance, dt, counter);
        }

        public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset)
        {
            // Not used for now.
            return false;
        }

    }
}