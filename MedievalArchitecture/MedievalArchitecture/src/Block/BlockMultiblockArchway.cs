using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{

    public class BlockMultiblockArchway : BlockGeneric, IMultiBlockColSelBoxes
    {
        public ValuesByMultiblockOffset ValuesByMultiblockOffset { get; set; } = new();

        ICoreClientAPI capi;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            ValuesByMultiblockOffset = ValuesByMultiblockOffset.FromAttributes(this);
        }

        Cuboidf[] IMultiBlockColSelBoxes.MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            if (ValuesByMultiblockOffset.CollisionBoxesByOffset.TryGetValue(offset, out Cuboidf[] collisionBoxes))
            {
                return collisionBoxes;
            }
            Block originaBlock = blockAccessor.GetBlock(pos.AddCopy(offset.X, offset.Y, offset.Z));
            return originaBlock.GetCollisionBoxes(blockAccessor, pos);
        }

        Cuboidf[] IMultiBlockColSelBoxes.MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            if (ValuesByMultiblockOffset.SelectionBoxesByOffset.TryGetValue(offset, out Cuboidf[] selectionBoxes))
            {
                return selectionBoxes;
            }
            Block originaBlock = blockAccessor.GetBlock(pos.AddCopy(offset.X, offset.Y, offset.Z));
            return originaBlock.GetSelectionBoxes(blockAccessor, pos);
        }

        public override float GetAmbientSoundStrength(IWorldAccessor world, BlockPos pos)
        {
            var conds = capi.World.Player.Entity.selfClimateCond;
            if (conds != null && conds.Rainfall > 0.1f && conds.Temperature > 3f && (world.BlockAccessor.GetRainMapHeightAt(pos) <= pos.Y || world.BlockAccessor.GetDistanceToRainFall(pos, 3, 1) <= 2))
            {
                return conds.Rainfall;
            }

            return 0;
        }
    }
}