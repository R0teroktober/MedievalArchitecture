//using System;
//using System.Text;
//using Vintagestory.API;
//using Vintagestory.API.Client;
//using Vintagestory.API.Common;
//using Vintagestory.API.Config;
//using Vintagestory.API.Datastructures;
//using Vintagestory.API.MathTools;
//using Vintagestory.GameContent;

//public class BlockBehaviorGateOLD : StrongBlockBehavior, IMultiBlockColSelBoxes, IMultiBlockBlockProperties
//{
//    private ICoreAPI api;



//    public int width;
//    public int height;
//    public int length;

//    public bool handopenable;
//    public bool airtight;

//    public AssetLocation OpenSound;
//    public AssetLocation CloseSound;


//    public BlockBehaviorGate(Block block) : base(block)
//    {
//        width = block.Attributes["width"].AsInt(1);
//        height = block.Attributes["height"].AsInt(1);
//        length = block.Attributes["length"].AsInt(1);

//        handopenable = block.Attributes["handopenable"].AsBool(true);
//        airtight = block.Attributes["airtight"].AsBool(true);
//    }

//    public override void OnLoaded(ICoreAPI api)
//    {
//        this.api = api;
//        OpenSound = LoadSound("openSound");
//        CloseSound = LoadSound("closeSound");
//        base.OnLoaded(api);
//    }

//    private AssetLocation LoadSound(string key)
//    {
//        JsonObject attr = block.Attributes[key];
//        return AssetLocation.Create(attr.Exists ? attr.AsString("sounds/block/door") : "sounds/block/door");
//    }

//    private BEBehaviorGateold GetBE(IBlockAccessor blockAccessor, BlockPos pos)
//    {
//        return blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorGateold>();
//    }

//    private BEBehaviorGateold GetRootBE(IBlockAccessor blockAccessor, BlockPos childPos, Vec3i offsetInv)
//    {
//        return GetBE(blockAccessor, childPos.AddCopy(offsetInv.X, offsetInv.Y, offsetInv.Z));
//    }

//    public override bool CanPlaceBlock(
//        IWorldAccessor world,
//        IPlayer byPlayer,
//        BlockSelection blockSel,
//        ref EnumHandling handling,
//        ref string failureCode)
//    {
//        if (blockSel == null || blockSel.Face != BlockFacing.UP)
//        {
//            handling = EnumHandling.PreventDefault;
//            failureCode = "requirefloor";
//            return false;
//        }

//        int rotDeg = GetPlacementRotDeg(byPlayer, blockSel);
//        IBlockAccessor ba = world.BlockAccessor;
//        BlockPos rootPos = blockSel.Position;

//        for (int dx = 0; dx < width; dx++)
//        {
//            for (int dy = 0; dy < height; dy++)
//            {
//                for (int dz = 0; dz < length; dz++)
//                {
//                    Vec3i off = RotateOffset(dx, dy, dz, rotDeg);
//                    BlockPos pos = rootPos.AddCopy(off.X, off.Y, off.Z);

//                    Block existing = ba.GetBlock(pos, BlockLayersAccess.Solid);
//                    if (existing.Id != 0 && !existing.IsReplacableBy(block))
//                    {
//                        handling = EnumHandling.PreventDefault;
//                        failureCode = "notenoughspace";
//                        return false;
//                    }

//                    // Nur unterste Ebene braucht Support
//                    if (dy == 0)
//                    {
//                        BlockPos belowPos = pos.DownCopy();
//                        Block below = ba.GetBlock(belowPos, BlockLayersAccess.Solid);

//                        if (below == null || !below.CanAttachBlockAt(ba, block, belowPos, BlockFacing.UP))
//                        {
//                            handling = EnumHandling.PreventDefault;
//                            failureCode = "requirefloor";
//                            return false;
//                        }
//                    }
//                }
//            }
//        }

//        return true;
//    }

//    public override bool DoPlaceBlock(
//     IWorldAccessor world,
//     IPlayer byPlayer,
//     BlockSelection blockSel,
//     ItemStack byItemStack,
//     ref EnumHandling handling)
//    {
//        if (blockSel == null || blockSel.Face != BlockFacing.UP)
//        {
//            handling = EnumHandling.PreventDefault;
//            return false;
//        }

//        int rotDeg = GetPlacementRotDeg(byPlayer, blockSel);
//        BlockPos rootPos = blockSel.Position.Copy();
//        IBlockAccessor ba = world.BlockAccessor;

//        ba.SetBlock(block.BlockId, rootPos, byItemStack);

//        var be = GetBE(ba, rootPos);
//        string type = byItemStack?.Attributes?.GetString(
//            "type",
//            block.Attributes?["defaultType"]?.AsString("oak") ?? "oak"
//        ) ?? "oak";

//        be?.ApplyPlacementData(rotDeg, type);

//        if (world.Side == EnumAppSide.Server)
//        {
//            PlaceMultiblockParts(world, rootPos, rotDeg);
//        }

//        handling = EnumHandling.PreventDefault;
//        return true;
//    }

//    private int GetPlacementRotDeg(IPlayer byPlayer, BlockSelection blockSel)
//    {
//        BlockFacing[] horVer = Block.SuggestedHVOrientation(byPlayer, blockSel);
//        int[] cardinalDeg = [90, 180, 270, 0];
//        int rotDeg = cardinalDeg[horVer[0].HorizontalAngleIndex];

//        return rotDeg;
//    }

//    private void PlaceMultiblockParts(IWorldAccessor world, BlockPos rootPos, int rotDeg)
//    {
//        IterateOverEach(rootPos, rotDeg, pos =>
//        {
//            if (pos.Equals(rootPos)) return true;

//            int dx = pos.X - rootPos.X;
//            int dy = pos.Y - rootPos.Y;
//            int dz = pos.Z - rootPos.Z;

//            Block filler = GetMultiblockHelper(world, dx, dy, dz);
//            if (filler == null || filler.Id == 0)
//            {
//                api.Logger.Warning($"[Gate] Missing multiblock helper block for dx={dx}, dy={dy}, dz={dz}, block={block.Code}");
//                return false;
//            }

//            world.BlockAccessor.SetBlock(filler.Id, pos);
//            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
//            return true;
//        });
//    }

//    private Block GetMultiblockHelper(IWorldAccessor world, int dx, int dy, int dz)
//    {
//        string path = "multiblock-monolithic" + OffsetToString(dx) + OffsetToString(dy) + OffsetToString(dz);

//        // Vanilla-nah: erst eigene Domain, dann game:
//        Block helper = world.GetBlock(new AssetLocation(block.Code.Domain, path));
//        if (helper != null && helper.Id != 0) return helper;

//        helper = world.GetBlock(new AssetLocation("game", path));
//        return helper;
//    }

//    private static string OffsetToString(int value)
//    {
//        if (value == 0) return "-0";
//        if (value < 0) return "-n" + (-value);
//        return "-p" + value;
//    }

//    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
//    {
//        handling = EnumHandling.PreventDefault;
//        return CreateTypedDrop(world, pos);
//    }

//    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
//    {
//        if (world.Side == EnumAppSide.Server)
//        {
//            int rotDeg = GetBE(world.BlockAccessor, pos)?.RotDeg ?? 0;

//            IterateOverEach(pos, rotDeg, partPos =>
//            {
//                if (partPos.Equals(pos)) return true;

//                if (world.BlockAccessor.GetBlock(partPos) is BlockMultiblock)
//                {
//                    world.BlockAccessor.SetBlock(0, partPos);
//                    world.BlockAccessor.TriggerNeighbourBlockUpdate(partPos);
//                }

//                return true;
//            });
//        }

//        base.OnBlockRemoved(world, pos, ref handling);
//    }

//    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropChanceMultiplier, ref EnumHandling handling)
//    {
//        handling = EnumHandling.PreventDefault;
//        return new ItemStack[] { CreateTypedDrop(world, pos) };
//    }

//    private ItemStack CreateTypedDrop(IWorldAccessor world, BlockPos pos)
//    {
//        var stack = new ItemStack(block);

//        stack.Attributes ??= new TreeAttribute();

//        string type = GetBE(world.BlockAccessor, pos)?.Type
//            ?? block.Attributes?["defaultType"]?.AsString("oak")
//            ?? "oak";

//        stack.Attributes.SetString("type", type);
//        return stack;
//    }

//    public void IterateOverEach(BlockPos rootPos, int rotDeg, ActionConsumable<BlockPos> onBlock)
//    {
//        BlockPos tmp = new BlockPos(rootPos.dimension);
//        int rot = rotDeg;

//        for (int dx = 0; dx < width; dx++)
//        {
//            for (int dy = 0; dy < height; dy++)
//            {
//                for (int dz = 0; dz < length; dz++)
//                {
//                    Vec3i off = RotateOffset(dx, dy, dz, rot);
//                    tmp.Set(rootPos.X + off.X, rootPos.Y + off.Y, rootPos.Z + off.Z);

//                    if (!onBlock(tmp)) return;
//                }
//            }
//        }
//    }

//    public static Vec3i RotateOffset(int dx, int dy, int dz, int rotDeg)
//    {
//        switch (rotDeg)
//        {
//            case 90: return new Vec3i(dz, dy, -dx);
//            case 180: return new Vec3i(-dx, dy, -dz);
//            case 270: return new Vec3i(-dz, dy, dx);
//            default: return new Vec3i(dx, dy, dz);
//        }
//    }

//    public static Vec3i InverseRotateOffset(int wx, int wy, int wz, int rotDeg)
//    {
//        switch (rotDeg)
//        {
//            case 90: return new Vec3i(-wz, wy, wx);
//            case 180: return new Vec3i(-wx, wy, -wz);
//            case 270: return new Vec3i(wz, wy, -wx);
//            default: return new Vec3i(wx, wy, wz);
//        }
//    }

//    public override bool OnBlockInteractStart(
//        IWorldAccessor world,
//        IPlayer byPlayer,
//        BlockSelection blockSel,
//        ref EnumHandling handling)
//    {
//        return GetBE(world.BlockAccessor, blockSel.Position)?.TryToggle(byPlayer, ref handling) == true;
//    }

//    public override void Activate(
//        IWorldAccessor world,
//        Caller caller,
//        BlockSelection blockSel,
//        ITreeAttribute activationArgs,
//        ref EnumHandling handled)
//    {
//        var be = GetBE(world.BlockAccessor, blockSel.Position);
//        if (be == null) return;

//        bool opened = activationArgs?.GetBool("opened", !be.Opened) ?? !be.Opened;
//        if (be.Opened != opened)
//        {
//            be.ToggleGateState(null, opened);
//        }

//        handled = EnumHandling.PreventDefault;
//    }

//    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
//    {
//        handled = EnumHandling.PreventSubsequent;
//        return GetBE(blockAccessor, pos)?.GetBoxesForPart(0, 0, 0);
//    }

//    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
//    {
//        handled = EnumHandling.PreventSubsequent;
//        return GetBE(blockAccessor, pos)?.GetBoxesForPart(0, 0, 0);
//    }

//    public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
//    {
//        handled = EnumHandling.PreventSubsequent;
//        return GetBE(blockAccessor, pos)?.GetBoxesForPart(0, 0, 0);
//    }

//    public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
//    {
//        var be = GetRootBE(blockAccessor, pos, offset);
//        if (be == null) return null;

//        // offset = child -> root
//        // für lokalen Part brauchen wir root -> child
//        Vec3i worldOffset = new Vec3i(-offset.X, -offset.Y, -offset.Z);
//        Vec3i localOffset = InverseRotateOffset(worldOffset.X, worldOffset.Y, worldOffset.Z, be.RotDeg);

//        return be.GetBoxesForPart(localOffset.X, localOffset.Y, localOffset.Z);
//    }

//    public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
//    {
//        return MBGetCollisionBoxes(blockAccessor, pos, offset);
//    }

//    public override float GetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, ref EnumHandling handled)
//    {
//        handled = EnumHandling.PreventDefault;
//        return GetLiquidBarrierHeight(GetBE(api.World.BlockAccessor, pos), face);
//    }

//    public float MBGetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, Vec3i offset)
//    {
//        return GetLiquidBarrierHeight(GetRootBE(api.World.BlockAccessor, pos, offset), face);
//    }

//    private float GetLiquidBarrierHeight(BEBehaviorGateold be, BlockFacing face)
//    {
//        if (be == null || !airtight || !be.IsSideSolid(face)) return 0f;
//        return 1f;
//    }

//    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, ref EnumHandling handled)
//    {
//        handled = EnumHandling.PreventDefault;
//        return GetRetention(GetBE(api.World.BlockAccessor, pos), pos, facing, type);
//    }

//    public int MBGetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, Vec3i offset)
//    {
//        BlockPos rootPos = pos.AddCopy(offset.X, offset.Y, offset.Z);
//        return GetRetention(GetBE(api.World.BlockAccessor, rootPos), rootPos, facing, type);
//    }

//    private int GetRetention(BEBehaviorGateold be, BlockPos pos, BlockFacing facing, EnumRetentionType type)
//    {
//        if (be == null) return 0;

//        if (type == EnumRetentionType.Sound)
//        {
//            return be.IsSideSolid(facing) ? 3 : 0;
//        }

//        if (!airtight) return 0;

//        if (api.World.Config.GetBool("openDoorsNotSolid", false))
//        {
//            return be.IsSideSolid(facing) ? GetInsulation(pos) : 0;
//        }

//        return (be.IsSideSolid(facing) || be.IsSideSolid(facing.Opposite)) ? GetInsulation(pos) : 3;
//    }

//    private int GetInsulation(BlockPos pos)
//    {
//        EnumBlockMaterial mat = block.GetBlockMaterial(api.World.BlockAccessor, pos);

//        if (mat == EnumBlockMaterial.Ore ||
//            mat == EnumBlockMaterial.Stone ||
//            mat == EnumBlockMaterial.Soil ||
//            mat == EnumBlockMaterial.Ceramic)
//        {
//            return -1;
//        }

//        return 1;
//    }

//    public bool MBCanAttachBlockAt(
//        IBlockAccessor blockAccessor,
//        Block block,
//        BlockPos pos,
//        BlockFacing blockFace,
//        Cuboidi attachmentArea,
//        Vec3i offsetInv)
//    {
//        return false;
//    }

//    public JsonObject MBGetAttributes(IBlockAccessor blockAccessor, BlockPos pos)
//    {
//        return null;
//    }

//    public override void GetHeldItemName(StringBuilder sb, ItemStack itemStack)
//    {
//        if (block.Variant.ContainsKey("wood"))
//        {
//            string doorname = sb.ToString();
//            sb.Clear();
//            sb.Append(Lang.Get("doorname-with-material", doorname, Lang.Get("material-" + block.Variant["wood"])));
//        }
//    }
//}
