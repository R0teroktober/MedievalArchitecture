using MedievalArchitecture;
using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class MedievalArchitectureModSystem : ModSystem
    {
        public static VariantTypesConfig Config { get; private set; }
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockBehaviorClass("BlockBehaviorSmallArchway", typeof(BlockBehaviorSmallArchway));
            api.RegisterBlockBehaviorClass("BlockBehaviorCancelInteraction", typeof(BlockBehaviorCancelInteraction));
            api.RegisterBlockBehaviorClass("BlockBehaviorSmallArchwayConstruction", typeof(BlockBehaviorSmallArchwayConstruction));
            api.RegisterBlockEntityClass("BlockEntitySmallArchwayConstruction", typeof(BlockEntitySmallArchwayConstruction));
            Config = api.LoadModConfig<VariantTypesConfig>("variant-types-config.json");
            if (Config == null)
            {
                Config = new VariantTypesConfig()
                {
                    RimStoneAmount = 6,
                    StateCodeByType = new()
                    {
                        { "state-0", "0" },
                        { "state-1", "1" },
                        { "state-2", "2" }
                    },
                    StyleCodeByType = new()
                    {
                        { "style-a", "a" },
                        { "style-b", "b" },
                        { "style-c", "c" },
                        { "style-d", "d" }
                    },
                    RockCodeByType = new()
                    {
                        { "rock-andesite", "andesite" },
                        { "rock-basalt", "basalt" },
                        { "rock-bauxite", "bauxite" },
                        { "rock-chert", "chert" },
                        { "rock-chalk", "chalk" },
                        { "rock-claystone", "claystone" },
                        { "rock-conglomerate", "conglomerate" },
                        { "rock-granite", "granite" },
                        { "rock-greenmarble", "greenmarble" },
                        { "rock-halite", "halite" },
                        { "rock-kimberlite", "kimberlite" },
                        { "rock-limestone", "limestone" },
                        { "rock-meteorite-iron", "meteorite-iron" },
                        { "rock-obsidian", "obsidian" },
                        { "rock-peridotite", "peridotite" },
                        { "rock-phyllite", "phyllite" },
                        { "rock-redmarble", "redmarble" },
                        { "rock-sandstone", "sandstone" },
                        { "rock-scoria", "scoria" },
                        { "rock-shale", "shale" },
                        { "rock-slate", "slate" },
                        { "rock-suevite", "suevite" },
                        { "rock-tuff", "tuff" },
                        { "rock-whitemarble", "whitemarble" },
                        { "rock-plaster", "plaster" },
                        { "rock-ash", "ash" },
                        { "rock-blue", "blue" },
                        { "rock-brown", "brown" },
                        { "rock-browngolden", "browngolden" },
                        { "rock-brownlight", "brownlight" },
                        { "rock-brownweathered", "brownweathered" },
                        { "rock-green", "green" },
                        { "rock-orange", "orange" },
                        { "rock-pink", "pink" },
                        { "rock-tan", "tan" },
                        { "rock-yellow", "yellow" }
                    },
                    WoodCodeByType = new()
                    {
                        {"wood-acacia", "acacia" },
                        {"wood-aged", "aged"},
                        {"wood-baldcypress", "baldcypress"},
                        {"wood-birch", "birch"},
                        {"wood-ebony", "ebony"},
                        {"wood-kapok", "kapok"},
                        {"wood-larch", "larch"},
                        {"wood-maple", "maple"},
                        {"wood-oak", "oak"},
                        {"wood-pine", "pine"},
                        {"wood-purpleheart", "purpleheart"},
                        {"wood-redwood", "redwood"},
                        {"wood-walnut", "walnut" }
                    },
                    GlassCodeByType = new()
                    {
                        { "glass-plain", "plain"},
                        {"glass-blue", "blue"},
                        {"glass-brown", "brown"},
                        {"glass-green", "green"},
                        {"glass-pink", "pink"},
                        {"glass-quartz", "quartz"},
                        {"glass-red", "red"},
                        {"glass-vintage", "vintage"},
                        {"glass-yellow", "yellow"}
                    },
                    OriginblockCodeByType = new()
                    {
                    {"originblock-rock", "rock" },
                    {"originblock-cobblestone", "cobblestone"},
                    {"originblock-brick", "brick"},
                    {"originblock-plaster", "plaster"},
                    {"originblock-ash", "ash"},
                    {"originblock-blue", "blue"},
                    {"originblock-brown", "brown"},
                    {"originblock-browngolden", "browngolden"},
                    {"originblock-brownlight", "brownlight"},
                    {"originblock-brownweathered", "brownweathered"},
                    {"originblock-green", "green"},
                    {"originblock-orange", "orange"},
                    {"originblock-pink", "pink"},
                    {"originblock-tan", "tan"},
                    {"originblock-yellow", "yellow"}
                    }
                };

                api.StoreModConfig(Config, "medievalarchitecture.json");
                api.Logger.Notification("[MedievalArchitecture] Default config created.");
            }
            else
            {
                api.Logger.Notification("[MedievalArchitecture] Config loaded successfully.");
            }
        }
    }

}
