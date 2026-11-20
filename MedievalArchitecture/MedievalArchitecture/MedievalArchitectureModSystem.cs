using MedievalArchitecture;
using System;
using Vintagestory.API.Common;

namespace MedievalArchitecture
{
    public class MedievalArchitectureModSystem : ModSystem
    {
        // Current config file name (written by every version since 1.0.0).
        private const string ConfigFileName = "medievalarchitecture.json";
        // Name that older versions tried to read. Only used as a fallback if a player created it by hand.
        private const string LegacyConfigFileName = "variant-types-config.json";

        public static MedievalArchitectureConfig Config { get; private set; } = MedievalArchitectureConfig.CreateDefault();

        /// <summary>Archway materials of this side (server and client each have their own instance).</summary>
        public ArchwayMaterialRegistry ArchwayMaterials { get; } = new ArchwayMaterialRegistry();

        public static ArchwayMaterialRegistry? GetArchwayMaterials(ICoreAPI api)
        {
            return api.ModLoader.GetModSystem<MedievalArchitectureModSystem>()?.ArchwayMaterials;
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockClass("BlockMultiblockArchway", typeof(BlockMultiblockArchway));
            api.RegisterBlockClass("BlockBrazier", typeof(BlockBrazier));
            api.RegisterBlockClass("BlockGate", typeof(BlockGateBase));
            api.RegisterBlockClass("BlockGatePart", typeof(BlockGatePart));
            api.RegisterBlockBehaviorClass("BlockBehaviorArchway", typeof(BlockBehaviorArchway));
            api.RegisterBlockBehaviorClass("BlockBehaviorCancelInteraction", typeof(BlockBehaviorCancelInteraction));
            api.RegisterBlockBehaviorClass("BlockBehaviorArchwayFrame", typeof(BlockBehaviorArchwayFrame));
            api.RegisterBlockBehaviorClass("DoubleTrapDoor", typeof(BlockBehaviorDoubleTrapDoor));
            api.RegisterBlockEntityClass("BlockEntityConstructable", typeof(BlockEntityConstructable));
            api.RegisterBlockEntityClass("BlockEntityBrazier", typeof(BlockEntityBrazier));
            api.RegisterBlockEntityClass("GatePart", typeof(BEGatePart));
            api.RegisterBlockEntityBehaviorClass("DoubleTrapDoorBE", typeof(BEBehaviorDoubleTrapDoor));
            api.RegisterBlockEntityBehaviorClass("Gate", typeof(BEBehaviorGate));
            LoadConfig(api);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            // All blocks and items are loaded now, so the code patterns of the registry can be resolved.
            ArchwayMaterials.Load(api);
        }

        private static void LoadConfig(ICoreAPI api)
        {
            MedievalArchitectureConfig? loaded = null;
            bool readFailed = false;

            try
            {
                loaded = api.LoadModConfig<MedievalArchitectureConfig>(ConfigFileName);
            }
            catch (Exception e)
            {
                readFailed = true;
                api.Logger.Error("[MedievalArchitecture] Could not read config file {0}, using default values. The file will not be overwritten, please fix or delete it. Error: {1}", ConfigFileName, e.Message);
            }

            if (loaded == null && !readFailed)
            {
                try
                {
                    loaded = api.LoadModConfig<MedievalArchitectureConfig>(LegacyConfigFileName);
                    if (loaded != null)
                    {
                        api.Logger.Notification("[MedievalArchitecture] Found legacy config {0}, its values are taken over into {1}.", LegacyConfigFileName, ConfigFileName);
                    }
                }
                catch (Exception e)
                {
                    api.Logger.Warning("[MedievalArchitecture] Could not read legacy config file {0}, ignoring it. Error: {1}", LegacyConfigFileName, e.Message);
                }
            }

            if (loaded == null)
            {
                Config = MedievalArchitectureConfig.CreateDefault();
                if (!readFailed) api.Logger.Notification("[MedievalArchitecture] Default config created.");
            }
            else
            {
                Config = loaded;
                int changes = Config.ApplyMissingDefaults();
                if (changes > 0) api.Logger.Notification("[MedievalArchitecture] Config loaded, {0} missing or invalid entries were set to default values.", changes);
                else api.Logger.Notification("[MedievalArchitecture] Config loaded successfully.");
            }

            // Write back so that new options appear in the file. Never overwrite a file we could not read.
            if (!readFailed)
            {
                api.StoreModConfig(Config, ConfigFileName);
            }
        }
    }

}
