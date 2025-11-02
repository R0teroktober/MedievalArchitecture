using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorCancelInteraction(Block block) : BlockBehavior(block)
    {


        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handling = EnumHandling.PreventDefault;
               return true;
            } else 
                 
               return base.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);
        }
        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handled = EnumHandling.PreventDefault;
                return secondsUsed < 0.1;
            } else
             return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel, ref handled);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handling = EnumHandling.PreventDefault;
                return true;
            }
            else
                return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {

            if (byPlayer.Entity.Controls.CtrlKey)
            {
                handled = EnumHandling.PreventDefault;
            }
            else
                base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel, ref handled);

        }
    }
}

