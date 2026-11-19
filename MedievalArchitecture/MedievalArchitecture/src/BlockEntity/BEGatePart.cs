using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace MedievalArchitecture
{
    /// <summary>
    /// Block entity of a gate dummy part (confession:gatepart).
    /// Only stores where the root (controller) block of its gate is, so gates can have any size.
    /// IMPORTANT (savegame compatibility): the tree attribute keys "rootDx", "rootDy", "rootDz" must not be renamed.
    /// </summary>
    public class BEGatePart : BlockEntity
    {
        /// <summary>
        /// Offset from this part to the root block (root = part position + offset).
        /// Same meaning as BlockMultiblock.OffsetInv. Null until the data is known
        /// (e.g. on the client for a moment after the block arrived).
        /// </summary>
        public Vec3i? OffsetToRoot { get; private set; }

        public BlockPos? RootPos => OffsetToRoot == null ? null : Pos.AddCopy(OffsetToRoot);

        /// <summary>
        /// Called by the gate when placing or converting its parts (server side).
        /// </summary>
        public void SetOffsetToRoot(Vec3i offsetToRoot)
        {
            OffsetToRoot = new Vec3i(offsetToRoot.X, offsetToRoot.Y, offsetToRoot.Z);
            MarkDirty();
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            if (tree.HasAttribute("rootDx") && tree.HasAttribute("rootDy") && tree.HasAttribute("rootDz"))
            {
                OffsetToRoot = new Vec3i(tree.GetInt("rootDx"), tree.GetInt("rootDy"), tree.GetInt("rootDz"));
            }
            else
            {
                OffsetToRoot = null;
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (OffsetToRoot != null)
            {
                tree.SetInt("rootDx", OffsetToRoot.X);
                tree.SetInt("rootDy", OffsetToRoot.Y);
                tree.SetInt("rootDz", OffsetToRoot.Z);
            }
        }
    }
}
