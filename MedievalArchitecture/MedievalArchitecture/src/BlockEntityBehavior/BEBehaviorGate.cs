using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

public class BEBehaviorGate : BEBehaviorAnimatable, IInteractable, IRotatable
{
    private bool opened;
    private MeshData mesh;

    private Cuboidf[] closedBoxes = Array.Empty<Cuboidf>();
    private Dictionary<string, Cuboidf[]> openedBoxesBaseByPart = new();
    private readonly Dictionary<string, Cuboidf[]> openedBoxesByPart = new();

    private BlockBehaviorGate gateBh;

    public int RotDeg { get; private set; }
    public float RotRad => RotDeg * GameMath.DEG2RAD;
    public bool Opened => opened;

    public BEBehaviorGate(BlockEntity blockentity) : base(blockentity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        gateBh = Blockentity.Block.GetBehavior<BlockBehaviorGate>();
        openedBoxesBaseByPart = LoadOpenedBoxes(Blockentity.Block.Attributes?["openedCollisionBoxes"]);

        RefreshShape();

        if (opened && animUtil != null && !animUtil.activeAnimationsByAnimCode.ContainsKey("opened"))
        {
            SyncAnimation();
        }
    }

    public void SetPlacementData(int rotDeg)
    {
        RotDeg = GameMath.Mod(rotDeg, 360);
        RefreshShape();
        Blockentity.MarkDirty(true);
    }

    public Cuboidf[] GetBoxesForPart(int dx, int dy, int dz)
    {
        if (!opened) return closedBoxes;
        return openedBoxesByPart.TryGetValue(Key(dx, dy, dz), out Cuboidf[] boxes) ? boxes : closedBoxes;
    }

    public bool IsSideSolid(BlockFacing face)
    {
        return !opened ? face == BlockFacing.DOWN : face == GetOpenSolidFace();
    }

    private BlockFacing GetOpenSolidFace()
    {
        return BlockFacing.DOWN
            .FaceWhenRotatedBy(90 * GameMath.DEG2RAD, 0, 0)
            .FaceWhenRotatedBy(0, RotRad, 0);
    }

    public bool TryToggle(IPlayer byPlayer, ref EnumHandling handling)
    {
        if (gateBh != null && !gateBh.handopenable && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            if (Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "nothandopenable", Lang.Get("This gate cannot be opened by hand."));
            }

            handling = EnumHandling.PreventDefault;
            return true;
        }

        ToggleGateState(byPlayer, !opened);
        handling = EnumHandling.PreventDefault;
        return true;
    }

    public bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        return TryToggle(byPlayer, ref handling);
    }

    public void ToggleGateState(IPlayer byPlayer, bool nowOpened)
    {
        opened = nowOpened;
        SyncAnimation();

        AssetLocation sound = opened ? gateBh?.OpenSound : gateBh?.CloseSound;
        float pitch = opened ? 1.1f : 0.9f;

        Api.World.PlaySoundAt(sound,Pos.X,Pos.Y, Pos.Z,byPlayer, EnumSoundType.Sound,pitch);

        Blockentity.MarkDirty(true);

        if (Api.Side == EnumAppSide.Server && gateBh != null)
        {
            gateBh.IterateOverEach(Pos, RotDeg, partPos =>
            {
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(partPos);
                return true;
            });
        }
    }

    private void SyncAnimation()
    {
        if (animUtil == null) return;

        if (!opened)
        {
            animUtil.StopAnimation("opened");
        }
        else
        {
            float easingSpeed = Blockentity.Block.Attributes?["easingSpeed"].AsFloat(10) ?? 10;

            animUtil.StartAnimation(new AnimationMetaData()
            {
                Animation = "opened",
                Code = "opened",
                EaseInSpeed = easingSpeed,
                EaseOutSpeed = easingSpeed
            });
        }

        Blockentity.MarkDirty();
    }

    private void RefreshShape()
    {
        if (Api?.Side == EnumAppSide.Client && gateBh != null)
        {
            InitAnimator();
            UpdateMesh();
        }

        UpdateBoxes();
    }

    private void InitAnimator()
    {
        string animKey = "gate-" + Blockentity.Block.Code.ToShortString();

        if (gateBh.animatableOrigMesh == null || gateBh.animatableDictKey != animKey)
        {
            gateBh.animatableOrigMesh = animUtil.CreateMesh(animKey, null, out Shape shape, null);
            gateBh.animatableShape = shape;
            gateBh.animatableDictKey = animKey;
        }

        if (gateBh.animatableOrigMesh != null)
        {
            animUtil.InitializeAnimator(
                gateBh.animatableDictKey,
                gateBh.animatableOrigMesh,
                gateBh.animatableShape,
                null
            );
        }
    }

    private void UpdateMesh()
    {
        if (gateBh?.animatableOrigMesh == null) return;

        mesh = gateBh.animatableOrigMesh.Clone();

        Matrixf tf = GetTransformMatrix();
        mesh.MatrixTransform(tf.Values);

        if (animUtil?.renderer != null)
        {
            animUtil.renderer.CustomTransform = tf.Values;
        }
    }

    private void UpdateBoxes()
    {
        Matrixf tf = GetTransformMatrix();

        closedBoxes = TransformBoxes(Blockentity.Block.CollisionBoxes ?? Array.Empty<Cuboidf>(), tf);

        openedBoxesByPart.Clear();
        foreach (var entry in openedBoxesBaseByPart)
        {
            openedBoxesByPart[entry.Key] = TransformBoxes(entry.Value, tf);
        }
    }

    private static Cuboidf[] TransformBoxes(Cuboidf[] boxes, Matrixf tf)
    {
        if (boxes == null || boxes.Length == 0) return Array.Empty<Cuboidf>();

        Cuboidf[] result = new Cuboidf[boxes.Length];
        for (int i = 0; i < boxes.Length; i++)
        {
            result[i] = boxes[i].TransformedCopy(tf.Values);
        }

        return result;
    }

    private Matrixf GetTransformMatrix()
    {
        return new Matrixf()
            .Translate(0.5f, 0.5f, 0.5f)
            .RotateYDeg(RotDeg)
            .Translate(-0.5f, -0.5f, -0.5f);
    }

    private static Dictionary<string, Cuboidf[]> LoadOpenedBoxes(JsonObject json)
    {
        var result = new Dictionary<string, Cuboidf[]>();
        if (json == null || !json.Exists) return result;

        Dictionary<string, float[][]> raw = json.AsObject<Dictionary<string, float[][]>>(null);
        if (raw == null) return result;

        foreach (var entry in raw)
        {
            float[][] rawBoxes = entry.Value;
            if (rawBoxes == null || rawBoxes.Length == 0)
            {
                result[entry.Key] = Array.Empty<Cuboidf>();
                continue;
            }

            Cuboidf[] boxes = new Cuboidf[rawBoxes.Length];
            for (int i = 0; i < rawBoxes.Length; i++)
            {
                float[] a = rawBoxes[i];
                boxes[i] = (a != null && a.Length == 6)
                    ? new Cuboidf(a[0], a[1], a[2], a[3], a[4], a[5])
                    : Cuboidf.Default();
            }

            result[entry.Key] = boxes;
        }

        return result;
    }

    private static string Key(int dx, int dy, int dz)
    {
        return $"{dx},{dy},{dz}";
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        bool skipDefault = base.OnTesselation(mesher, tessThreadTesselator);

        if (!skipDefault && mesh != null)
        {
            mesher.AddMeshData(mesh);
        }

        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        bool beforeOpened = opened;

        RotDeg = tree.GetInt("rotDeg");
        opened = tree.GetBool("opened");

        if (opened != beforeOpened && animUtil != null)
        {
            SyncAnimation();
        }

        if (Api != null && Api.Side == EnumAppSide.Client)
        {
            UpdateMesh();
            UpdateBoxes();

            if (opened && !beforeOpened && animUtil != null && !animUtil.activeAnimationsByAnimCode.ContainsKey("opened"))
            {
                SyncAnimation();
            }

            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("rotDeg", RotDeg);
        tree.SetBool("opened", opened);
    }

    public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree,int degreeRotation,Dictionary<int, AssetLocation> oldBlockIdMapping,Dictionary<int, AssetLocation> oldItemIdMapping,EnumAxis? flipAxis
)
    {
        int rotDeg = tree.GetInt("rotDeg");
        tree.SetInt("rotDeg", GameMath.Mod(rotDeg - degreeRotation, 360));
    }
}