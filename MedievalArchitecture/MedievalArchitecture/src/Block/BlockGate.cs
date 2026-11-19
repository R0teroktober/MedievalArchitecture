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

    public class BlockGateBase : BlockGeneric, IMultiBlockInteract, IMultiBlockColSelBoxes, IMultiBlockBlockBreaking
    {
        private int width = 1;
        private int height = 1;
        private int length = 1;

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

        private static bool IsStagedForRemoval(IBulkBlockAccessor accessor, BlockPos pos)
        {
            return accessor.StagedBlocks.ContainsKey(pos) && accessor.GetStagedBlockId(pos) == 0;
        }

        /// <summary>
        /// True if partPos lies inside the footprint of the gate whose root is at rootPos.
        /// Null if this cannot be decided right now (e.g. the root's block entity is not available).
        /// </summary>
        public bool? ContainsPart(IBlockAccessor blockAccessor, BlockPos rootPos, BlockPos partPos)
        {
            BEBehaviorGate? gateBehavior = blockAccessor.GetBlockEntity(rootPos)?.GetBehavior<BEBehaviorGate>();
            if (gateBehavior == null) return null;

            Vec3i offsetToRoot = new Vec3i(rootPos.X - partPos.X, rootPos.Y - partPos.Y, rootPos.Z - partPos.Z);
            Vec3i local = WorldOffsetToNorthLocal(offsetToRoot, gateBehavior.Rotation);

            return local.X >= 0 && local.X < width
                && local.Y >= 0 && local.Y < height
                && local.Z >= 0 && local.Z < length;
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

            if (controllerBlock == null || controllerBlock.Id == 0)
            {
                return;
            }

            controllerBlock.OnBlockBroken(world, controllerPos, byPlayer, dropQuantityMultiplier);
        }

        /// <summary>
        /// Removes all dummy parts (vanilla dummies and gateparts) of the gate at controllerPos.
        /// If an accessor is given (explosion), the removal is staged there and committed by the caller.
        /// </summary>
        private void RemoveDummyBlocks(IWorldAccessor world, BlockPos controllerPos, HorizontalRotation rotation, IBulkBlockAccessor? stagingAccessor = null)
        {
            IBulkBlockAccessor bulkAccessor = stagingAccessor ?? world.GetBlockAccessorBulkUpdate(world.Side == EnumAppSide.Server, false);

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = 0; dz < length; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;

                        Vec3i off = GetPlacementOffset(dx, dy, dz, rotation);
                        BlockPos partPos = controllerPos.AddCopy(off.X, off.Y, off.Z);
                        Block partBlock = world.BlockAccessor.GetBlock(partPos);

                        if (partBlock?.Code?.Domain == "game" &&
                            partBlock.Code.Path.StartsWith("multiblock-monolithic"))
                        {
                            bulkAccessor.SetBlock(0, partPos);
                        }
                        else if (partBlock is BlockGatePart)
                        {
                            // Only remove parts that belong to this gate (a neighbouring gate may touch this one)
                            BlockPos? partRoot = world.BlockAccessor.GetBlockEntity<BEGatePart>(partPos)?.RootPos;
                            if (partRoot == null || partRoot.Equals(controllerPos))
                            {
                                bulkAccessor.SetBlock(0, partPos);
                            }
                        }
                    }
                }
            }

            if (stagingAccessor == null)
            {
                bulkAccessor.Commit();
            }
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