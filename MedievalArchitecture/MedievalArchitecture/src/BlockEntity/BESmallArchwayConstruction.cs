using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using AttributeRenderingLibrary;

namespace MedievalArchitecture
{
    public class BlockEntitySmallArchwayConstruction : BlockEntity
    {
        private BlockEntityBehaviorShapeTexturesFromAttributes arlBeh;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
        }

        /// <summary>
        /// Setzt mehrere Varianten in einem Rutsch (Server) und sorgt für Sync & clientseitigen Mesh-Rebuild.
        /// </summary>
        public void SetVariants(IDictionary<string, string> variants)
        {
            if (variants == null || variants.Count == 0) return;
            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (arlBeh == null) return;

            // Setze alle Varianten serverseitig in der ARL-Behavior
            foreach (var kv in variants)
            {
                arlBeh.Variants?.Set(kv.Key, kv.Value);
            }

            // Server -> sendet TreeAttributes zum Client
            MarkDirty(true);

            // Optional: erzwinge Redraw fuer Clients
            Api.World.BlockAccessor.MarkBlockDirty(Pos);

            // Wenn wir clientseitig sind, rebuild direkt (auch FromTreeAttributes macht das)
            if (Api.Side == EnumAppSide.Client)
            {
                TryRebuildArlMeshClient();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        /// <summary>
        /// Convenience für einzelne Variant.
        /// </summary>
        public void SetVariant(string key, string value)
        {
            SetVariants(new Dictionary<string, string> { { key, value } });
        }

        // -----------------------
        // Persistenz
        // -----------------------
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (arlBeh != null)
            {
                try { arlBeh.FromTreeAttributes(tree, worldForResolving); } catch { /* safe */ }
            }

            // DER verlässliche Client-Hook: alle eingehenden Varianten sind jetzt da -> rebuild
            var capi = Api as ICoreClientAPI;
            if (capi != null)
            {
                TryRebuildArlMeshClient();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (arlBeh != null)
            {
                try { arlBeh.ToTreeAttributes(tree); } catch { /* safe */ }
            }
        }

        // Try rebuild using ARL's GenTexSource + GetOrCreateMesh signature; if not found, fallback to CallMethod("Init")
        private void TryRebuildArlMeshClient()
        {
            if (arlBeh == null) return;
            if (Api.Side != EnumAppSide.Client) return;

            var capi = Api as ICoreClientAPI;
            if (capi == null) return;
            arlBeh.CallMethod("Init");

        }
    }
}