using System.Collections.Generic;
using AttributeRenderingLibrary;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBrazier : Block, IIgnitable
    {
        private WorldInteraction[]? interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Debug: Check if ParticleProperties loaded from JSON
            string variant = Variant.TryGetValue("burnstate", out string? bs) ? bs : "unknown";
            int particleCount = ParticleProperties?.Length ?? 0;
            api.Logger.Debug($"[Brazier] OnLoaded variant={variant}, ParticleProperties.Length={particleCount}");

            // Setup world interactions
            List<ItemStack> igniteStacks = BlockBehaviorCanIgnite.CanIgniteStacks(api, true);

            interactions = ObjectCacheUtil.GetOrCreate(api, "brazierInteractions", () =>
            {
                return new WorldInteraction[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "confession:blockhelp-brazier-ignite",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift",
                        Itemstacks = igniteStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, es) =>
                        {
                            var be = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityBrazier;
                            if (be != null && !be.FuelSlot.Empty && !be.IsBurning)
                                return wi.Itemstacks;
                            return null;
                        }
                    },
                    new WorldInteraction
                    {
                        ActionLangCode = "confession:blockhelp-brazier-refuel",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift"
                    },
                    new WorldInteraction
                    {
                        ActionLangCode = "confession:blockhelp-brazier-construct",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift"
                    }
                };
            });
        }

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            // Fire damage when lit (5% chance, 0.5 damage)
            if (world.Rand.NextDouble() < 0.05)
            {
                var be = GetBlockEntity<BlockEntityBrazier>(pos);
                if (be != null && be.IsBurning)
                {
                    entity.ReceiveDamage(new DamageSource
                    {
                        Source = EnumDamageSource.Block,
                        SourceBlock = this,
                        Type = EnumDamageType.Fire,
                        SourcePos = pos.ToVec3d()
                    }, 0.5f);
                }
            }

            base.OnEntityInside(world, entity, pos);
        }

        // IIgnitable - ignite items FROM the brazier
        EnumIgniteState IIgnitable.OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
        {
            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBrazier;
            if (be != null && be.IsBurning)
            {
                if (secondsIgniting > 2f)
                    return EnumIgniteState.IgniteNow;
                return EnumIgniteState.Ignitable;
            }
            return EnumIgniteState.NotIgnitable;
        }

        // IIgnitable - ignite the brazier itself
        public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
        {
            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBrazier;
            if (be == null)
                return EnumIgniteState.NotIgnitable;

            return be.GetIgnitableState(secondsIgniting);
        }

        public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
        {
            var be = api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityBrazier;
            if (be != null && !be.canIgniteFuel)
            {
                be.canIgniteFuel = true;
                be.extinguishedTotalHours = api.World.Calendar.TotalHours;
            }
            handling = EnumHandling.PreventDefault;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null)
                return false;

            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
                return false;

            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityBrazier;
            if (be == null)
                return base.OnBlockInteractStart(world, byPlayer, blockSel);

            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            ItemStack? heldStack = activeSlot?.Itemstack;

            // Get current state from ARL
            string? currentState = GetStateFromBlockEntity(be);
            int stage = GetStageFromState(currentState);

            // If complete (stage 4), handle fuel and ignition
            if (stage == 4)
            {
                // Check if trying to ignite
                if (heldStack?.Block != null &&
                    heldStack.Block.HasBehavior<BlockBehaviorCanIgnite>(false) &&
                    be.GetIgnitableState(0f) == EnumIgniteState.Ignitable)
                {
                    return false; // Let ignition behavior handle it
                }

                // Try to add fuel
                if (heldStack != null &&
                    byPlayer.Entity.Controls.ShiftKey &&
                    heldStack.Collectible.CombustibleProps != null &&
                    heldStack.Collectible.CombustibleProps.BurnTemperature > 0)
                {
                    var moveOp = new ItemStackMoveOperation(world, EnumMouseButton.Right, EnumModifierKey.SHIFT, EnumMergePriority.DirectMerge, 1);
                    activeSlot?.TryPutInto(be.FuelSlot, ref moveOp);

                    if (moveOp.MovedQuantity > 0)
                    {
                        if (byPlayer is IClientPlayer clientPlayer)
                        {
                            clientPlayer.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                        }

                        // Play place sound
                        var placeSound = heldStack.ItemAttributes?["placeSound"];
                        if (placeSound != null && placeSound.Exists)
                        {
                            var soundLoc = AssetLocation.Create(placeSound.AsString(), heldStack.Collectible.Code.Domain);
                            api.World.PlaySoundAt(
                                soundLoc.WithPathPrefixOnce("sounds/"),
                                blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z,
                                byPlayer,
                                0.88f + (float)api.World.Rand.NextDouble() * 0.24f,
                                16f, 1f);
                        }

                        return true;
                    }
                }

                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            // Construction phase - add materials
            if (heldStack != null && TryConstruct(world, blockSel.Position, heldStack.Collectible, byPlayer, be, currentState))
            {
                if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    activeSlot?.TakeOut(1);
                }
                return true;
            }

            return false;
        }

        private bool TryConstruct(IWorldAccessor world, BlockPos pos, CollectibleObject obj, IPlayer player, BlockEntityBrazier be, string? currentState)
        {
            // Check if item is constructable
            if (obj.Attributes == null || !obj.Attributes.IsTrue("firepitConstructable"))
                return false;

            int stage = GetStageFromState(currentState);
            if (stage >= 4)
                return false;

            // Determine next state
            string nextState = currentState switch
            {
                //"construct0" => "construct1",
                "construct1" => "construct2",
                "construct2" => "construct3",
                "construct3" => "cold",
                _ => "cold"
            };

            // Set new state via ARL
            be.SetVariant("state", nextState);

            // Play sound
            if (Sounds?.Place != null)
            {
                //world.PlaySoundAt(Sounds.Place, pos.X, pos.Y, pos.Z, player, true, 32f, 1f);
            }

            // On final construction step, give some initial fuel
            if (nextState == "cold")
            {
                be.FuelSlot.Itemstack = new ItemStack(obj, 3);
                be.FuelSlot.MarkDirty();
            }

            // Trigger animation
            if (player is IClientPlayer clientPlayer)
            {
                clientPlayer.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            }

            return true;
        }

        private string? GetStateFromBlockEntity(BlockEntityBrazier be)
        {
            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh?.Variants != null)
            {
                // Read the state value directly from the Variants
                string? stateValue = beh.Variants.Get("state");
                if (!string.IsNullOrEmpty(stateValue))
                {
                    return stateValue;
                }
            }
            return "construct1"; // Default for new placement
        }

        private int GetStageFromState(string? state)
        {
            return state switch
            {
                //"construct0" => 0,
                "construct1" => 1,
                "construct2" => 2,
                "construct3" => 3,
                _ => 4
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            if (interactions == null)
                return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

            return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }
    }
}