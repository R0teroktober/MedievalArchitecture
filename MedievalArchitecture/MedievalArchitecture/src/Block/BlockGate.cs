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
    public enum HorizontalRotation
    {
        North,
        East,
        South,
        West
    }

    public class BlockGateBase : BlockGeneric, IMultiBlockInteract, IMultiBlockColSelBoxes
    {
        private int width = 1;
        private int height = 1;
        private int length = 1;

        // Runtime lookup tables for the active block.
        // Key format: "x/y/z" in north-based local coordinates.
        private readonly Dictionary<string, Cuboidf[]> closedBoxesByOffset = new();
        private readonly Dictionary<string, Cuboidf[]> openBoxesByOffset = new();


        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Read the north-based multiblock dimensions from block attributes.
            width = Attributes?["width"].AsInt(1) ?? 1;
            height = Attributes?["height"].AsInt(1) ?? 1;
            length = Attributes?["length"].AsInt(1) ?? 1;

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
            HorizontalRotation rotation = DetermineRotation(byPlayer, blockSel);
            BlockPos rootPos = blockSel.Position;

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = 0; dz < length; dz++)
                    {
                        Vec3i off = GetPlacementOffset(dx, dy, dz, rotation);
                        BlockPos testPos = rootPos.AddCopy(off.X, off.Y, off.Z);
                        Block existingBlock = world.BlockAccessor.GetBlock(testPos);

                        if (existingBlock.Id != 0 && !existingBlock.IsReplacableBy(this))
                        {
                            failureCode = "notenoughspace";
                            return false;
                        }
                    }
                }
            }

            return true;
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

                        Block dummyBlock = GetDummyBlockForOffset(world, off);
                        if (dummyBlock == null)
                        {
                            world.Logger.Warning(
                                "MedievalArchitecture: Missing dummy block for offset {0}/{1}/{2} while placing {3} at {4}",
                                off.X, off.Y, off.Z, Code, rootPos
                            );
                            continue;
                        }

                        bulkAccessor.SetBlock(dummyBlock.Id, dummyPos);
                    }
                }
            }

            bulkAccessor.Commit();

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
            // Direct controller break.
            BreakWholeMultiblock(world, pos, byPlayer, dropQuantityMultiplier);
        }

        #region BlockInteract

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {

            ModSystemBlockReinforcement bre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            if (bre.IsLockedForInteract(blockSel.Position, byPlayer))
            {
                if (world.Side == EnumAppSide.Client)
                {
                    (world.Api as ICoreClientAPI).TriggerIngameError(this, "locked", Lang.Get("ingameerror-locked"));
                }
                return false;
            } else

            return true;
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
            return base.GetPlacedBlockInteractionHelp(world, blockSel, forPlayer);
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
            // Dummy break routed by BlockMultiblock.
            BlockPos controllerPos = pos.AddCopy(offset);
            BreakWholeMultiblock(world, controllerPos, byPlayer, dropQuantityMultiplier);
        }

        private void BreakWholeMultiblock(IWorldAccessor world, BlockPos controllerPos, IPlayer byPlayer, float dropQuantityMultiplier)
        {
            HorizontalRotation rotation = GetStoredRotation(world, controllerPos);

            // Remove all dummy blocks first.
            // The controller is broken last through the normal block break path,
            // so drops and base break behavior happen only once.
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
                        BlockPos partPos = controllerPos.AddCopy(off.X, off.Y, off.Z);
                        Block partBlock = world.BlockAccessor.GetBlock(partPos);

                        // Only remove expected multiblock dummy blocks.
                        if (partBlock?.Code?.Domain == "game" && partBlock.Code.Path.StartsWith("multiblock-monolithic"))
                        {
                            world.BlockAccessor.SetBlock(0, partPos);
                        }
                    }
                }
            }

            // Break the controller through the normal path exactly once.
            base.OnBlockBroken(world, controllerPos, byPlayer, dropQuantityMultiplier);
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
      
    }
}