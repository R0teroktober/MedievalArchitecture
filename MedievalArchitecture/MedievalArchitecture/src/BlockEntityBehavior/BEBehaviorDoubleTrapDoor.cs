using System;
using System.Collections;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MedievalArchitecture
{

    public class BEBehaviorDoubleTrapDoor : BEBehaviorAnimatable, IInteractable, IRotatable
    {
        protected bool opened;
        protected MeshData mesh;
        protected Cuboidf[] boxesClosed = Array.Empty<Cuboidf>();
        protected Cuboidf[] boxesOpened = Array.Empty<Cuboidf>();
        protected Cuboidf[] openedBoxesBase = Array.Empty<Cuboidf>();

        protected BlockBehaviorDoubleTrapDoor doorBh;

        public int AttachedFace;
        public int RotDeg;
        public float RotRad => RotDeg * GameMath.DEG2RAD;

        public bool Opened => opened;
        public Cuboidf[] ColSelBoxes => opened ? boxesOpened : boxesClosed;

        public BlockFacing FacingWhenClosed
        {
            get
            {
                if (BlockFacing.ALLFACES[AttachedFace].IsVertical)
                {
                    return BlockFacing.ALLFACES[AttachedFace].Opposite;
                }

                return BlockFacing.DOWN.FaceWhenRotatedBy(
                    0f,
                    BlockFacing.ALLFACES[AttachedFace].HorizontalAngleIndex * 90f * GameMath.DEG2RAD + 90f * GameMath.DEG2RAD,
                    RotRad
                );
            }
        }

        public BlockFacing FacingWhenOpened
        {
            get
            {
                if (BlockFacing.ALLFACES[AttachedFace].IsVertical)
                {
                    return BlockFacing.ALLFACES[AttachedFace]
                        .Opposite
                        .FaceWhenRotatedBy((BlockFacing.ALLFACES[AttachedFace].Negative ? -90f : 90f) * GameMath.DEG2RAD, 0f, 0f)
                        .FaceWhenRotatedBy(0f, RotRad, 0f);
                }

                return BlockFacing.ALLFACES[AttachedFace].Opposite;
            }
        }

        public BEBehaviorDoubleTrapDoor(BlockEntity blockentity) : base(blockentity)
        {
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            doorBh = Blockentity.Block.GetBehavior<BlockBehaviorDoubleTrapDoor>();

            var attr = Blockentity.Block.Attributes;
            openedBoxesBase = LoadBoxesFromAttributes(attr?["openedCollisionBoxes"]) ?? Array.Empty<Cuboidf>();

            SetupRotationsAndColSelBoxes();

            if (opened && animUtil != null && !animUtil.activeAnimationsByAnimCode.ContainsKey("opened"))
            {
                ToggleDoorWing(true);
            }
        }

        protected void SetupRotationsAndColSelBoxes()
        {
            if (Api.Side == EnumAppSide.Client && doorBh != null)
            {
                string animKey = GetAnimCacheKey();

                if (doorBh.animatableOrigMesh == null || doorBh.animatableDictKey != animKey)
                {
                    doorBh.animatableOrigMesh = animUtil.CreateMesh(animKey, null, out Shape shape, null);
                    doorBh.animatableShape = shape;
                    doorBh.animatableDictKey = animKey;
                }

                if (doorBh.animatableOrigMesh != null)
                {
                    animUtil.InitializeAnimator(doorBh.animatableDictKey, doorBh.animatableOrigMesh, doorBh.animatableShape, null);

                    UpdateMeshAndAnimations();
                }
            }

            UpdateHitBoxes();
        }

        protected virtual string GetAnimCacheKey()
        {
            return "trapdoor-" + Blockentity.Block.Code.ToShortString();
        }

        protected void UpdateMeshAndAnimations()
        {
            if (doorBh?.animatableOrigMesh == null) return;

            mesh = doorBh.animatableOrigMesh.Clone();

            Matrixf mat = GetTfMatrix();
            mesh.MatrixTransform(mat.Values);

            if (animUtil?.renderer != null)
            {
                animUtil.renderer.CustomTransform = mat.Values;
            }
        }

        protected void UpdateHitBoxes()
        {
            Matrixf mat = GetTfMatrix();

            Cuboidf[] closedBase = Blockentity.Block.CollisionBoxes ?? Array.Empty<Cuboidf>();
            boxesClosed = TransformBoxes(closedBase, mat);

            Cuboidf[] openBase = openedBoxesBase ?? Array.Empty<Cuboidf>();
            boxesOpened = TransformBoxes(openBase, mat);
        }

        protected Cuboidf[] TransformBoxes(Cuboidf[] src, Matrixf mat)
        {
            Cuboidf[] dst = new Cuboidf[src.Length];

            for (int i = 0; i < src.Length; i++)
            {
                dst[i] = src[i].TransformedCopy(mat.Values);
            }

            return dst;
        }

        protected Matrixf GetTfMatrix()
        {
            BlockFacing face = BlockFacing.ALLFACES[AttachedFace];

            if (face.IsVertical)
            {
                return new Matrixf()
                    .Translate(0.5f, 0.5f, 0.5f)
                    .RotateYDeg(RotDeg)
                    .RotateZDeg(face.Negative ? 180 : 0)
                    .Translate(-0.5f, -0.5f, -0.5f);
            }

            int hai = face.HorizontalAngleIndex;

            return new Matrixf()
                .Translate(0.5f, 0.5f, 0.5f)
                .RotateYDeg(hai * 90)
                .RotateYDeg(90)
                .RotateZDeg(RotDeg)
                .Translate(-0.5f, -0.5f, -0.5f);
        }

        protected Cuboidf[] LoadBoxesFromAttributes(JsonObject jo)
        {
            if (jo == null || !jo.Exists) return null;

            JsonObject[] arr = jo.AsArray();
            List<Cuboidf> boxes = new List<Cuboidf>(arr.Length);

            foreach (JsonObject el in arr)
            {
                float[] a = el.AsArray<float>(null);
                if (a == null || a.Length != 6) continue;

                boxes.Add(new Cuboidf(a[0], a[1], a[2], a[3], a[4], a[5]));
            }

            return boxes.ToArray();
        }

        public void OnBlockPlaced(ItemStack byItemStack, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return;

            AttachedFace = blockSel.Face.Index;

            var center = blockSel.Face.ToAB(blockSel.Face.PlaneCenter);
            var hitpos = blockSel.Face.ToAB(blockSel.HitPosition.ToVec3f());

            RotDeg = (int)Math.Round(
                GameMath.RAD2DEG * Math.Atan2(center.A - hitpos.A, center.B - hitpos.B) / 90
            ) * 90;

            if (blockSel.Face == BlockFacing.WEST || blockSel.Face == BlockFacing.SOUTH)
            {
                RotDeg *= -1;
            }

            SetupRotationsAndColSelBoxes();
            Blockentity.MarkDirty(true);
        }

        public bool IsSideSolid(BlockFacing facing)
        {
            return (!opened && facing == FacingWhenClosed) || (opened && facing == FacingWhenOpened);
        }

        public bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            if (doorBh != null && !doorBh.handopenable && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                if (Api is ICoreClientAPI capi)
                {
                    capi.TriggerIngameError(this, "nothandopenable", Lang.Get("This door cannot be opened by hand."));
                }

                handling = EnumHandling.PreventDefault;
                return true;
            }

            ToggleDoorStateCustom(byPlayer, !opened);
            handling = EnumHandling.PreventDefault;
            return true;
        }

        public void ToggleDoorStateCustom(IPlayer byPlayer, bool nowOpened)
        {
            opened = nowOpened;
            ToggleDoorWing(nowOpened);

            var be = Blockentity;

            float pitch = nowOpened ? 1.1f : 0.9f;
            AssetLocation sound = nowOpened ? doorBh?.OpenSound : doorBh?.CloseSound;

            Api.World.PlaySoundAt(sound, Pos.X + 0.5f, Pos.Y + 0.5f, Pos.Z + 0.5f, byPlayer, EnumSoundType.Sound, pitch);

            be.MarkDirty(true);

            if (Api.Side == EnumAppSide.Server)
            {
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
            }
        }

        protected void ToggleDoorWing(bool nowOpened)
        {
            opened = nowOpened;

            if (animUtil == null) return;

            if (!nowOpened)
            {
                animUtil.StopAnimation("opened");
            }
            else
            {
                float easingSpeed = Blockentity.Block.Attributes?["easingSpeed"].AsFloat(10) ?? 10;

                animUtil.StartAnimation(new AnimationMetaData() { Animation = "opened", Code = "opened", EaseInSpeed = easingSpeed, EaseOutSpeed = easingSpeed });
            }

            Blockentity.MarkDirty();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            bool skipMesh = base.OnTesselation(mesher, tessThreadTesselator);

            if (!skipMesh && mesh != null)
            {
                mesher.AddMeshData(mesh);
            }

            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            bool beforeOpened = opened;

            AttachedFace = tree.GetInt("attachedFace");
            RotDeg = tree.GetInt("rotDeg");
            opened = tree.GetBool("opened");

            if (opened != beforeOpened && animUtil != null)
            {
                ToggleDoorWing(opened);
            }

            if (Api != null && Api.Side == EnumAppSide.Client)
            {
                UpdateMeshAndAnimations();

                if (opened && !beforeOpened && animUtil != null && !animUtil.activeAnimationsByAnimCode.ContainsKey("opened"))
                {
                    ToggleDoorWing(true);
                }

                UpdateHitBoxes();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetInt("attachedFace", AttachedFace);
            tree.SetInt("rotDeg", RotDeg);
            tree.SetBool("opened", opened);
        }

        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
        {
            AttachedFace = tree.GetInt("attachedFace");
            BlockFacing face = BlockFacing.ALLFACES[AttachedFace];

            if (face.IsVertical)
            {
                RotDeg = tree.GetInt("rotDeg");
                RotDeg = GameMath.Mod(RotDeg - degreeRotation, 360);
                tree.SetInt("rotDeg", RotDeg);
            }
            else
            {
                int rIndex = degreeRotation / 90;
                int horizontalAngleIndex = GameMath.Mod(face.HorizontalAngleIndex - rIndex, 4);
                BlockFacing newFace = BlockFacing.HORIZONTALS_ANGLEORDER[horizontalAngleIndex];

                AttachedFace = newFace.Index;
                tree.SetInt("attachedFace", AttachedFace);
            }
        }
    }
}