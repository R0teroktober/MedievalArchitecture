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
      
        public Dictionary<string, string> GlassCodeByType { get; set; } = new();

        public static MedievalArchitectureConfig CreateDefault()
        {
            return new MedievalArchitectureConfig()
            {
                RimStoneAmount = 6,
                BrazierBurnDurationModifier = 30f,
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
            GlassCodeByType = Merge(GlassCodeByType, defaults.GlassCodeByType, ref changes);

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
