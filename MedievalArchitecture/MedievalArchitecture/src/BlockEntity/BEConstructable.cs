using System;
using System.Collections.Generic;
using System.Linq;
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

            // Archways built before the material registry get their "fill" attribute. Done a moment later,
            // so that the block entity is not marked dirty while its chunk is still being loaded.
            if (api.Side == EnumAppSide.Server && NeedsLegacyFillConversion())
            {
                RegisterDelayedCallback(_ => TryConvertLegacyFill(), 500);
            }
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            // base copies the attributes of the item stack into the ARL behavior
            base.OnBlockPlaced(byItemStack);
            TryConvertLegacyFill();
        }

        /// <summary>
        /// True if this is an archway built before the material registry: finished (originblock set), but without "fill".
        /// Only blocks whose textures support "fill" are affected (not the brazier, frames or windows).
        /// </summary>
        private bool NeedsLegacyFillConversion()
        {
            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            Variants? variants = arlBeh?.Variants;
            if (variants == null || variants.ContainsKey(ArchwayMaterialRegistry.FillAttribute)) return false;

            string? originblock = variants.Get(ArchwayMaterialRegistry.LegacyFillAttribute);
            if (string.IsNullOrEmpty(originblock) || originblock == ArchwayMaterialRegistry.UnfinishedOriginblock) return false;

            Dictionary<string, Dictionary<string, CompositeTexture>>? textures = Block?.GetBehavior<BlockBehaviorShapeTexturesFromAttributes>()?.texturesByType;
            return textures != null && textures.Keys.Any(k => k.StartsWith(ArchwayMaterialRegistry.FillAttribute + "-", StringComparison.Ordinal));
        }

        /// <summary>
        /// Translates the old fill (originblock + rim rock) into a fill material of the registry. Server only.
        /// Only "fill" is added, all other attributes stay. Archways without matching materials are left unchanged.
        /// </summary>
        /// <returns>True if the archway was converted.</returns>
        public bool TryConvertLegacyFill(bool markDirty = true)
        {
            if (Api == null || Api.Side != EnumAppSide.Server || !NeedsLegacyFillConversion()) return false;

            ArchwayMaterialRegistry? registry = MedievalArchitectureModSystem.GetArchwayMaterials(Api);
            if (registry == null || !registry.Loaded) return false;

            Variants variants = arlBeh!.Variants;
            string? originblock = variants.Get(ArchwayMaterialRegistry.LegacyFillAttribute);
            string? rock = variants.Get(ArchwayMaterialRegistry.RimAttribute);
            string? fill = registry.ResolveLegacyFill(originblock, rock);

            if (fill == null || string.IsNullOrEmpty(rock) || !registry.TryGetRim(rock, out _))
            {
                Api.Logger.Debug("[MedievalArchitecture] Archway at {0} ({1}) not converted: no material for rim \"{2}\" with old fill \"{3}\".", Pos, Block?.Code, rock, originblock);
                return false;
            }

            variants.Set(ArchwayMaterialRegistry.FillAttribute, fill);
            if (markDirty) MarkDirty(true);
            Api.Logger.Debug("[MedievalArchitecture] Archway at {0} ({1}) converted: rim \"{2}\", old fill \"{3}\" -> fill \"{4}\".", Pos, Block?.Code, rock, originblock, fill);
            return true;
        }

        /// <summary>
        /// Deploys multiple variants in one go (server) and handles synchronization and client-side mesh rebuild
        /// </summary>
        public void SetVariants(IDictionary<string, string> variants)
        {
            if (variants == null || variants.Count == 0) return;
            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            if (arlBeh == null) return;

            // Set all variants on the server side in the ARL behavior
            foreach (var kv in variants)
            {
                arlBeh.Variants?.Set(kv.Key, kv.Value);
            }

            // Old fill values (e.g. from the frame or when glass/planks are added) are translated right away
            TryConvertLegacyFill(markDirty: false);

            // Server -> sends TreeAttributes to the client
            MarkDirty(true);

            // Optional: calls Redraw for Clients
            Api.World.BlockAccessor.MarkBlockDirty(Pos);

            // if client rebuild mesh
            if (Api.Side == EnumAppSide.Client)
            {
                TryRebuildArlMeshClient();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        /// <summary>
        /// Convenience for individual variants
        /// </summary>
        public void SetVariant(string key, string value)
        {
            SetVariants(new Dictionary<string, string> { { key, value } });
        }

        // -----------------------
        // Persistence
        // -----------------------
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            if (arlBeh == null) arlBeh = GetBehavior<BlockEntityBehaviorShapeTexturesFromAttributes>();
            string variantsBefore = arlBeh?.Variants?.ToString() ?? "";

            // base calls FromTreeAttributes of all behaviors, including ARL, which reloads the variants and rebuilds its (cached) mesh
            base.FromTreeAttributes(tree, worldForResolving);

            // Only redraw the chunk if the look actually changed. Redrawing on every update
            // (e.g. the brazier syncs every 500 ms while burning) causes a constant chunk re-tesselation
            if (Api is ICoreClientAPI && (arlBeh?.Variants?.ToString() ?? "") != variantsBefore)
            {
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        // ToTreeAttributes: base already calls ToTreeAttributes of all behaviors (incl. ARL), nothing to add here

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