using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture;

public class BlockMultiblockArchway : BlockGeneric, IMultiBlockColSelBoxes
{
    public ValuesByMultiblockOffset ValuesByMultiblockOffset { get; set; } = new();

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
}