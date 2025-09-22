using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace MedievalArchitecture
{
    public class BlockArchwayConstruction : Block
    {
        private int brickAmount;

        private float timeToConstruct;
        private List<ItemStack> brickStacks;
        private WorldInteraction[] interactions;

        public AssetLocation BuildSound { get; protected set; }
        public float lastUsedDuration = 0;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            brickStacks = new List<ItemStack>();

            brickAmount = Attributes["brickAmount"].AsInt(4);
            timeToConstruct = Attributes["timeToConstruct"].AsFloat(2);
            BuildSound = AssetLocation.Create(Attributes?["sound"]?.AsString());

            foreach (CollectibleObject obj in api.World.Collectibles)
            {
                if (obj.Code.Path.StartsWithFast("aocshingles"))
                {
                    brickStacks.Add(new ItemStack(obj, brickAmount));
                }
            }
            if (api.Side == EnumAppSide.Client)
            {
                interactions = new WorldInteraction[]
                {
                new()
                {
                    ActionLangCode = "confession:blockhelp-build-adddbrick",
                    Itemstacks = brickStacks.ToArray(),
                    MouseButton = EnumMouseButton.Right
                }
                };
            }
        }
    }
}
