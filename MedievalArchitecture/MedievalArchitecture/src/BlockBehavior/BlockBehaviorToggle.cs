using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorToggle(Block block) : StrongBlockBehavior(block), IClaimTraverseable
    {
        public Dictionary<string, string> stateCodeByType = new();
        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            var config = MedievalArchitectureModSystem.Config;
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);

        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            handling = EnumHandling.PreventDefault;
            return true;

        }
        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;
            if (world.Side == EnumAppSide.Server)
            {
                var be = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
                var beh = be?.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
                if (beh is not null)
                {
                    beh.Variants.FindByVariant(stateCodeByType, out string state);
                    //ToggleDoorState(world, byPlayer, be, beh, state);
                }
                else return;
            }
            

        }

        //public void ToggleDoorState(IWorldAccessor world, IPlayer byPlayer, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, string state)
        //{
        //    float animationSpeed = be.Block.Attributes["animationSpeed"].AsFloat(10);
        //    float easeInSpeed = be.Block.Attributes["easeInSpeed"].AsFloat(100);
        //    float easeOutSpeed = be.Block.Attributes["easeOutSpeed"].AsFloat(100);

        //    switch (state)
        //    {
        //        case "0":
        //            {
        //                beh.Variants.Set("state", "1");
        //                if (world.Side == EnumAppSide.Client)
        //                {
        //                    animUtil.StartAnimation(new AnimationMetaData() { Animation = "open", Code = "open", AnimationSpeed = animationSpeed, EaseInSpeed = easeInSpeed, EaseOutSpeed = easeOutSpeed });

        //                }

        //                cooldown = true;
        //                break;
        //            }
        //        case "1":
        //            {
        //                beh.Variants.Set("state", "0");
        //                if (world.Side == EnumAppSide.Client)
        //                {
        //                    animUtil.StopAnimation("open");

        //                }
        //                cooldown = true;
        //                break;
        //            }
        //    }
        //}
    }
}
