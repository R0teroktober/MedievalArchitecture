using AttributeRenderingLibrary;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    /// <summary>
    /// Brazier block entity. Burn logic is modelled after the vanilla firepit (BlockEntityFirepit).
    /// IMPORTANT (savegame compatibility): the tree attribute keys written in ToTreeAttributes must not be renamed.
    /// IMPORTANT (Real Smoke compatibility): the member names IsBurning, furnaceTemperature and CoolNow are
    /// referenced by name from patches/brazier-real-smoke-compatibility.json. Do not rename them.
    /// </summary>
    public class BlockEntityBrazier : BlockEntityConstructable, IHeatSource, ITemperatureSensitive
    {
        // Fuel storage - single slot, not full inventory.
        // Created once here (not in Initialize), because FromTreeAttributes runs BEFORE Initialize
        // when a chunk is loaded. Re-creating it in Initialize would delete the loaded fuel.
        private readonly ItemSlot _fuelSlot = new DummySlot();
        public ItemSlot FuelSlot => _fuelSlot;

        // Temperature system
        public float furnaceTemperature = 20f;
        public float prevFurnaceTemperature = 20f;
        public int maxTemperature;
        public float fuelBurnTime;
        public float maxFuelBurnTime;
        public float smokeLevel;

        // State tracking
        public bool canIgniteFuel;
        public double extinguishedTotalHours = -99.0;

        // Configuration

        // burn duration is multiplied by this value to get actual burn time in seconds - e.g. 24s * 75 = 30 minutes of burn time for a piece of firewood ( day has 48 mins)
        public virtual float BurnDurationModifier => MedievalArchitectureModSystem.Config.BrazierBurnDurationModifier;
        public virtual float HeatModifier => 1f;
        public float emptyFirepitBurnTimeMulBonus = 4f;

        // State properties
        public bool IsBurning => fuelBurnTime > 0f;
        public bool IsSmoldering => canIgniteFuel && !IsBurning;

        // ITemperatureSensitive: used by the vanilla "TemperatureSensitive" behavior (rain, water, watering can)
        // and by Real Smoke ("extinguishFunction": "CoolNow").
        public bool IsHot => IsBurning;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // All burn logic is server-authoritative, clients receive the state via FromTreeAttributes.
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnBurnTick, 100);
                RegisterGameTickListener(On500msTick, 500);
            }
        }

        private void OnBurnTick(float dt)
        {
            // Skip if still in construction phase
            string? currentState = GetCurrentState();
            if (currentState != null && currentState.StartsWith("construct"))
            {
                return;
            }

            // Burn fuel
            if (fuelBurnTime > 0f)
            {
                bool nearMaxTemp = Math.Abs(furnaceTemperature - (float)maxTemperature) < 50f;
                fuelBurnTime -= dt / (nearMaxTemp ? emptyFirepitBurnTimeMulBonus : 1f);

                if (fuelBurnTime <= 0f)
                {
                    fuelBurnTime = 0f;
                    maxFuelBurnTime = 0f;

                    if (!CanSmelt())
                    {
                        SetBrazierState("extinct");
                        extinguishedTotalHours = Api.World.Calendar.TotalHours;
                    }
                }
            }

            // Check for transition from extinct to cold after 6 hours
            if (!IsBurning && currentState == "extinct" &&
                Api.World.Calendar.TotalHours - extinguishedTotalHours > 6.0)
            {
                canIgniteFuel = false;
                SetBrazierState("cold");
            }

            // Update temperature
            if (IsBurning)
            {
                furnaceTemperature = ChangeTemperature(furnaceTemperature, maxTemperature, dt);
            }

            // Auto-ignite if smoldering and has fuel
            if (!IsBurning && canIgniteFuel && CanSmelt())
            {
                IgniteFuel();
            }

            // Cool down when not burning
            if (!IsBurning)
            {
                furnaceTemperature = ChangeTemperature(furnaceTemperature, GetEnvironmentTemperature(), dt);
            }
        }

        private void On500msTick(float dt)
        {
            // Sync to clients and mark for saving, like the vanilla firepit.
            // No client redraw here: visual changes (state/burnstate) trigger their own redraw.
            if (IsBurning || prevFurnaceTemperature != furnaceTemperature)
            {
                MarkDirty();
            }
            prevFurnaceTemperature = furnaceTemperature;
        }

        /// <summary>
        /// Extinguishes the brazier partially or completely. Same behavior as the vanilla firepit.
        /// Called by the vanilla "TemperatureSensitive" behavior (rain, submerged in water, watering can) and by Real Smoke.
        /// </summary>
        public void CoolNow(float amountRel, OnStackToCool onStackToCoolCallback)
        {
            // Server-authoritative. The behavior also ticks on the client, ignore it there.
            if (Api?.Side != EnumAppSide.Server || !IsBurning) return;

            Api.World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), Pos, -0.5, null, false, 16);

            fuelBurnTime -= amountRel / 10f;

            if (Api.World.Rand.NextDouble() < amountRel / 5f || fuelBurnTime <= 0f)
            {
                SetBrazierState("cold");
                extinguishedTotalHours = -99.0;
                canIgniteFuel = false;
                fuelBurnTime = 0f;
                maxFuelBurnTime = 0f;
            }

            MarkDirty(true);
        }

        public float ChangeTemperature(float fromTemp, float toTemp, float dt)
        {
            float diff = Math.Abs(fromTemp - toTemp);
            dt += dt * (diff / 28f);

            if (diff < dt)
                return toTemp;

            if (fromTemp > toTemp)
                dt = -dt;

            if (Math.Abs(fromTemp - toTemp) < 1f)
                return toTemp;

            return fromTemp + dt;
        }

        private bool CanSmelt()
        {
            if (FuelSlot.Empty)
                return false;

            var combustibleProps = FuelSlot.Itemstack?.Collectible?.CombustibleProps;
            if (combustibleProps == null)
                return false;

            return combustibleProps.BurnTemperature * HeatModifier > 0f;
        }

        public void IgniteFuel()
        {
            if (FuelSlot.Empty)
                return;

            ItemStack fuel = FuelSlot.Itemstack!;

            FuelSlot.Itemstack!.StackSize -= 1;
            if (FuelSlot.Itemstack.StackSize <= 0)
            {
                FuelSlot.Itemstack = null;
            }
            FuelSlot.MarkDirty();

            // Ignite after consuming, so the MarkDirty in IgniteWithFuel already contains the reduced fuel stack
            IgniteWithFuel(fuel);
        }

        public void IgniteWithFuel(ItemStack stack)
        {
            var combustibleProps = stack.Collectible.CombustibleProps;
            if (combustibleProps == null)
                return;

            maxFuelBurnTime = fuelBurnTime = combustibleProps.BurnDuration * BurnDurationModifier;
            maxTemperature = (int)(combustibleProps.BurnTemperature * HeatModifier);
            smokeLevel = combustibleProps.SmokeLevel;

            SetBrazierState("lit");
            MarkDirty(true);
        }

        /// <summary>
        /// Must be called after the fuel slot was changed from outside (e.g. refuelling),
        /// so that the change is saved and sent to the clients.
        /// </summary>
        public void OnFuelChanged()
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                MarkDirty();
            }
        }

        public EnumIgniteState GetIgnitableState(float secondsIgniting)
        {
            if (FuelSlot.Empty)
                return EnumIgniteState.NotIgnitablePreventDefault;

            if (IsBurning)
                return EnumIgniteState.NotIgnitablePreventDefault;

            if (secondsIgniting > 3f)
                return EnumIgniteState.IgniteNow;

            return EnumIgniteState.Ignitable;
        }

        /// <summary>
        /// Sets the brazier state via ARL and handles block exchange for lit/unlit variants
        /// </summary>
        public void SetBrazierState(string state)
        {
            // Determine burnstate variant based on state
            string burnstate = state == "lit" ? "lit" : "unlit";
            string currentBurnstate = Block.Variant.TryGetValue("burnstate", out string? bs) ? bs : "unlit";

            // Exchange block if burnstate changes (for light emission)
            if (burnstate != currentBurnstate)
            {
                AssetLocation newBlockCode = Block.CodeWithVariant("burnstate", burnstate);
                Block? newBlock = Api.World.GetBlock(newBlockCode);

                if (newBlock != null)
                {
                    Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
                    Block = newBlock;
                }
            }

            // Set ARL state attribute
            SetVariant("state", state);
        }

        private string? GetCurrentState()
        {
            // Try to read from ARL variants
            var beh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (beh?.Variants != null)
            {
                // Read the state value directly from the Variants
                string? stateValue = beh.Variants.Get("state");
                if (!string.IsNullOrEmpty(stateValue))
                {
                    return stateValue;
                }
            }
            return "construct0"; // Default for new placement
        }

        private int GetEnvironmentTemperature()
        {
            return 20;
        }

        // IHeatSource implementation
        public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
        {
            if (IsBurning)
                return 10f;
            if (IsSmoldering)
                return 0.25f;
            return 0f;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            furnaceTemperature = tree.GetFloat("furnaceTemperature", 20f);
            maxTemperature = tree.GetInt("maxTemperature", 0);
            fuelBurnTime = tree.GetFloat("fuelBurnTime", 0f);
            maxFuelBurnTime = tree.GetFloat("maxFuelBurnTime", 0f);
            extinguishedTotalHours = tree.GetDouble("extinguishedTotalHours", -99.0);
            canIgniteFuel = tree.GetBool("canIgniteFuel", false);
            smokeLevel = tree.GetFloat("smokeLevel", 0f);

            // Deserialize fuel slot
            var fuelStackBytes = tree.GetBytes("fuelStack");
            if (fuelStackBytes != null && fuelStackBytes.Length > 0)
            {
                try
                {
                    _fuelSlot.Itemstack = new ItemStack(fuelStackBytes);
                    _fuelSlot.Itemstack?.ResolveBlockOrItem(worldForResolving);
                }
                catch
                {
                    _fuelSlot.Itemstack = null;
                }
            }
            else
            {
                _fuelSlot.Itemstack = null;
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetFloat("furnaceTemperature", furnaceTemperature);
            tree.SetInt("maxTemperature", maxTemperature);
            tree.SetFloat("fuelBurnTime", fuelBurnTime);
            tree.SetFloat("maxFuelBurnTime", maxFuelBurnTime);
            tree.SetDouble("extinguishedTotalHours", extinguishedTotalHours);
            tree.SetBool("canIgniteFuel", canIgniteFuel);
            tree.SetFloat("smokeLevel", smokeLevel);

            // Serialize fuel slot
            if (FuelSlot.Itemstack != null)
            {
                tree.SetBytes("fuelStack", FuelSlot.Itemstack.ToBytes());
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
        }

        public override void OnBlockBroken(IPlayer? byPlayer = null)
        {
            // Drop fuel if any
            if (!FuelSlot.Empty && Api.Side == EnumAppSide.Server)
            {
                Api.World.SpawnItemEntity(FuelSlot.Itemstack!, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            base.OnBlockBroken(byPlayer);
        }
    }
}
