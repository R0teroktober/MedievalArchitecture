using AttributeRenderingLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{
    public class BlockBehaviorGate : StrongBlockBehavior
    {
        //Json settings
        public AssetLocation OpenSound;
        public AssetLocation CloseSound;
        public bool handopenable;
        public bool airtight;
        public int width;
        public int height;
        public int length;
        protected int[] cardinalDir = [90, 180, 270, 0];

        ICoreAPI api;
        public MeshData animatableOrigMesh;
        public Shape animatableShape;
        public string animatableDictKey;


        public BlockBehaviorGate(Block block) : base(block)
        {
            width = block.Attributes["width"].AsInt(1);
            height = block.Attributes["height"].AsInt(1);
            length = block.Attributes["length"].AsInt(1);
            airtight = block.Attributes["airtight"].AsBool(true);
            handopenable = block.Attributes["handopenable"].AsBool(true);
        }


        public override void OnLoaded(ICoreAPI api)
        {
            this.api = api;
            OpenSound = CloseSound = AssetLocation.Create(block.Attributes["triggerSound"].AsString("sounds/block/door"));

            if (block.Attributes["openSound"].Exists) OpenSound = AssetLocation.Create(block.Attributes["openSound"].AsString("sounds/block/door"));
            if (block.Attributes["closeSound"].Exists) CloseSound = AssetLocation.Create(block.Attributes["closeSound"].AsString("sounds/block/door"));

            base.OnLoaded(api);
        }


        //public override void Activate(IWorldAccessor world, Caller caller, BlockSelection blockSel, ITreeAttribute activationArgs, ref EnumHandling handled)
        //{
        //    var beh = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorGate>();

        //    bool toggled = !beh.Toggled;
        //    if (activationArgs != null)
        //    {
        //        toggled = activationArgs.GetBool("toggled", toggled);
        //    }

        //    if (beh.Toggled != toggled)
        //    {
        //        beh.ToggleGateState(null, toggled);
        //    }
        //}

        public override void OnDecalTesselation(IWorldAccessor world, MeshData decalMesh, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;

            var beh = world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorGate>();
            if (beh != null)
            {
                decalMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, beh.RotRad, 0);
            }
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            BlockFacing[] orient = Block.SuggestedHVOrientation(byPlayer, blockSel);
            BlockPos rootPos = blockSel.Position.Copy();
            BlockPos tempPos;
            

            
            int rotDeg = cardinalDir[orient[0].HorizontalAngleIndex];

            for(int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dz = 0; dz < length; dz++)
                    {
                        Vec3i off;

                        switch (rotDeg)
                        {
                            case 90:
                                off = new Vec3i(dz, dy, -dx);
                                break;
                            case 180:
                                off = new Vec3i(-dx, dy, -dz);
                                break;
                            case 270:
                                off = new Vec3i(-dz, dy, dx);
                                break;
                            default:
                                off = new Vec3i(dx, dy, dz);
                                break;
                        }
                        tempPos = rootPos.AddCopy(off.X, off.Y, off.Z);
                        var tempPosBlock = world.BlockAccessor.GetBlock(tempPos);
                        if (tempPosBlock.Id != 0 && !tempPosBlock.IsReplacableBy(block))
                        {
                            handling = EnumHandling.PreventDefault;
                            failureCode = "notenoughspace";
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
