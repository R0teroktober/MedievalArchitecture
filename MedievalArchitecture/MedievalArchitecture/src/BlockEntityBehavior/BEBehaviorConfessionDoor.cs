using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
#nullable disable

namespace MedievalArchitecture
{
    public class BEBehaviorToggleState : BEBehaviorAnimatable, IInteractable
    {
        protected bool cooldown = false;
        public Dictionary<string, string> stateCodeByType = new();
        public BEBehaviorToggleState(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            var config = MedievalArchitectureModSystem.Config;
            stateCodeByType = new Dictionary<string, string>(config.StateCodeByType);
        }


        public bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            var be = world.BlockAccessor?.GetBlockEntity(blockSel.Position);
            var beh = be.GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (!cooldown && be is not null && beh is not null)
            {
                beh.Variants.FindByVariant(stateCodeByType, out string state);
                ToggleDoorState(world, byPlayer, be, beh, state);

            }
            else return false;
            handling = EnumHandling.PreventDefault;
            return true;
        }

        public void ToggleDoorState(IWorldAccessor world, IPlayer byPlayer, BlockEntity be, BlockEntityBehaviorShapeTexturesFromAttributes beh, string state)
        {
            float animationSpeed = Blockentity.Block.Attributes["animationSpeed"].AsFloat(10);
            float easeInSpeed = Blockentity.Block.Attributes["easeInSpeed"].AsFloat(100);
            float easeOutSpeed = Blockentity.Block.Attributes["easeOutSpeed"].AsFloat(100);

            switch (state)
            {
                case "0":
                    {
                        beh.Variants.Set("state", "1");
                        if (world.Side == EnumAppSide.Client) {
                            animUtil.StartAnimation(new AnimationMetaData() { Animation = "open", Code = "open", AnimationSpeed = animationSpeed, EaseInSpeed = easeInSpeed, EaseOutSpeed = easeOutSpeed});
                        
                        }
                        
                        cooldown  = true;
                        break;
                    }
                case "1":
                    {
                        beh.Variants.Set("state", "0");
                        if (world.Side == EnumAppSide.Client)
                        {
                            animUtil.StopAnimation("open");

                        }
                        cooldown = true;
                        break;
                    }
            }
        }
    }
}
