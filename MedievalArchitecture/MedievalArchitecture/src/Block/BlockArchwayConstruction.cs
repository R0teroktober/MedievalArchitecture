using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorConstructible : BlockBehavior
    {
        public BlockBehaviorConstructible(Block block) : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
        }

    }
}
