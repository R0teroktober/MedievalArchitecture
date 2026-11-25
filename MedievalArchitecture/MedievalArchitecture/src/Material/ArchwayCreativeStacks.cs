using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace MedievalArchitecture
{
    /// <summary>
    /// Creates the creative inventory stacks of blocks that are made from rim stones (tiny windows, windowsill) from the
    /// rim stones of the archway material registry. Every rock, also of other mods, gets its own stack and with it a
    /// handbook page (the handbook only knows stacks that are listed in the creative inventory).
    /// <para>
    /// Blocks opt in with the attribute <c>"creativeStacksByRim": true</c>. The stacks in the block JSON are templates:
    /// their "types" without "rock" are combined with every buildable rim stone (sorted by id). Stacks without
    /// "types.rock" are kept as they are. If the registry has no rim stones, the JSON stacks stay unchanged.
    /// </para>
    /// <para>
    /// Server and client must create exactly the same stacks: the creative inventory is addressed by slot numbers, so a
    /// different list on one side presumably hands out the wrong item (B28, test T14). Only "buildable" of the registry
    /// counts here, never the textures (only the client knows them).
    /// </para>
    /// </summary>
    public static class ArchwayCreativeStacks
    {
        public const string OptInAttribute = "creativeStacksByRim";
        private const string TypesAttribute = "types";
        private const string LogPrefix = "[MedievalArchitecture]";

        /// <summary>Call after the registry was loaded (AssetsFinalize), on server and client.</summary>
        public static void Apply(ICoreAPI api, ArchwayMaterialRegistry registry)
        {
            if (!registry.Loaded) return;

            // "Buildable" and not "CanBuild": a rim stone without texture (client only) still gets its stacks, so that
            // server and client have the same list.
            List<string> rimIds = registry.Rims
                .Where(rim => rim.Buildable)
                .Select(rim => rim.Id)
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            List<string> rimsWithoutTexture = registry.Rims
                .Where(rim => rim.Buildable && rim.TextureMissing)
                .Select(rim => rim.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            if (rimsWithoutTexture.Count > 0)
            {
                api.Logger.Warning("{0} Rim stone(s) without texture still get creative stacks (same list as on the server): {1}", LogPrefix, string.Join(", ", rimsWithoutTexture));
            }

            int blockCount = 0;
            int stackCount = 0;

            foreach (Block block in api.World.Blocks)
            {
                if (block?.Code == null || block.IsMissing) continue;
                if (block.CreativeInventoryStacks == null || block.CreativeInventoryStacks.Length == 0) continue;
                if (block.Attributes?[OptInAttribute].AsBool(false) != true) continue;

                if (rimIds.Count == 0)
                {
                    api.Logger.Warning("{0} No rim stones in the archway material registry, creative stacks of {1} are not changed.", LogPrefix, block.Code);
                    continue;
                }

                foreach (CreativeTabAndStackList list in block.CreativeInventoryStacks)
                {
                    if (list?.Stacks == null) continue;
                    list.Stacks = Expand(api, block, list.Stacks, rimIds);
                    stackCount += list.Stacks.Length;
                }
                blockCount++;
            }

            if (blockCount > 0)
            {
                api.Logger.Notification("{0} Creative stacks ({1}): {2} blocks, {3} stacks from {4} rims.", LogPrefix, api.Side == EnumAppSide.Server ? "server" : "client", blockCount, stackCount, rimIds.Count);
            }
        }

        private static JsonItemStack[] Expand(ICoreAPI api, Block block, JsonItemStack[] stacks, List<string> rimIds)
        {
            var result = new List<JsonItemStack>();
            var seenTemplates = new HashSet<string>();

            foreach (JsonItemStack stack in stacks)
            {
                JObject? attributes = stack?.Attributes?.Token as JObject;
                JObject? types = attributes?[TypesAttribute] as JObject;
                if (stack == null || attributes == null || types == null || types[ArchwayMaterialRegistry.RimAttribute] == null)
                {
                    if (stack != null) result.Add(stack);
                    continue;
                }

                // Same template more than once (e.g. created already on the other side) -> only once
                if (!seenTemplates.Add(TemplateKey(stack, types))) continue;

                foreach (string rimId in rimIds)
                {
                    // Keep the order of the attributes of the template, only the rim stone changes
                    var newTypes = new JObject();
                    foreach (JProperty property in types.Properties())
                    {
                        newTypes[property.Name] = property.Name == ArchwayMaterialRegistry.RimAttribute ? new JValue(rimId) : property.Value.DeepClone();
                    }

                    var newAttributes = (JObject)attributes.DeepClone();
                    newAttributes[TypesAttribute] = newTypes;

                    var newStack = new JsonItemStack
                    {
                        Type = stack.Type,
                        Code = stack.Code,
                        StackSize = stack.StackSize,
                        Attributes = new JsonObject(newAttributes)
                    };

                    if (newStack.Resolve(api.World, "creative stacks of " + block.Code))
                    {
                        result.Add(newStack);
                    }
                }
            }

            return result.ToArray();
        }

        /// <summary>Code + "types" without the rim stone, independent of the attribute order.</summary>
        private static string TemplateKey(JsonItemStack stack, JObject types)
        {
            IEnumerable<string> parts = types.Properties()
                .Where(property => property.Name != ArchwayMaterialRegistry.RimAttribute)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => property.Name + "=" + property.Value.ToString(Formatting.None));
            return stack.Type + ":" + stack.Code + "|" + string.Join(";", parts);
        }
    }
}
