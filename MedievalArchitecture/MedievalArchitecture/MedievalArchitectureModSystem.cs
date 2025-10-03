using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class MedievalArchitectureModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterBlockBehaviorClass("BlockBehaviorConstructionStateChanger", typeof(BlockBehaviorConstructionStateChanger));
        }
    }
}
