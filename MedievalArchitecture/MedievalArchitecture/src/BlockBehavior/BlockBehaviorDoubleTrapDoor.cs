using System.Text;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace MedievalArchitecture
{
    public class BlockBehaviorDoubleTrapDoor : BlockBehaviorTrapDoor
    {
        private ICoreAPI sapi;
        public int height;
        public int width;

        public BlockBehaviorDoubleTrapDoor(Block block) : base(block)
        {
            width = block.Attributes["width"].AsInt(1);
            height = block.Attributes["height"].AsInt(1);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            sapi = api;
        }

        private BEBehaviorDoubleTrapDoor GetBE(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return blockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorDoubleTrapDoor>();
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer,ItemStack itemstack,BlockSelection blockSel,ref EnumHandling handling,ref string failureCode)
        {
            handling = EnumHandling.PreventDefault;

            BlockPos pos = blockSel.Position;
            IBlockAccessor ba = world.BlockAccessor;

            if (ba.GetBlock(pos, BlockLayersAccess.Solid).Id == 0 && block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                return PlaceDoorCustom(world, byPlayer, itemstack, blockSel, pos, ba);
            }

            return false;
        }

        private bool PlaceDoorCustom(IWorldAccessor world,IPlayer byPlayer,ItemStack itemstack,BlockSelection blockSel,BlockPos pos,IBlockAccessor ba)
        {
            ba.SetBlock(block.BlockId, pos);

            var beh = GetBE(ba, pos);
            beh?.OnBlockPlaced(itemstack, byPlayer, blockSel);

            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            var beh = GetBE(world.BlockAccessor, blockSel.Position);
            if (beh == null) return false;

            if (!handopenable && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                if (sapi is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "nothandopenable", Lang.Get("This door cannot be opened by hand."));
                }

                handling = EnumHandling.PreventDefault;
                return true;
            }

            beh.ToggleDoorStateCustom(byPlayer, !beh.Opened);
            handling = EnumHandling.PreventDefault;
            return true;
        }

        public override void Activate(IWorldAccessor world, Caller caller, BlockSelection blockSel, ITreeAttribute activationArgs, ref EnumHandling handled)
        {
            var beh = GetBE(world.BlockAccessor, blockSel.Position);
            if (beh == null) return;

            bool opened = !beh.Opened;
            if (activationArgs != null)
            {
                opened = activationArgs.GetBool("opened", opened);
            }

            if (beh.Opened != opened)
            {
                beh.ToggleDoorStateCustom(null, opened);
            }

            handled = EnumHandling.PreventDefault;
        }

        public override void OnDecalTesselation(IWorldAccessor world, MeshData decalMesh, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            var beh = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorDoubleTrapDoor>();
            if (beh != null)
            {
                decalMesh.Rotate(Vec3f.Half, 0, beh.RotRad, 0);
            }
        }

        public override void GetDecal(IWorldAccessor world, BlockPos pos,ITexPositionSource decalTexSource,ref MeshData decalModelData,ref MeshData blockModelData, ref EnumHandling handled)
        {
            var beh = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorDoubleTrapDoor>();

            if (beh != null && beh.Opened)
            {
                decalModelData = decalModelData.Rotate(Vec3f.Half, 90 * GameMath.DEG2RAD, 0, 0);
                decalModelData = decalModelData.Scale(Vec3f.Half, 1, -1f, 1);
            }

            base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData, ref handled);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventSubsequent;
            return GetBE(blockAccessor, pos)?.ColSelBoxes;
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventSubsequent;
            return GetBE(blockAccessor, pos)?.ColSelBoxes;
        }

        public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventSubsequent;
            return GetBE(blockAccessor, pos)?.ColSelBoxes;
        }

        public override float GetLiquidBarrierHeightOnSide(BlockFacing face, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            var beh = GetBE(sapi.World.BlockAccessor, pos);
            if (beh == null) return 0f;
            if (!beh.IsSideSolid(face)) return 0f;
            if (!airtight) return 0f;

            return 1f;
        }

        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            var beh = GetBE(sapi.World.BlockAccessor, pos);
            if (beh == null) return 0;

            if (type == EnumRetentionType.Sound) return beh.IsSideSolid(facing) ? 3 : 0;
            if (!airtight) return 0;

            if (sapi.World.Config.GetBool("openDoorsNotSolid", false))
            {
                return beh.IsSideSolid(facing) ? GetInsulation(pos) : 0;
            }

            return (beh.IsSideSolid(facing) || beh.IsSideSolid(facing.Opposite)) ? GetInsulation(pos) : 3;
        }

        private int GetInsulation(BlockPos pos)
        {
            var mat = block.GetBlockMaterial(sapi.World.BlockAccessor, pos);

            if (mat == EnumBlockMaterial.Ore || mat == EnumBlockMaterial.Stone || mat == EnumBlockMaterial.Soil || mat == EnumBlockMaterial.Ceramic)
            {
                return -1;
            }

            return 1;
        }

        public override void GetHeldItemName(StringBuilder sb, ItemStack itemStack)
        {
            if (block.Variant.ContainsKey("wood"))
            {
                string doorname = sb.ToString();
                sb.Clear();
                sb.Append(Lang.Get("doorname-with-material", doorname, Lang.Get("material-" + block.Variant["wood"])));
            }
        }
    }
}