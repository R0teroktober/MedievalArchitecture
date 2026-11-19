using System.Collections.Generic;

namespace MedievalArchitecture
{
    /// <summary>
    /// Mod config, stored as ModConfig/medievalarchitecture.json.
    /// IMPORTANT: Property names are the JSON keys of existing player config files. Do not rename them.
    /// </summary>
    public class MedievalArchitectureConfig
    {
        public int RimStoneAmount { get; set; } = 6;
        public float BrazierBurnDurationModifier { get; set; } = 30f;
        public Dictionary<string, string> StateCodeByType { get; set; } = new();
        public Dictionary<string, string> StyleCodeByType { get; set; } = new();
        public Dictionary<string, string> RockCodeByType { get; set; } = new();
        public Dictionary<string, string> WoodCodeByType { get; set; } = new();
        public Dictionary<string, string> GlassCodeByType { get; set; } = new();
        public Dictionary<string, string> OriginblockCodeByType { get; set; } = new();

        public static MedievalArchitectureConfig CreateDefault()
        {
            return new MedievalArchitectureConfig()
            {
                RimStoneAmount = 6,
                BrazierBurnDurationModifier = 30f,
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
                    { "style-d", "d" },
                    { "style-e", "e" },
                    { "style-f", "f" },
                    { "style-g", "g" },
                    { "style-h", "h" }
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
                    { "rock-kimberlite", "kimberlite" },
                    { "rock-limestone", "limestone" },
                    { "rock-peridotite", "peridotite" },
                    { "rock-phyllite", "phyllite" },
                    { "rock-redmarble", "redmarble" },
                    { "rock-sandstone", "sandstone" },
                    { "rock-scoria", "scoria" },
                    { "rock-shale", "shale" },
                    { "rock-slate", "slate" },
                    { "rock-suevite", "suevite" },
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
                    { "wood-acacia", "acacia" },
                    { "wood-aged", "aged" },
                    { "wood-baldcypress", "baldcypress" },
                    { "wood-birch", "birch" },
                    { "wood-ebony", "ebony" },
                    { "wood-kapok", "kapok" },
                    { "wood-larch", "larch" },
                    { "wood-maple", "maple" },
                    { "wood-oak", "oak" },
                    { "wood-pine", "pine" },
                    { "wood-purpleheart", "purpleheart" },
                    { "wood-redwood", "redwood" },
                    { "wood-walnut", "walnut" }
                },
                GlassCodeByType = new()
                {
                    { "glass-plain", "plain" },
                    { "glass-blue", "blue" },
                    { "glass-brown", "brown" },
                    { "glass-green", "green" },
                    { "glass-pink", "pink" },
                    { "glass-quartz", "quartz" },
                    { "glass-red", "red" },
                    { "glass-violet", "violet" },
                    { "glass-vintage", "vintage" },
                    { "glass-yellow", "yellow" }
                },
                OriginblockCodeByType = new()
                {
                    { "originblock-none", "none" },
                    { "originblock-rock", "rock" },
                    { "originblock-cobblestone", "cobblestone" },
                    { "originblock-brick", "brick" },
                    { "originblock-plaster", "plaster" },
                    { "originblock-ash", "ash" },
                    { "originblock-blue", "blue" },
                    { "originblock-brown", "brown" },
                    { "originblock-browngolden", "browngolden" },
                    { "originblock-brownlight", "brownlight" },
                    { "originblock-brownweathered", "brownweathered" },
                    { "originblock-green", "green" },
                    { "originblock-orange", "orange" },
                    { "originblock-pink", "pink" },
                    { "originblock-tan", "tan" },
                    { "originblock-yellow", "yellow" }
                }
            };
        }

        /// <summary>
        /// Fills in everything an older or hand-edited config file is missing, so that
        /// new default entries added in later mod versions always reach existing players.
        /// Existing values from the file are kept. Returns the number of corrected/added entries.
        /// </summary>
        public int ApplyMissingDefaults()
        {
            var defaults = CreateDefault();
            int changes = 0;

            if (RimStoneAmount < 1) { RimStoneAmount = defaults.RimStoneAmount; changes++; }
            if (BrazierBurnDurationModifier <= 0f) { BrazierBurnDurationModifier = defaults.BrazierBurnDurationModifier; changes++; }

            StateCodeByType = Merge(StateCodeByType, defaults.StateCodeByType, ref changes);
            StyleCodeByType = Merge(StyleCodeByType, defaults.StyleCodeByType, ref changes);
            RockCodeByType = Merge(RockCodeByType, defaults.RockCodeByType, ref changes);
            WoodCodeByType = Merge(WoodCodeByType, defaults.WoodCodeByType, ref changes);
            GlassCodeByType = Merge(GlassCodeByType, defaults.GlassCodeByType, ref changes);
            OriginblockCodeByType = Merge(OriginblockCodeByType, defaults.OriginblockCodeByType, ref changes);

            return changes;
        }

        private static Dictionary<string, string> Merge(Dictionary<string, string>? current, Dictionary<string, string> defaults, ref int changes)
        {
            current ??= new();
            foreach (var entry in defaults)
            {
                if (!current.ContainsKey(entry.Key))
                {
                    current[entry.Key] = entry.Value;
                    changes++;
                }
            }
            return current;
        }
    }
}
