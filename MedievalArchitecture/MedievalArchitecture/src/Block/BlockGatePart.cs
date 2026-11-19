using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    /// <summary>
    /// Invisible dummy part of a gate (confession:gatepart).
    ///
    /// Works like the vanilla dummy BlockMultiblock (game:multiblock-monolithic-*): everything is forwarded
    /// to the root (controller) block through the same IMultiBlock* interfaces, so the gate code treats
    /// old vanilla dummies and gateparts the same way.
    ///
    /// Differences to the vanilla dummy:
    /// - The offset to the root is stored in a block entity (BEGatePart) instead of the block code,
    ///   so there is no size limit.
    /// - Explosions, resistance and block material are forwarded to the root as well.
    ///
    /// As long as the offset is unknown (client: block arrived before its block entity data) or the root is missing,
    /// the part behaves neutral: default selection box, no collision, no interaction.
    /// Orphaned parts (no gate anymore) remove themselves on the server: when loaded and when interacted with.
    ///
    /// IMPORTANT (savegame compatibility): registered as block class "BlockGatePart". Do not rename.
    /// </summary>
    public class BlockGatePart : Block, IMultiblockOffset
    {
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Same as the gate: while sneaking, ask this part before the held item (plumb and square / padlock redirect)
            PlacedPriorityInteract = true;
        }

        #region Root lookup

        /// <summary>
        /// Finds the root block of this part. Returns false if the offset is not known yet or no valid root exists.
        /// </summary>
        private bool TryGetRoot(IBlockAccessor blockAccessor, BlockPos pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock)
        {
            rootPos = null!;
            offsetInv = null!;
            rootBlock = null!;

            BEGatePart? be = blockAccessor.GetBlockEntity<BEGatePart>(pos);
            if (be?.OffsetToRoot == null) return false;

            offsetInv = be.OffsetToRoot;
            rootPos = pos.AddCopy(offsetInv);
            rootBlock = blockAccessor.GetBlock(rootPos);

            // Only gates use gateparts. Anything else at the root position (air, another block, a dummy) means: no valid root.
            // This also prevents endless recursion if blocks are arranged incorrectly (e.g. broken world).
            if (rootBlock is not BlockGateBase) return false;

            return true;
        }

        /// <summary>
        /// True if this part has no gate anymore. Null if this cannot be decided right now
        /// (root chunk not loaded, root block entity not available) - "not loaded" does not mean "not there".
        /// Only meaningful on the server.
        /// </summary>
        public bool? IsOrphan(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BEGatePart? be = blockAccessor.GetBlockEntity<BEGatePart>(pos);
            if (be == null) return null;

            // A server-side part without offset can never find its gate
            if (be.OffsetToRoot == null) return true;

            BlockPos rootPos = pos.AddCopy(be.OffsetToRoot);
            if (blockAccessor.GetChunkAtBlockPos(rootPos) == null) return null;

            if (blockAccessor.GetBlock(rootPos) is not BlockGateBase gate) return true;

            bool? contains = gate.ContainsPart(blockAccessor, rootPos, pos);
            if (contains == null) return null;

            return !contains.Value;
        }

        /// <summary>
        /// Removes this part if it certainly has no gate anymore (e.g. root removed with WorldEdit). Server only.
        /// </summary>
        public void TryRemoveIfOrphan(IWorldAccessor world, BlockPos pos)
        {
            if (world.Side != EnumAppSide.Server) return;
            if (world.BlockAccessor.GetBlock(pos) != this) return;

            if (IsOrphan(world.BlockAccessor, pos) == true)
            {
                world.Logger.Debug("[MedievalArchitecture] Removed orphaned gate part at {0}", pos);
                world.BlockAccessor.SetBlock(0, pos);
            }
        }

        private T? GetRootInterface<T>(Block rootBlock, BlockPos rootPos) where T : class
        {
            return rootBlock as T ?? rootBlock.GetInterface<T>(api.World, rootPos);
        }

        public BlockPos GetControlBlockPos(BlockPos pos)
        {
            BlockPos? rootPos = api?.World?.BlockAccessor.GetBlockEntity<BEGatePart>(pos)?.RootPos;
            return rootPos ?? pos.Copy();
        }

        #endregion

        #region Selection and collision

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            if (!TryGetRoot(blockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return new[] { Cuboidf.Default() };
            }

            var inf = GetRootInterface<IMultiBlockColSelBoxes>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetSelectionBoxes(blockAccessor, pos, offsetInv);

            return new[] { Cuboidf.Default() };
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            if (!TryGetRoot(blockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return Array.Empty<Cuboidf>();
            }

            var inf = GetRootInterface<IMultiBlockColSelBoxes>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetCollisionBoxes(blockAccessor, pos, offsetInv);

            return new[] { Cuboidf.Default() };
        }

        public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetCollisionBoxes(blockAccessor, pos);
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            if (!TryGetRoot(world.BlockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock)) return false;

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            return inf?.MBDoPartialSelection(world, pos, offsetInv) ?? false;
        }

        #endregion

        #region Interaction

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!TryGetRoot(world.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                TryRemoveIfOrphan(world, blockSel.Position);
                return false;
            }

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBOnBlockInteractStart(world, byPlayer, blockSel, offsetInv);

            return rootBlock.OnBlockInteractStart(world, byPlayer, Shifted(blockSel, offsetInv));
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!TryGetRoot(world.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock)) return false;

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBOnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, offsetInv);

            return rootBlock.OnBlockInteractStep(secondsUsed, world, byPlayer, Shifted(blockSel, offsetInv));
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!TryGetRoot(world.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock)) return;

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null)
            {
                inf.MBOnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, offsetInv);
                return;
            }

            rootBlock.OnBlockInteractStop(secondsUsed, world, byPlayer, Shifted(blockSel, offsetInv));
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            if (!TryGetRoot(world.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock)) return true;

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBOnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, cancelReason, offsetInv);

            return rootBlock.OnBlockInteractCancel(secondsUsed, world, byPlayer, Shifted(blockSel, offsetInv), cancelReason);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection blockSel, IPlayer forPlayer)
        {
            if (!TryGetRoot(world.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return Array.Empty<WorldInteraction>();
            }

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetPlacedBlockInteractionHelp(world, blockSel, forPlayer, offsetInv);

            return rootBlock.GetPlacedBlockInteractionHelp(world, Shifted(blockSel, offsetInv), forPlayer);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if (!TryGetRoot(world.BlockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return base.OnPickBlock(world, pos);
            }

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBOnPickBlock(world, pos, offsetInv);

            return rootBlock.OnPickBlock(world, rootPos);
        }

        public override BlockSounds GetSounds(IBlockAccessor blockAccessor, BlockSelection blockSel, ItemStack? stack = null)
        {
            if (!TryGetRoot(blockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return base.GetSounds(blockAccessor, blockSel, stack);
            }

            var inf = GetRootInterface<IMultiBlockInteract>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetSounds(blockAccessor, blockSel, stack, offsetInv);

            return rootBlock.GetSounds(blockAccessor, Shifted(blockSel, offsetInv), stack);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            // Shows the root's info on every part (e.g. "Reinforced by ...")
            if (!TryGetRoot(world.BlockAccessor, pos, out BlockPos rootPos, out _, out Block rootBlock)) return "";

            return rootBlock.GetPlacedBlockInfo(world, rootPos, forPlayer);
        }

        #endregion

        #region Breaking and explosions

        public override float GetResistance(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // Every part should feel like the root block when mined
            if (!TryGetRoot(blockAccessor, pos, out BlockPos rootPos, out _, out Block rootBlock))
            {
                return base.GetResistance(blockAccessor, pos);
            }

            return rootBlock.GetResistance(blockAccessor, rootPos);
        }

        public override EnumBlockMaterial GetBlockMaterial(IBlockAccessor blockAccessor, BlockPos pos, ItemStack? stack = null)
        {
            if (pos == null || !TryGetRoot(blockAccessor, pos, out BlockPos rootPos, out _, out Block rootBlock))
            {
                return base.GetBlockMaterial(blockAccessor, pos, stack);
            }

            return rootBlock.GetBlockMaterial(blockAccessor, rootPos, stack);
        }

        public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            if (!TryGetRoot(api.World.BlockAccessor, blockSel.Position, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
            }

            // The root's behaviors (e.g. reinforcement) decide the mining speed
            var inf = GetRootInterface<IMultiBlockBlockBreaking>(rootBlock, rootPos);
            if (inf != null) return inf.MBOnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter, offsetInv);

            if (api is ICoreClientAPI capi)
            {
                capi.World.CloneBlockDamage(blockSel.Position, rootPos);
            }
            return rootBlock.OnGettingBroken(player, Shifted(blockSel, offsetInv), itemslot, remainingResistance, dt, counter);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (!TryGetRoot(world.BlockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                // No root: just remove this part (drops nothing)
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
                return;
            }

            // The root decides (reinforcement may prevent breaking; if it breaks, it removes all parts)
            var inf = rootBlock as IMultiBlockBlockBreaking ?? rootBlock.GetBehavior(typeof(IMultiBlockBlockBreaking), true) as IMultiBlockBlockBreaking;
            if (inf != null)
            {
                inf.MBOnBlockBroken(world, pos, offsetInv, byPlayer, dropQuantityMultiplier);
                return;
            }

            rootBlock.OnBlockBroken(world, rootPos, byPlayer, dropQuantityMultiplier);
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, string ignitedByPlayerUid)
        {
            if (!TryGetRoot(world.BlockAccessor, pos, out BlockPos rootPos, out _, out Block rootBlock))
            {
                base.OnBlockExploded(world, pos, explosionCenter, blastType, ignitedByPlayerUid);
                return;
            }

            // The vanilla dummy does not do this: an explosion on any part counts as an explosion on the root
            // (reinforcement of the root loses strength; if the root is destroyed, the gate removes all its parts).
            rootBlock.OnBlockExploded(world, rootPos, explosionCenter, blastType, ignitedByPlayerUid);
        }

        public override double ExplosionDropChance(IWorldAccessor world, BlockPos pos, EnumBlastType blastType)
        {
            return 0; // parts never drop anything
        }

        #endregion

        #region Colors (particles, break decals)

        public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1)
        {
            if (!TryGetRoot(capi.World.BlockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return base.GetRandomColor(capi, pos, facing, rndIndex);
            }

            var inf = GetRootInterface<IMultiBlockBlockBreaking>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetRandomColor(capi, pos, facing, rndIndex, offsetInv);

            return rootBlock.GetRandomColor(capi, rootPos, facing, rndIndex);
        }

        public override int GetColorWithoutTint(ICoreClientAPI capi, BlockPos pos)
        {
            if (!TryGetRoot(capi.World.BlockAccessor, pos, out BlockPos rootPos, out Vec3i offsetInv, out Block rootBlock))
            {
                return base.GetColorWithoutTint(capi, pos);
            }

            var inf = GetRootInterface<IMultiBlockBlockBreaking>(rootBlock, rootPos);
            if (inf != null) return inf.MBGetColorWithoutTint(capi, pos, offsetInv);

            return rootBlock.GetColorWithoutTint(capi, rootPos);
        }

        #endregion

        private static BlockSelection Shifted(BlockSelection blockSel, Vec3i offsetInv)
        {
            BlockSelection shifted = blockSel.Clone();
            shifted.Position.Add(offsetInv);
            return shifted;
        }
    }
}
