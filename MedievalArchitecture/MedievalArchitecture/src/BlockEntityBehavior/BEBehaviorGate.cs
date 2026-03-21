using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;


namespace MedievalArchitecture
{
    public class BEBehaviorGate : BEBehaviorAnimatable
    {
        private HorizontalRotation rotation = HorizontalRotation.North;
        private bool toggled = false;


        // Sounds
        private float animationSpeed = 1f;
        private float easingSpeed = 10f;

        private AssetLocation openSound;
        private AssetLocation closeSound;

        protected AnimationMetaData openedAnimation;

        protected MeshData mesh;
        protected MeshData animatableOrigMesh;
        protected Shape animatableShape;
        protected string animatableDictKey;

        public HorizontalRotation Rotation => rotation;
        public bool Toggled => toggled;

        public BEBehaviorGate(BlockEntity blockentity) : base(blockentity)
        {
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            string openSoundPath = Block?.Attributes?["openSound"].AsString();
            openSound = !string.IsNullOrEmpty(openSoundPath) ? AssetLocation.Create(openSoundPath, Block.Code.Domain) : openSound = AssetLocation.Create("sounds/block/door", "game");

       
            string closeSoundPath = Block?.Attributes?["closeSound"].AsString();
            closeSound = !string.IsNullOrEmpty(closeSoundPath) ?  AssetLocation.Create(closeSoundPath, Block.Code.Domain) : closeSound = AssetLocation.Create("sounds/block/door", "game");

            animationSpeed = Block?.Attributes?["animationSpeed"].AsFloat(1f) ?? 1f;
            easingSpeed = Block?.Attributes?["easingSpeed"].AsFloat(10f) ?? 10f;

            if (api.Side == EnumAppSide.Client)
            {
                EnsureAnimator();
                UpdateMeshAndAnimations();

                if (toggled && animUtil != null && !animUtil.activeAnimationsByAnimCode.ContainsKey("opened"))
                {
                    SyncAnimationState(true, false);
                }
            }
        }
        private void EnsureAnimator()
        {
            if (Api?.Side != EnumAppSide.Client)
            {
                return;
            }

            string animKey = GetAnimCacheKey();

            if (animatableOrigMesh == null || animatableDictKey != animKey)
            {
                animatableOrigMesh = animUtil.CreateMesh(animKey, null, out Shape shape, null);
                animatableShape = shape;
                animatableDictKey = animKey;
            }

            if (animatableOrigMesh != null && animUtil.animator == null)
            {
                animUtil.InitializeAnimator(animatableDictKey, animatableOrigMesh, animatableShape, null);
            }
        }

        public void SetRotation(HorizontalRotation rotation)
        {
            this.rotation = rotation;

            if (Api?.Side == EnumAppSide.Client)
            {
                EnsureAnimator();
                UpdateMeshAndAnimations();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public void Toggle()
        {
            toggled = !toggled;

            SyncAnimationState(toggled, true);

            Blockentity.MarkDirty(true);
        }

        private void SyncAnimationState(bool nowOpened, bool playSound = true)
        {
            if (animUtil != null)
            {
                if (!nowOpened)
                {
                    animUtil.StopAnimation("opened");
                }
                else
                {
                    animUtil.StartAnimation(new AnimationMetaData()
                    {
                        Animation = "opened",
                        Code = "opened",
                        AnimationSpeed = animationSpeed,
                        EaseInSpeed = easingSpeed,
                        EaseOutSpeed =3f
                    });
                }
            }

            if (playSound && Api?.Side == EnumAppSide.Client)
            {
                AssetLocation sound = nowOpened ? openSound : closeSound;
                Api.World.PlaySoundAt(sound, Pos.X + 0.5f, Pos.Y + 0.5f, Pos.Z + 0.5f, null);
            }

            if (Api?.Side == EnumAppSide.Client)
            {
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
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

        protected virtual string GetAnimCacheKey()
        {
            return "gate-" + Blockentity.Block.Code.ToShortString();
        }
        protected void UpdateMeshAndAnimations()
        {
            if (animatableOrigMesh == null)
            {
                return;
            }

            mesh = animatableOrigMesh.Clone();

            float rotY = RotationToDegrees(rotation);

            Matrixf mat = new Matrixf()
                .Translate(0.5f, 0.5f, 0.5f)
                .RotateYDeg(rotY)
                .Translate(-0.5f, -0.5f, -0.5f);

            mesh.MatrixTransform(mat.Values);

            if (animUtil?.renderer != null)
            {
                animUtil.renderer.CustomTransform = mat.Values;
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("rotation", rotation.ToString());
            tree.SetBool("toggled", toggled);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            HorizontalRotation beforeRotation = rotation;
            bool beforeToggled = toggled;

            base.FromTreeAttributes(tree, worldAccessForResolve);

            string rotText = tree.GetString("rotation", HorizontalRotation.North.ToString());
            if (System.Enum.TryParse(rotText, out HorizontalRotation loadedRotation))
            {
                rotation = loadedRotation;
            }

            toggled = tree.GetBool("toggled", false);

            if (Api != null && Api.Side == EnumAppSide.Client)
            {
                EnsureAnimator();

                if (mesh == null || rotation != beforeRotation)
                {
                    UpdateMeshAndAnimations();
                }

                if (toggled != beforeToggled && animUtil != null)
                {
                    SyncAnimationState(toggled, true);
                }

                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }


        private static float RotationToDegrees(HorizontalRotation rotation)
        {
            return rotation switch
            {
                HorizontalRotation.East => 270f,
                HorizontalRotation.South => 180f,
                HorizontalRotation.West => 90f,
                _ => 0f
            };
        }
    }
}