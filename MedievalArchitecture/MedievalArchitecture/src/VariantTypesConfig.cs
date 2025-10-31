using System.Collections.Generic;

namespace MedievalArchitecture
{
    public class VariantTypesConfig
    {
        public int RimStoneAmount { get; set; } = 6;
        public Dictionary<string, string> StateCodeByType { get; set; } = new();
        public Dictionary<string, string> StyleCodeByType { get; set; } = new();
        public Dictionary<string, string> RockCodeByType { get; set; } = new();
        public Dictionary<string, string> WoodCodeByType { get; set; } = new();
        public Dictionary<string, string> GlassCodeByType { get; set; } = new();
        public Dictionary<string, string> OriginblockCodeByType { get; set; } = new();
    }
}