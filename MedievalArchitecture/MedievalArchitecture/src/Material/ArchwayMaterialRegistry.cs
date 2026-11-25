using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace MedievalArchitecture
{
    public enum ArchwayMaterialKind
    {
        Rim,
        Fill,
        /// <summary>Boards for the door lintel.</summary>
        Wood
    }

    /// <summary>
    /// A resolved archway material (one concrete rim stone or fill block).
    /// </summary>
    public sealed class ArchwayMaterial
    {
        /// <summary>Material id. Saved in archways, never changes.</summary>
        public string Id { get; }
        public ArchwayMaterialKind Kind { get; }
        /// <summary>
        /// Texture location without "textures/" prefix and ".png" suffix. Client: the first existing texture of the list.
        /// Server: always the first texture of the list (the server does not check textures and never renders).
        /// </summary>
        public AssetLocation Texture { get; }
        /// <summary>All textures of the entry (placeholders filled in), in order. Same on server and client.</summary>
        public IReadOnlyList<AssetLocation> TextureCandidates { get; }
        /// <summary>Fills only: texture of the ledge of big archways (default: same as Texture).</summary>
        public AssetLocation LedgeTexture { get; set; }
        /// <summary>False = only kept for existing archways, cannot be used for new ones.</summary>
        public bool Buildable { get; }
        /// <summary>
        /// Client only: none of the textures exists. The material keeps its id (server and client assign the same
        /// materials), but it cannot be used for new archways. Always false on the server.
        /// </summary>
        public bool TextureMissing { get; internal set; }
        /// <summary>True if new archways can be built with this material: buildable and, on the client, with a texture.</summary>
        public bool CanBuild => Buildable && !TextureMissing;
        /// <summary>Block/item codes that represent this material.</summary>
        public List<AssetLocation> SourceCodes { get; } = new();

        public ArchwayMaterial(string id, ArchwayMaterialKind kind, AssetLocation texture, IReadOnlyList<AssetLocation> textureCandidates, bool buildable)
        {
            Id = id;
            Kind = kind;
            Texture = texture;
            TextureCandidates = textureCandidates;
            LedgeTexture = texture;
            Buildable = buildable;
        }
    }

    /// <summary>
    /// Loads assets/confession/config/archway-materials.json and resolves the code patterns against all loaded
    /// blocks and items. Each side (server/client) has its own instance.
    /// <para>
    /// B28: a dedicated server does not load textures. So which entry owns a block/item code never depends on textures:
    /// the first entry whose code pattern matches wins, on both sides. Only the client checks the textures; a material
    /// without texture keeps its id there but cannot be built (<see cref="ArchwayMaterial.TextureMissing"/>).
    /// </para>
    /// </summary>
    public class ArchwayMaterialRegistry
    {
        public static readonly AssetLocation RegistryLocation = new AssetLocation("confession", "config/archway-materials.json");
        private const string LogPrefix = "[MedievalArchitecture]";
        private const string AnyLegacyKey = "*";

        /// <summary>ARL attribute names stored in archways.</summary>
        public const string RimAttribute = "rock";
        public const string FillAttribute = "fill";
        public const string WoodAttribute = "wood";
        /// <summary>Old fill category (before the registry). "none" = archway not finished yet.</summary>
        public const string LegacyFillAttribute = "originblock";
        public const string UnfinishedOriginblock = "none";

        /// <summary>
        /// True if an archway with these attributes is finished: it has a fill material, or it was finished
        /// before the registry existed (old fill category in "originblock", "none" = unfinished).
        /// </summary>
        public static bool IsFinishedArchway(string? fill, string? originblock)
        {
            return !string.IsNullOrEmpty(fill) || (!string.IsNullOrEmpty(originblock) && originblock != UnfinishedOriginblock);
        }

        /// <summary>
        /// Every material texture is additionally registered under these paths in our own domain, so the archway
        /// blocks can reference any material (also from other mods) with a single ARL placeholder:
        /// confession:block/archway/rim/{rock} and confession:block/archway/fill/{fill}.
        /// </summary>
        public const string AliasDomain = "confession";
        public const string RimAliasPath = "block/archway/rim/";
        public const string FillAliasPath = "block/archway/fill/";
        public const string LedgeAliasPath = "block/archway/ledge/";
        public const string WoodAliasPath = "block/archway/wood/";

        private static readonly Regex PlaceholderRegex = new Regex(@"\{([a-z0-9_]+)\}", RegexOptions.Compiled);
        private static readonly Regex ValidIdRegex = new Regex(@"^[a-z0-9_-]+$", RegexOptions.Compiled);

        private readonly Dictionary<string, ArchwayMaterial> rimsById = new();
        private readonly Dictionary<string, ArchwayMaterial> fillsById = new();
        private readonly Dictionary<AssetLocation, ArchwayMaterial> rimsByCode = new();
        private readonly Dictionary<AssetLocation, ArchwayMaterial> fillsByCode = new();
        private readonly Dictionary<string, ArchwayMaterial> woodsById = new();
        private readonly Dictionary<AssetLocation, ArchwayMaterial> woodsByCode = new();
        private readonly Dictionary<string, List<string>> legacyFills = new();

        public bool Loaded { get; private set; }
        public IReadOnlyCollection<ArchwayMaterial> Rims => rimsById.Values;
        public IReadOnlyCollection<ArchwayMaterial> Fills => fillsById.Values;
        public IReadOnlyCollection<ArchwayMaterial> Woods => woodsById.Values;

        public bool TryGetRim(string id, [MaybeNullWhen(false)] out ArchwayMaterial material) => rimsById.TryGetValue(id, out material);
        public bool TryGetFill(string id, [MaybeNullWhen(false)] out ArchwayMaterial material) => fillsById.TryGetValue(id, out material);
        public bool TryGetRimByCode(AssetLocation code, [MaybeNullWhen(false)] out ArchwayMaterial material) => rimsByCode.TryGetValue(code, out material);
        public bool TryGetFillByCode(AssetLocation code, [MaybeNullWhen(false)] out ArchwayMaterial material) => fillsByCode.TryGetValue(code, out material);
        public bool TryGetWood(string id, [MaybeNullWhen(false)] out ArchwayMaterial material) => woodsById.TryGetValue(id, out material);
        public bool TryGetWoodByCode(AssetLocation code, [MaybeNullWhen(false)] out ArchwayMaterial material) => woodsByCode.TryGetValue(code, out material);

        /// <summary>
        /// Translates the fill of an archway built before the registry existed into a fill id.
        /// Several candidates may be listed, the first existing fill wins.
        /// Returns null if the archway is unfinished or no matching fill material exists.
        /// </summary>
        public string? ResolveLegacyFill(string? originblock, string? rock)
        {
            if (string.IsNullOrEmpty(originblock) || originblock == UnfinishedOriginblock) return null;

            if (!legacyFills.TryGetValue(originblock, out List<string>? templates)
                && !legacyFills.TryGetValue(AnyLegacyKey, out templates))
            {
                return null;
            }

            foreach (string template in templates)
            {
                if (template.Contains("{rock}") && string.IsNullOrEmpty(rock)) continue;

                string id = template.Replace("{rock}", rock ?? "").Replace("{originblock}", originblock);
                if (fillsById.ContainsKey(id)) return id;
            }
            return null;
        }

        public void Load(ICoreAPI api)
        {
            rimsById.Clear();
            fillsById.Clear();
            rimsByCode.Clear();
            fillsByCode.Clear();
            woodsById.Clear();
            woodsByCode.Clear();
            legacyFills.Clear();
            Loaded = false;

            string side = api.Side == EnumAppSide.Server ? "server" : "client";

            ArchwayMaterialsFile? file = ReadFile(api);
            if (file == null) return;

            var stats = new LoadStats();
            ResolveEntries(api, file.Rims, ArchwayMaterialKind.Rim, rimsById, rimsByCode, stats);
            ResolveEntries(api, file.Fills, ArchwayMaterialKind.Fill, fillsById, fillsByCode, stats);
            ResolveEntries(api, file.Woods, ArchwayMaterialKind.Wood, woodsById, woodsByCode, stats);

            if (file.LegacyFills != null)
            {
                foreach ((string key, JToken value) in file.LegacyFills)
                {
                    List<string> templates = ReadTextureList(value);
                    if (!string.IsNullOrWhiteSpace(key) && templates.Count > 0) legacyFills[key] = templates;
                }
            }

            Loaded = true;

            // The numbers before "Missing texture" are the same on server and client.
            api.Logger.Notification("{0} Archway materials ({1}): {2} rims, {3} fills, {4} woods from {5} entries. Skipped: {6} (invalid), {7} (id conflict). Missing texture: {8}.",
                LogPrefix, side, rimsById.Count, fillsById.Count, woodsById.Count, stats.Entries, stats.Invalid, stats.Conflicts,
                api.Side == EnumAppSide.Client ? stats.MissingTexture.ToString() : "not checked on the server");

            LogLegacyCoverage(api, side);

            // Only the client renders. ARL resolves the alias textures when it builds the archway meshes.
            if (api.Side == EnumAppSide.Client)
            {
                RegisterTextureAliases(api);
            }
        }

        private void RegisterTextureAliases(ICoreAPI api)
        {
            int registered = 0;
            registered += RegisterTextureAliases(api, rimsById.Values, RimAliasPath);
            registered += RegisterTextureAliases(api, fillsById.Values, FillAliasPath);
            registered += RegisterTextureAliases(api, fillsById.Values, LedgeAliasPath, m => m.LedgeTexture);
            registered += RegisterTextureAliases(api, woodsById.Values, WoodAliasPath);
            api.Logger.Notification("{0} {1} archway textures registered (client).", LogPrefix, registered);
        }

        private static int RegisterTextureAliases(ICoreAPI api, IEnumerable<ArchwayMaterial> materials, string aliasPath, System.Func<ArchwayMaterial, AssetLocation>? selectTexture = null)
        {
            int registered = 0;
            foreach (ArchwayMaterial material in materials)
            {
                // Already reported while loading. Without alias ARL shows "?", but such a material cannot be built.
                if (material.TextureMissing) continue;

                AssetLocation source = selectTexture?.Invoke(material) ?? material.Texture;
                IAsset? texture = api.Assets.TryGet(ToTextureFile(source));
                if (texture == null)
                {
                    api.Logger.Warning("{0} Archway texture {1} of \"{2}\" could not be loaded.", LogPrefix, source, material.Id);
                    continue;
                }

                api.Assets.Add(ToTextureFile(new AssetLocation(AliasDomain, aliasPath + material.Id)), texture);
                registered++;
            }
            return registered;
        }

        private static ArchwayMaterialsFile? ReadFile(ICoreAPI api)
        {
            IAsset? asset = api.Assets.TryGet(RegistryLocation);
            if (asset == null)
            {
                api.Logger.Error("{0} Archway material registry {1} not found. Archways cannot be built.", LogPrefix, RegistryLocation);
                return null;
            }

            try
            {
                return JToken.Parse(asset.ToText()).ToObject<ArchwayMaterialsFile>() ?? new ArchwayMaterialsFile();
            }
            catch (Exception e)
            {
                api.Logger.Error("{0} Could not read archway material registry {1}. Archways cannot be built. Error: {2}", LogPrefix, RegistryLocation, e.Message);
                return null;
            }
        }

        private static void ResolveEntries(ICoreAPI api, List<ArchwayMaterialEntry>? entries, ArchwayMaterialKind kind,
            Dictionary<string, ArchwayMaterial> byId, Dictionary<AssetLocation, ArchwayMaterial> byCode, LoadStats stats)
        {
            if (entries == null) return;

            string kindName = kind switch { ArchwayMaterialKind.Rim => "rim", ArchwayMaterialKind.Wood => "wood", _ => "fill" };

            foreach (ArchwayMaterialEntry entry in entries)
            {
                stats.Entries++;

                // Entries without "code" are legacy-only materials (see ResolveLegacyOnlyEntry).
                if (entry != null && string.IsNullOrWhiteSpace(entry.Code))
                {
                    ResolveLegacyOnlyEntry(api, entry, kind, kindName, byId, stats);
                    continue;
                }

                if (entry == null || string.IsNullOrWhiteSpace(entry.Code) || string.IsNullOrWhiteSpace(entry.Id) || ReadTextureList(entry.Texture).Count == 0)
                {
                    stats.Invalid++;
                    api.Logger.Warning("{0} Archway {1} entry skipped: \"code\", \"id\" and \"texture\" are required.", LogPrefix, kindName);
                    continue;
                }

                AssetLocation codePattern = AssetLocation.Create(entry.Code.Trim());
                string idTemplate = entry.Id.Trim();
                List<string> textureTemplates = ReadTextureList(entry.Texture);
                List<string> ledgeTemplates = ReadTextureList(entry.LedgeTexture);

                List<string> placeholders = PlaceholderRegex.Matches(codePattern.Path).Select(m => m.Groups[1].Value).ToList();
                string? unknown = PlaceholderRegex.Matches(idTemplate + " " + string.Join(" ", textureTemplates) + " " + string.Join(" ", ledgeTemplates))
                    .Select(m => m.Groups[1].Value)
                    .FirstOrDefault(p => !placeholders.Contains(p));
                if (unknown != null)
                {
                    stats.Invalid++;
                    api.Logger.Warning("{0} Archway {1} entry {2} skipped: placeholder {{{3}}} is used in id or texture but not in code.", LogPrefix, kindName, entry.Code, unknown);
                    continue;
                }

                if (placeholders.Count != placeholders.Distinct().Count())
                {
                    stats.Invalid++;
                    api.Logger.Warning("{0} Archway {1} entry {2} skipped: a placeholder is used twice in code.", LogPrefix, kindName, entry.Code);
                    continue;
                }

                Regex codeRegex = BuildCodeRegex(codePattern.Path);
                string literalPrefix = GetLiteralPrefix(codePattern.Path);

                int matches = 0;
                foreach (CollectibleObject collectible in EnumerateCollectibles(api))
                {
                    AssetLocation? code = collectible.Code;
                    if (code == null || code.Domain != codePattern.Domain || !code.Path.StartsWith(literalPrefix, StringComparison.Ordinal)) continue;

                    Match match = codeRegex.Match(code.Path);
                    if (!match.Success) continue;

                    matches++;

                    string id = FillPlaceholders(idTemplate, placeholders, match);

                    if (!ValidIdRegex.IsMatch(id))
                    {
                        stats.Invalid++;
                        api.Logger.Warning("{0} Archway {1} {2} skipped: id \"{3}\" contains invalid characters (allowed: a-z 0-9 - _).", LogPrefix, kindName, code, id);
                        continue;
                    }

                    // An earlier (more specific) entry already defines this block/item: it wins, also if its texture is missing (B28).
                    if (byCode.TryGetValue(code, out ArchwayMaterial? codeOwner))
                    {
                        if (codeOwner.Id != id)
                        {
                            stats.Conflicts++;
                            api.Logger.Warning("{0} Archway {1} {2} matches two entries (\"{3}\" and \"{4}\"). The first one is used.", LogPrefix, kindName, code, codeOwner.Id, id);
                        }
                        continue;
                    }

                    List<AssetLocation> candidates = textureTemplates.Select(t => AssetLocation.Create(FillPlaceholders(t, placeholders, match), code.Domain)).ToList();

                    if (byId.TryGetValue(id, out ArchwayMaterial? existing))
                    {
                        // Compare the texture lists, not the resolved textures: the result must be the same on server and client.
                        if (!existing.TextureCandidates.SequenceEqual(candidates))
                        {
                            stats.Conflicts++;
                            api.Logger.Error("{0} Archway {1} id \"{2}\" is defined twice with different textures ({3} and {4}). {5} is ignored.", LogPrefix, kindName, id,
                                string.Join(", ", existing.TextureCandidates), string.Join(", ", candidates), code);
                            continue;
                        }

                        existing.SourceCodes.Add(code);
                        byCode[code] = existing;
                        continue;
                    }

                    List<AssetLocation> ledgeCandidates = kind == ArchwayMaterialKind.Fill
                        ? ledgeTemplates.Select(t => AssetLocation.Create(FillPlaceholders(t, placeholders, match), code.Domain)).ToList()
                        : new List<AssetLocation>();

                    ArchwayMaterial material = CreateMaterial(api, id, kind, candidates, ledgeCandidates, entry.Buildable, kindName, code.ToString(), stats);
                    material.SourceCodes.Add(code);
                    byId[id] = material;
                    byCode[code] = material;
                }

                if (matches == 0)
                {
                    api.Logger.Notification("{0} Archway {1} entry {2} matches no loaded block or item.", LogPrefix, kindName, entry.Code);
                }
            }
        }

        /// <summary>
        /// An entry without "code" describes a material that exists only so that old archways keep their look
        /// (e.g. vanilla has a cobblestone texture for halite, but no halite cobblestone block). It can never be built.
        /// No placeholders are allowed here.
        /// </summary>
        private static void ResolveLegacyOnlyEntry(ICoreAPI api, ArchwayMaterialEntry entry, ArchwayMaterialKind kind, string kindName,
            Dictionary<string, ArchwayMaterial> byId, LoadStats stats)
        {
            string id = entry.Id?.Trim() ?? "";
            List<string> textureTemplates = ReadTextureList(entry.Texture);
            List<string> ledgeTemplates = ReadTextureList(entry.LedgeTexture);

            if (id.Length == 0 || textureTemplates.Count == 0 || !ValidIdRegex.IsMatch(id)
                || PlaceholderRegex.IsMatch(id + " " + string.Join(" ", textureTemplates) + " " + string.Join(" ", ledgeTemplates)))
            {
                stats.Invalid++;
                api.Logger.Warning("{0} Archway {1} entry without code skipped: needs a valid \"id\" and \"texture\", placeholders are not allowed.", LogPrefix, kindName);
                return;
            }

            // A real (buildable) material with the same id always wins.
            if (byId.ContainsKey(id)) return;

            List<AssetLocation> candidates = textureTemplates.Select(t => AssetLocation.Create(t)).ToList();
            List<AssetLocation> ledgeCandidates = ledgeTemplates.Select(t => AssetLocation.Create(t)).ToList();
            byId[id] = CreateMaterial(api, id, kind, candidates, ledgeCandidates, buildable: false, kindName, "legacy only", stats);
        }

        /// <summary>
        /// Creates a material. "texture" may list several textures (e.g. a texture pack first, vanilla as fallback): on the client
        /// the first existing one wins. The server never checks textures (B28: a dedicated server does not load them), it keeps
        /// the first one of the list. A material without texture on the client keeps its id, but cannot be built.
        /// </summary>
        private static ArchwayMaterial CreateMaterial(ICoreAPI api, string id, ArchwayMaterialKind kind, List<AssetLocation> candidates, List<AssetLocation> ledgeCandidates,
            bool buildable, string kindName, string source, LoadStats stats)
        {
            bool checkTextures = api.Side == EnumAppSide.Client;

            AssetLocation? texture = checkTextures ? candidates.FirstOrDefault(t => TextureExists(api, t)) : candidates[0];
            var material = new ArchwayMaterial(id, kind, texture ?? candidates[0], candidates, buildable);

            if (texture == null)
            {
                material.TextureMissing = true;
                stats.MissingTexture++;
                api.Logger.Warning("{0} Archway {1} \"{2}\" ({3}): texture {4} not found. It cannot be used for new archways.", LogPrefix, kindName, id, source, string.Join(", ", candidates));
            }

            if (ledgeCandidates.Count > 0)
            {
                AssetLocation? ledge = checkTextures ? ledgeCandidates.FirstOrDefault(t => TextureExists(api, t)) : ledgeCandidates[0];
                material.LedgeTexture = ledge ?? material.Texture;
            }

            return material;
        }

        /// <summary>
        /// Logs which old archways (rim + old fill category) cannot be translated to a fill material.
        /// These are exactly the combinations that show missing textures in older versions.
        /// </summary>
        private void LogLegacyCoverage(ICoreAPI api, string side)
        {
            foreach (string legacyKey in legacyFills.Keys.Where(k => k != AnyLegacyKey).OrderBy(k => k))
            {
                List<string> unresolved = rimsById.Keys
                    .Where(rock => ResolveLegacyFill(legacyKey, rock) == null)
                    .OrderBy(k => k)
                    .ToList();

                if (unresolved.Count == 0) continue;

                api.Logger.Notification("{0} Legacy archway fill \"{1}\" ({2}) has no fill material for rim(s): {3}. Old archways with these combinations will not be converted.",
                    LogPrefix, legacyKey, side, string.Join(", ", unresolved));
            }
        }

        private static IEnumerable<CollectibleObject> EnumerateCollectibles(ICoreAPI api)
        {
            foreach (Block block in api.World.Blocks)
            {
                if (block?.Code != null) yield return block;
            }
            foreach (Item item in api.World.Items)
            {
                if (item?.Code != null) yield return item;
            }
        }

        /// <summary>Client only: a dedicated server does not load textures, there this is false for nearly every texture (B28).</summary>
        private static bool TextureExists(ICoreAPI api, AssetLocation texture)
        {
            return api.Assets.Exists(ToTextureFile(texture));
        }

        /// <summary>A texture field may be a single string or a list of strings (first existing one wins).</summary>
        private static List<string> ReadTextureList(JToken? token)
        {
            var result = new List<string>();
            if (token == null) return result;
            if (token.Type == JTokenType.String)
            {
                string? value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (JToken item in token)
                {
                    if (item.Type != JTokenType.String) continue;
                    string? value = item.Value<string>();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
                }
            }
            return result;
        }

        /// <summary>block/stone/rock/granite1 -> textures/block/stone/rock/granite1.png</summary>
        private static AssetLocation ToTextureFile(AssetLocation texture)
        {
            return new AssetLocation(texture.Domain, "textures/" + texture.Path + ".png");
        }

        private static Regex BuildCodeRegex(string pattern)
        {
            var sb = new StringBuilder("^");
            int last = 0;
            foreach (Match m in PlaceholderRegex.Matches(pattern))
            {
                sb.Append(EscapeLiteral(pattern.Substring(last, m.Index - last)));
                sb.Append("(?<").Append(m.Groups[1].Value).Append(">.+?)");
                last = m.Index + m.Length;
            }
            sb.Append(EscapeLiteral(pattern.Substring(last)));
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
        }

        // A "*" in a code pattern matches anything without capturing it.
        private static string EscapeLiteral(string literal) => Regex.Escape(literal).Replace(@"\*", ".*?");

        private static string GetLiteralPrefix(string pattern)
        {
            int end = pattern.Length;
            int brace = pattern.IndexOf('{');
            int star = pattern.IndexOf('*');
            if (brace >= 0) end = Math.Min(end, brace);
            if (star >= 0) end = Math.Min(end, star);
            return pattern.Substring(0, end);
        }

        private static string FillPlaceholders(string template, List<string> placeholders, Match match)
        {
            foreach (string name in placeholders)
            {
                template = template.Replace("{" + name + "}", match.Groups[name].Value);
            }
            return template;
        }

        private sealed class LoadStats
        {
            public int Entries;
            public int MissingTexture;
            public int Invalid;
            public int Conflicts;
        }
    }

    /// <summary>JSON layout of archway-materials.json.</summary>
    public class ArchwayMaterialsFile
    {
        [JsonProperty("rims")]
        public List<ArchwayMaterialEntry>? Rims { get; set; }

        [JsonProperty("fills")]
        public List<ArchwayMaterialEntry>? Fills { get; set; }

        /// <summary>Boards for the door lintel of archways.</summary>
        [JsonProperty("woods")]
        public List<ArchwayMaterialEntry>? Woods { get; set; }

        /// <summary>Old fill category -> fill id (string) or list of fill ids (first existing wins).</summary>
        [JsonProperty("legacyFills")]
        public Dictionary<string, JToken>? LegacyFills { get; set; }
    }

    public class ArchwayMaterialEntry
    {
        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("id")]
        public string? Id { get; set; }

        /// <summary>String or list, the first existing texture is used (e.g. a texture pack first, vanilla as fallback).</summary>
        [JsonProperty("texture")]
        public JToken? Texture { get; set; }

        /// <summary>Fills only, optional: string or list, the first existing texture is used for the ledge of big archways.</summary>
        [JsonProperty("ledgeTexture")]
        public JToken? LedgeTexture { get; set; }

        [JsonProperty("buildable")]
        public bool Buildable { get; set; } = true;
    }
}
