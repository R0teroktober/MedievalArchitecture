using System;
using AttributeRenderingLibrary;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockEntityBrazier : BlockEntityConstructable, IHeatSource
    {
        // Fuel storage - single slot, not full inventory
        private ItemSlot? _fuelSlot;
        public ItemSlot FuelSlot => _fuelSlot ??= new DummySlot();

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
        public virtual float BurnDurationModifier => 10f;
        public virtual float HeatModifier => 1f;
        public float emptyFirepitBurnTimeMulBonus = 4f;

        // State properties
        public bool IsBurning => fuelBurnTime > 0f;
        public bool IsSmoldering => canIgniteFuel && !IsBurning;

        // Weather system for rain check
        private WeatherSystemBase? wsys;
        private Vec3d tmpPos = new Vec3d();

        // Tick listener tracking for cleanup
        private long burnTickListenerId;
        private long tickListenerId;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            _fuelSlot = new DummySlot();
            wsys = api.ModLoader.GetModSystem<WeatherSystemBase>(true);

            burnTickListenerId = RegisterGameTickListener(OnBurnTick, 100);
            tickListenerId = RegisterGameTickListener(On500msTick, 500);
        }

        private void OnBurnTick(float dt)
        {
            // Only server should modify burn state - client gets updates via FromTreeAttributes
            if (Api.Side != EnumAppSide.Server) return;

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
            // Server-side sync - use MarkDirty(true) to ensure network sync to clients
            if (Api.Side == EnumAppSide.Server && (IsBurning || IsSmoldering || prevFurnaceTemperature != furnaceTemperature))
            {
                MarkDirty(true);
            }
            prevFurnaceTemperature = furnaceTemperature;

            // Rain extinguishment check
            if (ShouldExtinguishFromRainFall(out float rainLevel))
            {
                Api.World.PlaySoundAt(
                    new AssetLocation("sounds/effect/extinguish"),
                    Pos.X + 0.5, Pos.Y, Pos.Z + 0.5,
                    null, false, 16f, 1f);

                fuelBurnTime -= rainLevel / 10f;

                if (Api.World.Rand.NextDouble() < (double)(rainLevel / 5f) || fuelBurnTime <= 0f)
                {
                    SetBrazierState("cold");
                    extinguishedTotalHours = -99.0;
                    canIgniteFuel = false;
                    fuelBurnTime = 0f;
                    maxFuelBurnTime = 0f;
                }

                MarkDirty(true);
            }
        }

        public bool ShouldExtinguishFromRainFall(out float rainLevel)
        {
            rainLevel = 0f;

            if (Api.Side != EnumAppSide.Server || !IsBurning || wsys == null)
                return false;

            if (Api.World.Rand.NextDouble() > 0.5)
                return false;

            // Check if exposed to sky
            if (Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z) > Pos.Y)
                return false;

            tmpPos.Set(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
            rainLevel = wsys.GetPrecipitation(tmpPos);

            if (rainLevel > 0.04 && Api.World.Rand.NextDouble() < (double)(rainLevel * 5f))
            {
                return true;
            }

            return false;
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

            IgniteWithFuel(FuelSlot.Itemstack!);

            FuelSlot.Itemstack!.StackSize -= 1;
            if (FuelSlot.Itemstack.StackSize <= 0)
            {
                FuelSlot.Itemstack = null;
            }
            FuelSlot.MarkDirty();
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
            if (_fuelSlot == null)
                _fuelSlot = new DummySlot();

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

        public override void OnBlockBroken(IPlayer? byPlayer = null)
        {
            // Drop fuel if any
            if (!FuelSlot.Empty && Api.Side == EnumAppSide.Server)
            {
                Api.World.SpawnItemEntity(FuelSlot.Itemstack!, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }

            base.OnBlockBroken(byPlayer);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            UnregisterGameTickListener(burnTickListenerId);
            UnregisterGameTickListener(tickListenerId);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            UnregisterGameTickListener(burnTickListenerId);
            UnregisterGameTickListener(tickListenerId);
        }
    }
}