using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using AttributeRenderingLibrary;

namespace MedievalArchitecture
{
    public class BlockEntityConstructable : BlockEntity
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
            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            string variantsBefore = arlBeh?.Variants?.ToString() ?? "";

            // base calls FromTreeAttributes of all behaviors, including ARL, which reloads the variants and rebuilds its (cached) mesh
            base.FromTreeAttributes(tree, worldForResolving);

            // Only redraw the chunk if the look actually changed. Redrawing on every update
            // (e.g. the brazier syncs every 500 ms while burning) causes a constant chunk re-tesselation.
            if (Api is ICoreClientAPI && (arlBeh?.Variants?.ToString() ?? "") != variantsBefore)
            {
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        // ToTreeAttributes: base already calls ToTreeAttributes of all behaviors (incl. ARL), nothing to add here.

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